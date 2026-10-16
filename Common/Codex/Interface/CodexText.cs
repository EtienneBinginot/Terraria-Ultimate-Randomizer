using System.Collections.Generic;
using System.Linq;
using System.Text;
using Terraria.Localization;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// The codex texts ("Mods.UltimateRandomizer.Codex." + key). A missing key is logged once and shown as readable
	// English made from the key ("Source.Chest" -> "Chest", "TabEnemies" -> "Enemies"), never as the raw key.
	// tools/py/check_loc.py checks every CodexText.Get("...") key, and the keys made from enum names, offline.
	public static class CodexText
	{
		public const string Prefix = "Mods.UltimateRandomizer.Codex.";
		private static readonly HashSet<string> warned = new();

		public static string Get(string key, params object[] args)
		{
			string full = Prefix + key;
			if (Language.Exists(full)) return Language.GetTextValue(full, args);
			if (warned.Add(full)) ModContent.GetInstance<UltimateRandomizer>()?.Logger.Warn($"Codex: missing text {full}");
			string fallback = Readable(key);
			return args.Length == 0 ? fallback : fallback + ": " + string.Join(", ", args);
		}

		// The text of an enum value ("Source." + CodexSource.Chest...)
		public static string Of<T>(string group, T value) where T : struct => Get(group + "." + value);

		public static LocalizedText Live(string key) => Language.GetText(Prefix + key);

		// "Category.WeaponMelee" -> "Weapon melee", "TabCrafts" -> "Crafts"
		public static string Readable(string key)
		{
			string last = key.Split('.').Last();
			if (last.Length > 3 && last.StartsWith("Tab") && char.IsUpper(last[3])) last = last.Substring(3);
			StringBuilder text = new();
			for (int i = 0; i < last.Length; i++)
			{
				char c = last[i];
				if (i > 0 && char.IsUpper(c))
				{
					text.Append(' ');
					text.Append(char.ToLowerInvariant(c));
				}
				else
				{
					text.Append(c);
				}
			}
			return text.ToString();
		}
	}
}
