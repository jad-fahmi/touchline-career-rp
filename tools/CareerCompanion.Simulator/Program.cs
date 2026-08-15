using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using CareerCompanion.Core.Providers.Fifa18;
using System.Runtime.InteropServices;

// Answers "what will the next launch do?" against a copy of a real database, so the question can be
// settled before the app touches the player's own data.
if(args.Length>0&&args[0]=="--preflight-fifa18")
{
    var original=Path.GetFullPath(args[1]);
    var copy=Path.Combine(Path.GetTempPath(),$"touchline-preflight-{DateTime.Now:HHmmss}.db");
    File.Copy(original,copy,true);
    foreach(var suffix in new[]{"-wal","-shm"})if(File.Exists(original+suffix))File.Copy(original+suffix,copy+suffix,true);
    var database=new Database(copy);database.Migrate();
    var savePath=args.Length>2?Path.GetFullPath(args[2]):new Fifa18SaveLocator().FindLatestCareer();
    if(savePath is null){Console.WriteLine("No FIFA 18 career save found.");return;}
    var (data,fingerprint)=await new Fifa18SaveParser().ParseFileAsync(savePath);
    var routed=database.FindCareerIdByFifaPlayerId(new Fifa18CareerNormalizer().Normalize(data,savePath,fingerprint).PlayerId);
    Console.WriteLine($"save: {Path.GetFileName(savePath)}");
    Console.WriteLine($"routes to career: {(routed is null?"none (a new career would be offered)":routed.ToString())}");
    if(routed is null)return;
    var linked=database.GetCareer(routed.Value);
    Console.WriteLine($"  career {linked.Id}: {linked.PlayerName} @ {linked.Club}, matches before={database.GetMatches(linked.Id,500).Count}");
    Fifa18SyncState? prior=null;
    if(database.GetLatestProviderPayload(linked.Id,FifaSyncService.ProviderName) is { } payload)
        prior=System.Text.Json.JsonSerializer.Deserialize<Fifa18SyncState>(payload);
    var parsed=new Fifa18CareerNormalizer().Normalize(data,savePath,fingerprint,prior,database.GetCachedProviderNews(linked.Id,FifaSyncService.ProviderName));
    var outcome=new FifaSyncService(database).Apply(linked.Id,parsed);
    Console.WriteLine($"  {outcome.Message}");
    foreach(var m in outcome.Imported)Console.WriteLine($"  imported: {FifaSyncService.Describe(m)} | goals={m.Goals} assists={m.Assists}");
    foreach(var m in outcome.NeedsReview)Console.WriteLine($"  review:   {FifaSyncService.Describe(m)} | goals={m.Goals} assists={m.Assists}");
    Console.WriteLine($"  matches after={database.GetMatches(linked.Id,500).Count}");
    return;
}

// Read-only look at the running game. The fixture list is generated at load and never written to the save,
// so the only place it exists is the live process. Facts already known from the save (career date, player
// id, club id) are used as anchors: whatever holds those values is a career structure, and no pointer paths
// or module offsets are needed, which keeps this independent of where Windows happens to load the game.
if(args.Length>0&&args[0]=="--probe-fifa18-memory")
{
    var anchors=args.Skip(1).Select(x=>int.TryParse(x,out var v)?v:0).Where(x=>x!=0).Distinct().ToArray();
    if(anchors.Length==0)anchors=[20170828,138449,47];
    var game=System.Diagnostics.Process.GetProcesses()
        .Where(p=>p.ProcessName.Contains("fifa",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p=>{try{return p.WorkingSet64;}catch{return 0L;}}).FirstOrDefault();
    if(game is null){Console.WriteLine("No FIFA process found. Start the game and load the career first.");return;}
    Console.WriteLine($"process: {game.ProcessName} (pid {game.Id}), working set {game.WorkingSet64/1024/1024} MB");
    Console.WriteLine($"anchors: {string.Join(", ",anchors)}");
    var handle=MemoryProbe.OpenProcess(MemoryProbe.QueryInformation|MemoryProbe.VmRead,false,game.Id);
    if(handle==IntPtr.Zero)
    {
        Console.WriteLine($"OpenProcess failed (win32 error {Marshal.GetLastWin32Error()}). Try this terminal as administrator.");
        return;
    }
    var hits=anchors.ToDictionary(x=>x,_=>new List<ulong>());
    var buffer=new byte[4*1024*1024];
    var clock=System.Diagnostics.Stopwatch.StartNew();
    ulong address=0x10000,scanned=0,regions=0;
    while(address<0x7FFFFFFFFFFF)
    {
        if(MemoryProbe.VirtualQueryEx(handle,(IntPtr)address,out var info,(uint)Marshal.SizeOf<MemoryProbe.RegionInfo>())==0)break;
        var size=info.RegionSize;
        if(size==0)break;
        const uint committed=0x1000,guard=0x100,noAccess=0x01,readable=0x02|0x04|0x08|0x20|0x40|0x80;
        if(info.State==committed&&(info.Protect&guard)==0&&(info.Protect&noAccess)==0&&(info.Protect&readable)!=0&&size<=512UL*1024*1024)
        {
            regions++;
            for(ulong offset=0;offset<size;)
            {
                var take=(int)Math.Min((ulong)buffer.Length,size-offset);
                if(MemoryProbe.ReadProcessMemory(handle,(IntPtr)(address+offset),buffer,take,out var read)&&read>0)
                {
                    scanned+=(ulong)read;
                    for(var i=0;i+4<=read;i+=4)
                    {
                        var value=BitConverter.ToInt32(buffer,i);
                        if(value!=0&&hits.TryGetValue(value,out var found)&&found.Count<200)found.Add(address+offset+(ulong)i);
                    }
                }
                offset+=(ulong)take;
            }
        }
        address+=size;
    }
    Console.WriteLine($"scanned {scanned/1024/1024} MB across {regions} regions in {clock.ElapsedMilliseconds} ms (4-byte aligned)");
    foreach(var anchor in anchors)
        Console.WriteLine($"  {anchor,-10} {hits[anchor].Count,4} hits{(hits[anchor].Count>0?"  first at 0x"+hits[anchor][0].ToString("X"):"")}");

    // What sits around an anchor is the interesting part: a fixture record would place a date beside two
    // team ids, so the first hits are dumped for inspection rather than guessed at.
    var window=new byte[192];
    foreach(var anchor in anchors)
        foreach(var hit in hits[anchor].Take(2))
        {
            var start=hit>=64?hit-64:hit;
            if(!MemoryProbe.ReadProcessMemory(handle,(IntPtr)start,window,window.Length,out var read)||read<64)continue;
            Console.WriteLine($"\n{anchor} @ 0x{hit:X} (window from 0x{start:X}):");
            for(var row=0;row+16<=read;row+=16)
            {
                var offsetInWindow=row;
                var ints=string.Join(" ",Enumerable.Range(0,4).Select(i=>BitConverter.ToInt32(window,offsetInWindow+i*4).ToString().PadLeft(11)));
                Console.WriteLine($"   +{row-64,4}  {Convert.ToHexString(window,row,16)}  {ints}");
            }
        }
    MemoryProbe.CloseHandle(handle);
    return;
}

// Finds the two halves of a fixture. A schedule record has to place the two clubs close together, so
// every place in memory where both team ids sit within a short distance is a candidate, and the save
// already tells us which pairing to expect.
// Looks for the season schedule without knowing its layout: an array of fixtures shows up as many career
// dates spaced at a fixed stride, which no other structure does.
if(args.Length>0&&args[0]=="--probe-fifa18-schedule")
{
    var game=System.Diagnostics.Process.GetProcesses()
        .Where(p=>p.ProcessName.Contains("fifa",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p=>{try{return p.WorkingSet64;}catch{return 0L;}}).FirstOrDefault();
    if(game is null){Console.WriteLine("No FIFA process found.");return;}
    var handle=MemoryProbe.OpenProcess(MemoryProbe.QueryInformation|MemoryProbe.VmRead,false,game.Id);
    if(handle==IntPtr.Zero){Console.WriteLine($"OpenProcess failed (win32 error {Marshal.GetLastWin32Error()}).");return;}
    var dates=new List<ulong>();
    var buffer=new byte[4*1024*1024];
    ulong address=0x10000;
    while(address<0x7FFFFFFFFFFF)
    {
        if(MemoryProbe.VirtualQueryEx(handle,(IntPtr)address,out var info,(uint)Marshal.SizeOf<MemoryProbe.RegionInfo>())==0)break;
        var size=info.RegionSize;
        if(size==0)break;
        const uint committed=0x1000,guard=0x100,noAccess=0x01,readable=0x02|0x04|0x08|0x20|0x40|0x80;
        if(info.State==committed&&(info.Protect&guard)==0&&(info.Protect&noAccess)==0&&(info.Protect&readable)!=0&&size<=512UL*1024*1024)
            for(ulong offset=0;offset<size;)
            {
                var take=(int)Math.Min((ulong)buffer.Length,size-offset);
                if(MemoryProbe.ReadProcessMemory(handle,(IntPtr)(address+offset),buffer,take,out var read)&&read>0)
                    for(var i=0;i+4<=read;i+=4)
                    {
                        var value=BitConverter.ToInt32(buffer,i);
                        if(value is >=20170000 and <20190000&&dates.Count<2_000_000)dates.Add(address+offset+(ulong)i);
                    }
                offset+=(ulong)take;
            }
        address+=size;
    }
    dates.Sort();
    Console.WriteLine($"career-dated int32 values in memory: {dates.Count}");
    var known=dates.ToHashSet();
    var runs=new List<(ulong Start,ulong Stride,int Count)>();
    var seen=new HashSet<ulong>();
    foreach(var start in dates)
    {
        if(seen.Contains(start))continue;
        for(ulong stride=8;stride<=256;stride+=4)
        {
            var runLength=1;var at=start;
            while(known.Contains(at+stride)){runLength++;at+=stride;}
            if(runLength<6)continue;
            for(var step=start;step<=at;step+=stride)seen.Add(step);
            runs.Add((start,stride,runLength));
            break;
        }
        if(runs.Count>=200)break;
    }
    foreach(var run in runs.OrderByDescending(x=>x.Count).Take(12))
        Console.WriteLine($"  run of {run.Count,4} dates at stride {run.Stride,3} starting 0x{run.Start:X}");
    if(runs.Count==0)Console.WriteLine("  no regularly spaced runs of dates found");
    MemoryProbe.CloseHandle(handle);
    return;
}

// Finds fixtures by the shape already confirmed in memory: two team ids followed closely by a career date.
// Filtering on the career club keeps it to the fixtures Touchline actually needs.
if(args.Length>0&&args[0]=="--probe-fifa18-fixtures-live")
{
    var club=int.Parse(args[1]);
    var game=System.Diagnostics.Process.GetProcesses()
        .Where(p=>p.ProcessName.Contains("fifa",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p=>{try{return p.WorkingSet64;}catch{return 0L;}}).FirstOrDefault();
    if(game is null){Console.WriteLine("No FIFA process found.");return;}
    var handle=MemoryProbe.OpenProcess(MemoryProbe.QueryInformation|MemoryProbe.VmRead,false,game.Id);
    if(handle==IntPtr.Zero){Console.WriteLine($"OpenProcess failed (win32 error {Marshal.GetLastWin32Error()}).");return;}
    var found=new List<(ulong Address,int A,int B,int Date,int X,int Y)>();
    var buffer=new byte[4*1024*1024];
    ulong address=0x10000;
    while(address<0x7FFFFFFFFFFF)
    {
        if(MemoryProbe.VirtualQueryEx(handle,(IntPtr)address,out var info,(uint)Marshal.SizeOf<MemoryProbe.RegionInfo>())==0)break;
        var size=info.RegionSize;
        if(size==0)break;
        const uint committed=0x1000,guard=0x100,noAccess=0x01,readable=0x02|0x04|0x08|0x20|0x40|0x80;
        if(info.State==committed&&(info.Protect&guard)==0&&(info.Protect&noAccess)==0&&(info.Protect&readable)!=0&&size<=512UL*1024*1024)
            for(ulong offset=0;offset<size;)
            {
                var take=(int)Math.Min((ulong)buffer.Length,size-offset);
                if(MemoryProbe.ReadProcessMemory(handle,(IntPtr)(address+offset),buffer,take,out var read)&&read>0)
                    for(var i=12;i+12<=read;i+=4)
                    {
                        var date=BitConverter.ToInt32(buffer,i);
                        if(date is < 20170000 or >=20190000)continue;
                        var a=BitConverter.ToInt32(buffer,i-8);
                        var b=BitConverter.ToInt32(buffer,i-4);
                        if(a!=club&&b!=club)continue;
                        if(a<=0||b<=0||a==b||a>200000||b>200000)continue;
                        // A match record puts the two goal tallies straight after the date. Nothing else
                        // that mentions a club near a date does that, so it separates fixtures from noise.
                        var x=BitConverter.ToInt32(buffer,i+4);var y=BitConverter.ToInt32(buffer,i+8);
                        if(x is < 0 or > 15||y is < 0 or > 15)continue;
                        if(found.Count<400)found.Add((address+offset+(ulong)i,a,b,date,x,y));
                    }
                offset+=(ulong)take;
            }
        address+=size;
    }
    Console.WriteLine($"fixture-shaped records mentioning club {club}: {found.Count}");
    foreach(var row in found.OrderBy(x=>x.Date).Take(60))
        Console.WriteLine($"  0x{row.Address:X}  {row.A,7} v {row.B,7}  {row.Date}   then {row.X,4} {row.Y,4}");
    MemoryProbe.CloseHandle(handle);
    return;
}

// Exercises the shipped reader rather than the ad-hoc probes, so what the app will do can be checked.
if(args.Length>0&&args[0]=="--probe-fifa18-live")
{
    var club=int.Parse(args[1]);var date=args[2];
    var clock=System.Diagnostics.Stopwatch.StartNew();
    var match=new Fifa18LiveMatchReader().FindMatch(club,date);
    Console.WriteLine(match is null
        ?$"no live record for club {club} on {date} ({clock.ElapsedMilliseconds} ms)"
        :$"live record: club {match.ClubTeamId} v {match.OpponentTeamId} on {match.Date}, {match.TeamScore}-{match.OpponentScore} ({clock.ElapsedMilliseconds} ms)");
    return;
}

// Raw window of the live game's memory, printed as int32 rows, for mapping a structure once it is found.
if(args.Length>0&&args[0]=="--probe-fifa18-dump")
{
    var start=Convert.ToUInt64(args[1].Replace("0x",""),16);
    var length=args.Length>2?int.Parse(args[2]):1024;
    var stride=args.Length>3?int.Parse(args[3]):4;
    var game=System.Diagnostics.Process.GetProcesses()
        .Where(p=>p.ProcessName.Contains("fifa",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p=>{try{return p.WorkingSet64;}catch{return 0L;}}).FirstOrDefault();
    if(game is null){Console.WriteLine("No FIFA process found.");return;}
    var handle=MemoryProbe.OpenProcess(MemoryProbe.QueryInformation|MemoryProbe.VmRead,false,game.Id);
    if(handle==IntPtr.Zero){Console.WriteLine($"OpenProcess failed (win32 error {Marshal.GetLastWin32Error()}).");return;}
    var bytes=new byte[length];
    if(!MemoryProbe.ReadProcessMemory(handle,(IntPtr)start,bytes,length,out var got)||got<=0)
    {
        Console.WriteLine($"read failed at 0x{start:X} (win32 error {Marshal.GetLastWin32Error()})");
        MemoryProbe.CloseHandle(handle);return;
    }
    for(var row=0;row+stride*4<=got;row+=stride*4)
        Console.WriteLine($"0x{start+(ulong)row:X}  {string.Join(" ",Enumerable.Range(0,stride).Select(i=>BitConverter.ToInt32(bytes,row+i*4).ToString().PadLeft(11)))}");
    MemoryProbe.CloseHandle(handle);
    return;
}

if(args.Length>0&&args[0]=="--probe-fifa18-pair")
{
    var left=int.Parse(args[1]);var right=int.Parse(args[2]);
    var window=args.Length>3?int.Parse(args[3]):64;
    var game=System.Diagnostics.Process.GetProcesses()
        .Where(p=>p.ProcessName.Contains("fifa",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p=>{try{return p.WorkingSet64;}catch{return 0L;}}).FirstOrDefault();
    if(game is null){Console.WriteLine("No FIFA process found. Start the game and load the career first.");return;}
    var handle=MemoryProbe.OpenProcess(MemoryProbe.QueryInformation|MemoryProbe.VmRead,false,game.Id);
    if(handle==IntPtr.Zero){Console.WriteLine($"OpenProcess failed (win32 error {Marshal.GetLastWin32Error()}).");return;}
    Console.WriteLine($"searching for {left} and {right} within {window} bytes of each other (pid {game.Id})");
    var lefts=new List<ulong>();var rights=new List<ulong>();
    var buffer=new byte[4*1024*1024];
    ulong address=0x10000;
    while(address<0x7FFFFFFFFFFF)
    {
        if(MemoryProbe.VirtualQueryEx(handle,(IntPtr)address,out var info,(uint)Marshal.SizeOf<MemoryProbe.RegionInfo>())==0)break;
        var size=info.RegionSize;
        if(size==0)break;
        const uint committed=0x1000,guard=0x100,noAccess=0x01,readable=0x02|0x04|0x08|0x20|0x40|0x80;
        if(info.State==committed&&(info.Protect&guard)==0&&(info.Protect&noAccess)==0&&(info.Protect&readable)!=0&&size<=512UL*1024*1024)
            for(ulong offset=0;offset<size;)
            {
                var take=(int)Math.Min((ulong)buffer.Length,size-offset);
                if(MemoryProbe.ReadProcessMemory(handle,(IntPtr)(address+offset),buffer,take,out var read)&&read>0)
                    for(var i=0;i+4<=read;i+=4)
                    {
                        var value=BitConverter.ToInt32(buffer,i);
                        if(value==left&&lefts.Count<400000)lefts.Add(address+offset+(ulong)i);
                        else if(value==right&&rights.Count<400000)rights.Add(address+offset+(ulong)i);
                    }
                offset+=(ulong)take;
            }
        address+=size;
    }
    Console.WriteLine($"{left}: {lefts.Count} hits, {right}: {rights.Count} hits");
    var rightSet=rights.ToHashSet();
    var pairs=lefts.Where(a=>Enumerable.Range(-window/4,window/2).Any(step=>step!=0&&rightSet.Contains((ulong)((long)a+step*4)))).Take(12).ToList();
    Console.WriteLine($"co-located pairs: {pairs.Count}{(pairs.Count==12?"+ (showing first 12)":"")}");
    var dump=new byte[160];
    foreach(var hit in pairs)
    {
        var start=hit>=64?hit-64:hit;
        if(!MemoryProbe.ReadProcessMemory(handle,(IntPtr)start,dump,dump.Length,out var read)||read<64)continue;
        Console.WriteLine($"\npair at 0x{hit:X}:");
        for(var row=0;row+16<=read;row+=16)
            Console.WriteLine($"   +{row-64,4}  {string.Join(" ",Enumerable.Range(0,4).Select(i=>BitConverter.ToInt32(dump,row+i*4).ToString().PadLeft(11)))}");
    }
    MemoryProbe.CloseHandle(handle);
    return;
}

if(args.Length>0&&args[0]=="--probe-fifa18")
{
    var path=args.Length>1?Path.GetFullPath(args[1]):new Fifa18SaveLocator().FindLatestCareer();
    if(path is null)throw new FileNotFoundException("No FIFA 18 Career save found.");
    var (data,fingerprint)=await new Fifa18SaveParser().ParseFileAsync(path);
    var parsed=new Fifa18CareerNormalizer().Normalize(data,path,fingerprint);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(parsed,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    return;
}

if(args.Length>0&&args[0]=="--dump-fifa18-tables")
{
    var path=args.Length>1?Path.GetFullPath(args[1]):new Fifa18SaveLocator().FindLatestCareer();
    if(path is null)throw new FileNotFoundException("No FIFA 18 Career save found.");
    var bytes=await File.ReadAllBytesAsync(path);
    foreach(var table in Fifa18SaveInspector.Describe(bytes).OrderByDescending(x=>x.RecordCount))
        Console.WriteLine($"{table.ShortName} rows={table.RecordCount,6} size={table.RecordSize,5} fields={string.Join(",",table.Fields.Select(f=>$"{f.ShortName}:{f.Type}:{f.BitDepth}"))}");
    return;
}

if(args.Length>0&&args[0]=="--diff-fifa18")
{
    var left=Path.GetFullPath(args[1]);var right=Path.GetFullPath(args[2]);
    var a=Fifa18SaveInspector.Describe(await File.ReadAllBytesAsync(left)).ToDictionary(x=>x.ShortName,x=>x);
    var b=Fifa18SaveInspector.Describe(await File.ReadAllBytesAsync(right)).ToDictionary(x=>x.ShortName,x=>x);
    foreach(var key in a.Keys.Union(b.Keys).OrderBy(x=>x))
    {
        var before=a.GetValueOrDefault(key)?.RecordCount??-1;var after=b.GetValueOrDefault(key)?.RecordCount??-1;
        if(before!=after)Console.WriteLine($"{key} {before} -> {after} ({after-before:+#;-#;0})");
    }
    return;
}

if(args.Length>0&&args[0]=="--dump-fifa18-table")
{
    var path=Path.GetFullPath(args[1]);var name=args[2];var take=args.Length>3?int.Parse(args[3]):20;
    var rows=Fifa18SaveInspector.ReadTable(await File.ReadAllBytesAsync(path),name);
    Console.WriteLine($"[{name}] rows={rows.Count}");
    foreach(var row in rows.TakeLast(take))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(row.ToDictionary(x=>x.Key,x=>x.Value is string s&&s.Length>300?s[..300]+"…":x.Value)));
    return;
}

// Schema discovery by data rather than by field width: finds every table whose values look like career
// dates or team ids, which is how an unmapped fixture, schedule, or result table would announce itself.
if(args.Length>0&&args[0]=="--find-fifa18-fixtures")
{
    var path=Path.GetFullPath(args[1]);var bytes=await File.ReadAllBytesAsync(path);
    var teamIds=Fifa18SaveInspector.ReadTable(bytes,"lyxL").Select(r=>r.TryGetValue("mCXg",out var v)&&v is long id?id:-1).Where(x=>x>0).ToHashSet();
    Console.WriteLine($"known team ids: {teamIds.Count}");
    foreach(var table in Fifa18SaveInspector.Describe(bytes).OrderByDescending(x=>x.RecordCount))
    {
        if(table.RecordCount==0||(long)table.RecordCount*table.Fields.Count>600_000)continue;
        var rows=Fifa18SaveInspector.ReadTable(bytes,table.ShortName);
        if(rows.Count==0)continue;
        var dateFields=new List<string>();var teamFields=new List<string>();
        // FIFA stores an integer as value-minus-RangeLow, and the inspector reads raw bits, so a date is
        // only recognisable once the usual 20080101 base is added back. Both readings are tested.
        static bool IsDate(long v)=>v is >=20170000 and <20200000||v is >=2017000000 and <2020000000
            ||v>0&&v+20080101 is >=20170000 and <20200000;
        foreach(var field in table.Fields.Select(x=>x.ShortName))
        {
            var values=rows.Select(r=>r.TryGetValue(field,out var v)&&v is long n?n:long.MinValue).Where(x=>x!=long.MinValue&&x!=0).ToList();
            if(values.Count<Math.Max(2,rows.Count/4))continue;
            var spread=values.Distinct().Count();
            var dates=values.Count(IsDate);
            // Team ids start at 1, so small integers match by accident. A real team reference has many
            // distinct values and mostly sits above the handful of single-digit ids.
            var teams=values.Count(x=>teamIds.Contains(x)&&x>100);
            if(dates>=values.Count*3/4)dateFields.Add($"{field}({spread} distinct, e.g. {values[0]})");
            if(teams>=values.Count*3/4&&spread>=8)teamFields.Add($"{field}({spread} distinct)");
        }
        if(dateFields.Count>0||teamFields.Count>=2)
            Console.WriteLine($"{table.ShortName} rows={table.RecordCount,6} dates=[{string.Join(", ",dateFields)}] teams=[{string.Join(", ",teamFields)}]");
    }
    return;
}

// Every row that changed between two saves, table by table. The decisive test for whether a match result
// is persisted anywhere: if it is, the rows holding it move when a match is played.
if(args.Length>0&&args[0]=="--diff-fifa18-rows")
{
    var before=await File.ReadAllBytesAsync(Path.GetFullPath(args[1]));
    var after=await File.ReadAllBytesAsync(Path.GetFullPath(args[2]));
    foreach(var table in Fifa18SaveInspector.Describe(after).OrderByDescending(x=>x.RecordCount))
    {
        if(table.RecordCount==0)continue;
        var a=Fifa18SaveInspector.ReadTable(before,table.ShortName);
        var b=Fifa18SaveInspector.ReadTable(after,table.ShortName);
        if(a.Count==0||b.Count==0)continue;
        var changed=0;var samples=new List<string>();
        for(var i=0;i<Math.Min(a.Count,b.Count);i++)
        {
            var diffs=b[i].Where(x=>a[i].TryGetValue(x.Key,out var old)&&!Equals(old,x.Value))
                .Select(x=>$"{x.Key} {a[i][x.Key]}->{x.Value}").ToList();
            if(diffs.Count==0)continue;
            changed++;
            if(samples.Count<3)samples.Add($"row{i}: {string.Join(", ",diffs.Take(8))}");
        }
        if(changed>0||a.Count!=b.Count)
        {
            Console.WriteLine($"{table.ShortName} rows {a.Count}->{b.Count}, {changed} rows changed");
            foreach(var sample in samples)Console.WriteLine($"    {sample}");
        }
    }
    return;
}

// Value-level diff for one team across two saves. Whatever a played match changes about a club is what
// the save records about results, which is how an unnamed opponent could be identified from standings
// rather than from a news article that has already rotated away.
if(args.Length>0&&args[0]=="--diff-fifa18-team")
{
    var before=await File.ReadAllBytesAsync(Path.GetFullPath(args[1]));
    var after=await File.ReadAllBytesAsync(Path.GetFullPath(args[2]));
    var teamId=long.Parse(args[3]);
    Console.WriteLine($"changes for team {teamId}:");
    foreach(var table in Fifa18SaveInspector.Describe(after).OrderByDescending(x=>x.RecordCount))
    {
        if(table.RecordCount==0||!table.Fields.Any(f=>f.ShortName=="mCXg"))continue;
        var a=ByTeam(Fifa18SaveInspector.ReadTable(before,table.ShortName));
        var b=ByTeam(Fifa18SaveInspector.ReadTable(after,table.ShortName));
        if(a is null||b is null||!a.TryGetValue(teamId,out var rowA)||!b.TryGetValue(teamId,out var rowB))continue;
        var changes=rowB.Where(x=>rowA.TryGetValue(x.Key,out var old)&&!Equals(old,x.Value))
            .Select(x=>$"{x.Key}: {rowA[x.Key]} -> {x.Value}").ToList();
        if(changes.Count>0)Console.WriteLine($"  {table.ShortName} (rows={table.RecordCount}) {string.Join(", ",changes)}");
    }
    return;

    // Rows can only be compared across saves when the team id identifies them uniquely.
    static Dictionary<long,IReadOnlyDictionary<string,object>>? ByTeam(IReadOnlyList<IReadOnlyDictionary<string,object>> rows)
    {
        var map=new Dictionary<long,IReadOnlyDictionary<string,object>>();
        foreach(var row in rows)
        {
            if(!row.TryGetValue("mCXg",out var value)||value is not long id||!map.TryAdd(id,row))return null;
        }
        return map;
    }
}

// Walks a series of real saves the way the app does: the state of the last imported match becomes
// the baseline for the next scan. Shows exactly which matches would import without review.
if(args.Length>0&&args[0]=="--probe-fifa18-sequence")
{
    var parser=new Fifa18SaveParser();var normalizer=new Fifa18CareerNormalizer();Fifa18SyncState? previous=null;
    foreach(var file in args.Skip(1))
    {
        var (data,fingerprint)=await parser.ParseFileAsync(Path.GetFullPath(file));
        var parsed=normalizer.Normalize(data,Path.GetFullPath(file),fingerprint,previous);
        Console.WriteLine($"\n=== {Path.GetFileName(file)} | {parsed.PlayerName} @ {parsed.ClubName} | {parsed.CurrentDate} | apps={parsed.State.Appearances} g={parsed.State.Goals} a={parsed.State.Assists} ===");
        if(parsed.MissedClubMatches>0)Console.WriteLine($"  club matches missed by the player: {parsed.MissedClubMatches}");
        if(parsed.PendingMatches.Count==0)Console.WriteLine("  no new match");
        foreach(var m in parsed.PendingMatches)
        {
            Console.WriteLine($"  {m.Date} {(m.IsHomeKnown?m.IsHome?"vs":"at":"v?")} {m.Opponent,-22} {m.Competition,-22} {m.ScoreLabel,-13} started={m.Started} min={m.Minutes} rating={m.Rating} g={m.Goals} a={m.Assists} derby={m.IsDerby} conf={m.Confidence} review={m.RequiresReview}");
            if(m.RequiresReview)Console.WriteLine($"      why: {m.Evidence}");
        }
        // The app advances the baseline once a detected match is imported or dismissed, so model that here.
        if(parsed.PendingMatches.Any(x=>x.TeamContext=="Club"))previous=parsed.State;
    }
    foreach(var line in new[]{""})Console.WriteLine(line);
    return;
}

// Full-pipeline replay: builds a real career database and applies each save through FifaSyncService,
// exactly as the app does when the player saves in FIFA.
if(args.Length>0&&args[0]=="--replay-fifa18")
{
    var target=Path.Combine(Path.GetTempPath(),$"touchline-replay-{DateTime.Now:yyyyMMdd-HHmmss}.db");
    var database=new Database(target);database.Migrate();
    var parser=new Fifa18SaveParser();var normalizer=new Fifa18CareerNormalizer();
    var sync=new FifaSyncService(database);long careerId=0;
    foreach(var file in args.Skip(1))
    {
        var path=Path.GetFullPath(file);
        var (data,fingerprint)=await parser.ParseFileAsync(path);
        Fifa18SyncState? prior=null;
        if(careerId>0&&database.GetLatestProviderPayload(careerId,FifaSyncService.ProviderName) is { } payload)
            prior=System.Text.Json.JsonSerializer.Deserialize<Fifa18SyncState>(payload);
        var remembered=careerId>0?database.GetCachedProviderNews(careerId,FifaSyncService.ProviderName):null;
        var parsed=normalizer.Normalize(data,path,fingerprint,prior,remembered);
        if(careerId==0)careerId=database.CreateCareer(Path.GetFileName(path),parsed.PlayerName,parsed.NationalityName,
            parsed.Age,parsed.ClubName,parsed.LeagueName,parsed.Season,parsed.Position,parsed.ShirtNumber,parsed.CurrentDate);
        if(parsed.ArticleCache is {Count:>0} cache)database.CacheProviderNews(careerId,FifaSyncService.ProviderName,cache);
        var outcome=sync.Apply(careerId,parsed);
        Console.WriteLine($"\n=== {Path.GetFileName(path)} | {parsed.CurrentDate} ===");
        Console.WriteLine($"  {outcome.Message}");
        foreach(var m in outcome.Imported)Console.WriteLine($"  imported: {FifaSyncService.Describe(m)} | started={m.Started} min={m.Minutes} rating={m.Rating} g={m.Goals} a={m.Assists} derby={m.IsDerby}");
        foreach(var m in outcome.NeedsReview)Console.WriteLine($"  review:   {FifaSyncService.Describe(m)}");
    }
    Console.WriteLine($"\n--- resulting career world ({target}) ---");
    Console.WriteLine($"matches: {database.GetMatches(careerId,500).Count}  events: {database.GetEvents(careerId,500).Count}  notifications: {database.GetNotifications(careerId,500).Count}  news: {database.GetNews(careerId,200).Count}");
    foreach(var match in database.GetMatches(careerId,500))
        Console.WriteLine($"  MATCH {match.Input.Date} {match.Input.VenueLabel} {match.Input.Opponent,-20} {match.Input.ScoreLabel,-13} {match.Result} rating={match.Input.Rating} g={match.Input.Goals} squad={database.GetMatchPerformances(match.Id).Count}");
    foreach(var match in database.GetMatches(careerId,500).TakeLast(2))
        Console.WriteLine($"  BRIEF: {new CareerCompanion.Core.Simulation.MatchNarrativeBuilder(database).Build(database.GetCareer(careerId),match).Brief()}");
    foreach(var character in database.GetCharacters(careerId).Take(60))
    {
        var messages=database.GetMessages(careerId,character.Id,20);
        foreach(var message in messages)Console.WriteLine($"  [{character.Type}] {character.Name}: {message.DisplayContent}");
    }
    return;
}

// Opens a copy of an existing career database, applies migrations, and reads every screen's data back.
if(args.Length>0&&args[0]=="--verify-db")
{
    var original=Path.GetFullPath(args[1]);
    var copy=Path.Combine(Path.GetTempPath(),$"touchline-migrate-check-{DateTime.Now:HHmmss}.db");
    File.Copy(original,copy,true);
    // Copy the write-ahead log too, otherwise a live database looks empty.
    foreach(var suffix in new[]{"-wal","-shm"})if(File.Exists(original+suffix))File.Copy(original+suffix,copy+suffix,true);
    var checkDb=new Database(copy);
    checkDb.Migrate();
    foreach(var saved in checkDb.GetCareers())
    {
        var matches=checkDb.GetMatches(saved.Id,500);
        Console.WriteLine($"{saved.SaveName}: {saved.PlayerName} @ {saved.Club} | matches={matches.Count} characters={checkDb.GetCharacters(saved.Id).Count} events={checkDb.GetEvents(saved.Id,2000).Count} notifications={checkDb.GetNotifications(saved.Id,500).Count} fixtures={checkDb.GetFixtures(saved.Id).Count} news={checkDb.GetNews(saved.Id,200).Count}");
        foreach(var match in matches.TakeLast(4))
            Console.WriteLine($"    {match.Input.Date} {match.Input.VenueLabel} {match.Input.Opponent} {match.Input.ScoreLabel} {match.Result} homeKnown={match.Input.IsHomeKnown} performances={checkDb.GetMatchPerformances(match.Id).Count}");
        foreach(var person in checkDb.GetCharacters(saved.Id).Take(3))
            Console.WriteLine($"    msgs {person.Name}: {checkDb.GetMessages(saved.Id,person.Id,3).Count}");
        checkDb.GetCachedProviderNews(saved.Id,"FIFA 18 Save");
        checkDb.GetUnscoredProviderMatches(saved.Id);
        checkDb.GetRecentDialogueKeys(saved.Id,1);
    }
    Console.WriteLine($"Migration verified against a copy of {original}");
    return;
}

if(args.Length>0&&args[0]=="--probe-fifa18-context")
{
    var path=args.Length>1?Path.GetFullPath(args[1]):new Fifa18SaveLocator().FindLatestCareer();
    if(path is null)throw new FileNotFoundException("No FIFA 18 Career save found.");
    var (data,_)=await new Fifa18SaveParser().ParseFileAsync(path);
    var user=data.Table("career_users").First(x=>Convert.ToInt64(x["usertype"])==2);
    var userId=Convert.ToInt32(user["userid"]);var playerId=Convert.ToInt32(data.Table("career_playasplayer").First(x=>Convert.ToInt32(x["userid"])==userId)["playerid"]);
    foreach(var table in new[]{"career_users","career_playasplayerhistory","career_playermatchratinghistory","career_playerlastmatchhistory"})
    {
        Console.WriteLine($"\n[{table}]");
        foreach(var row in data.Table(table).Where(row=>!row.TryGetValue("playerid",out var p)||Convert.ToInt32(p)==playerId))
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(row));
    }
    Console.WriteLine("\n[relevant career_news]");
    foreach(var row in data.Table("career_news").Where(row=>Convert.ToInt32(row.GetValueOrDefault("date",0))>=20170826&&
        (Convert.ToInt32(row.GetValueOrDefault("teamid",-1))==Convert.ToInt32(user["nationalteamid"])||
         (Convert.ToInt32(row.GetValueOrDefault("date",0))==20170901&&
          (Convert.ToString(row.GetValueOrDefault("title",""))!.Contains("Portugal",StringComparison.OrdinalIgnoreCase)||
           Convert.ToString(row.GetValueOrDefault("title",""))!.Contains("England",StringComparison.OrdinalIgnoreCase)||
           Convert.ToString(row.GetValueOrDefault("body",""))!.Contains("Portugal",StringComparison.OrdinalIgnoreCase)||
           Convert.ToString(row.GetValueOrDefault("body",""))!.Contains("England",StringComparison.OrdinalIgnoreCase)))||
         Convert.ToString(row.GetValueOrDefault("title",""))!.Contains("Oliveira",StringComparison.OrdinalIgnoreCase)||
         Convert.ToString(row.GetValueOrDefault("body",""))!.Contains("Oliveira",StringComparison.OrdinalIgnoreCase))))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(row));
    return;
}

// Everything above returns. Anything still here is the simulation, which writes a brand new career into
// the destination it is given. An unrecognised flag must never reach it: falling through once wrote a
// simulation into a real career database that happened to be named on the command line.
if(args.Length>0&&args[0].StartsWith("--",StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'. This tool writes a simulation when given no command, so it stops rather than guess.");
    Environment.ExitCode=1;
    return;
}
var count=args.Length>0&&int.TryParse(args[0],out var n)?Math.Clamp(n,1,500):20;
var explicitDestination=args.Skip(1).FirstOrDefault(x=>!x.StartsWith("--",StringComparison.Ordinal));
var destination=explicitDestination is null?Path.Combine(Path.GetTempPath(),$"touchline-simulation-{DateTime.Now:yyyyMMdd-HHmmss}.db"):Path.GetFullPath(explicitDestination);
var db=new Database(destination);db.Migrate();var career=db.CreateCareer("Simulation Run","Test Player","Test",20,"Simulation FC","Test League","2017/18","ST",9);db.AddCharacter(career,"Outspoken Teammate",25,"Test","Simulation FC","CM","Starter",CharacterType.Teammate,new(70,80,60,65,45,35,75,60,35,65,55,70),CommunicationStyle.Balanced);db.AddCharacter(career,"Quiet Teammate",22,"Test","Simulation FC","CB","Rotation",CharacterType.Teammate,new(45,60,20,20,20,70,55,30,70,25,75,80),new("very brief",40,5,10,20,50,45,5));db.AddCharacter(career,"Test Manager",52,"Test","Simulation FC","Manager","Manager",CharacterType.Manager);
var random=new Random(1818);var service=new CareerService(db);var media=new MediaService(db);var world=new AutomaticWorldService(db);var totals=new Dictionary<string,int>();
for(var i=0;i<count;i++){var outcome=random.Next(100);var us=outcome<55?random.Next(1,4):outcome<78?random.Next(0,3):random.Next(0,2);var them=outcome<55?random.Next(0,us):outcome<78?us:random.Next(us+1,us+4);var goals=random.Next(100)<42?random.Next(1,Math.Min(4,us+1)):0;var result=service.ProcessMatch(career,new(new DateTime(2017,8,1).AddDays(i*7).ToString("yyyy-MM-dd"),i%7==0?"Cup":"League",$"Opponent {i+1}",i%2==0,us,them,true,90,goals,random.Next(100)<25?1:0,goals>=2?9.1:7.0,false,i==Math.Min(9,count-1),false,false,i%8==0?"late winner":"",null,i%10==0,i%7==0));media.GenerateDeterministic(career,result.Events);world.ApplyMatch(result,true,true,false,true,true);foreach(var e in result.Events)totals[e.Type]=totals.GetValueOrDefault(e.Type)+1;}
var allMessages=db.GetCharacters(career).SelectMany(c=>db.GetMessages(career,c.Id,10000).Where(m=>m.Role=="assistant").Select(m=>(Character:c.Name,m.Content))).ToList();
var distinct=allMessages.Select(x=>x.Content).Distinct(StringComparer.Ordinal).Count();
Console.WriteLine($"Simulation complete: {count} matches -> {destination}");
Console.WriteLine($"Automatic messages: {allMessages.Count}; distinct texts: {distinct} ({(allMessages.Count==0?0:100*distinct/allMessages.Count)}% unique)");
if(args.Contains("--show-messages"))foreach(var m in allMessages)Console.WriteLine($"  {m.Character}: {m.Content}");
var repeats=allMessages.GroupBy(x=>x.Content,StringComparer.Ordinal).Where(g=>g.Count()>1).OrderByDescending(g=>g.Count()).Take(6).ToList();
if(repeats.Count>0){Console.WriteLine("Most repeated lines:");foreach(var g in repeats)Console.WriteLine($"  x{g.Count()} {g.Key[..Math.Min(90,g.Key.Length)]}");}Console.WriteLine("NEWS SAMPLE:");foreach(var item in db.GetNews(career,10000).Take(6))Console.WriteLine($"  [{item.Outlet}] {item.Headline} | {item.Body}");foreach(var item in db.GetSocial(career,10000).Take(4))Console.WriteLine($"  ({item.Author}) {item.Content}");
Console.WriteLine($"Events: {db.GetEvents(career,10000).Count}; news: {db.GetNews(career,10000).Count}; social: {db.GetSocial(career,10000).Count}");foreach(var x in totals.OrderByDescending(x=>x.Value))Console.WriteLine($"{x.Key,-24} {x.Value,4}");

/// <summary>Read-only access to another process. Nothing here writes to the game or to a save.</summary>
static class MemoryProbe
{
    public const int QueryInformation=0x0400,VmRead=0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct RegionInfo
    {
        public IntPtr BaseAddress;public IntPtr AllocationBase;public uint AllocationProtect;public uint Alignment1;
        public ulong RegionSize;public uint State;public uint Protect;public uint Type;public uint Alignment2;
    }

    [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(int access,bool inheritHandle,int processId);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,int size,out int read);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern int VirtualQueryEx(IntPtr process,IntPtr address,out RegionInfo info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern bool CloseHandle(IntPtr handle);
}
