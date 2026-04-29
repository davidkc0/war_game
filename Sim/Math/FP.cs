using System;
using System.Runtime.CompilerServices;

namespace WarGame.Sim.Math;

// Q32.32 fixed-point. Backing field is a 64-bit signed integer:
//   high 32 bits = integer part (signed two's complement),
//   low 32 bits  = fractional part.
//
// Why fixed-point at all: floats are not bit-identical across CPUs / OSes / JITs
// once you involve transcendentals, denormals, or fused-multiply-add. Lockstep
// netcode needs every client to produce the same state from the same inputs,
// which requires bit-identical arithmetic. Integer ops are bit-identical.
public readonly struct FP : IEquatable<FP>, IComparable<FP>
{
    public const int FractionalBits = 32;
    public const long FractionalMask = (1L << FractionalBits) - 1;
    public const long OneRaw = 1L << FractionalBits;

    public readonly long Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FP(long raw) { Raw = raw; }

    public static readonly FP Zero = new(0);
    public static readonly FP One = new(OneRaw);
    public static readonly FP Half = new(OneRaw >> 1);
    public static readonly FP MinValue = new(long.MinValue);
    public static readonly FP MaxValue = new(long.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP FromRaw(long raw) => new(raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP FromInt(int value) => new((long)value << FractionalBits);

    // Float conversions are render/UI-only. Never call this from /Sim — it
    // re-introduces float non-determinism.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP FromFloatUnsafe(float value) => new((long)(value * OneRaw));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToInt() => (int)(Raw >> FractionalBits);

    // Same caveat as FromFloatUnsafe: render/UI only.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ToFloatUnsafe() => Raw / (float)OneRaw;

    public double ToDoubleUnsafe() => Raw / (double)OneRaw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP operator +(FP a, FP b) => new(a.Raw + b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP operator -(FP a, FP b) => new(a.Raw - b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP operator -(FP a) => new(-a.Raw);

    // Multiply: (a * b) >> 32. The naive `(a.Raw * b.Raw) >> 32` overflows for
    // any operand whose absolute integer-part magnitude exceeds ~2 (since
    // raw values are 64-bit and the product is 128-bit). Use Math.BigMul to
    // get the full 128-bit product, then shift.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP operator *(FP a, FP b)
    {
        long high = System.Math.BigMul(a.Raw, b.Raw, out long low);
        // low is unsigned 64-bit semantically; we shift it right 32 bits
        // (logical shift), then OR in the low 32 bits of `high` as the new
        // top 32 bits. Cast through ulong to avoid sign-extending the shift.
        long lowShifted = (long)((ulong)low >> FractionalBits);
        long highShifted = high << (64 - FractionalBits);
        return new FP(lowShifted | highShifted);
    }

    // Divide: ((a << 32) / b). Same overflow concern — promote to 128-bit
    // numerator. .NET 8's Math.BigMul gives us a 128-bit product; for division
    // we do it manually using Int128 (which is deterministic, integer-only).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FP operator /(FP a, FP b)
    {
        if (b.Raw == 0) throw new DivideByZeroException();
        Int128 numerator = (Int128)a.Raw << FractionalBits;
        return new FP((long)(numerator / b.Raw));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(FP a, FP b) => a.Raw == b.Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(FP a, FP b) => a.Raw != b.Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(FP a, FP b) => a.Raw < b.Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(FP a, FP b) => a.Raw > b.Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(FP a, FP b) => a.Raw <= b.Raw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(FP a, FP b) => a.Raw >= b.Raw;

    public static FP Abs(FP a) => a.Raw < 0 ? new FP(-a.Raw) : a;

    public static FP Min(FP a, FP b) => a.Raw < b.Raw ? a : b;
    public static FP Max(FP a, FP b) => a.Raw > b.Raw ? a : b;

    // Deterministic integer sqrt on the raw value (Newton's method on integers).
    // Returns the FP whose square is closest to (but not exceeding) `value`.
    public static FP Sqrt(FP value)
    {
        if (value.Raw < 0) throw new ArgumentException("sqrt of negative");
        if (value.Raw == 0) return Zero;

        // sqrt(raw * 2^32) = sqrt(raw) * 2^16. To keep integer math, we
        // compute sqrt of (raw << 32) as a 96-bit value, returning it as a
        // 64-bit FP (whose own raw is sqrt(raw)*2^16, fitting in 48 bits for
        // realistic gameplay-scale values).
        Int128 scaled = (Int128)value.Raw << FractionalBits;
        Int128 x = scaled;
        Int128 y = (x + 1) >> 1;
        // Integer Newton iteration: y_{n+1} = (y_n + scaled / y_n) / 2.
        // Converges in O(log bits) iterations and is bit-identical across
        // platforms because every operation is on integers.
        while (y < x)
        {
            x = y;
            y = (x + scaled / x) >> 1;
        }
        return new FP((long)x);
    }

    public bool Equals(FP other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is FP fp && Raw == fp.Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(FP other) => Raw.CompareTo(other.Raw);
    public override string ToString() =>
        ToDoubleUnsafe().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
