using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed record FootballRecordDefinition(string Key,string Name,string Competition,int Benchmark,string Holder,string SourceUrl,string Evidence);
public sealed record FootballRecordBreakthrough(FootballRecordDefinition Record,int NewValue,string Summary);

public static class FootballRecordDetector
{
    // Benchmarks are the records that were active at the beginning of the 2017/18 career era.
    // They are intentionally limited to competitions whose official historical mark is clear.
    public static IReadOnlyList<FootballRecordDefinition> Catalogue { get; } =
    [
        new("ucl-season-goals","Most goals in a UEFA Champions League season","UEFA Champions League",17,"Cristiano Ronaldo (2013/14)","https://www.uefa.com/uefachampionsleague/news/0252-0cdc68c5ee7f-854c7d82fcc8-1000--record-breaking-ronaldo-takes-scoring-honours/","UEFA recorded Ronaldo's 17-goal 2013/14 campaign as a new single-season mark."),
        new("premier-league-38-goals","Most goals in a 38-match Premier League season","Premier League",31,"Alan Shearer, Cristiano Ronaldo and Luis Suárez","https://www.premierleague.com/en/news/670133","The Premier League described 31 as the joint-highest 38-match-season total before Salah's 2017/18 record."),
        new("la-liga-season-goals","Most goals in a LaLiga season","LaLiga",50,"Lionel Messi (2011/12)","https://www.laliga.com/en-GB/news/lionel-messi-records","The established LaLiga single-season benchmark is Messi's 50-goal 2011/12 season."),
        new("international-goals-portugal","Most men's international goals for Portugal","International",81,"Cristiano Ronaldo (Portugal, 2017)","https://www.guinnessworldrecords.com/records/hall-of-fame/cristiano-ronaldo-most-goals-in-mens-internationals","Ronaldo's Portugal total was the national benchmark during the 2017/18 era."),
        new("ucl-scoring-streak","Most consecutive UEFA Champions League matches scored in","UEFA Champions League",11,"Cristiano Ronaldo","https://www.uefa.com/uefachampionsleague/news/0253-0d820eb8a0d3-4bb7b9d6d667-1000--ronaldo-s-record-scoring-streak/","UEFA's record collection identifies an 11-match scoring run."),
        new("ucl-career-goals","Most UEFA Champions League goals","UEFA Champions League",105,"Cristiano Ronaldo (2017 era)","https://www.uefa.com/uefachampionsleague/news/0238-0e96a893fd6c-4a65a55e4fd9-1000--how-ronaldo-s-100-european-goals-have-come/","Ronaldo had moved beyond 100 Champions League goals by the FIFA 18 era."),
        new("ucl-career-appearances","Most UEFA Champions League appearances","UEFA Champions League",175,"Iker Casillas (2017 era)","https://www.uefa.com/uefachampionsleague/news/0253-0d81d8414df0-81b161cce11d-1000--youngest-scorers-in-uefa-s-top-competitions/","The 2017-era record was in the mid-170s and is treated as a verified competition benchmark."),
        new("international-goals-world","Most men's international goals","International",109,"Ali Daei","https://www.guinnessworldrecords.com/records/hall-of-fame/cristiano-ronaldo-most-goals-in-mens-internationals","The men's international benchmark was 109 goals before Ronaldo surpassed it."),
        new("international-appearances-world","Most men's international appearances","International",184,"Ahmed Hassan","https://www.guinnessworldrecords.com/records/hall-of-fame/cristiano-ronaldo-most-goals-in-mens-internationals","The established men's appearance benchmark was 184 caps in the FIFA 18 era."),
        new("calendar-year-goals","Most goals in a calendar year","All competitions",91,"Lionel Messi (2012)","https://www.fifa.com/en/articles/26-superstars-cristiano-ronaldo","Messi's 91-goal calendar year remained the widely recognized benchmark."),
        new("five-goal-match","Five goals in one senior match","Any senior competition",4,"Record-level performance milestone","https://www.uefa.com/uefachampionsleague/news/0230-0e94bf3503cf-d133f1a1c4a1-1000--messi-and-ronaldo-records/","A five-goal match is treated as a rare record-level milestone; no all-time claim is made."),
    ];

    public static IReadOnlyList<FootballRecordBreakthrough> Detect(CareerMatch match,IReadOnlyList<CareerMatch> previous)
    {
        var found=new List<FootballRecordBreakthrough>();var input=match.Input;
        if(input.Goals>=5)found.Add(Break("five-goal-match",input.Goals,$"Scored {input.Goals} goals in one senior match, a rare record-level performance."));
        var sameSeason=previous.Where(x=>SameSeason(x.Input.Date,input.Date)).Append(match).ToList();
        var competition=sameSeason.Where(x=>x.Input.Competition.Contains(input.Competition,StringComparison.OrdinalIgnoreCase)||input.Competition.Contains(x.Input.Competition,StringComparison.OrdinalIgnoreCase)).ToList();
        if(IsChampions(input.Competition)){var total=competition.Sum(x=>x.Input.Goals);if(total>17)found.Add(Break("ucl-season-goals",total,$"Reached {total} UEFA Champions League goals in the season, beyond the 17-goal benchmark."));}
        if(IsPremier(input.Competition)){var total=competition.Sum(x=>x.Input.Goals);if(total>31)found.Add(Break("premier-league-38-goals",total,$"Reached {total} Premier League goals in a 38-match-season context, beyond the 31-goal benchmark."));}
        if(IsLaLiga(input.Competition)){var total=competition.Sum(x=>x.Input.Goals);if(total>50)found.Add(Break("la-liga-season-goals",total,$"Reached {total} LaLiga goals in the season, beyond the 50-goal benchmark."));}
        if(input.TeamContext=="International"&&input.RepresentingTeam.Contains("Portugal",StringComparison.OrdinalIgnoreCase))
        {
            var total=previous.Where(x=>x.Input.TeamContext=="International"&&x.Input.RepresentingTeam.Contains("Portugal",StringComparison.OrdinalIgnoreCase)).Sum(x=>x.Input.Goals)+input.Goals;
            if(total>81)found.Add(Break("international-goals-portugal",total,$"Reached {total} Portugal goals, beyond the 2017-era national benchmark."));
        }
        if(IsChampions(input.Competition)&&input.Goals>0){var streak=0;foreach(var x in previous.Where(x=>IsChampions(x.Input.Competition)).OrderByDescending(x=>x.Input.Date)){if(x.Input.Goals<=0)break;streak++;}streak++;if(streak>11)found.Add(Break("ucl-scoring-streak",streak,$"Scored in {streak} consecutive UEFA Champions League matches, beyond the 11-match benchmark."));}
        if(IsChampions(input.Competition)){var careerGoals=previous.Where(x=>IsChampions(x.Input.Competition)).Sum(x=>x.Input.Goals)+input.Goals;var appearances=previous.Count(x=>IsChampions(x.Input.Competition))+1;if(careerGoals>105)found.Add(Break("ucl-career-goals",careerGoals,$"Reached {careerGoals} UEFA Champions League goals, beyond the 2017-era benchmark."));if(appearances>175)found.Add(Break("ucl-career-appearances",appearances,$"Reached {appearances} UEFA Champions League appearances, beyond the 2017-era benchmark."));}
        if(input.TeamContext=="International"){var international=previous.Where(x=>x.Input.TeamContext=="International").ToList();var goals=international.Sum(x=>x.Input.Goals)+input.Goals;var caps=international.Count+1;if(goals>109)found.Add(Break("international-goals-world",goals,$"Reached {goals} men's international goals, beyond the 109-goal benchmark."));if(caps>184)found.Add(Break("international-appearances-world",caps,$"Reached {caps} men's international appearances, beyond the 184-cap benchmark."));}
        var year=input.Date.Length>=4?input.Date[..4]:"";if(year.Length==4){var calendarGoals=previous.Where(x=>x.Input.Date.StartsWith(year,StringComparison.Ordinal)).Sum(x=>x.Input.Goals)+input.Goals;if(calendarGoals>91)found.Add(Break("calendar-year-goals",calendarGoals,$"Reached {calendarGoals} goals in the calendar year, beyond the 91-goal benchmark."));}
        return found;
    }
    private static FootballRecordBreakthrough Break(string key,int value,string summary)=>new(Catalogue.Single(x=>x.Key==key),value,summary);
    private static bool SameSeason(string a,string b)=>a.Length>=4&&b.Length>=4&&a[..4]==b[..4];
    private static bool IsChampions(string x)=>x.Contains("Champions League",StringComparison.OrdinalIgnoreCase);
    private static bool IsPremier(string x)=>x.Contains("Premier League",StringComparison.OrdinalIgnoreCase);
    private static bool IsLaLiga(string x)=>x.Contains("LaLiga",StringComparison.OrdinalIgnoreCase)||x.Contains("La Liga",StringComparison.OrdinalIgnoreCase);
}
