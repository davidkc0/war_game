using System;
using WarGame.Sim.Generation;
using WarGame.Sim.State;

var result = MapGenerator.GenerateOnce(42);
var map = result.Map;
var cities = result.Cities;

char[] chars = new char[map.Width * map.Height];
for (int y = 0; y < map.Height; y++) {
    for (int x = 0; x < map.Width; x++) {
        TileType t = map.GetTileUnchecked(x, y);
        char c = '.';
        if (t == TileType.Water) c = '~';
        else if (t == TileType.Mountain) c = '^';
        else if (t == TileType.Forest) c = 't';
        else if (t == TileType.Road) c = '#';
        chars[y * map.Width + x] = c;
    }
}

foreach (var city in cities) {
    char c = city.IsCapital ? 'C' : 'c';
    if (city.Owner == PlayerId.Player2) c = city.IsCapital ? 'K' : 'k';
    chars[city.TileY * map.Width + city.TileX] = c;
}

for (int y = 0; y < map.Height; y++) {
    Console.WriteLine(new string(chars, y * map.Width, map.Width));
}
