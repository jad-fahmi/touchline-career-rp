using CareerCompanion.Core.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed partial class Fifa18CareerNormalizer(Fifa18PlayerNameResolver? names = null)
{
    private readonly Fifa18PlayerNameResolver _names = names ?? new();

    public Fifa18ParsedCareer Normalize(Fifa18SaveData data, string sourcePath, string fingerprint, Fifa18SyncState? previous = null)
    {
        var diagnostics = new List<string>();
        var user = data.Table("career_users").FirstOrDefault(r => L(r,"usertype") == 2)
            ?? data.Table("career_users").FirstOrDefault()
            ?? throw new Fifa18SaveFormatException("No career user record was found.");
        var userId = I(user,"userid");
        var play = data.Table("career_playasplayer").FirstOrDefault(r => I(r,"userid") == userId)
            ?? throw new Fifa18SaveFormatException("This save does not appear to be a Player Career.");
        var playerId = I(play,"playerid");
        var clubId = I(user,"clubteamid");
        var player = data.Table("players").FirstOrDefault(r => I(r,"playerid") == playerId);
        var club = data.Table("teams").FirstOrDefault(r => I(r,"teamid") == clubId);
        var clubName = S(club,"teamname"); if (string.IsNullOrWhiteSpace(clubName)) clubName = $"FIFA Team #{clubId}";
        var nationalityId=I(user,"nationalityid");var nation=data.Table("nations").FirstOrDefault(r=>I(r,"nationid")==nationalityId);
        var nationalityName=S(nation,"nationname");
        if(string.IsNullOrWhiteSpace(nationalityName))
            nationalityName=data.Table("players").Where(r=>I(r,"nationality")==nationalityId)
                .Select(r=>_names.Find(I(r,"playerid"))?.Nationality).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??$"FIFA nationality #{nationalityId}";
        var leagueId=I(user,"leagueid");var league=data.Table("leagues").FirstOrDefault(r=>I(r,"leagueid")==leagueId);
        var leagueName=S(league,"leaguename");if(string.IsNullOrWhiteSpace(leagueName))leagueName=$"FIFA league #{leagueId}";
        var edited = data.Table("editedplayernames").FirstOrDefault(r => I(r,"playerid") == playerId);
        var playerName = JoinName(S(edited,"firstname"),S(edited,"surname"));
        if (string.IsNullOrWhiteSpace(playerName)) playerName = JoinName(S(user,"firstname"),S(user,"surname"));
        if (string.IsNullOrWhiteSpace(playerName)) playerName = $"FIFA Player #{playerId}";
        var calendar = data.Table("career_calendar").FirstOrDefault();
        var currentDate = I(calendar,"currdate");
        var startDate = I(calendar,"startdate");
        var seasonNumber = Math.Max(1,I(user,"seasoncount"));
        var history = data.Table("career_playasplayerhistory")
            .Where(r => I(r,"userid") == userId).OrderByDescending(r => I(r,"season")).FirstOrDefault();
        var rating = data.Table("career_playermatchratinghistory")
            .Where(r => I(r,"playerid") == playerId).OrderByDescending(r => I(r,"date"))
            .ThenByDescending(r => I(r,"artificialkey")).FirstOrDefault();
        var clubLink = data.Table("teamplayerlinks").FirstOrDefault(r => I(r,"playerid") == playerId && I(r,"teamid") == clubId);
        var state = new Fifa18SyncState(playerId,clubId,currentDate,seasonNumber,I(history,"appearances"),I(history,"goals"),
            I(history,"assists"),I(history,"totalyellows"),I(history,"totalreds"),I(rating,"artificialkey"),I(rating,"date"));
        var detected = DetectMatch(data, state, previous, playerName, clubName, clubId, rating, diagnostics);
        var birthDateValue = I(player,"birthdate");
        var birthDate = birthDateValue > 0 ? new DateTime(1582,10,15).AddDays(birthDateValue) : new DateTime(1999,1,1);
        var careerDate = ParseFifaDate(currentDate) ?? DateTime.Today;
        var age = Math.Max(15, careerDate.Year-birthDate.Year-(careerDate.Date<birthDate.AddYears(careerDate.Year-birthDate.Year).Date?1:0));
        var careerStartYear = (ParseFifaDate(startDate) ?? careerDate).Year;
        var startYear=careerStartYear+seasonNumber-1;
        var squad = NormalizeSquad(data, clubId, playerId, clubName, careerDate);
        var nextFixture = DetectNextFixture(data, state, clubName, clubId);
        var opponentScout = nextFixture is null ? null : BuildOpponentScout(data,nextFixture.Opponent,clubId,careerDate);
        var manager=data.Table("managers").FirstOrDefault(r=>I(r,"teamid")==clubId);var managerName=JoinName(S(manager,"firstname"),S(manager,"surname"));
        var agentName=S(user,"agentname");
        var nationalTeamId=I(user,"nationalteamid");var nationalTeamName=nationalTeamId>0?S(data.Table("teams").FirstOrDefault(r=>I(r,"teamid")==nationalTeamId),"teamname"):"";
        var worldNews=data.Table("career_news").Where(r=>I(r,"date")>0&&!string.IsNullOrWhiteSpace(S(r,"title")))
            .OrderByDescending(r=>I(r,"date")).Take(60).Select(r=>{var date=I(r,"date");var title=S(r,"title");var body=S(r,"body");var raw=$"{date}|{title}|{body}";var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();var importance=Math.Clamp(15+I(r,"importance")*14,15,85);return new Fifa18WorldNews(key,(ParseFifaDate(date)??careerDate).ToString("yyyy-MM-dd"),title,body,importance);}).ToList();
        var squadCount = squad.Count + 1;
        diagnostics.Add($"Parsed {data.TableNames.Count} supported tables; squad links={squadCount}.");
        return new(sourcePath,fingerprint,File.GetLastWriteTimeUtc(sourcePath),playerName,playerId,nationalityId,nationalityName,age,
            clubName,clubId,leagueId,leagueName,$"{startYear}/{(startYear+1)%100:00}",careerDate.ToString("yyyy-MM-dd"),
            PositionName(I(play,"position")),I(clubLink,"jerseynumber"),state,detected,squad,nextFixture,squadCount,diagnostics,
            I(history,"overall"),I(clubLink,"form"),false,managerName,agentName,worldNews,false,nationalTeamId,nationalTeamName,opponentScout);
    }

    private Fifa18OpponentScout? BuildOpponentScout(Fifa18SaveData data,string opponentName,int clubId,DateTime careerDate)
    {
        var team=data.Table("teams").FirstOrDefault(r=>Same(S(r,"teamname"),opponentName));if(team is null)return null;
        var teamId=I(team,"teamid");if(teamId<=0)return null;var manager=data.Table("managers").FirstOrDefault(r=>I(r,"teamid")==teamId);
        var managerName=JoinName(S(manager,"firstname"),S(manager,"surname"));var stadium=S(data.Table("teamstadiumlinks").FirstOrDefault(r=>I(r,"teamid")==teamId),"stadiumname");
        var club=data.Table("teams").FirstOrDefault(r=>I(r,"teamid")==clubId);var rival=I(club,"rivalteam",-1)==teamId||I(team,"rivalteam",-1)==clubId;
        var keyPlayers=NormalizeSquad(data,teamId,-1,opponentName,careerDate).OrderByDescending(x=>x.Overall).ThenBy(x=>x.Name).Take(5).Select(x=>new Fifa18ScoutPlayer(x.Name,x.Position,x.Overall)).ToList();
        var evidence=$"FIFA save squad snapshot for {opponentName}";return new(opponentName,managerName,stadium,rival,keyPlayers,evidence);
    }

    private IReadOnlyList<Fifa18SquadMember> NormalizeSquad(Fifa18SaveData data, int clubId, int careerPlayerId,
        string clubName, DateTime careerDate)
    {
        var players = data.Table("players").ToDictionary(r => I(r,"playerid"));
        var edited = data.Table("editedplayernames").ToDictionary(r => I(r,"playerid"));
        var dynamicNames = data.Table("dcplayernames").Where(r=>I(r,"nameid")>0)
            .GroupBy(r=>I(r,"nameid")).ToDictionary(x=>x.Key,x=>S(x.First(),"name"));
        var result = new List<Fifa18SquadMember>();
        foreach (var link in data.Table("teamplayerlinks")
                     .Where(r => I(r,"teamid") == clubId && I(r,"playerid") != careerPlayerId)
                     .OrderBy(r => I(r,"jerseynumber")))
        {
            var id = I(link,"playerid");
            if (!players.TryGetValue(id, out var player)) continue;
            edited.TryGetValue(id, out var custom);
            var identity = _names.Find(id);
            var name = JoinName(S(custom,"firstname"),S(custom,"surname"));
            if (string.IsNullOrWhiteSpace(name)) name = S(custom,"commonname");
            if (string.IsNullOrWhiteSpace(name)&&dynamicNames.TryGetValue(I(player,"commonnameid"),out var common))name=common;
            if (string.IsNullOrWhiteSpace(name))name=JoinName(dynamicNames.GetValueOrDefault(I(player,"firstnameid"))??"",dynamicNames.GetValueOrDefault(I(player,"lastnameid"))??"");
            if (string.IsNullOrWhiteSpace(name)) name = identity?.Name ?? $"FIFA Player #{id}";
            var birthValue = I(player,"birthdate");
            var birth = birthValue > 0 ? new DateTime(1582,10,15).AddDays(birthValue) : careerDate.AddYears(-24);
            var age = Math.Max(15, careerDate.Year-birth.Year-(careerDate.Date<birth.AddYears(careerDate.Year-birth.Year).Date?1:0));
            result.Add(new(id,name,identity?.Nationality ?? $"FIFA nationality #{I(player,"nationality")}",age,
                PositionName(I(player,"preferredposition1")),I(link,"jerseynumber"),I(player,"overallrating"),
                I(link,"form"),I(link,"injury") != 0));
        }
        return result;
    }

    private static Fifa18DetectedFixture? DetectNextFixture(Fifa18SaveData data, Fifa18SyncState state,
        string clubName, int clubId)
    {
        return data.Table("career_news")
            .Where(r => I(r,"date") > state.LatestRatingDate &&
                        (state.CareerDate <= 0 || I(r,"date") <= state.CareerDate))
            .OrderByDescending(r => I(r,"date"))
            .Select(r => ParseFixturePreview(S(r,"title"),I(r,"date"),clubName,clubId,S(r,"body")))
            .FirstOrDefault(x => x is not null);
    }

    private static Fifa18DetectedMatch? DetectMatch(Fifa18SaveData data,Fifa18SyncState state,Fifa18SyncState? previous,
        string playerName,string clubName,int clubId,IReadOnlyDictionary<string,object>? rating,List<string> diagnostics)
    {
        if (rating is null || state.LatestRatingKey < 0 || state.LatestRatingDate <= 0)
        { diagnostics.Add("No player match-rating record is available yet."); return null; }
        var date = ParseFifaDate(state.LatestRatingDate) ?? DateTime.Today;
        var report = data.Table("career_news").Where(r => I(r,"date") == state.LatestRatingDate)
            .Select(r => ParseMatchReport(S(r,"title"),S(r,"body"),clubName)).FirstOrDefault(x => x is not null);
        var sameSeason=previous is not null&&previous.Season==state.Season;var appearanceDelta=sameSeason?state.Appearances-previous!.Appearances:0;var singleMatch=sameSeason&&appearanceDelta==1&&state.LatestRatingDate>previous!.LatestRatingDate;
        var goals = singleMatch ? Math.Max(0,state.Goals-previous!.Goals) : 0;
        var assists = singleMatch ? Math.Max(0,state.Assists-previous!.Assists) : 0;
        var yellow = singleMatch && state.YellowCards>previous!.YellowCards;
        var red = singleMatch && state.RedCards>previous!.RedCards;
        var minutes = Math.Max(0,I(rating,"minsplayed"));
        var eventKey = $"p{state.PlayerId}:rating:{state.LatestRatingKey}:date:{state.LatestRatingDate}";
        var confidence = report is null ? 55 : singleMatch ? 96 : previous is null ? 78 : 72;
        var evidence = report is null
            ? $"FIFA save rating history: {minutes} minutes, rating {I(rating,"rating")}. Opponent and score require review."
            : $"FIFA save and generated match report: {report.Evidence}";
        if (previous is null) diagnostics.Add("No previous imported snapshot: per-match goal, assist, and card deltas require review.");
        else if(!singleMatch)diagnostics.Add($"Exactly one new appearance could not be proven (appearance delta {appearanceDelta}); individual stat deltas require review.");
        if (report is null) diagnostics.Add("No matching FIFA match-review article: opponent and score could not be proven.");
        return new(eventKey,date.ToString("yyyy-MM-dd"),report?.Competition??"Career match",report?.Opponent??"Review opponent",
            report?.IsHome??true,report?.TeamScore??0,report?.OpponentScore??0,true,minutes,goals,assists,
            I(rating,"rating"),yellow,red,confidence,evidence,!singleMatch||report is null);
    }

    public static MatchReport? ParseMatchReport(string title,string body,string clubName)
    {
        var titleMatch=ReviewTitle().Match(title);
        if(!titleMatch.Success)return null;
        var competition=titleMatch.Groups["competition"].Value.Trim();
        var home=titleMatch.Groups["home"].Value.Trim();var away=titleMatch.Groups["away"].Value.Trim();
        var isHome=Same(home,clubName);if(!isHome&&!Same(away,clubName))return null;
        var opponent=isHome?away:home;
        int? homeScore=null,awayScore=null;
        var win=Victory().Match(body);
        if(win.Success)
        {
            var winner=win.Groups["winner"].Value.Trim();var loser=win.Groups["loser"].Value.Trim();
            var winnerScore=int.Parse(win.Groups["winnerScore"].Value,CultureInfo.InvariantCulture);
            var loserScore=int.Parse(win.Groups["loserScore"].Value,CultureInfo.InvariantCulture);
            if(Same(winner,home)&&Same(loser,away)){homeScore=winnerScore;awayScore=loserScore;}
            else if(Same(winner,away)&&Same(loser,home)){homeScore=loserScore;awayScore=winnerScore;}
        }
        if(homeScore is null)
        {
            var score=Score().Match(body);if(score.Success){homeScore=int.Parse(score.Groups["a"].Value);awayScore=int.Parse(score.Groups["b"].Value);}
        }
        if(homeScore is null||awayScore is null)return null;
        return new(competition,opponent,isHome,isHome?homeScore.Value:awayScore.Value,isHome?awayScore.Value:homeScore.Value,
            $"{title}; {home} {homeScore}-{awayScore} {away}");
    }

    public static Fifa18DetectedFixture? ParseFixturePreview(string title, int date, string clubName, int clubId,string body="")
    {
        var match=PreviewTitle().Match(title);
        if(!match.Success)return null;
        var competition=match.Groups["competition"].Value.Trim();
        var home=match.Groups["home"].Value.Trim();var away=match.Groups["away"].Value.Trim();
        var isHome=Same(home,clubName);if(!isHome&&!Same(away,clubName))return null;
        var parsedDate=ParseFifaDate(date);if(parsedDate is null)return null;
        var opponent=isHome?away:home;
        var evidence=string.IsNullOrWhiteSpace(body)?$"FIFA generated preview: {title}":$"FIFA generated preview: {title}. {body.Trim()}";
        return new($"club:{clubId}:fixture:{date}:{Normalize(opponent).ToLowerInvariant()}",parsedDate.Value.ToString("yyyy-MM-dd"),
            competition,opponent,isHome,90,evidence);
    }

    private static bool Same(string a,string b)=>string.Equals(Normalize(a),Normalize(b),StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string x)=>Regex.Replace(x.Trim().TrimEnd('.'),@"\s+"," ");
    private static string JoinName(string first,string last)=>string.Join(" ",new[]{first,last}.Where(x=>!string.IsNullOrWhiteSpace(x))).Trim();
    private static int I(IReadOnlyDictionary<string,object>? row,string key,int fallback=0)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToInt32(x,CultureInfo.InvariantCulture):fallback;
    private static long L(IReadOnlyDictionary<string,object>? row,string key,long fallback=0)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToInt64(x,CultureInfo.InvariantCulture):fallback;
    private static string S(IReadOnlyDictionary<string,object>? row,string key)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToString(x,CultureInfo.InvariantCulture)??"":"";
    private static DateTime? ParseFifaDate(int value)=>DateTime.TryParseExact(value.ToString(CultureInfo.InvariantCulture),"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    public static string PositionName(int id)=>id switch{0=>"GK",1=>"SW",2=>"RWB",3=>"RB",4=>"RCB",5=>"CB",6=>"LCB",7=>"LB",8=>"LWB",9=>"RDM",10=>"CDM",11=>"LDM",12=>"RM",13=>"RCM",14=>"CM",15=>"LCM",16=>"LM",17=>"RAM",18=>"CAM",19=>"LAM",20=>"RF",21=>"CF",22=>"LF",23=>"RW",24=>"RS",25=>"ST",26=>"LS",27=>"LW",28=>"SUB",29=>"RES",_=>$"POS {id}"};

    [GeneratedRegex(@"^(?<competition>.+?)\s+Review:\s*(?<home>.+?)\s+vs\s+(?<away>.+?)\s*$",RegexOptions.IgnoreCase)] private static partial Regex ReviewTitle();
    [GeneratedRegex(@"^(?<competition>.+?)\s+Preview:\s*(?<home>.+?)\s+vs\s+(?<away>.+?)\s*$",RegexOptions.IgnoreCase)] private static partial Regex PreviewTitle();
    [GeneratedRegex(@"(?<winner>[^.\r\n]+?)\s+were victorious\s+(?<winnerScore>\d+)\s*[-–]\s*(?<loserScore>\d+)\s+over\s+(?<loser>[^.\r\n]+?)(?:\s+in\s+|[.,\r\n])",RegexOptions.IgnoreCase)] private static partial Regex Victory();
    [GeneratedRegex(@"\b(?<a>\d+)\s*[-–]\s*(?<b>\d+)\b")] private static partial Regex Score();
}

public sealed record MatchReport(string Competition,string Opponent,bool IsHome,int TeamScore,int OpponentScore,string Evidence);
