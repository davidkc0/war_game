using System;

namespace WarGame.Sim.State;

// Static terrain layout. Constructed once at game start and never mutated.
//
// Storage is a flat row-major byte array, indexed (y * Width + x). Single
// allocation, contiguous memory, trivially serializable. Determinism: the
// hash of GameState includes Width, Height, and the raw byte buffer.
//
// Why a struct: MapState is logically a value, but its `_tiles` field is a
// reference-type array. Two MapState instances that share the same array
// alias each other's tiles. Phase 1 treats the map as immutable after init
// (terrain never changes), so aliasing is fine and we avoid an allocation
// per copy. If terrain ever needs to mutate during sim (it shouldn't), this
// design needs revisiting.
public readonly struct MapState
{
    public readonly int Width;
    public readonly int Height;
    private readonly byte[] _tiles;

    public MapState(int width, int height, byte[] tiles)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("map dimensions must be positive");
        if (tiles.Length != width * height)
            throw new ArgumentException(
                $"tile buffer length {tiles.Length} does not match {width}x{height}");
        Width = width;
        Height = height;
        _tiles = tiles;
    }

    public ReadOnlySpan<byte> RawTiles => _tiles;
    public int TileCount => _tiles.Length;

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public TileType GetTile(int x, int y)
    {
        if (!InBounds(x, y))
            throw new ArgumentOutOfRangeException($"({x},{y}) out of {Width}x{Height} map");
        return (TileType)_tiles[y * Width + x];
    }

    public TileType GetTileUnchecked(int x, int y) => (TileType)_tiles[y * Width + x];

    // Builder used at game start (and by Phase 2's procgen). Returns a
    // MapState whose backing array is fresh — modifying the builder after
    // Build() is called does not affect the returned MapState.
    public sealed class Builder
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _tiles;

        public Builder(int width, int height, TileType fill = TileType.Plains)
        {
            _width = width;
            _height = height;
            _tiles = new byte[width * height];
            if (fill != TileType.Plains)
                Array.Fill(_tiles, (byte)fill);
        }

        public int Width => _width;
        public int Height => _height;

        public Builder Set(int x, int y, TileType t)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
                throw new ArgumentOutOfRangeException($"({x},{y}) out of {_width}x{_height}");
            _tiles[y * _width + x] = (byte)t;
            return this;
        }

        public Builder FillRect(int x0, int y0, int x1, int y1, TileType t)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Set(x, y, t);
            return this;
        }

        public MapState Build()
        {
            // Defensive copy: callers may keep mutating the builder.
            byte[] copy = new byte[_tiles.Length];
            Array.Copy(_tiles, copy, _tiles.Length);
            return new MapState(_width, _height, copy);
        }
    }
}
