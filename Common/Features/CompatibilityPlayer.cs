using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Features
{
	public class CompatibilityPlayer : ModPlayer
	{
		public override void OnEnterWorld()
		{
			if (ModLoader.HasMod("tRandomizer"))
			{
				Main.NewText(Language.GetTextValue("Mods.UltimateRandomizer.Chat.OldRandomizerEnabled"), Color.Orange);
			}
		}
	}
}
