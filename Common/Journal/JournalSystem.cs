using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using UltimateRandomizer.Common.Codex.Interface;

namespace UltimateRandomizer.Common.Journal
{
	// The codex window (J), client side; the keybind keeps its old "Journal" name so players keep their binding
	public class JournalSystem : ModSystem
	{
		public static ModKeybind ToggleKeybind { get; private set; }

		private static UserInterface userInterface;
		private static CodexUIState state;

		// Made when first needed, not in Load: the mod's texts are only loaded after Load (LanguageManager.ReloadLanguage
		// at the SetupContent stage), so texts read in OnInitialize then were the raw keys, for good
		public static CodexUIState State
		{
			get
			{
				if (state == null && userInterface != null)
				{
					state = new CodexUIState();
					state.Activate();
				}
				return state;
			}
		}

		public static bool IsOpen => userInterface?.CurrentState != null;

		public override void Load()
		{
			ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "Journal", "J");
			if (Main.dedServ) return;
			userInterface = new UserInterface();
		}

		public override void Unload()
		{
			ToggleKeybind = null;
			userInterface = null;
			state = null;
		}

		public static void Toggle()
		{
			if (userInterface == null) return;
			if (userInterface.CurrentState == null)
			{
				State.Refresh();
				userInterface.SetState(state);
			}
			else
			{
				state.OnClosed();
				userInterface.SetState(null);
			}
		}

		// Opens the codex (if closed) on a page
		public static void OpenAt(CodexPage page)
		{
			if (userInterface == null) return;
			if (userInterface.CurrentState == null)
			{
				State.Refresh();
				userInterface.SetState(state);
			}
			state.Navigate(page);
		}

		public override void OnWorldUnload()
		{
			if (IsOpen) Toggle();
			state?.ClearIcons();
		}

		public override void UpdateUI(GameTime gameTime)
		{
			if (userInterface?.CurrentState == null) return;
			userInterface.Update(gameTime);
			// Closed after the update, not from inside an element's handler
			if (state != null && state.CloseRequested) Toggle();
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
			if (index < 0) return;
			layers.Insert(index, new LegacyGameInterfaceLayer("UltimateRandomizer: Codex", () =>
			{
				if (userInterface?.CurrentState != null) userInterface.Draw(Main.spriteBatch, new GameTime());
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
