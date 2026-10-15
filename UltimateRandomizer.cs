using Terraria.ModLoader;

namespace UltimateRandomizer
{
	public class UltimateRandomizer : Mod
	{
		public override void Load()
		{
			if (ModLoader.HasMod("tRandomizer"))
			{
				Logger.Warn("tRandomizer is enabled too: loot would be randomized twice. Enable only one of the two randomizers.");
			}
		}
	}
}
