using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using CareerCompanion.Core.Providers.Fifa18;

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
