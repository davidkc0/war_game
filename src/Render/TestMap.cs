namespace WarGame.Render;

using WarGame.Sim.State;

// Hand-authored 30×20 sandbox for Phase 1. Procgen lands in Phase 2 — this
// file is a one-shot scaffold so the human can immediately play the game
// rather than waiting for the generator. The intent is "small enough to
// learn the systems, big enough to feel real":
//
//   - Two capitals at opposite corners.
//   - One regular city per player, mid-flank.
//   - Forest belt across the middle creates a natural skirmish line.
//   - Mountains block heavies on the flanks (forces some routing).
//   - A road threads through the center for fast deployment.
//   - A small lake on one side rules out one approach.
public static class TestMap
{
    public const int Width = 30;
    public const int Height = 20;

    public static GameState Build()
    {
        var s = GameState.Initial(seed: 12345);

        var b = new MapState.Builder(Width, Height);

        // Forest belt + a road bridge across it.
        for (int x = 0; x < Width; x++)
            b.Set(x, 10, TileType.Forest);
        b.Set(15, 10, TileType.Road);

        // Roads from each capital toward the central junction.
        for (int x = 2; x <= 14; x++) b.Set(x, 9,  TileType.Road);
        for (int x = 15; x <= 27; x++) b.Set(x, 11, TileType.Road);

        // Mountains: a flanking ridge along each side.
        for (int y = 4; y <= 8; y++)  b.Set(2,  y, TileType.Mountain);
        for (int y = 12; y <= 16; y++) b.Set(27, y, TileType.Mountain);

        // Lake on the southwest, blocks one approach.
        b.FillRect(5, 14, 9, 17, TileType.Water);

        // City and capital tiles.
        b.Set(2, 2, TileType.Capital);
        b.Set(27, 17, TileType.Capital);
        b.Set(8, 6, TileType.City);
        b.Set(21, 13, TileType.City);

        s.Map = b.Build();
        s.TileOwner = new byte[Width * Height];

        // Cities. Owned by their respective players.
        s.Cities.Add(City.Create(0, 2,  2,  PlayerId.Player1, isCapital: true));
        s.Cities.Add(City.Create(1, 8,  6,  PlayerId.Player1, isCapital: false));
        s.Cities.Add(City.Create(2, 27, 17, PlayerId.Player2, isCapital: true));
        s.Cities.Add(City.Create(3, 21, 13, PlayerId.Player2, isCapital: false));

        // Each side starts with one light unit on its capital. The rest is
        // earned via production once the player issues a build order.
        s.Units.Add(Unit.Create(0, PlayerId.Player1, UnitType.Light, 2, 2));
        s.Units.Add(Unit.Create(1, PlayerId.Player2, UnitType.Light, 27, 17));

        return s;
    }
}
