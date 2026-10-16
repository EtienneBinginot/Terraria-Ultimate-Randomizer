using System;
using System.Collections.Generic;
using System.Reflection;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Relative chance of each item category (0 = never). Subclasses declare one int field per category, named like it;
	// a category without a field (boss summons) is never picked.
	public abstract class WeightTable
	{
		private static readonly Dictionary<Type, FieldInfo[]> fieldsByType = new();

		public int Get(ItemCategory category)
		{
			FieldInfo field = Fields(GetType())[(int)category];
			return field == null ? 0 : Math.Max(0, (int)field.GetValue(this));
		}

		private static FieldInfo[] Fields(Type type)
		{
			lock (fieldsByType)
			{
				if (fieldsByType.TryGetValue(type, out FieldInfo[] fields)) return fields;
				fields = new FieldInfo[ItemCategories.Count];
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					FieldInfo field = type.GetField(((ItemCategory)c).ToString(), BindingFlags.Public | BindingFlags.Instance);
					if (field != null && field.FieldType == typeof(int)) fields[c] = field;
				}
				fieldsByType[type] = fields;
				return fields;
			}
		}

		public override bool Equals(object obj) => obj != null && obj.GetType() == GetType() && GetHashCode() == obj.GetHashCode();

		public override int GetHashCode()
		{
			int hash = 17;
			for (int i = 0; i < ItemCategories.Count; i++) hash = hash * 31 + Get((ItemCategory)i);
			return hash;
		}
	}
}
