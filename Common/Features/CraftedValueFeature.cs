using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Features
{
	// An item made through a randomized recipe sells for what its ingredients were worth (never more than its vanilla
	// value), so a randomized result can't turn cheap ingredients into money. The value is per crafted instance: the same
	// item found elsewhere, or made by a vanilla recipe, keeps its vanilla value.
	// Runs where the item lives: crafting on the client (OnCreated, then OnStack onto the mouse item); the value travels with
	// the item through NetSend/NetReceive (inventories, chests, dropped items) and SaveData/LoadData.
	// item.value is reset by every SetDefaults: ItemIO.Load and ItemIO.Receive (also used by Item.Refresh, so by
	// reforging) call SetDefaults, then Prefix, then the globals' LoadData/NetReceive, where the value is set again with
	// the multiplier of the item's prefix. Item.Prefix multiplies the current value, so a reforge (ResetPrefix ->
	// Refresh -> our NetReceive at prefix 0, then Prefix) also ends on the right value.
	// Shimmer: what a crafted-value item shimmers into keeps the discount (CapShimmer), else crafting cheaply then shimmering
	// would sell for the full price (randomized shimmer results are also capped relative to the vanilla values, ShimmerFeature).
	// Shimmering runs in single player or on the server: the outputs are synced again with their value.
	public class CraftedValueItem : GlobalItem
	{
		public override bool InstancePerEntity => true;

		// Unit value without prefix; -1 = the vanilla value
		public int CraftedUnitValue = -1;

		private const string Key = "CraftedUnitValue";

		// Value multiplier of each (type, prefix), measured on a fresh item
		private static readonly Dictionary<(int type, int prefix), double> ratios = new();

		public static void ClearCache() => ratios.Clear();

		public static int VanillaValue(int type) => ContentSamples.ItemsByType.TryGetValue(type, out Item sample) ? Math.Max(sample.value, 0) : 0;

		private static double PrefixRatio(int type, int prefix)
		{
			if (prefix <= 0) return 1;
			if (ratios.TryGetValue((type, prefix), out double ratio)) return ratio;
			int vanilla = VanillaValue(type);
			ratio = 1;
			if (vanilla > 0)
			{
				Item probe = new(type);
				if (probe.Prefix(prefix)) ratio = probe.value / (double)vanilla;
			}
			ratios[(type, prefix)] = ratio;
			return ratio;
		}

		// The value of an instance from its unit value (idempotent)
		private static void Apply(Item item, int unit)
		{
			if (unit < 0) return;
			item.value = (int)Math.Min(int.MaxValue, unit * PrefixRatio(item.type, item.prefix));
		}

		public static int UnitValueOf(Item item) => item.TryGetGlobalItem(out CraftedValueItem crafted) ? crafted.CraftedUnitValue : -1;

		// The share of their vanilla value the items a shimmer gives keep: what the input was worth (its crafted unit value
		// times its stack) over the vanilla value of everything it gives, at most 1. Pure (ShimmerNoMoneyLoop test)
		public static double ShimmerShare(long inputValue, long outputVanillaValue) =>
			outputVanillaValue <= 0 ? 1 : Math.Clamp(inputValue / (double)outputVanillaValue, 0, 1);

		// The unit value of an item of vanilla value outputVanilla given with that share; -1: its vanilla value
		public static int ShimmerUnit(double share, int outputVanilla)
		{
			if (outputVanilla <= 0 || share >= 1) return -1;
			return (int)Math.Floor(outputVanilla * Math.Max(0, share));
		}

		// While an item with a crafted value shimmers: every item spawned meanwhile (decrafted ingredients, the extra stack of a
		// transformation beyond the new max stack, special results); null: none shimmering
		private static List<Item> shimmerOutputs;

		public override void Load() => On_Item.GetShimmered += CapShimmer;

		// Measured, not predicted (vanilla has special results: Rod of Discord, Clentaminator, Bottomless Bucket, Lunar Bricks,
		// music boxes; partial decrafts, RecipeLoader.ConsumeIngredient, modded results): what the shimmer consumed (the unit
		// value times the stack it took, the whole stack when the item changed) over the vanilla value of everything it gave
		// (the spawned items, and the item itself when its type changed). Every output keeps that share of its vanilla value.
		// Runs where the shimmer runs (single player, server): vanilla synced the outputs inside orig (message 145), they are
		// synced again with their value the same way (number2 = 1: the shimmer state kept on the clients)
		private static void CapShimmer(On_Item.orig_GetShimmered orig, Item item)
		{
			int unit = UnitValueOf(item);
			if (unit < 0 || shimmerOutputs != null)
			{
				orig(item);
				return;
			}
			int typeBefore = item.type;
			int stackBefore = Math.Max(0, item.stack);
			List<Item> outputs = new();
			shimmerOutputs = outputs;
			try
			{
				orig(item);
			}
			finally
			{
				shimmerOutputs = null;
			}
			bool changed = item.type != typeBefore;
			long consumed = (long)unit * (changed ? stackBefore : Math.Max(0, stackBefore - Math.Max(0, item.stack)));
			List<(Item item, bool self)> given = outputs.Where(o => o != null && !o.IsAir).Select(o => (o, false)).ToList();
			if (changed && !item.IsAir && item.active) given.Add((item, true));
			long vanilla = given.Sum(g => (long)VanillaValue(g.item.type) * Math.Max(1, g.item.stack));
			if (given.Count == 0 || vanilla <= 0) return;
			double share = ShimmerShare(consumed, vanilla);
			foreach ((Item output, bool self) in given)
			{
				int outputUnit = ShimmerUnit(share, VanillaValue(output.type));
				if (outputUnit < 0 && UnitValueOf(output) < 0) continue;
				SetUnitValue(output, outputUnit);
				if (Main.netMode == NetmodeID.Server && output.whoAmI >= 0 && output.whoAmI < Main.maxItems && Main.item[output.whoAmI] == output)
				{
					NetMessage.SendData(MessageID.SyncItemsWithShimmer, -1, -1, null, output.whoAmI, 1f);
				}
			}
		}

		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (shimmerOutputs != null && !item.IsAir) shimmerOutputs.Add(item);
		}

		// Sets the unit value of an instance (-1: back to vanilla)
		public static void SetUnitValue(Item item, int unit)
		{
			if (!item.TryGetGlobalItem(out CraftedValueItem crafted)) return;
			int vanilla = VanillaValue(item.type);
			crafted.CraftedUnitValue = unit < 0 || unit >= vanilla ? -1 : unit;
			Apply(item, crafted.CraftedUnitValue >= 0 ? crafted.CraftedUnitValue : vanilla);
		}

		// The created stack is worth what was consumed, at most its vanilla value
		public static void SetFromIngredients(Item item, IEnumerable<Item> consumed)
		{
			long total = consumed.Where(c => c != null && !c.IsAir).Sum(c => (long)Math.Max(c.value, 0) * c.stack);
			long unit = total / Math.Max(1, item.stack);
			if (unit < VanillaValue(item.type)) SetUnitValue(item, (int)unit);
		}

		// Any recipe made of a crafted-value ingredient passes the lower value on (it only ever lowers it): otherwise a cheap
		// randomized result used in a vanilla recipe would give back the full price
		public override void OnCreated(Item item, ItemCreationContext context)
		{
			if (context is not RecipeItemCreationContext craft || craft.Recipe == null || !CraftingConfig.Instance.CraftedValueFromIngredients) return;
			List<Item> consumed = craft.ConsumedItems ?? new List<Item>();
			if (!RecipeSystem.IsRandomized(craft.Recipe.RecipeIndex) && !consumed.Any(c => c != null && UnitValueOf(c) >= 0)) return;
			SetFromIngredients(item, consumed);
		}

		// Vanilla bases the reforge price on value * stack: a cheap crafted item would reforge for nothing
		public override bool ReforgePrice(Item item, ref int reforgePrice, ref bool canApplyDiscount)
		{
			if (CraftedUnitValue >= 0) reforgePrice = (int)Math.Min(int.MaxValue, VanillaValue(item.type) * PrefixRatio(item.type, item.prefix) * Math.Max(1, item.stack));
			return true;
		}

		// Weighted average of the unit values; called before the transfer (destination.stack is still the old one)
		public override void OnStack(Item destination, Item source, int numToTransfer)
		{
			int sourceUnit = UnitValueOf(source);
			if (CraftedUnitValue < 0 && sourceUnit < 0) return;
			long vanilla = VanillaValue(destination.type);
			long mine = CraftedUnitValue >= 0 ? CraftedUnitValue : vanilla;
			long theirs = sourceUnit >= 0 ? sourceUnit : vanilla;
			long count = Math.Max(0, destination.stack) + Math.Max(0, numToTransfer);
			if (count <= 0) return;
			long average = (mine * Math.Max(0, destination.stack) + theirs * Math.Max(0, numToTransfer)) / count;
			CraftedUnitValue = average >= vanilla ? -1 : (int)average;
			Apply(destination, CraftedUnitValue >= 0 ? CraftedUnitValue : (int)vanilla);
		}

		// The new stack is usually a clone already: copied anyway
		public override void SplitStack(Item destination, Item source, int numToTransfer)
		{
			CraftedUnitValue = UnitValueOf(source);
			destination.value = source.value;
		}

		public override void PostReforge(Item item) => Apply(item, CraftedUnitValue);

		public override void SaveData(Item item, TagCompound tag)
		{
			if (CraftedUnitValue >= 0) tag[Key] = CraftedUnitValue;
		}

		public override void LoadData(Item item, TagCompound tag)
		{
			if (!tag.ContainsKey(Key)) return;
			CraftedUnitValue = tag.GetInt(Key);
			Apply(item, CraftedUnitValue);
		}

		// 1 byte for the usual "no crafted value"
		public override void NetSend(Item item, BinaryWriter writer) => writer.Write7BitEncodedInt(CraftedUnitValue + 1);

		public override void NetReceive(Item item, BinaryReader reader)
		{
			CraftedUnitValue = reader.Read7BitEncodedInt() - 1;
			Apply(item, CraftedUnitValue);
		}
	}
}
