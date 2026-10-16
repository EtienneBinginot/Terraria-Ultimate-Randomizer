using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace UltimateRandomizer.Common.Debug
{
	public class DebugSystem : ModSystem
	{
		public static ModKeybind ToggleKeybind { get; private set; }

		private static UserInterface userInterface;
		private static DebugUIState state;

		public override void Load()
		{
			ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "Debug", "K");
			if (Main.dedServ) return;
			userInterface = new UserInterface();
		}

		public override void Unload()
		{
			ToggleKeybind = null;
			userInterface = null;
			state = null;
		}

		public static bool Visible => userInterface?.CurrentState != null;

		public static void Toggle()
		{
			if (userInterface == null) return;
			if (userInterface.CurrentState == null)
			{
				// Made when first opened: the mod's texts are not loaded yet in Load (they would stay raw keys)
				if (state == null)
				{
					state = new DebugUIState();
					state.Activate();
				}
				state.Rebuild();
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
			layers.Insert(index, new LegacyGameInterfaceLayer("UltimateRandomizer: Debug", () =>
			{
				if (userInterface?.CurrentState != null) userInterface.Draw(Main.spriteBatch, new GameTime());
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
