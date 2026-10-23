using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Relative chance of each item category (0 = never). Subclasses declare one int field per category, named like it;
	// a category without a field (boss summons) is never picked.
	public abstract class WeightTable
	{
		// Compiled field readers (read on every weighted roll: reflection would be slow), still reading the live values
		private static readonly Dictionary<Type, Func<WeightTable, int>[]> gettersByType = new();

		public int Get(ItemCategory category)
		{
			Func<WeightTable, int> getter = Getters(GetType())[(int)category];
			return getter == null ? 0 : Math.Max(0, getter(this));
		}

		private static Func<WeightTable, int>[] Getters(Type type)
		{
			lock (gettersByType)
			{
				if (gettersByType.TryGetValue(type, out Func<WeightTable, int>[] getters)) return getters;
				getters = new Func<WeightTable, int>[ItemCategories.Count];
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					FieldInfo field = type.GetField(((ItemCategory)c).ToString(), BindingFlags.Public | BindingFlags.Instance);
					if (field == null || field.FieldType != typeof(int)) continue;
					ParameterExpression table = Expression.Parameter(typeof(WeightTable));
					getters[c] = Expression.Lambda<Func<WeightTable, int>>(Expression.Field(Expression.Convert(table, type), field), table).Compile();
				}
				gettersByType[type] = getters;
				return getters;
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
