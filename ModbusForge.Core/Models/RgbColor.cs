using System;

namespace ModbusForge.Models
{
    /// <summary>
    /// Portable RGB/RGBA color used by view-agnostic core models.
    /// UI projects can convert to the platform-specific color type.
    /// </summary>
    public readonly struct RgbColor : IEquatable<RgbColor>
    {
        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public RgbColor(byte r, byte g, byte b)
        {
            A = 255;
            R = r;
            G = g;
            B = b;
        }

        public RgbColor(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public static RgbColor FromRgb(byte r, byte g, byte b) => new(r, g, b);

        public static RgbColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

        public bool Equals(RgbColor other) => A == other.A && R == other.R && G == other.G && B == other.B;

        public override bool Equals(object? obj) => obj is RgbColor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, R, G, B);

        public static bool operator ==(RgbColor left, RgbColor right) => left.Equals(right);

        public static bool operator !=(RgbColor left, RgbColor right) => !left.Equals(right);

        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }
}
