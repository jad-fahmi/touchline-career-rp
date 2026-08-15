using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CareerCompanion.Core.Providers.Fifa18;

/// <summary>A match as the running game holds it: who played, on what date, and the score.</summary>
public sealed record Fifa18LiveMatch(int ClubTeamId, int OpponentTeamId, string Date, int TeamScore, int OpponentScore);

public interface IFifa18LiveMatchSource
{
    /// <summary>The club's match on that date, or null when the game is closed or the record is not there.</summary>
    Fifa18LiveMatch? FindMatch(int clubTeamId, string date);
}

/// <summary>
/// Reads a played match out of the running game. FIFA never writes the opponent of a match to the save, and
/// the news article that names it is discarded within a few matchdays, so for a match the save cannot prove
/// this is the only remaining source of truth.
///
/// It searches by shape rather than by address: a match record places the club id, the opponent id, the
/// career date, and the two goal tallies next to each other, and nothing else in memory looks like that. No
/// pointer paths or module offsets are involved, so it keeps working across launches despite ASLR and needs
/// no maintenance for a game that will not be patched again.
///
/// Strictly read-only. The process is opened for reading alone, nothing is written to the game or the save,
/// and every failure (game closed, access denied, unfamiliar layout) returns null so the caller carries on.
/// </summary>
public sealed class Fifa18LiveMatchReader(TimeSpan? budget = null) : IFifa18LiveMatchSource
{
    private readonly TimeSpan _budget = budget ?? TimeSpan.FromSeconds(30);

    /// <summary>Goal tallies are small; a field pair outside this range is not a scoreline.</summary>
    private const int MaxGoals = 15;
    private const int MaxTeamId = 200_000;

    public Fifa18LiveMatch? FindMatch(int clubTeamId, string date)
    {
        if (!OperatingSystem.IsWindows() || clubTeamId <= 0) return null;
        if (!DateTime.TryParse(date, out var parsed)) return null;
        var stamp = parsed.Year * 10000 + parsed.Month * 100 + parsed.Day;
        try { return Search(clubTeamId, stamp, date); }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
    }

    private Fifa18LiveMatch? Search(int clubTeamId, int stamp, string date)
    {
        using var game = Process.GetProcesses()
            .Where(x => x.ProcessName.Contains("fifa", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => { try { return x.WorkingSet64; } catch { return 0L; } })
            .FirstOrDefault();
        if (game is null) return null;
        var handle = Native.OpenProcess(Native.QueryInformation | Native.VmRead, false, game.Id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            // Duplicate copies of the same record are normal, so candidates are counted and the reading that
            // the game agrees with most often wins. A single stray match never decides anything.
            var votes = new Dictionary<(int Opponent, int Team, int Against), int>();
            var buffer = new byte[4 * 1024 * 1024];
            var clock = Stopwatch.StartNew();
            ulong address = 0x10000;
            while (address < 0x7FFFFFFFFFFF && clock.Elapsed < _budget)
            {
                if (Native.VirtualQueryEx(handle, (IntPtr)address, out var info, (uint)Marshal.SizeOf<Native.RegionInfo>()) == 0) break;
                var size = info.RegionSize;
                if (size == 0) break;
                if (IsReadableData(info) && size <= 512UL * 1024 * 1024)
                    for (ulong offset = 0; offset < size && clock.Elapsed < _budget;)
                    {
                        var take = (int)Math.Min((ulong)buffer.Length, size - offset);
                        if (Native.ReadProcessMemory(handle, (IntPtr)(address + offset), buffer, take, out var read) && read > 0)
                            Collect(buffer, read, clubTeamId, stamp, votes);
                        offset += (ulong)take;
                    }
                address += size;
            }
            if (votes.Count == 0) return null;
            var best = votes.OrderByDescending(x => x.Value).First().Key;
            return new(clubTeamId, best.Opponent, date, best.Team, best.Against);
        }
        finally { Native.CloseHandle(handle); }
    }

    /// <summary>Finds every record in one buffer that reads as this club's match on this date.</summary>
    private static void Collect(byte[] buffer, int read, int clubTeamId, int stamp,
        Dictionary<(int, int, int), int> votes)
    {
        for (var i = 8; i + 12 <= read; i += 4)
        {
            if (BitConverter.ToInt32(buffer, i) != stamp) continue;
            var first = BitConverter.ToInt32(buffer, i - 8);
            var second = BitConverter.ToInt32(buffer, i - 4);
            var left = BitConverter.ToInt32(buffer, i + 4);
            var right = BitConverter.ToInt32(buffer, i + 8);
            if (left is < 0 or > MaxGoals || right is < 0 or > MaxGoals) continue;
            if (first <= 0 || second <= 0 || first > MaxTeamId || second > MaxTeamId || first == second) continue;
            // The club sits on one side and the opponent on the other; the goal tallies follow the same order.
            (int Opponent, int Team, int Against) key;
            if (first == clubTeamId) key = (second, left, right);
            else if (second == clubTeamId) key = (first, right, left);
            else continue;
            votes[key] = votes.GetValueOrDefault(key) + 1;
        }
    }

    private static bool IsReadableData(Native.RegionInfo info)
    {
        const uint committed = 0x1000, guard = 0x100, noAccess = 0x01, readable = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
        return info.State == committed && (info.Protect & guard) == 0 && (info.Protect & noAccess) == 0
            && (info.Protect & readable) != 0;
    }

    /// <summary>Read-only process access. Nothing here can write to the game or to a save.</summary>
    private static class Native
    {
        public const int QueryInformation = 0x0400, VmRead = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct RegionInfo
        {
            public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect; public uint Alignment1;
            public ulong RegionSize; public uint State; public uint Protect; public uint Type; public uint Alignment2;
        }

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern int VirtualQueryEx(IntPtr process, IntPtr address, out RegionInfo info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
    }
}
