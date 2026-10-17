using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Puts every setting of the mod (all server configs and the client one) back to its default value
	public static class ConfigReset
	{
		private const string Prefix = "Mods.UltimateRandomizer.Chat.";

		public static ModConfig[] All => new ModConfig[]
		{
			GeneralConfig.Instance, ProgressionConfig.Instance, LootConfig.Instance, EconomyConfig.Instance, CraftingConfig.Instance,
			EnemiesConfig.Instance, ExtrasConfig.Instance, ClientConfig.Instance
		};

		// Server settings are changed by the host only (RandomizerConfig.AcceptClientChanges): other clients are told so
		// before anything is sent
		// While every config is saved (RandomizerConfig.OnChanged waits for the end)
		public static bool Batching { get; private set; }

		public static bool Allowed => Main.netMode != NetmodeID.MultiplayerClient || Main.countsAsHostForGameplay[Main.myPlayer];

		// A copy of the config with tML's defaults, made the way its "Restore defaults" button does (ModConfig defaults
		// come from the [DefaultValue] attributes applied by tML's serializer, not from new). keepReload: the settings
		// that need a reload keep their current value (a world is open: tML refuses to save them without a reload).
		public static ModConfig Defaults(ModConfig active, bool keepReload)
		{
			ModConfig clone = ConfigManager.GeneratePopulatedClone(active);
			JsonConvert.PopulateObject("{}", clone, ConfigManager.serializerSettings);
			if (!keepReload) return clone;
			foreach (PropertyFieldWrapper member in ConfigManager.GetFieldsAndProperties(active))
			{
				if (!member.CanWrite || member.MemberInfo.GetCustomAttribute<ReloadRequiredAttribute>() == null) continue;
				object value = member.GetValue(active);
				// A deep copy: the clone must not share lists with the live config
				object copy = value == null ? null : JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value, ConfigManager.serializerSettings), member.Type, ConfigManager.serializerSettings);
				member.SetValue(clone, copy);
			}
			return clone;
		}

		// Saves the defaults of every config through ModConfig.SaveChanges, like the Save button: on a client the server
		// configs go to the server as change requests (it answers and refreshes the open config page), elsewhere they are
		// saved and OnChanged runs (SeedSystem.NotifyConfigSaved). Returns the message to show.
		public static string ResetAll()
		{
			bool keepReload = !Main.gameMenu;
			bool sent = false, refused = false;
			Batching = true;
			try
			{
				foreach (ModConfig config in All)
				{
					if (config == null) continue;
					ConfigSaveResult result = config.SaveChanges(Defaults(config, keepReload), null, true, true);
					if (result == ConfigSaveResult.RequestSentToServer) sent = true;
					else if (result == ConfigSaveResult.NeedsReload) refused = true;
				}
			}
			finally
			{
				Batching = false;
			}
			SeedSystem.NotifyConfigSaved();
			string message = Language.GetTextValue(Prefix + (sent ? "ResetSent" : "ResetDone"));
			if (refused) message += " " + Language.GetTextValue(Prefix + "ResetNeedsReload");
			else if (keepReload) message += " " + Language.GetTextValue(Prefix + "ResetKeptReload");
			return message;
		}
	}

	// The "Reset all settings" button at the top of the first config page: a first click asks for confirmation, a
	// second one within a few seconds resets everything and shows the new values on the open page
	public class ResetAllElement : ConfigElement
	{
		private const string Prefix = "Mods.UltimateRandomizer.Chat.";
		private static readonly TimeSpan ConfirmTime = TimeSpan.FromSeconds(4);
		private DateTime confirmUntil = DateTime.MinValue;

		private bool Confirming => DateTime.UtcNow < confirmUntil;

		public override void OnBind()
		{
			base.OnBind();
			string label = Label;
			TextDisplayFunction = () => Confirming ? Language.GetTextValue(Prefix + "ResetConfirm") : label;
			OnLeftClick += (_, _) => Click();
		}

		private void Click()
		{
			UIState page = Page();
			if (!ConfigReset.Allowed)
			{
				SoundEngine.PlaySound(SoundID.MenuClose);
				SetMessage(page, Language.GetTextValue(Prefix + "ConfigHostOnly"), Color.Red);
				return;
			}
			if (!Confirming)
			{
				SoundEngine.PlaySound(SoundID.MenuTick);
				confirmUntil = DateTime.UtcNow + ConfirmTime;
				return;
			}
			confirmUntil = DateTime.MinValue;
			SoundEngine.PlaySound(SoundID.MenuOpen);
			string message = ConfigReset.ResetAll();
			if (page == null) return;
			SetMessage(page, message, Color.Green);
			Refresh(page);
		}

		// The config page (tML's UIModConfig, internal: reached by reflection)
		private static readonly Type pageType = typeof(ConfigElement).Assembly.GetType("Terraria.ModLoader.Config.UI.UIModConfig");
		private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		private static readonly MethodInfo setMessage = pageType?.GetMethod("SetMessage", Any, null, new[] { typeof(string), typeof(Color) }, null);
		private static readonly MethodInfo doMenuModeState = pageType?.GetMethod("DoMenuModeState", Any, null, new[] { typeof(bool) }, null);

		private UIState Page()
		{
			for (UIElement e = Parent; e != null; e = e.Parent)
			{
				if (e is UIState state && pageType != null && pageType.IsInstanceOfType(state)) return state;
			}
			return null;
		}

		private static void SetMessage(UIState page, string text, Color color)
		{
			try
			{
				if (page != null) setMessage?.Invoke(page, new object[] { text, color });
			}
			catch (Exception)
			{
				// Only the message is lost
			}
		}

		// The page shows a copy of the config made when it opened: open it again so that it copies the new values, the
		// way tML's "Restore defaults" does (DoMenuModeState keeps the message; done by hand if it is not found)
		private static void Refresh(UIState page)
		{
			try
			{
				if (doMenuModeState != null)
				{
					doMenuModeState.Invoke(page, new object[] { true });
					return;
				}
			}
			catch (Exception)
			{
				// Falls back below
			}
			if (Main.gameMenu)
			{
				Main.MenuUI.SetState(null);
				Main.menuMode = 10024;
			}
			else
			{
				Main.InGameUI.SetState(null);
				Main.InGameUI.SetState(page);
			}
		}
	}
}
