using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace UltimateRandomizer.Common.Journal
{
	public class JournalSystem : ModSystem
	{
		public static ModKeybind ToggleKeybind { get; private set; }

		private static UserInterface userInterface;
		private static JournalUIState state;

		public override void Load()
		{
			ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "Journal", "J");
			if (Main.dedServ) return;
			userInterface = new UserInterface();
			state = new JournalUIState();
			state.Activate();
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
				state.Refresh();
				userInterface.SetState(state);
			}
			else
			{
				userInterface.SetState(null);
			}
		}

		public override void OnWorldUnload() => userInterface?.SetState(null);

		public override void UpdateUI(GameTime gameTime)
		{
			if (userInterface?.CurrentState != null) userInterface.Update(gameTime);
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
			if (index < 0) return;
			layers.Insert(index, new LegacyGameInterfaceLayer("UltimateRandomizer: Journal", () =>
			{
				if (userInterface?.CurrentState != null) userInterface.Draw(Main.spriteBatch, new GameTime());
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
