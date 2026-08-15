using System.Buffers.Binary;
using System.Text;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed record Fifa18FieldInfo(string ShortName, uint Type, int BitOffset, int BitDepth);
public sealed record Fifa18TableInfo(string ShortName, int RecordCount, int RecordSize, IReadOnlyList<Fifa18FieldInfo> Fields);

/// <summary>
/// Read-only structural inspection of a FIFA 18 career save. Used by probe tooling and parser tests to
/// discover tables and fields that are not yet in <see cref="Fifa18Metadata"/>. It never writes to a save.
/// </summary>
public static class Fifa18SaveInspector
{
    private static readonly byte[] Header = [0x44, 0x42, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00];

    public static IReadOnlyList<Fifa18TableInfo> Describe(ReadOnlySpan<byte> save)
    {
        var tables = new List<Fifa18TableInfo>();
        Walk(save, (shortName, recordCount, recordSize, fields, _, _) =>
        {
            tables.Add(new(shortName, recordCount, recordSize, fields));
            return false;
        });
        return tables;
    }

    /// <summary>Reads every field of a table by its four-character short name, keyed by field short name.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> ReadTable(ReadOnlySpan<byte> save, string shortName)
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        var db = save;
        Walk(save, (name, recordCount, recordSize, fields, recordsStart, database) =>
        {
            if (!string.Equals(name, shortName, StringComparison.Ordinal)) return false;
            for (var row = 0; row < recordCount; row++)
            {
                var start = recordsStart + row * recordSize;
                if (start < 0 || start + recordSize > database.Length) break;
                var record = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var field in fields)
                    record[field.ShortName] = ReadValue(database, start, field);
                rows.Add(record);
            }
            return true;
        });
        return rows;
    }

    private static object ReadValue(ReadOnlySpan<byte> db, int start, Fifa18FieldInfo field)
    {
        try
        {
            return field.Type switch
            {
                0 => ReadString(db, start + (field.BitOffset >> 3), field.BitDepth >> 3),
                3 => (long)ReadBits(db, start, field.BitOffset, field.BitDepth),
                4 => (long)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(start + (field.BitOffset >> 3), 4)),
                _ => string.Empty
            };
        }
        catch (ArgumentOutOfRangeException) { return string.Empty; }
    }

    private delegate bool TableVisitor(string shortName, int recordCount, int recordSize,
        IReadOnlyList<Fifa18FieldInfo> fields, int recordsStart, ReadOnlySpan<byte> database);

    private static void Walk(ReadOnlySpan<byte> save, TableVisitor visit)
    {
        var offset = IndexOf(save, Header, 0);
        while (offset >= 0)
        {
            if (offset + 12 > save.Length) return;
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(save.Slice(offset + 8, 4));
            if (size < 32 || offset + size > save.Length) return;
            if (WalkDatabase(save.Slice(offset, size), visit)) return;
            offset = IndexOf(save, Header, offset + size);
        }
    }

    private static bool WalkDatabase(ReadOnlySpan<byte> db, TableVisitor visit)
    {
        var p = 8;
        p += 4; // declared size
        p += 4;
        var tableCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(p, 4)); p += 4;
        p += 4;
        if (tableCount is < 0 or > 2000) return false;
        var directory = new (string ShortName, int Offset)[tableCount];
        for (var i = 0; i < tableCount; i++)
        {
            if (p + 8 > db.Length) return false;
            directory[i] = (Encoding.ASCII.GetString(db.Slice(p, 4)), (int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(p + 4, 4)));
            p += 8;
        }
        p += 4;
        var tablesStart = p;
        foreach (var table in directory)
        {
            var tp = tablesStart + table.Offset;
            if (tp < 0 || tp + 36 > db.Length) continue;
            tp += 4;
            var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(tp, 4)); tp += 4;
            tp += 10;
            var recordCount = BinaryPrimitives.ReadUInt16LittleEndian(db.Slice(tp, 2)); tp += 2;
            tp += 4;
            var fieldCount = db[tp++];
            tp += 11;
            if (recordSize is <= 0 or > 65536 || recordCount > 60000) continue;
            var fields = new List<Fifa18FieldInfo>(fieldCount);
            var valid = true;
            for (var i = 0; i < fieldCount; i++)
            {
                if (tp + 16 > db.Length) { valid = false; break; }
                var type = BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(tp, 4)); tp += 4;
                var bitOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(tp, 4)); tp += 4;
                var name = Encoding.ASCII.GetString(db.Slice(tp, 4)); tp += 4;
                var bitDepth = (int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(tp, 4)); tp += 4;
                fields.Add(new(name, type, bitOffset, bitDepth));
            }
            if (!valid) continue;
            if (visit(table.ShortName, recordCount, recordSize, fields, tp, db)) return true;
        }
        return false;
    }

    private static ulong ReadBits(ReadOnlySpan<byte> data, int recordStart, int bitOffset, int depth)
    {
        if (depth is < 1 or > 63) return 0;
        ulong value = 0;
        for (var bit = 0; bit < depth; bit++)
        {
            var absolute = bitOffset + bit;
            var index = recordStart + (absolute >> 3);
            if (index < 0 || index >= data.Length) return value;
            if ((data[index] & (1 << (absolute & 7))) != 0) value |= 1UL << bit;
        }
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int start, int maxLength)
    {
        if (start < 0 || maxLength < 0 || start > data.Length - maxLength) return "";
        var slice = data.Slice(start, maxLength);
        var zero = slice.IndexOf((byte)0);
        if (zero >= 0) slice = slice[..zero];
        return Encoding.UTF8.GetString(slice).Replace("\r", "").Replace("\t", "").Trim();
    }

    private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern, int start)
    {
        if (start >= data.Length) return -1;
        var relative = data[start..].IndexOf(pattern);
        return relative < 0 ? -1 : start + relative;
    }
}
