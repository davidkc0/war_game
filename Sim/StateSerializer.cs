using System.Buffers.Binary;
using System.IO;
using WarGame.Sim.State;

namespace WarGame.Sim;

// Canonical deterministic serializer for GameState. Every test, every
// replay, every desync detector hashes through this. If the byte layout
// here changes, bump GameState.CurrentVersion in the same commit.
//
// Why hand-rolled rather than BinaryWriter: BinaryWriter's int/long methods
// are little-endian on every platform .NET 8 supports, but other types
// (decimal, double) are not. We avoid those entirely; integer primitives
// only, written via BinaryPrimitives.WriteXxxLittleEndian for explicit
// platform-independence.
public static class StateSerializer
{
    public static void Write(GameState s, Stream stream)
    {
        WriteI32(stream, s.Version);
        WriteI32(stream, s.Tick);
        WriteU64(stream, s.Rng.State);

        // Map.
        WriteI32(stream, s.Map.Width);
        WriteI32(stream, s.Map.Height);
        foreach (byte b in s.Map.RawTiles) stream.WriteByte(b);

        // Units (already in deterministic List order — Id == index).
        WriteI32(stream, s.Units.Count);
        foreach (Unit u in s.Units)
        {
            WriteI32(stream, u.Id);
            stream.WriteByte((byte)u.Owner);
            stream.WriteByte((byte)u.Type);
            WriteI32(stream, u.TileX);
            WriteI32(stream, u.TileY);
            WriteI64(stream, u.Hp.Raw);
            WriteI64(stream, u.ProgressRaw);

            int pathLen = u.Path is null ? 0 : u.Path.Count;
            WriteI32(stream, pathLen);
            if (u.Path is not null)
                foreach (int p in u.Path) WriteI32(stream, p);
        }

        // Cities.
        WriteI32(stream, s.Cities.Count);
        foreach (City c in s.Cities)
        {
            WriteI32(stream, c.Id);
            WriteI32(stream, c.TileX);
            WriteI32(stream, c.TileY);
            stream.WriteByte((byte)c.Owner);
            stream.WriteByte((byte)c.OriginalOwner);
            stream.WriteByte((byte)(c.IsCapital ? 1 : 0));
            WriteI32(stream, c.SupplyCapacity);
            WriteI64(stream, c.ProductionProgress.Raw);
            stream.WriteByte(c.ProductionOrder);
            WriteI32(stream, c.CaptureHp);
        }

        // Players (fixed length array — but write the length anyway so the
        // serializer survives a future schema where the array grows).
        WriteI32(stream, s.Players.Length);
        foreach (Player p in s.Players)
        {
            stream.WriteByte((byte)p.Id);
            WriteI64(stream, p.Eco.Raw);
            stream.WriteByte(p.DoctrineId);
        }

        // TileOwner (length first; 0 if null).
        int ownerLen = s.TileOwner is null ? 0 : s.TileOwner.Length;
        WriteI32(stream, ownerLen);
        if (s.TileOwner is not null)
            foreach (byte b in s.TileOwner) stream.WriteByte(b);

        // Supply arrays.
        int supplyLen = s.TileSupplyOwner is null ? 0 : s.TileSupplyOwner.Length;
        WriteI32(stream, supplyLen);
        if (s.TileSupplyOwner is not null)
            foreach (byte b in s.TileSupplyOwner) stream.WriteByte(b);

        int roadSupplyLen = s.TileRoadSupplyOwner is null ? 0 : s.TileRoadSupplyOwner.Length;
        WriteI32(stream, roadSupplyLen);
        if (s.TileRoadSupplyOwner is not null)
            foreach (byte b in s.TileRoadSupplyOwner) stream.WriteByte(b);

        int unitSupplyLen = s.UnitSupplyStatus is null ? 0 : s.UnitSupplyStatus.Length;
        WriteI32(stream, unitSupplyLen);
        if (s.UnitSupplyStatus is not null)
            foreach (byte b in s.UnitSupplyStatus) stream.WriteByte(b);

        // Win state.
        stream.WriteByte((byte)s.Winner);
        int holdLen = s.CityHoldTicks is null ? 0 : s.CityHoldTicks.Length;
        WriteI32(stream, holdLen);
        if (s.CityHoldTicks is not null)
            foreach (int t in s.CityHoldTicks) WriteI32(stream, t);

        // Pending forts.
        int fortLen = s.PendingForts is null ? 0 : s.PendingForts.Count;
        WriteI32(stream, fortLen);
        if (s.PendingForts is not null)
        {
            foreach (var f in s.PendingForts)
            {
                WriteI32(stream, f.TileX);
                WriteI32(stream, f.TileY);
                stream.WriteByte((byte)f.Owner);
                WriteI32(stream, f.TicksRemaining);
            }
        }

        // Pending road/bridge construction.
        int roadLen = s.PendingRoads is null ? 0 : s.PendingRoads.Count;
        WriteI32(stream, roadLen);
        if (s.PendingRoads is not null)
        {
            foreach (var r in s.PendingRoads)
            {
                WriteI32(stream, r.UnitId);
                stream.WriteByte((byte)r.Owner);
                WriteI32(stream, r.TargetX);
                WriteI32(stream, r.TargetY);
                WriteI32(stream, r.CurrentPathIndex);
                WriteI32(stream, r.TicksRemainingOnTile);
                int pathLen = r.Path is null ? 0 : r.Path.Count;
                WriteI32(stream, pathLen);
                if (r.Path is not null)
                    foreach (int p in r.Path) WriteI32(stream, p);
            }
        }
    }

    public static byte[] ToBytes(GameState s)
    {
        using var ms = new MemoryStream();
        Write(s, ms);
        return ms.ToArray();
    }

    private static void WriteI32(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteI64(Stream s, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, v);
        s.Write(b);
    }

    private static void WriteU64(Stream s, ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        s.Write(b);
    }
}
