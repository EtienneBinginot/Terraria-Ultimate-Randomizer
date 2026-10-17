using System.IO;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex;

namespace UltimateRandomizer
{
	public enum PacketType : byte { CodexRequestFull, CodexEntries, CodexReset, CodexFullDone, HintRefused }

	public class UltimateRandomizer : Mod
	{
		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			PacketType type = (PacketType)reader.ReadByte();
			switch (type)
			{
				case PacketType.CodexRequestFull:
				case PacketType.CodexEntries:
				case PacketType.CodexReset:
				case PacketType.CodexFullDone:
				case PacketType.HintRefused:
					CodexSystem.HandlePacket(type, reader, whoAmI);
					break;
				default:
					Logger.Warn("Unknown packet " + (int)type);
					break;
			}
		}

		public override void Load()
		{
			if (ModLoader.HasMod("tRandomizer"))
			{
				Logger.Warn("tRandomizer is enabled too: loot would be randomized twice. Enable only one of the two randomizers.");
			}
		}
	}
}
