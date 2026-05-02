using System.Runtime.CompilerServices;

namespace WarGame.Sim.Generation;

// Deterministic integer-only value noise for terrain generation.
// Uses a seeded permutation table (no floats, no platform-dependent math).
//
// The noise function returns values in [0, 65535] (16-bit range) for a
// given (x, y) coordinate. Multiple octaves are combined by the caller.
//
// Why not simplex noise: simplex noise implementations rely heavily on
// float gradients and interpolation. Since this runs in Sim/ (must be
// deterministic across platforms), we use integer-only value noise with
// bilinear interpolation via fixed-point arithmetic. The quality is
// lower than simplex but more than sufficient for 60×60 terrain maps.
public sealed class IntegerNoise
{
    private readonly byte[] _perm; // 256-entry permutation table

    public IntegerNoise(ref Math.SimRng rng)
    {
        // Build a Fisher-Yates shuffled permutation table.
        // `rng` is taken by ref so the caller's state advances; otherwise
        // two `IntegerNoise` instances built from the same outer rng would
        // share an identical permutation table (SimRng is a struct), and
        // any "independent" noise channels would produce correlated output.
        _perm = new byte[256];
        for (int i = 0; i < 256; i++) _perm[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
        }
    }

    /// <summary>
    /// Hash a 2D coordinate to a pseudo-random byte using the permutation
    /// table. Wraps on 256×256 grid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Hash(int x, int y)
    {
        return _perm[(_perm[x & 255] + y) & 255];
    }

    /// <summary>
    /// Returns raw noise in [0, 65535] at integer grid point (x, y) with
    /// the given frequency (grid spacing). Points between grid cells are
    /// bilinearly interpolated using integer math.
    ///
    /// freq = grid cell size in tiles. e.g., freq=8 means one noise cell
    /// spans 8 tiles. Larger freq = smoother terrain.
    /// </summary>
    public int Sample(int tileX, int tileY, int freq)
    {
        if (freq <= 0) freq = 1;

        // Grid cell coordinates.
        int gx = tileX / freq;
        int gy = tileY / freq;

        // Fractional position within cell, scaled to [0, 256] for integer lerp.
        int fx = (tileX % freq) * 256 / freq;
        int fy = (tileY % freq) * 256 / freq;

        // Handle negative coordinates (C# modulo can be negative).
        if (fx < 0) { fx += 256; gx--; }
        if (fy < 0) { fy += 256; gy--; }

        // Four corner hashes, scaled to 16-bit range.
        int v00 = Hash(gx,     gy    ) * 257; // 0–255 → 0–65535
        int v10 = Hash(gx + 1, gy    ) * 257;
        int v01 = Hash(gx,     gy + 1) * 257;
        int v11 = Hash(gx + 1, gy + 1) * 257;

        // Bilinear interpolation with smoothstep on fractions.
        // Smoothstep: t' = 3t² - 2t³ (in [0,256] domain).
        int sx = SmoothStep256(fx);
        int sy = SmoothStep256(fy);

        int top    = Lerp256(v00, v10, sx);
        int bottom = Lerp256(v01, v11, sx);
        return Lerp256(top, bottom, sy);
    }

    /// <summary>
    /// Multi-octave noise. Returns value in [0, 65535].
    /// </summary>
    public int OctaveNoise(int tileX, int tileY, int baseFreq, int octaves)
    {
        int value = 0;
        int amp = 65536; // Starting amplitude (will be divided by total)
        int totalAmp = 0;
        int freq = baseFreq;

        for (int i = 0; i < octaves; i++)
        {
            value += Sample(tileX, tileY, freq) * amp / 65536;
            totalAmp += amp;
            freq = System.Math.Max(1, freq / 2);
            amp /= 2;
        }

        // Normalize to [0, 65535].
        if (totalAmp > 0)
            value = value * 65536 / totalAmp;
        return System.Math.Clamp(value, 0, 65535);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Lerp256(int a, int b, int t)
    {
        // t in [0, 256]. Returns a + (b-a) * t / 256.
        return a + (b - a) * t / 256;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SmoothStep256(int t)
    {
        // Hermite smoothstep: 3t² - 2t³ for t in [0, 256].
        // Scale: t/256 → 3*(t/256)² - 2*(t/256)³ → multiply back by 256.
        // = (3 * t * t - 2 * t * t * t / 256) / 256
        // Using long to avoid overflow (t*t*t can exceed int32 for t=256).
        long t2 = (long)t * t;     // max 65536
        long t3 = t2 * t;          // max 16M
        return (int)((3 * t2 * 256 - 2 * t3) / (256 * 256));
    }
}
