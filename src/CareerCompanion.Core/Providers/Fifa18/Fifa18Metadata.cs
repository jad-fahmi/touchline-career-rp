namespace CareerCompanion.Core.Providers.Fifa18;

internal sealed record FieldMeta(string Name, long RangeLow = 0);
internal sealed record TableMeta(string Name, IReadOnlyDictionary<string, FieldMeta> Fields);

// Focused, read-only subset of FIFA 18 fifa_ng_db-meta.xml. Field/table names are
// derived from fifa-career-save-parser (ISC, Sammy Griffiths) and xAranaktu's work.
internal static class Fifa18Metadata
{
    private static Dictionary<string, FieldMeta> F(params (string Short, string Name, long Low)[] fields)
        => fields.ToDictionary(x => x.Short, x => new FieldMeta(x.Name, x.Low), StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, TableMeta> Tables =
        new Dictionary<string, TableMeta>(StringComparer.Ordinal)
        {
            ["mPrV"] = new("career_users", F(
                ("uipx","userid",-1),("HdeP","firstname",0),("rREd","surname",0),
                ("GzsD","playertype",-1),("daeI","nationalteamid",-1),("Hwev","usertype",-1),
                ("NTyS","clubteamid",-1),("aQrQ","leagueid",-1),("CXJt","seasoncount",0),("zvSh","agentname",0),
                ("NGIq","nationalityid",0))),
            ["bxis"] = new("career_playasplayer", F(
                ("uipx","userid",0),("hWNP","playedlastmatch",0),("ykFq","playerid",-1),
                ("SxkG","numconsecclubbenched",0),("QdCn","numconsecnatbenched",0),("vjla","position",-1))),
            ["GJUr"] = new("career_calendar", F(
                ("KNVN","setupdate",20080101),("ZPUX","enddate",20080101),
                ("vHhZ","startdate",20080101),("aLZZ","currdate",20080101))),
            ["YMgA"] = new("career_playasplayerhistory", F(
                ("uipx","userid",0),("vojK","season",0),("mCXg","teamid",-1),
                ("nVWT","appearances",0),("MERA","goals",0),("xEsZ","assists",0),
                ("isoi","totalyellows",0),("GGVF","totalreds",0),("zjtP","wins",0),
                ("EBvI","draws",0),("BsgO","loses",0),("qnUa","matchratings",0),("VvuV","overall",1))),
            ["TtHG"] = new("career_playermatchratinghistory", F(
                ("JMld","artificialkey",0),("Amxm","minsplayed",-1),("Xxmh","rating",-1),
                ("ykFq","playerid",0),("HBfc","date",20080101),("vjla","position",0))),
            ["TPOP"] = new("career_playerlastmatchhistory", F(
                ("JMld","artificialkey",0),("mCXg","teamid",-1),("Amxm","minsplayed",0),
                ("MRaj","playeroverall",-1),("ykFq","playerid",-1),("BRof","playerfact",-1),
                ("vjla","position",-1))),
            ["ulAT"] = new("career_news", F(
                ("Xdcs","newsid",0),("mCXg","teamid",-1),("AmzQ","title",0),
                ("gKSa","importance",0),("ykFq","playerid",-1),("HBfc","date",20080101),
                ("WrfA","body",0))),
            ["Knen"] = new("managers", F(
                ("mCXg","teamid",1),("VHIB","managerid",1),("HdeP","firstname",0),("rREd","surname",0))),
            ["kISL"] = new("teamstadiumlinks", F(
                ("mCXg","teamid",1),("DmlS","stadiumname",0),("fwCQ","stadiumid",0),("WMtm","forcedhome",0))),
            ["NgwF"] = new("career_competitionprogress", F(
                ("mCXg","teamid",0),("GFQY","compshortname",0),("SDel","hasteamwon",0),
                ("vojK","season",0),("KPUK","stageid",-1),("OvfW","compobjid",0))),
            ["KNNX"] = new("career_trophies", F(("vojK","season",0),("glmx","flags",0),("uipx","userid",0))),
            ["cPet"] = new("career_playerawards", F(
                ("mCXg","teamid",1),("Bwgx","typeid",0),("vojK","season",0),
                ("ykFq","playerid",0),("ytjZ","count",1),("OvfW","compobjid",0))),
            ["lyxL"] = new("teams", F(
                ("mCXg","teamid",1),("AUsv","teamname",0),("erSL","rivalteam",1))),
            ["onMQ"] = new("leagues", F(
                ("aQrQ","leagueid",1),("HEQX","leaguename",0),("WDGJ","countryid",0),("paPI","level",1))),
            ["Crbb"] = new("nations", F(
                ("LEtt","nationid",0),("zMVU","nationname",0),("UItq","isocountrycode",0))),
            ["RrqT"] = new("teamplayerlinks", F(
                ("JMld","artificialkey",0),("mCXg","teamid",1),("JFiY","jerseynumber",1),
                ("UMDX","leaguegoals",0),("ykFq","playerid",0),("Vili","injury",0),
                ("jtWI","yellows",0),("jIcz","reds",0),("stFk","leagueappearances",0),
                ("rLZx","form",0),("vjla","position",0))),
            ["CZUM"] = new("players", F(
                ("ykFq","playerid",0),("tHlO","firstnameid",0),("QCfa","lastnameid",0),
                ("HDYx","commonnameid",0),("WVIU","birthdate",0),("enmm","nationality",0),
                ("wZQU","preferredposition1",0),("UERs","overallrating",1))),
            ["nQVU"] = new("editedplayernames", F(
                ("ykFq","playerid",0),("HdeP","firstname",0),("rREd","surname",0),
                ("kRfb","playerjerseyname",0),("xnfZ","commonname",0))),
            ["bneD"] = new("dcplayernames", F(("FuiB","nameid",34000),("vIys","name",0)))
        };
}
