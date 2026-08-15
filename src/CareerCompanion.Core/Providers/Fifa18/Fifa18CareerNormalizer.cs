using CareerCompanion.Core.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed partial class Fifa18CareerNormalizer(Fifa18PlayerNameResolver? names = null)
{
    private readonly Fifa18PlayerNameResolver _names = names ?? new();

    public Fifa18ParsedCareer Normalize(Fifa18SaveData data, string sourcePath, string fingerprint, Fifa18SyncState? previous = null,
        IReadOnlyList<CachedProviderArticle>? rememberedArticles = null)
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
        var nationalTeamId=I(user,"nationalteamid");var nationalTeamName=nationalTeamId>0?S(data.Table("teams").FirstOrDefault(r=>I(r,"teamid")==nationalTeamId),"teamname"):"";
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
            .Where(r => I(r,"userid") == userId && I(r,"teamid") == clubId).OrderByDescending(r => I(r,"season")).FirstOrDefault()
            ?? data.Table("career_playasplayerhistory").Where(r => I(r,"userid") == userId).OrderByDescending(r => I(r,"season")).FirstOrDefault();
        var rating = data.Table("career_playermatchratinghistory")
            .Where(r => I(r,"playerid") == playerId).OrderByDescending(r => I(r,"date"))
            .ThenByDescending(r => I(r,"artificialkey")).FirstOrDefault();
        var clubLink = data.Table("teamplayerlinks").FirstOrDefault(r => I(r,"playerid") == playerId && I(r,"teamid") == clubId);
        var state = new Fifa18SyncState(playerId,clubId,currentDate,seasonNumber,I(history,"appearances"),I(history,"goals"),
            I(history,"assists"),I(history,"totalyellows"),I(history,"totalreds"),I(rating,"artificialkey"),I(rating,"date"));
        var nameOf = BuildNameResolver(data);
        var appearances = BuildAppearances(data, playerId);
        var clubPlayerIds = data.Table("teamplayerlinks").Where(r=>I(r,"teamid")==clubId).Select(r=>I(r,"playerid")).ToHashSet();
        clubPlayerIds.Add(playerId);
        var matchDays = BuildMatchDays(data, nameOf, clubPlayerIds);
        var articles = BuildArticleIndex(data, rememberedArticles);
        var teamNames = BuildTeamNames(data);
        var clubMatches = DetectClubMatches(data, articles, teamNames, state, previous, appearances, matchDays, clubName, clubId, diagnostics);
        var clubMatch = clubMatches.LastOrDefault();
        var internationalMatch = DetectInternationalMatch(data,state,playerId,nationalTeamId,nationalTeamName,diagnostics);
        var detected = Latest(clubMatch,internationalMatch);
        var pending = internationalMatch is null ? clubMatches
            : clubMatches.Concat([internationalMatch]).OrderBy(x => x.Date, StringComparer.Ordinal).ToList();
        var missedClubMatches = CountMissedClubMatches(matchDays, appearances, playerId, previous);
        var articleCache = articles.Where(x=>x.Date is not null).Select(x=>new CachedProviderArticle(ArticleKey(x),
            x.Date!.Value.ToString("yyyy-MM-dd"),x.Own,x.Related,x.Title,x.Body)).DistinctBy(x=>x.Key).ToList();
        var resolvedResults = ResolveRecentResults(articles, teamNames, appearances, clubId, clubName, careerDateForResults(currentDate));
        var birthDateValue = I(player,"birthdate");
        var birthDate = birthDateValue > 0 ? new DateTime(1582,10,15).AddDays(birthDateValue) : new DateTime(1999,1,1);
        var careerDate = ParseFifaDate(currentDate) ?? DateTime.Today;
        var age = Math.Max(15, careerDate.Year-birthDate.Year-(careerDate.Date<birthDate.AddYears(careerDate.Year-birthDate.Year).Date?1:0));
        var careerStartYear = (ParseFifaDate(startDate) ?? careerDate).Year;
        var startYear=careerStartYear+seasonNumber-1;
        var squad = NormalizeSquad(data, clubId, playerId, clubName, careerDate);
        var playerAvailability = DetectPlayerAvailability(data, playerId, playerName, currentDate);
        var transferRequest = DetectTransferRequest(data, playerId, playerName, currentDate);
        var nextFixture = DetectNextFixture(data, articles, teamNames, state, clubName, clubId,nationalTeamId,nationalTeamName,internationalMatch,playerName,playerAvailability,appearances);
        var opponentScout = nextFixture is null ? null : BuildOpponentScout(data,nextFixture.Opponent,clubId,careerDate);
        var manager=data.Table("managers").FirstOrDefault(r=>I(r,"teamid")==clubId);var managerName=JoinName(S(manager,"firstname"),S(manager,"surname"));
        var agentName=S(user,"agentname");
        var worldNews=data.Table("career_news").Where(r=>I(r,"date")>0&&!string.IsNullOrWhiteSpace(S(r,"title")))
            .OrderByDescending(r=>I(r,"date")).Take(60).Select(r=>
            {
                var date=I(r,"date");var title=S(r,"title");var body=S(r,"body");
                var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{date}|{title}|{body}"))).ToLowerInvariant();
                var importance=Math.Clamp(15+I(r,"importance")*14,15,85);
                var aboutPlayer=I(r,"playerid")==playerId||MentionsPlayer(title+" "+body,playerName);
                var aboutClub=I(r,"teamid")==clubId||TeamId(r,"relatedteamid")==clubId;
                return new Fifa18WorldNews(key,(ParseFifaDate(date)??careerDate).ToString("yyyy-MM-dd"),title,body,importance,aboutPlayer,aboutClub);
            }).ToList();
        var squadCount = squad.Count + 1;
        diagnostics.Add($"Parsed {data.TableNames.Count} supported tables; squad links={squadCount}.");
        return new(sourcePath,fingerprint,File.GetLastWriteTimeUtc(sourcePath),playerName,playerId,nationalityId,nationalityName,age,
            clubName,clubId,leagueId,leagueName,$"{startYear}/{(startYear+1)%100:00}",careerDate.ToString("yyyy-MM-dd"),
            PositionName(I(play,"position")),I(clubLink,"jerseynumber"),state,detected,squad,nextFixture,squadCount,diagnostics,
            I(history,"overall"),I(clubLink,"form"),playerAvailability=="Injured",managerName,agentName,worldNews,
            playerAvailability=="Injured",nationalTeamId,nationalTeamName,opponentScout,playerAvailability,transferRequest,
            pending,appearances,missedClubMatches,articleCache,resolvedResults);
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

    private static Fifa18DetectedFixture? DetectNextFixture(Fifa18SaveData data,IReadOnlyList<NewsArticle> articles,
        IReadOnlyDictionary<int,string> teamNames, Fifa18SyncState state,
        string clubName, int clubId,int nationalTeamId,string nationalTeamName,Fifa18DetectedMatch? internationalMatch,
        string playerName,string playerAvailability,IReadOnlyList<Fifa18Appearance> appearances)
    {
        var clubFixture=DetectClubFixture(articles,teamNames,state,clubName,clubId,playerName,playerAvailability,appearances);
        var nationalFixture=nationalTeamId<=0?null:data.Table("career_news")
            .Where(r=>I(r,"teamid")==nationalTeamId&&I(r,"playerid")==state.PlayerId)
            .OrderByDescending(r=>I(r,"date"))
            .Select(r=>ParseInternationalFixture(S(r,"title"),S(r,"body"),I(r,"date"),nationalTeamId,nationalTeamName,playerName,playerAvailability))
            .FirstOrDefault(x=>x is not null && (internationalMatch is null || x.Date!=internationalMatch.Date));
        return Latest(clubFixture,nationalFixture);
    }

    /// <summary>
    /// The upcoming club fixture. A headline naming both clubs is used first because it also fixes the venue;
    /// otherwise the article's structured team id still identifies the opponent, and the venue stays unknown
    /// unless the report says plainly who is hosting.
    /// </summary>
    private static Fifa18DetectedFixture? DetectClubFixture(IReadOnlyList<NewsArticle> articles,
        IReadOnlyDictionary<int,string> teamNames,Fifa18SyncState state,string clubName,int clubId,
        string playerName,string availabilityHint,IReadOnlyList<Fifa18Appearance> appearances)
    {
        var played=appearances.Select(x=>x.Date).ToHashSet(StringComparer.Ordinal);
        var lastPlayed=ParseFifaDate(state.LatestRatingDate);
        var careerDate=ParseFifaDate(state.CareerDate);
        var candidates=articles
            .Where(x=>lastPlayed is null||x.Date!.Value>lastPlayed.Value)
            .Where(x=>careerDate is null||x.Date!.Value<=careerDate.Value.AddDays(10))
            .Where(x=>!played.Contains(x.Date!.Value.ToString("yyyy-MM-dd")))
            .Where(x=>x.Own==clubId||x.Related==clubId)
            .OrderByDescending(x=>x.Date).ToList();
        foreach(var article in candidates.OrderBy(x=>x.Headline() is null?1:0).ThenByDescending(x=>x.Date))
        {
            var date=article.Date!.Value;
            var availability=DetectFixtureAvailability(article.Title,article.Body,playerName,availabilityHint);
            if(article.Headline() is { } headline)
            {
                bool isHome;
                if(Same(headline.Home,clubName))isHome=true;else if(Same(headline.Away,clubName))isHome=false;else continue;
                var named=isHome?headline.Away:headline.Home;
                if(string.IsNullOrWhiteSpace(named)||Same(named,clubName))continue;
                return new($"club:{clubId}:fixture:{date:yyyyMMdd}:{Normalize(named).ToLowerInvariant()}",
                    date.ToString("yyyy-MM-dd"),headline.Competition,named,isHome,90,
                    $"FIFA preview: {article.Title}. {article.Body.Trim()}","Club",clubName,availability);
            }
            var opponentId=article.Own==clubId?article.Related:article.Own;
            if(opponentId<=0||opponentId==clubId)continue;
            var opponent=teamNames.GetValueOrDefault(opponentId,"");
            if(string.IsNullOrWhiteSpace(opponent)||Same(opponent,clubName))continue;
            if(!MentionsMatch(article.Title+" "+article.Body))continue;
            var venue=ReadVenue(article.Body,clubName,opponent);
            return new($"club:{clubId}:fixture:{date:yyyyMMdd}:{Normalize(opponent).ToLowerInvariant()}",
                date.ToString("yyyy-MM-dd"),CompetitionHint(article.Title,article.Body),opponent,venue??true,
                venue is null?70:80,$"FIFA preview: {article.Title}. {article.Body.Trim()}","Club",clubName,
                availability,venue is not null);
        }
        return null;
    }

    /// <summary>True when the club is hosting, false when it is travelling, null when the report does not say.</summary>
    private static bool? ReadVenue(string body,string clubName,string opponent)
    {
        if(Mentions(body,clubName,"host","hosts","welcome","welcomes","at home"))return true;
        if(Mentions(body,clubName,"visit","visits","travel to","travels to","away at","make the trip"))return false;
        if(Mentions(body,opponent,"host","hosts","welcome","welcomes"))return false;
        if(Mentions(body,opponent,"visit","visits","travel to","travels to"))return true;
        return null;
    }

    private static bool Mentions(string body,string team,params string[] verbs)
    {
        if(string.IsNullOrWhiteSpace(team))return false;
        var verbGroup=string.Join("|",verbs.Select(Regex.Escape));
        var pattern=BoundaryPattern(Regex.Escape(team),verbGroup);
        return Regex.IsMatch(body,pattern,RegexOptions.IgnoreCase);
    }

    /// <summary>Matches "&lt;team&gt; ... &lt;verb&gt;" inside one sentence, used to read who is hosting a fixture.</summary>
    private static string BoundaryPattern(string team,string verbs)
        =>@"\b"+team+@"\b[^.\r\n]{0,40}?\b(?:"+verbs+@")\b";

    private static Fifa18DetectedMatch? DetectInternationalMatch(Fifa18SaveData data,Fifa18SyncState state,
        int playerId,int nationalTeamId,string nationalTeamName,List<string> diagnostics)
    {
        if(nationalTeamId<=0||string.IsNullOrWhiteSpace(nationalTeamName))return null;
        var articles=data.Table("career_news").Where(r=>I(r,"teamid")==nationalTeamId).OrderByDescending(r=>I(r,"date")).ToList();
        var result=articles.FirstOrDefault(r=>I(r,"playerid")==playerId&&InternationalOutcome(S(r,"title"),S(r,"body"))!=0);
        if(result is null)return null;
        var date=I(result,"date");if(date<=state.LatestRatingDate)return null;
        var outcome=InternationalOutcome(S(result,"title"),S(result,"body"));
        var resultDate=ParseFifaDate(date)??DateTime.Today;var related=articles.Where(r=>ParseFifaDate(I(r,"date")) is { } articleDate&&articleDate<=resultDate&&articleDate>=resultDate.AddDays(-21)).ToList();
        var opponent=related.Select(r=>InternationalOpponent(S(r,"title")+" "+S(r,"body"),nationalTeamName)).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"Opponent unknown";
        var competition=related.Select(r=>InternationalCompetition(S(r,"title"))).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"International";
        var raw=$"{date}|{S(result,"title")}|{S(result,"body")}";var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..20];
        diagnostics.Add($"International appearance detected for {nationalTeamName}. FIFA exposed the appearance news but not the scoreline or individual performance fields; those fields remain unknown.");
        var evidence=$"FIFA national-team news: {S(result,"title")}. {S(result,"body").Trim()} Exact score, minutes, rating, goals, assists, cards, venue, and starter status were not exposed.";
        return new($"p{playerId}:national:{nationalTeamId}:news:{hash}",(ParseFifaDate(date)??DateTime.Today).ToString("yyyy-MM-dd"),competition,opponent,true,
            0,0,false,0,0,0,0,false,false,88,evidence,false,false,"International",nationalTeamName,false,false,-1,null,false);
    }

    private static Fifa18DetectedFixture? ParseInternationalFixture(string title,string body,int date,int teamId,string teamName,
        string playerName = "",string availabilityHint = "Unknown")
    {
        if(InternationalOutcome(title,body)!=0)return null;
        var opponent=InternationalOpponent(title+" "+body,teamName);if(string.IsNullOrWhiteSpace(opponent))return null;
        var parsedDate=ParseFifaDate(date);if(parsedDate is null)return null;
        var competition=InternationalCompetition(title);if(string.IsNullOrWhiteSpace(competition))competition="International";
        var availability=DetectFixtureAvailability(title,body,playerName,availabilityHint);
        return new($"national:{teamId}:fixture:{date}:{Normalize(opponent).ToLowerInvariant()}",parsedDate.Value.ToString("yyyy-MM-dd"),competition,opponent,true,76,
            $"FIFA national-team news: {title}. {body.Trim()}","International",teamName,availability);
    }

    private static int InternationalOutcome(string title,string body)
    {
        var text=title+" "+body;var appeared=ContainsAny(text,"international career","debut showing","international appearance","newly capped","international debut");
        if(!appeared||ContainsAny(text,"prepares for","looming","eager to see"))return 0;
        if(ContainsAny(text,"victory","winners","ran out worthy winners","won "))return 1;
        if(ContainsAny(text,"defeat","lost ","losing debut"))return -1;
        if(ContainsAny(text,"draw","stalemate"))return 2;
        return 0;
    }

    private static string InternationalOpponent(string text,string teamName)
    {
        var patterns=new[]{@"\bsoon to face\s+(?<opponent>[^,.;\r\n]+)",@"\bface(?:s|d)?\s+(?<opponent>[^,.;\r\n]+)",@"\bagainst\s+(?<opponent>[^,.;\r\n]+)"};
        foreach(var pattern in patterns){var m=Regex.Match(text,pattern,RegexOptions.IgnoreCase);if(m.Success){var value=Normalize(m.Groups["opponent"].Value);if(!Same(value,teamName))return value;}}
        return "";
    }

    private static string InternationalCompetition(string title)
    {
        var m=Regex.Match(title,@"\bSquad for\s+(?<competition>.+)$",RegexOptions.IgnoreCase);return m.Success?Normalize(m.Groups["competition"].Value):"";
    }

    private static bool ContainsAny(string text,params string[] values)=>values.Any(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));
    private static T? Latest<T>(T? a,T? b) where T:class
    {
        if(a is null)return b;if(b is null)return a;
        var aDate=a switch{Fifa18DetectedMatch x=>x.Date,Fifa18DetectedFixture x=>x.Date,_=>""};var bDate=b switch{Fifa18DetectedMatch x=>x.Date,Fifa18DetectedFixture x=>x.Date,_=>""};return string.CompareOrdinal(aDate,bDate)>=0?a:b;
    }

    private Func<int,string> BuildNameResolver(Fifa18SaveData data)
    {
        var players=data.Table("players").GroupBy(r=>I(r,"playerid")).ToDictionary(x=>x.Key,x=>x.First());
        var edited=data.Table("editedplayernames").GroupBy(r=>I(r,"playerid")).ToDictionary(x=>x.Key,x=>x.First());
        var dynamicNames=data.Table("dcplayernames").Where(r=>I(r,"nameid")>0).GroupBy(r=>I(r,"nameid")).ToDictionary(x=>x.Key,x=>S(x.First(),"name"));
        var cache=new Dictionary<int,string>();
        return id=>
        {
            if(cache.TryGetValue(id,out var cached))return cached;
            edited.TryGetValue(id,out var custom);players.TryGetValue(id,out var player);
            var name=JoinName(S(custom,"firstname"),S(custom,"surname"));
            if(string.IsNullOrWhiteSpace(name))name=S(custom,"commonname");
            if(string.IsNullOrWhiteSpace(name)&&player is not null&&dynamicNames.TryGetValue(I(player,"commonnameid"),out var common))name=common;
            if(string.IsNullOrWhiteSpace(name)&&player is not null)name=JoinName(dynamicNames.GetValueOrDefault(I(player,"firstnameid"))??"",dynamicNames.GetValueOrDefault(I(player,"lastnameid"))??"");
            if(string.IsNullOrWhiteSpace(name))name=_names.Find(id)?.Name??$"FIFA Player #{id}";
            return cache[id]=name;
        };
    }

    /// <summary>The career player's complete appearance log, oldest first.</summary>
    private static IReadOnlyList<Fifa18Appearance> BuildAppearances(Fifa18SaveData data,int playerId)
        => data.Table("career_playermatchratinghistory")
            .Where(r=>I(r,"playerid")==playerId&&I(r,"date")>0)
            .OrderBy(r=>I(r,"date")).ThenBy(r=>I(r,"artificialkey"))
            .Select(r=>new Fifa18Appearance(I(r,"artificialkey"),FifaDateText(I(r,"date")),
                Math.Max(0,I(r,"minsplayed")),I(r,"rating"),I(r,"position")))
            .ToList();

    /// <summary>
    /// Club matchdays reconstructed from the rating history. FIFA writes one row per player who
    /// appeared, so a real team sheet has many rows on the same date. Sparse dates are unrelated
    /// records and are ignored rather than treated as matches.
    /// </summary>
    private static IReadOnlyList<Fifa18MatchDay> BuildMatchDays(Fifa18SaveData data,Func<int,string> nameOf,IReadOnlySet<int> clubPlayerIds)
        => data.Table("career_playermatchratinghistory").Where(r=>I(r,"date")>0)
            .GroupBy(r=>I(r,"date"))
            .Where(g=>g.Count()>=MinimumTeamSheetSize&&g.Count(r=>clubPlayerIds.Contains(I(r,"playerid")))>=MinimumClubOverlap)
            .OrderBy(g=>g.Key)
            .Select(g=>new Fifa18MatchDay(FifaDateText(g.Key),g.Max(r=>I(r,"artificialkey")),
                g.Select(r=>new Fifa18SquadPerformance(I(r,"playerid"),nameOf(I(r,"playerid")),
                    PositionName(I(r,"position")),I(r,"position"),Math.Max(0,I(r,"minsplayed")),I(r,"rating")))
                 .OrderByDescending(x=>x.Rating).ThenByDescending(x=>x.Minutes).ToList()))
            .ToList();

    private const int MinimumTeamSheetSize=7;
    // A matchday only belongs to the current club when enough of its current squad appears in it,
    // so fixtures played before a transfer are not mistaken for matches the player was left out of.
    private const int MinimumClubOverlap=4;

    private static int CountMissedClubMatches(IReadOnlyList<Fifa18MatchDay> matchDays,IReadOnlyList<Fifa18Appearance> appearances,
        int playerId,Fifa18SyncState? previous)
    {
        if(previous is null)return 0;
        var baseline=FifaDateText(previous.LatestRatingDate);
        var played=appearances.Select(x=>x.Date).ToHashSet(StringComparer.Ordinal);
        return matchDays.Count(day=>string.CompareOrdinal(day.Date,baseline)>0&&!played.Contains(day.Date)&&day.For(playerId) is null);
    }

    /// <summary>
    /// Every appearance recorded after the previously imported one, oldest first. The rating history
    /// is a complete log, so "exactly one new appearance" is a fact rather than an inference from
    /// cumulative counters. Only a single new appearance lets per-match goals, assists, and cards be
    /// attributed from season totals; anything else keeps those values unknown.
    /// </summary>
    private static IReadOnlyList<Fifa18DetectedMatch> DetectClubMatches(Fifa18SaveData data,IReadOnlyList<NewsArticle> articles,
        IReadOnlyDictionary<int,string> teamNames,Fifa18SyncState state,
        Fifa18SyncState? previous,IReadOnlyList<Fifa18Appearance> appearances,IReadOnlyList<Fifa18MatchDay> matchDays,
        string clubName,int clubId,List<string> diagnostics)
    {
        if(appearances.Count==0){diagnostics.Add("No player match-rating record is available yet.");return [];}
        var baselineDate=previous is null?"":FifaDateText(previous.LatestRatingDate);
        var baselineKey=previous?.LatestRatingKey??-1;
        var fresh=previous is null
            ? appearances.TakeLast(1).ToList()
            : appearances.Where(x=>string.CompareOrdinal(x.Date,baselineDate)>0||
                                   (string.CompareOrdinal(x.Date,baselineDate)==0&&x.RatingKey>baselineKey)).ToList();
        if(fresh.Count==0)return [];
        if(previous is null&&appearances.Count>1)
            diagnostics.Add($"First synchronization with {appearances.Count} appearances already played. Only the most recent one is offered; earlier matches stay as career history.");
        else if(fresh.Count>1)
            diagnostics.Add($"{fresh.Count} appearances were played since the last synchronization. Each is imported in order, but FIFA only stores season totals, so per-match goals, assists, and cards cannot be split between them.");

        var sameSeason=previous is not null&&previous.Season==state.Season;
        // The season counters must describe this one appearance and nothing else. Requiring both the
        // rating log and FIFA's own appearance counter to agree stops a first link from crediting a whole
        // season's goals to a single match.
        var firstEver=previous is null&&appearances.Count==1&&state.Appearances<=1;
        var seasonOpening=previous is not null&&previous.Season<state.Season;
        var priorGoals=sameSeason?previous!.Goals:0;var priorAssists=sameSeason?previous!.Assists:0;
        var priorYellow=sameSeason?previous!.YellowCards:0;var priorRed=sameSeason?previous!.RedCards:0;
        // Season totals can only be attributed to one match, and only when the totals and the
        // appearance both belong to the same season the counters describe.
        var attributable=fresh.Count==1&&(firstEver||sameSeason||(seasonOpening&&state.Appearances==1));
        if(!attributable&&fresh.Count==1)
            diagnostics.Add("Season totals could not be matched to this appearance, so goals, assists, and cards stay unknown.");

        var rivals=BuildRivals(data,clubId);
        var results=new List<Fifa18DetectedMatch>();
        foreach(var appearance in fresh)
        {
            var last=appearance.RatingKey==fresh[^1].RatingKey&&appearance.Date==fresh[^1].Date;
            var article=FindMatchArticle(articles,appearance.Date,clubId,clubName,teamNames);
            var goals=attributable&&last?Math.Max(0,state.Goals-priorGoals):0;
            var assists=attributable&&last?Math.Max(0,state.Assists-priorAssists):0;
            var yellow=attributable&&last&&state.YellowCards>priorYellow;
            var red=attributable&&last&&state.RedCards>priorRed;
            var day=matchDays.FirstOrDefault(x=>x.Date==appearance.Date);
            var performances=day?.Squad??[];
            var opponentId=article?.OpponentTeamId??-1;
            var derby=opponentId>0&&rivals.Contains(opponentId);
            var evidence=BuildEvidence(appearance,article,attributable&&last,performances.Count,state,fresh.Count);
            var confidence=Confidence(article,attributable&&last);
            var needsReview=article is null||!article.OpponentKnown||!(attributable&&last);
            if(article is null)
                diagnostics.Add($"No FIFA article identified the opponent for the match on {appearance.Date}.");
            else if(!article.ScoreKnown)
                diagnostics.Add($"FIFA named {article.Opponent} on {appearance.Date} but did not publish a final scoreline.");
            results.Add(new($"p{state.PlayerId}:rating:{appearance.RatingKey}:date:{appearance.Date.Replace("-","")}",
                appearance.Date,article?.Competition??"Career match",article?.Opponent??"Opponent unknown",
                article?.IsHome??true,article?.TeamScore??0,article?.OpponentScore??0,appearance.Started,
                appearance.Minutes,goals,assists,appearance.Rating,yellow,red,confidence,evidence,needsReview,
                true,"Club",clubName,article?.ScoreKnown??false,derby,opponentId,performances,article?.IsHomeKnown??false,appearance.RatingKey));
        }
        return results;
    }

    private static int Confidence(Fifa18MatchArticle? article,bool attributable)
    {
        if(article is null)return attributable?70:50;
        if(!article.OpponentKnown)return attributable?72:52;
        if(!article.ScoreKnown)return attributable?86:62;
        return attributable?97:74;
    }

    private static string BuildEvidence(Fifa18Appearance appearance,Fifa18MatchArticle? article,bool attributable,
        int squadSize,Fifa18SyncState state,int newAppearances)
    {
        var parts=new List<string>
        {
            $"FIFA appearance log: {(appearance.Started?"started":"came off the bench")} at {appearance.Position}, {appearance.Minutes} minutes, match rating {appearance.Rating}"
        };
        if(article is not null)parts.Add(article.Evidence);
        else parts.Add("FIFA did not publish an article naming the opponent for this date.");
        if(squadSize>0)parts.Add($"{squadSize} rated team-mate performances recorded for the same matchday");
        parts.Add(attributable
            ? "Goals, assists, and cards were taken from the change in FIFA season totals across exactly one new appearance"
            : $"FIFA only stores season totals, currently {state.Goals} goals and {state.Assists} assists across {state.Appearances} appearances, and {(newAppearances>1?$"{newAppearances} new appearances arrived together":"no earlier total is on record")}, so this match's own goals and assists cannot be worked out. Enter them and everything else is already filled in");
        return string.Join(". ",parts)+".";
    }

    private static IReadOnlyDictionary<int,string> BuildTeamNames(Fifa18SaveData data)
    {
        var names=new Dictionary<int,string>();
        foreach(var row in data.Table("teams")){var id=I(row,"teamid");var name=S(row,"teamname");if(id>0&&!string.IsNullOrWhiteSpace(name))names[id]=name;}
        return names;
    }

    private static IReadOnlySet<int> BuildRivals(Fifa18SaveData data,int clubId)
    {
        var rivals=new HashSet<int>();
        foreach(var row in data.Table("rivals"))
        {
            var a=I(row,"teamid");var b=I(row,"rivalteamid");
            if(a==clubId&&b>0)rivals.Add(b);else if(b==clubId&&a>0)rivals.Add(a);
        }
        var club=data.Table("teams").FirstOrDefault(r=>I(r,"teamid")==clubId);
        var primary=I(club,"rivalteam",-1);if(primary>0)rivals.Add(primary);
        return rivals;
    }

    /// <summary>
    /// Finds the FIFA article covering a club match and reads the opponent from the article's
    /// structured team ids. Names in the headline are only used to decide the venue and as a
    /// fallback when the ids are missing, so a renamed or unlicensed club still resolves.
    /// </summary>
    /// <summary>
    /// Every article available to opponent resolution: the save's own rolling feed plus reports remembered
    /// from earlier scans, because FIFA keeps only the most recent stories.
    /// </summary>
    private static IReadOnlyList<NewsArticle> BuildArticleIndex(Fifa18SaveData data,IReadOnlyList<CachedProviderArticle>? remembered)
    {
        var index=data.Table("career_news")
            .Select(row=>new NewsArticle(ParseFifaDate(I(row,"date")),I(row,"teamid"),TeamId(row,"relatedteamid"),
                S(row,"title"),S(row,"body"))).ToList();
        if(remembered is not null)
            index.AddRange(remembered.Select(x=>new NewsArticle(DateTime.TryParse(x.Date,out var date)?date:null,
                x.TeamId,x.RelatedTeamId,x.Title,x.Body)));
        return index.Where(x=>x.Date is not null&&!string.IsNullOrWhiteSpace(x.Title)).DistinctBy(ArticleKey).ToList();
    }

    private static string ArticleKey(NewsArticle article)
        =>$"{article.Date:yyyy-MM-dd}|{article.Own}|{article.Title}";

    private static Fifa18MatchArticle? FindMatchArticle(IReadOnlyList<NewsArticle> articles,string matchDate,int clubId,string clubName,
        IReadOnlyDictionary<int,string> teamNames)
    {
        if(!DateTime.TryParse(matchDate,out var date))return null;
        var window=articles.Where(x=>Math.Abs((x.Date!.Value-date).TotalDays)<=3)
            .OrderBy(x=>Math.Abs((x.Date!.Value-date).TotalDays)).ToList();
        return ResolveFromHeadline(window,clubId,clubName,teamNames)
            ?? ResolveFromRelatedTeam(window,clubId,clubName,teamNames)
            ?? ResolveFromOpponentArticle(window,clubId,clubName,teamNames);
    }

    /// <summary>A headline that names both clubs is the strongest evidence: it fixes the venue too.</summary>
    private static Fifa18MatchArticle? ResolveFromHeadline(IReadOnlyList<NewsArticle> window,int clubId,string clubName,
        IReadOnlyDictionary<int,string> teamNames)
    {
        foreach(var article in window.OrderBy(x=>x.Headline() is {IsPreview:false}?0:1))
        {
            if(article.Headline() is not { } headline)continue;
            var relatedName=article.Own==clubId&&article.Related>0&&article.Related!=clubId
                ?teamNames.GetValueOrDefault(article.Related,""):"";
            bool isHome;
            if(!string.IsNullOrWhiteSpace(relatedName)&&Same(headline.Away,relatedName))isHome=true;
            else if(!string.IsNullOrWhiteSpace(relatedName)&&Same(headline.Home,relatedName))isHome=false;
            else if(Same(headline.Home,clubName))isHome=true;
            else if(Same(headline.Away,clubName))isHome=false;
            else continue;
            var opponentName=string.IsNullOrWhiteSpace(relatedName)?(isHome?headline.Away:headline.Home):relatedName;
            if(string.IsNullOrWhiteSpace(opponentName)||Same(opponentName,clubName))continue;
            var opponentId=article.Own==clubId&&article.Related>0&&article.Related!=clubId?article.Related:LookupTeamId(teamNames,opponentName);
            var (teamScore,opponentScore,scoreKnown)=headline.HomeScore is { } homeGoals&&headline.AwayScore is { } awayGoals
                ?(isHome?homeGoals:awayGoals,isHome?awayGoals:homeGoals,true)
                :ResolveScore(article.Title,article.Body,clubName,opponentName,isHome,headline.Home,headline.Away);
            return new(headline.Competition,opponentName,opponentId,isHome,teamScore,opponentScore,scoreKnown,
                Describe(article.Title,clubName,opponentName,teamScore,opponentScore,scoreKnown,headline.Competition),true);
        }
        return null;
    }

    /// <summary>An article about our club that carries a different related club id names the opponent, but not the venue.</summary>
    private static Fifa18MatchArticle? ResolveFromRelatedTeam(IReadOnlyList<NewsArticle> window,int clubId,string clubName,
        IReadOnlyDictionary<int,string> teamNames)
    {
        foreach(var article in window.Where(x=>x.Own==clubId&&x.Related>0&&x.Related!=clubId))
        {
            var opponentName=teamNames.GetValueOrDefault(article.Related,"");
            if(string.IsNullOrWhiteSpace(opponentName)||Same(opponentName,clubName))continue;
            if(!MentionsMatch(article.Title+" "+article.Body))continue;
            var (teamScore,opponentScore,scoreKnown)=ResolveScore(article.Title,article.Body,clubName,opponentName,true,clubName,opponentName);
            return new(CompetitionHint(article.Title,article.Body),opponentName,article.Related,true,teamScore,opponentScore,scoreKnown,
                Describe(article.Title,clubName,opponentName,teamScore,opponentScore,scoreKnown,null),false);
        }
        return null;
    }

    /// <summary>
    /// The opponent's own preview or report often names our club without FIFA linking the two team ids.
    /// The article's team id is still a save fact, so it identifies the opponent even though the venue stays unknown.
    /// </summary>
    private static Fifa18MatchArticle? ResolveFromOpponentArticle(IReadOnlyList<NewsArticle> window,int clubId,string clubName,
        IReadOnlyDictionary<int,string> teamNames)
    {
        foreach(var article in window.Where(x=>x.Own>0&&x.Own!=clubId))
        {
            var text=article.Title+" "+article.Body;
            if(!Same(clubName,"")&&!ContainsTeamName(text,clubName))continue;
            if(!MentionsMatch(text))continue;
            var opponentName=teamNames.GetValueOrDefault(article.Own,"");
            if(string.IsNullOrWhiteSpace(opponentName)||Same(opponentName,clubName))continue;
            var (teamScore,opponentScore,scoreKnown)=ResolveScore(article.Title,article.Body,clubName,opponentName,true,clubName,opponentName);
            return new(CompetitionHint(article.Title,article.Body),opponentName,article.Own,true,teamScore,opponentScore,scoreKnown,
                Describe(article.Title,clubName,opponentName,teamScore,opponentScore,scoreKnown,null),false);
        }
        return null;
    }

    private static string Describe(string title,string clubName,string opponentName,int teamScore,int opponentScore,
        bool scoreKnown,string? competition)
        =>scoreKnown
            ?$"FIFA article \"{title}\" recorded {clubName} {teamScore}-{opponentScore} {opponentName}"
            :$"FIFA article \"{title}\" identified {opponentName}{(string.IsNullOrWhiteSpace(competition)?"":$" in {competition}")} but published no final scoreline";

    private static bool MentionsMatch(string text)
        =>ContainsAny(text,"vs","versus"," face","facing","opponent","final","fixture","match","clash","host"," visit",
            "travel to","meet ","tie ","kick-off","kick off","review","preview","encounter","round of games");
    private static bool ContainsTeamName(string text,string name)
    {
        if(string.IsNullOrWhiteSpace(name))return false;
        if(text.Contains(name,StringComparison.OrdinalIgnoreCase))return true;
        var core=NormalizeTeam(name);
        return core.Length>=4&&text.Contains(core,StringComparison.OrdinalIgnoreCase);
    }
    private static int LookupTeamId(IReadOnlyDictionary<int,string> teamNames,string name)
    {
        foreach(var entry in teamNames)if(Same(entry.Value,name))return entry.Key;
        return -1;
    }
    private static string CompetitionHint(string title,string body)
    {
        foreach(var known in new[]{"Supercopa","Champions' Cup","Champions Cup","Euro League","Champions Trophy","European Int’l Cup","European Int'l Cup","Cup Final","Cup","League"})
            if(title.Contains(known,StringComparison.OrdinalIgnoreCase)||body.Contains(known,StringComparison.OrdinalIgnoreCase))return known;
        return "Career match";
    }

    /// <summary>Scores that FIFA published for appearances already in the career, used to fill in a score that arrived late.</summary>
    private static IReadOnlyList<Fifa18ResolvedResult> ResolveRecentResults(IReadOnlyList<NewsArticle> articles,
        IReadOnlyDictionary<int,string> teamNames,IReadOnlyList<Fifa18Appearance> appearances,int clubId,string clubName,DateTime careerDate)
    {
        var results=new List<Fifa18ResolvedResult>();
        foreach(var appearance in appearances.Where(x=>DateTime.TryParse(x.Date,out var d)&&(careerDate-d).TotalDays<=120).TakeLast(20))
        {
            var article=FindMatchArticle(articles,appearance.Date,clubId,clubName,teamNames);
            if(article is null||!article.ScoreKnown)continue;
            results.Add(new(appearance.Date,article.Opponent,article.TeamScore,article.OpponentScore,article.Evidence));
        }
        return results;
    }

    private static DateTime careerDateForResults(int currentDate)=>ParseFifaDate(currentDate)??DateTime.Today;

    private sealed record NewsArticle(DateTime? Date,int Own,int Related,string Title,string Body)
    {
        private FixtureHeadline? _headline;private bool _parsed;
        public FixtureHeadline? Headline(){if(!_parsed){_parsed=true;_headline=ParseFixtureHeadline(Title);}return _headline;}
    }
    internal sealed record FixtureHeadline(string Competition,string Home,string Away,bool IsPreview,int? HomeScore,int? AwayScore);

    /// <summary>Reads the FIFA headline formats that name both clubs: "Comp Review: A vs B", "Comp Preview: A vs B", and "A 1-0 B".</summary>
    internal static FixtureHeadline? ParseFixtureHeadline(string title)
    {
        var versus=ReviewTitle().Match(title);
        if(versus.Success)
        {
            var competition=versus.Groups["competition"].Value.Trim();var preview=false;
            foreach(var suffix in new[]{"Preview","Review","Report"})
                if(competition.EndsWith(" "+suffix,StringComparison.OrdinalIgnoreCase))
                {preview=suffix.Equals("Preview",StringComparison.OrdinalIgnoreCase);competition=competition[..^(suffix.Length+1)].Trim();}
            return new(competition.Length==0?"Career match":competition,versus.Groups["home"].Value.Trim(),
                versus.Groups["away"].Value.Trim(),preview,null,null);
        }
        var scoreline=ScorelineTitle().Match(title);
        if(scoreline.Success&&int.TryParse(scoreline.Groups["a"].Value,out var home)&&int.TryParse(scoreline.Groups["b"].Value,out var away))
            return new("Career match",scoreline.Groups["home"].Value.Trim(),scoreline.Groups["away"].Value.Trim(),false,home,away);
        return null;
    }

    private static int TeamId(IReadOnlyDictionary<string,object> row,string key)
    {
        var value=I(row,key,-1);
        // FIFA writes an all-ones value in the optional 19-bit id fields when they are not set.
        return value is <=0 or >=0x7FFFF?-1:value;
    }

    /// <summary>
    /// Reads a final score from an article. The digits alone are ambiguous, so the goal totals are
    /// only accepted once the winning side is established from the report's own wording. When the
    /// wording cannot settle it, the score stays unknown rather than being guessed.
    /// </summary>
    internal static (int TeamScore,int OpponentScore,bool Known) ResolveScore(string title,string body,string clubName,
        string opponentName,bool isHome,string homeName,string awayName)
    {
        var text=$"{title}. {body}";
        var pair=FindScorePair(body)??FindScorePair(title);
        if(pair is null)return (0,0,false);
        var (a,b)=pair.Value;
        var explicitGoals=ScoredAgainst().Match(text);
        if(explicitGoals.Success&&int.TryParse(explicitGoals.Groups["n"].Value,out var scored)&&(scored==a||scored==b))
        {
            var other=scored==a?b:a;
            if(MentionsTeam(explicitGoals.Groups["team"].Value,clubName))return (scored,other,true);
            if(MentionsTeam(explicitGoals.Groups["team"].Value,opponentName))return (other,scored,true);
        }
        if(a==b&&LooksLikeDraw(text))return (a,b,true);
        var winner=WinningSide(text,clubName,opponentName,homeName,awayName,isHome);
        if(winner==0)return a==b?(a,b,true):(0,0,false);
        var high=Math.Max(a,b);var low=Math.Min(a,b);
        if(high==low)return (a,b,true);
        return winner>0?(high,low,true):(low,high,true);
    }

    private static (int A,int B)? FindScorePair(string text)
    {
        foreach(Match match in Score().Matches(text))
        {
            var tail=text[Math.Min(text.Length,match.Index+match.Length)..];
            if(AggregateSuffix().IsMatch(tail))continue;
            if(int.TryParse(match.Groups["a"].Value,out var a)&&int.TryParse(match.Groups["b"].Value,out var b)&&a<=30&&b<=30)
                return (a,b);
        }
        return null;
    }

    private static bool LooksLikeDraw(string text)
        =>ContainsAny(text,"draw","drew","stalemate","shared the points","shared the spoils","all square","honours even","honors even");

    /// <summary>1 when the career player's club won, -1 when the opponent won, 0 when undetermined.</summary>
    private static int WinningSide(string text,string clubName,string opponentName,string homeName,string awayName,bool isHome)
    {
        var clubAliases=new[]{clubName,isHome?homeName:awayName}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var opponentAliases=new[]{opponentName,isHome?awayName:homeName}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(clubAliases.Any(x=>WonAsSubject(text,x))||opponentAliases.Any(x=>LostAsObject(text,x)))return 1;
        if(opponentAliases.Any(x=>WonAsSubject(text,x))||clubAliases.Any(x=>LostAsObject(text,x)))return -1;
        return 0;
    }

    // Only settled, past-tense wording counts. Previews are full of "hoping to win" and
    // "will be looking to beat", which must never be read as a result.
    private const string WinVerbs=@"were\s+victorious|were\s+the\s+[^.\r\n]{0,15}?victors|victors\s+over|ran\s+out[^.\r\n]{0,20}?winners|beat|defeated|overcame|edged|thrashed|hammered|saw\s+off|swept\s+aside|brushed\s+aside|downed|dispatched|prevailed|triumphed|came\s+out\s+on\s+top|won|recorded\s+a[^.\r\n]{0,25}?win|took\s+the|claimed\s+the|secured\s+the|sealed\s+the|clinched\s+the";
    private const string ConditionalPrefix=@"(?<!\bto\s)(?<!\bwill\s)(?<!\bcan\s)(?<!\bcould\s)(?<!\bmust\s)(?<!\bshould\s)(?<!\bwould\s)(?<!\bmay\s)(?<!\bmight\s)";

    private static bool WonAsSubject(string text,string team)
        =>Regex.IsMatch(text,$@"\b{Regex.Escape(team)}\b[^.\r\n]{{0,70}}?{ConditionalPrefix}\b(?:{WinVerbs})\b",RegexOptions.IgnoreCase);
    private static bool LostAsObject(string text,string team)
        =>Regex.IsMatch(text,$@"\b(?:victory|victorious|victors?|win|winners?|triumph|success)\b[^.\r\n]{{0,40}}?\b(?:over|against)\s+(?:the\s+)?{Regex.Escape(team)}\b",RegexOptions.IgnoreCase)
        ||Regex.IsMatch(text,$@"\b{Regex.Escape(team)}\b[^.\r\n]{{0,50}}?\b(?:were\s+beaten|were\s+defeated|lost|slumped|fell\s+to|suffered\s+defeat|went\s+down)\b",RegexOptions.IgnoreCase);

    private static bool MentionsTeam(string fragment,string team)
        =>!string.IsNullOrWhiteSpace(team)&&fragment.Contains(team,StringComparison.OrdinalIgnoreCase);

    private static string FifaDateText(int value)=>(ParseFifaDate(value)??DateTime.MinValue).ToString("yyyy-MM-dd");

    public static MatchContext? ParseMatchContext(string title,string body,string clubName)
    {
        var titleMatch=ReviewTitle().Match(title);if(!titleMatch.Success)return null;
        var competition=titleMatch.Groups["competition"].Value.Trim();var home=titleMatch.Groups["home"].Value.Trim();var away=titleMatch.Groups["away"].Value.Trim();
        var isHome=Same(home,clubName);if(!isHome&&!Same(away,clubName))return null;var opponent=isHome?away:home;
        return new(competition,opponent,isHome,$"{title}; {body.Trim()}");
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

    public static Fifa18DetectedFixture? ParseFixturePreview(string title, int date, string clubName, int clubId,string body="",
        string playerName = "", string availabilityHint = "Unknown")
    {
        var match=PreviewTitle().Match(title);
        if(!match.Success)return null;
        var competition=match.Groups["competition"].Value.Trim();
        var home=match.Groups["home"].Value.Trim();var away=match.Groups["away"].Value.Trim();
        var isHome=Same(home,clubName);if(!isHome&&!Same(away,clubName))return null;
        var parsedDate=ParseFifaDate(date);if(parsedDate is null)return null;
        var opponent=isHome?away:home;
        var evidence=string.IsNullOrWhiteSpace(body)?$"FIFA generated preview: {title}":$"FIFA generated preview: {title}. {body.Trim()}";
        var availability=DetectFixtureAvailability(title,body,playerName,availabilityHint);
        return new($"club:{clubId}:fixture:{date}:{Normalize(opponent).ToLowerInvariant()}",parsedDate.Value.ToString("yyyy-MM-dd"),
            competition,opponent,isHome,90,evidence,"Club",clubName,availability);
    }

    private static string DetectPlayerAvailability(Fifa18SaveData data,int playerId,string playerName,int careerDate)
    {
        var current=ParseFifaDate(careerDate);
        var relevant=data.Table("career_news")
            .Where(row=>I(row,"date")>0&&I(row,"date")<=careerDate&&MentionsPlayer(row,playerId,playerName))
            .Select(row=>new{Row=row,Date=ParseFifaDate(I(row,"date"))})
            .Where(x=>x.Date is not null&&(current is null||(current.Value-x.Date!.Value).TotalDays<=45))
            .OrderByDescending(x=>x.Date).FirstOrDefault();
        if(relevant is null)return "Unknown";
        return ClassifyAvailability(S(relevant.Row,"title")+" "+S(relevant.Row,"body"));
    }

    private static Fifa18TransferRequestSignal? DetectTransferRequest(Fifa18SaveData data,int playerId,string playerName,int careerDate)
    {
        var relevant=data.Table("career_news")
            .Where(row=>I(row,"date")>0&&I(row,"date")<=careerDate&&MentionsPlayer(row,playerId,playerName))
            .Select(row=>new{Row=row,Date=I(row,"date"),Status=ClassifyTransferRequest(S(row,"title")+" "+S(row,"body"))})
            .Where(x=>x.Status!="None")
            .OrderByDescending(x=>x.Date).FirstOrDefault();
        if(relevant is null)return null;
        var title=S(relevant.Row,"title");var body=S(relevant.Row,"body");var raw=$"{relevant.Date}|{title}|{body}";var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return new($"transfer-request:{key}",(ParseFifaDate(relevant.Date)??DateTime.Today).ToString("yyyy-MM-dd"),relevant.Status,$"FIFA career news: {title}. {body.Trim()}");
    }

    private static string ClassifyTransferRequest(string text)
    {
        if(ContainsAny(text,"transfer request accepted","request has been accepted","request was accepted","request approved","allowed to leave","permission to leave","will leave the club"))return "Accepted";
        if(ContainsAny(text,"transfer request rejected","request has been rejected","request was rejected","request denied","request refused","not allowed to leave","blocked from leaving"))return "Rejected";
        if(ContainsAny(text,"transfer request","requested a transfer","asked to leave","wants to leave","wants a transfer","handed in a request","handed in his transfer","submitted a transfer request","transfer listed"))return "Requested";
        return "None";
    }

    private static string DetectFixtureAvailability(string title,string body,string playerName,string availabilityHint)
    {
        var explicitStatus=string.IsNullOrWhiteSpace(playerName)?"Unknown":ClassifyAvailability(title+" "+body,playerName);
        return explicitStatus!="Unknown"?explicitStatus:(string.IsNullOrWhiteSpace(availabilityHint)?"Unknown":availabilityHint);
    }

    private static string ClassifyAvailability(string text,string? playerName=null)
    {
        if(playerName is not null&&!MentionsPlayer(text,playerName))return "Unknown";
        if(ContainsAny(text,"no longer injured","returned from injury","back from injury","recovered from injury","injury has healed","fit again","available again"))return "Unknown";
        if(ContainsAny(text,"suspended","suspension","sent off","sending-off","sending off","red card"))return "Suspended";
        if(ContainsAny(text,"injured","injury","injuries","sidelined","out injured","medical issue"))return "Injured";
        if(ContainsAny(text,"not selected","not in the squad","left out","omitted from the squad","dropped from the squad","will miss the match","miss the match","ruled out","unavailable"))return "NotSelected";
        if(ContainsAny(text,"on the bench","named among the substitutes","named as a substitute","will start","starting xi","starting eleven","starting lineup"))
            return ContainsAny(text,"on the bench","named among the substitutes","named as a substitute")?"Benched":"Selected";
        return "Unknown";
    }

    private static bool MentionsPlayer(IReadOnlyDictionary<string,object> row,int playerId,string playerName)
        => I(row,"playerid")==playerId||MentionsPlayer(S(row,"title")+" "+S(row,"body"),playerName);

    private static bool MentionsPlayer(string text,string playerName)
    {
        if(string.IsNullOrWhiteSpace(playerName))return false;
        if(text.Contains(playerName,StringComparison.OrdinalIgnoreCase))return true;
        var surname=playerName.Split(' ',StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return !string.IsNullOrWhiteSpace(surname)&&surname.Length>=3&&Regex.IsMatch(text,$@"\b{Regex.Escape(surname)}\b",RegexOptions.IgnoreCase);
    }

    private static bool Same(string a,string b)
    {
        var left=NormalizeTeam(a);var right=NormalizeTeam(b);return string.Equals(left,right,StringComparison.OrdinalIgnoreCase)||left.EndsWith(right,StringComparison.OrdinalIgnoreCase)||right.EndsWith(left,StringComparison.OrdinalIgnoreCase);
    }
    private static string NormalizeTeam(string value)
    {
        var normalized=Normalize(value).ToLowerInvariant();foreach(var suffix in new[]{" football club"," football team"," fc"," cf"," c.f."," s.a.d."})if(normalized.EndsWith(suffix,StringComparison.Ordinal))normalized=normalized[..^suffix.Length].Trim();return normalized;
    }
    private static string Normalize(string x)=>Regex.Replace(x.Trim().TrimEnd('.'),@"\s+"," ");
    private static string JoinName(string first,string last)=>string.Join(" ",new[]{first,last}.Where(x=>!string.IsNullOrWhiteSpace(x))).Trim();
    private static int I(IReadOnlyDictionary<string,object>? row,string key,int fallback=0)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToInt32(x,CultureInfo.InvariantCulture):fallback;
    private static long L(IReadOnlyDictionary<string,object>? row,string key,long fallback=0)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToInt64(x,CultureInfo.InvariantCulture):fallback;
    private static string S(IReadOnlyDictionary<string,object>? row,string key)=>row is not null&&row.TryGetValue(key,out var x)?Convert.ToString(x,CultureInfo.InvariantCulture)??"":"";
    private static DateTime? ParseFifaDate(int value)=>DateTime.TryParseExact(value.ToString(CultureInfo.InvariantCulture),"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    public static string PositionName(int id)=>id switch{0=>"GK",1=>"SW",2=>"RWB",3=>"RB",4=>"RCB",5=>"CB",6=>"LCB",7=>"LB",8=>"LWB",9=>"RDM",10=>"CDM",11=>"LDM",12=>"RM",13=>"RCM",14=>"CM",15=>"LCM",16=>"LM",17=>"RAM",18=>"CAM",19=>"LAM",20=>"RF",21=>"CF",22=>"LF",23=>"RW",24=>"RS",25=>"ST",26=>"LS",27=>"LW",28=>"SUB",29=>"RES",_=>$"POS {id}"};

    [GeneratedRegex(@"^(?<competition>.+?)(?:\s+Review)?\s*[:\-]\s*(?<home>.+?)\s+vs\.?\s+(?<away>.+?)\s*$",RegexOptions.IgnoreCase)] private static partial Regex ReviewTitle();
    [GeneratedRegex(@"^(?<competition>.+?)\s+Preview:\s*(?<home>.+?)\s+vs\s+(?<away>.+?)\s*$",RegexOptions.IgnoreCase)] private static partial Regex PreviewTitle();
    [GeneratedRegex(@"(?<winner>[^.\r\n]+?)\s+were victorious\s+(?<winnerScore>\d+)\s*[-–]\s*(?<loserScore>\d+)\s+over\s+(?<loser>[^.\r\n]+?)(?:\s+in\s+|[.,\r\n])",RegexOptions.IgnoreCase)] private static partial Regex Victory();
    [GeneratedRegex(@"\b(?<a>\d+)\s*[-–]\s*(?<b>\d+)\b")] private static partial Regex Score();
    [GeneratedRegex(@"^\s*\)?\s*(?:agg|aggregate|on aggregate)",RegexOptions.IgnoreCase)] private static partial Regex AggregateSuffix();
    [GeneratedRegex(@"(?<team>[^.\r\n]{2,60}?)\s+scored\s+(?<n>\d+)\s+(?:goals?\s+)?against\s+(?<opponent>[^.,;\r\n]+)",RegexOptions.IgnoreCase)] private static partial Regex ScoredAgainst();
    [GeneratedRegex(@"^(?<home>[^\d]{2,40}?)\s+(?<a>\d{1,2})\s*[-–]\s*(?<b>\d{1,2})\s+(?<away>[^\d]{2,40})$",RegexOptions.IgnoreCase)] private static partial Regex ScorelineTitle();
}

public sealed record MatchReport(string Competition,string Opponent,bool IsHome,int TeamScore,int OpponentScore,string Evidence);
public sealed record MatchContext(string Competition,string Opponent,bool IsHome,string Evidence);
public sealed record Fifa18MatchArticle(string Competition,string Opponent,int OpponentTeamId,bool IsHome,
    int TeamScore,int OpponentScore,bool ScoreKnown,string Evidence,bool IsHomeKnown=false)
{
    public bool OpponentKnown=>!string.IsNullOrWhiteSpace(Opponent)&&Opponent!="Opponent unknown";
}
