using System;
using System.Runtime.CompilerServices;

namespace WarGame.Sim.Math;

public readonly struct FPVec2 : IEquatable<FPVec2>
{
    public readonly FP X;
    public readonly FP Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FPVec2(FP x, FP y) { X = x; Y = y; }

    public static readonly FPVec2 Zero = new(FP.Zero, FP.Zero);
    public static readonly FPVec2 UnitX = new(FP.One, FP.Zero);
    public static readonly FPVec2 UnitY = new(FP.Zero, FP.One);

    public static FPVec2 FromInts(int x, int y) => new(FP.FromInt(x), FP.FromInt(y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator +(FPVec2 a, FPVec2 b) => new(a.X + b.X, a.Y + b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator -(FPVec2 a, FPVec2 b) => new(a.X - b.X, a.Y - b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator -(FPVec2 a) => new(-a.X, -a.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator *(FPVec2 a, FP s) => new(a.X * s, a.Y * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator *(FP s, FPVec2 a) => new(a.X * s, a.Y * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FPVec2 operator /(FPVec2 a, FP s) => new(a.X / s, a.Y / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(FPVec2 a, FPVec2 b) => a.X == b.X && a.Y == b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(FPVec2 a, FPVec2 b) => !(a == b);

    public FP LengthSquared => X * X + Y * Y;
    public FP Length => FP.Sqrt(LengthSquared);

    public static FP Dot(FPVec2 a, FPVec2 b) => a.X * b.X + a.Y * b.Y;

    public FPVec2 Normalized()
    {
        FP len = Length;
        if (len == FP.Zero) return Zero;
        return new FPVec2(X / len, Y / len);
    }

    public bool Equals(FPVec2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FPVec2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);
    public override string ToString() => $"({X}, {Y})";
}
