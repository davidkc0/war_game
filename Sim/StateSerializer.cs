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
            stream.WriteByte((byte)(c.IsCapital ? 1 : 0));
            WriteI32(stream, c.SupplyCapacity);
            WriteI64(stream, c.ProductionProgress.Raw);
            stream.WriteByte(c.ProductionOrder);
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
