using System;

namespace UltimateRandomizer.Common.Core
{
	// Bell-shaped random multipliers: values near normal (1) are common, values far from it are rarer but possible.
	// Works in log space, so x2 and x0.5 are equally far from normal.
	public static class StatCurve
	{
		// A position in [-1, 1]: |u|^extremeness pushes most draws towards 0 (normal)
		public static double Position(Random rng, double extremeness)
		{
			double u = rng.NextDouble() * 2 - 1;
			return Math.Sign(u) * Math.Pow(Math.Abs(u), Math.Max(1, extremeness));
		}

		// A position in [0, 1], for one-sided ranges (giant and mini sizes)
		public static double OneSided(Random rng, double extremeness) => Math.Pow(rng.NextDouble(), Math.Max(1, extremeness));

		// Position -1 gives min, 0 gives normal (1, kept inside the range), 1 gives max
		public static float FromPosition(double position, float min, float max)
		{
			if (min > max) (min, max) = (max, min);
			double normal = Math.Clamp(1.0, min, max);
			double bound = position >= 0 ? max : min;
			if (normal <= 0 || bound <= 0) return (float)normal;
			return (float)(normal * Math.Pow(bound / normal, Math.Abs(position)));
		}

		public static float Sample(Random rng, float min, float max, double extremeness) => FromPosition(Position(rng, extremeness), min, max);

		// Between a and b (a = most likely), geometrically
		public static float Between(double position01, float a, float b)
		{
			if (a <= 0 || b <= 0) return a;
			return (float)(a * Math.Pow(b / a, Math.Clamp(position01, 0, 1)));
		}
	}
}
