using System;

namespace MagicCampus.Runtime
{
    // Namespace-local argument guards (Contracts' Ids/PortGuard are internal to Contracts).
    internal static class RuntimeGuard
    {
        internal static string Id(string v, string name)
        {
            if (v == null) throw new ArgumentNullException(name);
            if (v.Length == 0) throw new ArgumentException("must not be empty", name);
            foreach (var c in v)
                if (char.IsWhiteSpace(c) || char.IsControl(c)) throw new ArgumentException("whitespace/control characters not allowed", name);
            return v;
        }

        internal static T Required<T>(T v, string name) where T : class
        {
            if (v == null) throw new ArgumentNullException(name);
            return v;
        }

        internal static double NonNegative(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) throw new ArgumentOutOfRangeException(name);
            return v;
        }

        internal static double Positive(double v, string name)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) throw new ArgumentOutOfRangeException(name);
            return v;
        }
    }
}
