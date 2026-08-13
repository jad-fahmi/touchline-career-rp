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

var count=args.Length>0&&int.TryParse(args[0],out var n)?Math.Clamp(n,1,500):20;
var destination=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(Path.GetTempPath(),$"touchline-simulation-{DateTime.Now:yyyyMMdd-HHmmss}.db");
var db=new Database(destination);db.Migrate();var career=db.CreateCareer("Simulation Run","Test Player","Test",20,"Simulation FC","Test League","2017/18","ST",9);db.AddCharacter(career,"Outspoken Teammate",25,"Test","Simulation FC","CM","Starter",CharacterType.Teammate,new(70,80,60,65,45,35,75,60,35,65,55,70),CommunicationStyle.Balanced);db.AddCharacter(career,"Quiet Teammate",22,"Test","Simulation FC","CB","Rotation",CharacterType.Teammate,new(45,60,20,20,20,70,55,30,70,25,75,80),new("very brief",40,5,10,20,50,45,5));db.AddCharacter(career,"Test Manager",52,"Test","Simulation FC","Manager","Manager",CharacterType.Manager);
var random=new Random(1818);var service=new CareerService(db);var media=new MediaService(db);var totals=new Dictionary<string,int>();
for(var i=0;i<count;i++){var outcome=random.Next(100);var us=outcome<55?random.Next(1,4):outcome<78?random.Next(0,3):random.Next(0,2);var them=outcome<55?random.Next(0,us):outcome<78?us:random.Next(us+1,us+4);var goals=random.Next(100)<42?random.Next(1,Math.Min(4,us+1)):0;var result=service.ProcessMatch(career,new(new DateTime(2017,8,1).AddDays(i*7).ToString("yyyy-MM-dd"),i%7==0?"Cup":"League",$"Opponent {i+1}",i%2==0,us,them,true,90,goals,random.Next(100)<25?1:0,goals>=2?9.1:7.0,false,i==Math.Min(9,count-1),false,false,i%8==0?"late winner":"",null,i%10==0,i%7==0));media.GenerateDeterministic(career,result.Events);foreach(var e in result.Events)totals[e.Type]=totals.GetValueOrDefault(e.Type)+1;}
Console.WriteLine($"Simulation complete: {count} matches -> {destination}");Console.WriteLine($"Events: {db.GetEvents(career,10000).Count}; news: {db.GetNews(career,10000).Count}; social: {db.GetSocial(career,10000).Count}");foreach(var x in totals.OrderByDescending(x=>x.Value))Console.WriteLine($"{x.Key,-24} {x.Value,4}");
