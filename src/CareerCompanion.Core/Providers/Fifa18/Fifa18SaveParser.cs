using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed class Fifa18SaveFormatException(string message) : Exception(message);

public sealed class Fifa18SaveData
{
    private readonly Dictionary<string, List<IReadOnlyDictionary<string, object>>> _tables = new(StringComparer.Ordinal);
    internal void Add(string table, IEnumerable<IReadOnlyDictionary<string, object>> records)
    {
        if (!_tables.TryGetValue(table, out var target)) _tables[table] = target = [];
        target.AddRange(records);
    }
    public IReadOnlyList<IReadOnlyDictionary<string, object>> Table(string name)
        => _tables.TryGetValue(name, out var rows) ? rows : [];
    public IReadOnlyCollection<string> TableNames => _tables.Keys;
}

public sealed partial class Fifa18SaveParser
{
    private static readonly byte[] Header = [0x44,0x42,0x00,0x08,0x00,0x00,0x00,0x00];

    public async Task<(Fifa18SaveData Data, string Fingerprint)> ParseFileAsync(string path, CancellationToken ct = default)
    {
        var bytes = await ReadStableAsync(path, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        return (Parse(bytes), fingerprint);
    }

    public Fifa18SaveData Parse(ReadOnlySpan<byte> save)
    {
        var result = new Fifa18SaveData();
        var offset = Find(save, Header, 0);
        if (offset < 0) throw new Fifa18SaveFormatException("The file does not contain a FIFA career database header.");
        var databases = 0;
        while (offset >= 0)
        {
            if (offset + 12 > save.Length) throw new Fifa18SaveFormatException("The FIFA database header is truncated.");
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(save.Slice(offset + 8, 4)));
            if (size < 32 || offset + size > save.Length) throw new Fifa18SaveFormatException("An embedded FIFA database has an invalid size.");
            ParseDatabase(save.Slice(offset, size), result);
            databases++;
            offset = Find(save, Header, offset + size);
        }
        if (databases == 0) throw new Fifa18SaveFormatException("No FIFA databases were parsed.");
        return result;
    }

    private static void ParseDatabase(ReadOnlySpan<byte> db, Fifa18SaveData result)
    {
        var p = 8;
        var declared = ReadU32(db, ref p);
        if (declared != db.Length) throw new Fifa18SaveFormatException("Embedded FIFA database size mismatch.");
        p += 4;
        var tableCount = checked((int)ReadU32(db, ref p));
        p += 4;
        if (tableCount < 0 || tableCount > 2000) throw new Fifa18SaveFormatException("Invalid FIFA table count.");
        var tables = new (string ShortName, int Offset)[tableCount];
        for (var i = 0; i < tableCount; i++)
        {
            Ensure(db, p, 8);
            tables[i] = (Encoding.ASCII.GetString(db.Slice(p,4)), checked((int)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(p+4,4))));
            p += 8;
        }
        p += 4;
        var tablesStart = p;
        foreach (var table in tables)
        {
            if (!Fifa18Metadata.Tables.TryGetValue(table.ShortName, out var meta)) continue;
            var tp = checked(tablesStart + table.Offset);
            Ensure(db, tp, 36);
            tp += 4;
            var recordSize = checked((int)ReadU32(db, ref tp));
            tp += 10;
            var recordCount = ReadU16(db, ref tp);
            tp += 4;
            var fieldCount = db[tp++];
            tp += 11;
            if (recordSize <= 0 || recordSize > 65536 || recordCount > 60000) throw new Fifa18SaveFormatException($"Invalid {meta.Name} table dimensions.");
            var fields = new List<BinaryField>(fieldCount);
            for (var i = 0; i < fieldCount; i++)
            {
                Ensure(db, tp, 16);
                var type = ReadU32(db, ref tp);
                var bitOffset = checked((int)ReadU32(db, ref tp));
                var shortName = Encoding.ASCII.GetString(db.Slice(tp,4)); tp += 4;
                var bitDepth = checked((int)ReadU32(db, ref tp));
                if (meta.Fields.TryGetValue(shortName, out var fieldMeta)) fields.Add(new(type, bitOffset, bitDepth, fieldMeta));
            }
            var recordsStart = tp;
            var records = new List<IReadOnlyDictionary<string, object>>(recordCount);
            for (var row = 0; row < recordCount; row++)
            {
                var start = checked(recordsStart + row * recordSize);
                Ensure(db, start, recordSize);
                var record = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var field in fields)
                {
                    object value = field.Type switch
                    {
                        0 => ReadString(db, start + (field.BitOffset >> 3), field.BitDepth >> 3),
                        3 => checked((long)ReadBits(db, start, field.BitOffset, field.BitDepth) + field.Meta.RangeLow),
                        4 => (long)BinaryPrimitives.ReadUInt32LittleEndian(db.Slice(start + (field.BitOffset >> 3),4)),
                        _ => string.Empty
                    };
                    record[field.Meta.Name] = value;
                }
                records.Add(record);
            }
            result.Add(meta.Name, records);
        }
    }

    private static async Task<byte[]> ReadStableAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("FIFA career save not found.", path);
        long previousLength = -1;
        DateTime previousWrite = default;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length == previousLength && info.LastWriteTimeUtc == previousWrite)
            {
                try
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                        1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var bytes = new byte[info.Length];
                    await stream.ReadExactlyAsync(bytes, ct);
                    return bytes;
                }
                catch (IOException) when (attempt < 7) { }
            }
            previousLength = info.Length;
            previousWrite = info.LastWriteTimeUtc;
            await Task.Delay(350, ct);
        }
        throw new IOException("FIFA is still writing the career save. Try again in a moment.");
    }

    private static ulong ReadBits(ReadOnlySpan<byte> data, int recordStart, int bitOffset, int depth)
    {
        if (depth is < 1 or > 63) throw new Fifa18SaveFormatException($"Unsupported integer depth {depth}.");
        ulong value = 0;
        for (var bit = 0; bit < depth; bit++)
        {
            var absolute = bitOffset + bit;
            var index = recordStart + (absolute >> 3);
            Ensure(data,index,1);
            if ((data[index] & (1 << (absolute & 7))) != 0) value |= 1UL << bit;
        }
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int start, int maxLength)
    {
        Ensure(data,start,maxLength);
        var slice = data.Slice(start,maxLength);
        var zero = slice.IndexOf((byte)0);
        if (zero >= 0) slice = slice[..zero];
        return Encoding.UTF8.GetString(slice).Replace("\r","").Replace("\t","").Trim();
    }

    private static int Find(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern, int start)
    {
        var relative = data[start..].IndexOf(pattern);
        return relative < 0 ? -1 : start + relative;
    }
    private static uint ReadU32(ReadOnlySpan<byte> data, ref int p){Ensure(data,p,4);var x=BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p,4));p+=4;return x;}
    private static ushort ReadU16(ReadOnlySpan<byte> data, ref int p){Ensure(data,p,2);var x=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p,2));p+=2;return x;}
    private static void Ensure(ReadOnlySpan<byte> data,int start,int length){if(start<0||length<0||start>data.Length-length)throw new Fifa18SaveFormatException("Unexpected end of FIFA database.");}
    private sealed record BinaryField(uint Type,int BitOffset,int BitDepth,FieldMeta Meta);
}
