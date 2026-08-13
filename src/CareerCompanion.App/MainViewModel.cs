using CareerCompanion.Core.Domain;
using CareerCompanion.Core.LLM;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using CareerCompanion.Core.Providers.Fifa18;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Net.Http;
using System.IO;

namespace CareerCompanion.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Database _db; private readonly CareerService _careers; private Character? _selectedCharacter; private SquadMemberView? _selectedSquadMember; private Career? _currentCareer; private Fifa18ParsedCareer? _pendingFifa;
    public ObservableCollection<Career> Careers {get;}=[]; public ObservableCollection<Character> Characters{get;}=[];public ObservableCollection<SquadMemberView> SquadMembers{get;}=[];public ObservableCollection<SquadMemberView> FormerTeammates{get;}=[];public ObservableCollection<Character> OtherCharacters{get;}=[];public ObservableCollection<CareerFixture> Fixtures{get;}=[];public ObservableCollection<CareerEvent> Events{get;}=[];public ObservableCollection<NewsItem> News{get;}=[];public ObservableCollection<SocialPost> Social{get;}=[];public ObservableCollection<ConversationMessage> Messages{get;}=[];public ObservableCollection<string> DashboardItems{get;}=[];
    public IReadOnlyList<string> CharacterTypes{get;}=Enum.GetNames<CharacterType>();public IReadOnlyList<string> SceneTypes{get;}=Enum.GetNames<SceneType>();
    public IReadOnlyList<string> SquadFilters{get;}=["All","Goalkeepers","Defenders","Midfielders","Forwards","Injured"];
    public event PropertyChangedEventHandler? PropertyChanged;
    public MainViewModel(Database db){_db=db;_careers=new(db);LoadSettings();FifaSettingsDirectory=new Fifa18SaveLocator().FindSettingsDirectory()??"FIFA 18 settings directory not found";ReloadCareers();CurrentCareer=Careers.FirstOrDefault();MatchDate=DateTime.Today.ToString("yyyy-MM-dd");}
    private void OnChanged([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    private bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;OnChanged(name);return true;}
    public Career? CurrentCareer{get=>_currentCareer;set{if(Set(ref _currentCareer,value)){Refresh();OnChanged(nameof(NoCareerVisibility));}}}
    public Character? SelectedCharacter{get=>_selectedCharacter;set{if(Set(ref _selectedCharacter,value)){LoadMessages();OnChanged(nameof(SelectedCharacterSummary));OnChanged(nameof(SelectedRelationship));}}}
    public SquadMemberView? SelectedSquadMember{get=>_selectedSquadMember;set{if(Set(ref _selectedSquadMember,value)){if(value is not null)SelectedCharacter=value.Character;OnChanged(nameof(SelectedSquadFacts));}}}
    string _selectedSquadFilter="All";public string SelectedSquadFilter{get=>_selectedSquadFilter;set{if(Set(ref _selectedSquadFilter,value))RebuildSquadViews();}}
    public Visibility NoCareerVisibility=>CurrentCareer is null?Visibility.Visible:Visibility.Collapsed;
    public string PlayerSummary=>CurrentCareer is null?"":$"#{CurrentCareer.ShirtNumber} · {CurrentCareer.Position} · {CurrentCareer.Nationality}";
    public string RecentMatchSummary{get;private set;}="No matches logged";public string CurrentForm{get;private set;}="No form";public string NextFixture{get{if(CurrentCareer is null)return"Not available";var fixture=_db.GetFixtures(CurrentCareer.Id).FirstOrDefault(x=>x.Status=="Upcoming");return fixture is null?_db.GetSetting($"career:{CurrentCareer.Id}:next")??CurrentCareer.NextOpponent??"Not entered":$"{fixture.Date} · {(fixture.IsHome?"vs":"at")} {fixture.Opponent} · {fixture.Competition}";}}
    public string SelectedCharacterSummary=>SelectedCharacter is null?"Select a character":$"{SelectedCharacter.Type} · {SelectedCharacter.Age} · {SelectedCharacter.Nationality}\n{SelectedCharacter.Position} · {SelectedCharacter.SquadRole}";
    public string SelectedRelationship{get{if(SelectedCharacter is null)return"";var r=_db.GetRelationship(SelectedCharacter.Id);return $"Relationship {Label(r.Score)} · Trust {Label(r.Trust)} · Respect {Label(r.Respect)}";}}
    private static string Label(int n)=>n switch{>=60=>"very strong",>=25=>"positive",<=-60=>"hostile",<=-25=>"strained",_=>"neutral"};
    public string SelectedSquadFacts=>SelectedSquadMember is null?"Select a player to inspect their save data.":$"{SelectedSquadMember.ShirtLabel} | {SelectedSquadMember.Position} | {SelectedSquadMember.OverallLabel}\n{SelectedSquadMember.FormLabel} | {SelectedSquadMember.Availability}\n{SelectedSquadMember.Age} years old | {SelectedSquadMember.Nationality} | {SelectedSquadMember.SourceLabel}";
    private CareerFixture? UpcomingFixture=>Fixtures.FirstOrDefault(x=>x.Status=="Upcoming");
    public string PreMatchOpponent=>UpcomingFixture is null?"No upcoming fixture detected":UpcomingFixture.Opponent;
    public string PreMatchMeta=>UpcomingFixture is null?"Play or save before a fixture in FIFA 18 to update this briefing.":$"{UpcomingFixture.Date} | {(UpcomingFixture.IsHome?"HOME":"AWAY")} | {UpcomingFixture.Competition}";
    public string PreMatchForm=>CurrentForm;
    public string PreMatchAvailability{get{var active=SquadMembers.Count;var injured=SquadMembers.Count(x=>x.Injured);return active==0?"Squad data unavailable":injured==0?$"{active} players available":$"{active-injured} available | {injured} injured";}}
    public string PreMatchKeyPlayers{get{var players=SquadMembers.Where(x=>x.Overall is not null).OrderByDescending(x=>x.Overall).Take(3).Select(x=>$"{x.Name} ({x.Position}, {x.Overall})").ToList();return players.Count==0?"No player ratings detected":string.Join(" | ",players);}}

    string _status="Ready";public string Status{get=>_status;set=>Set(ref _status,value);}string _matchResultSummary="";public string MatchResultSummary{get=>_matchResultSummary;set=>Set(ref _matchResultSummary,value);}string _generationStatus="";public string GenerationStatus{get=>_generationStatus;set=>Set(ref _generationStatus,value);}
    public string NewSaveName{get;set;}="My Career";public string NewPlayerName{get;set;}="";public string NewNationality{get;set;}="";public int NewAge{get;set;}=18;public string NewClub{get;set;}="";public string NewLeague{get;set;}="";public string NewSeason{get;set;}="2017/18";public string NewPosition{get;set;}="CAM";public int NewShirtNumber{get;set;}=10;
    public string MatchDate{get;set;}="";public string MatchCompetition{get;set;}="League";public string MatchOpponent{get;set;}="";public bool MatchHome{get;set;}=true;public int TeamScore{get;set;}=0;public int OpponentScore{get;set;}=0;public int Goals{get;set;}=0;public int Assists{get;set;}=0;public double Rating{get;set;}=6.5;public int Minutes{get;set;}=90;public bool Started{get;set;}=true;public bool YellowCard{get;set;}public bool RedCard{get;set;}public bool PenaltyScored{get;set;}public bool PenaltyMissed{get;set;}public bool IsDerby{get;set;}public bool IsMajor{get;set;}public string MatchNextOpponent{get;set;}="";public string MatchNotes{get;set;}="";
    public string CharacterName{get;set;}="";public int CharacterAge{get;set;}=24;public string CharacterNationality{get;set;}="";public string CharacterPosition{get;set;}="CM";public string CharacterRole{get;set;}="Squad Player";public string CharacterType{get;set;}=nameof(CareerCompanion.Core.Domain.CharacterType.Teammate);
    string _messageText="";public string MessageText{get=>_messageText;set=>Set(ref _messageText,value);}public string SelectedScene{get;set;}=nameof(SceneType.PrivateMessage);public string PressAnswer{get;set;}="";public string PressQuestion{get;private set;}="No major recent event. Log a match to open a contextual press topic.";
    string _defaultModel="gpt-5.6-luna";public string DefaultModel{get=>_defaultModel;set=>Set(ref _defaultModel,value);}string _premiumModel="gpt-5.6-sol";public string PremiumModel{get=>_premiumModel;set=>Set(ref _premiumModel,value);}public bool PremiumRouting{get;set;}=true;public bool AutoTeammates{get;set;}=true;public bool AutoManager{get;set;}=true;public bool AutoNews{get;set;}=true;public bool AutoSocial{get;set;}=true;public bool AutoPress{get;set;}=true;
    bool _debugMode;public bool DebugMode{get=>_debugMode;set{if(Set(ref _debugMode,value))OnChanged(nameof(DebugVisibility));}}public Visibility DebugVisibility=>DebugMode?Visibility.Visible:Visibility.Collapsed;public string DebugText{get;private set;}="";
    string _fifaSyncStatus="Waiting for a FIFA 18 career save.";public string FifaSyncStatus{get=>_fifaSyncStatus;set=>Set(ref _fifaSyncStatus,value);}public string FifaSettingsDirectory{get;private set;}="";public bool AutoFifaWatch{get;set;}=true;public bool AutoFifaSquadSync{get;set;}=true;public bool HasPendingFifaMatch=>_pendingFifa?.LatestMatch is not null;
    public Visibility FifaReviewVisibility=>HasPendingFifaMatch?Visibility.Visible:Visibility.Collapsed;
    public string FifaSyncBadge=>HasPendingFifaMatch?"MATCH READY":"FIFA SYNC";
    public string LastFifaSyncText=>CurrentCareer is null?"No linked career":_db.GetSetting($"career:{CurrentCareer.Id}:fifa_last_sync")??"Not synchronized yet";
    public string FifaSourceText=>CurrentCareer is null?FifaSettingsDirectory:_db.GetSetting($"career:{CurrentCareer.Id}:fifa_source")??FifaSettingsDirectory;

    public void ReloadCareers(){Careers.Clear();foreach(var c in _db.GetCareers())Careers.Add(c);}
    public void CreateCareer(){if(string.IsNullOrWhiteSpace(NewPlayerName)||string.IsNullOrWhiteSpace(NewClub))throw new InvalidOperationException("Player name and club are required.");var id=_db.CreateCareer(NewSaveName,NewPlayerName,NewNationality,NewAge,NewClub,NewLeague,NewSeason,NewPosition,NewShirtNumber);ReloadCareers();CurrentCareer=Careers.Single(x=>x.Id==id);Status="Career created";}
    public void AddCharacter(){RequireCareer();if(string.IsNullOrWhiteSpace(CharacterName))throw new InvalidOperationException("Character name is required.");_db.AddCharacter(CurrentCareer!.Id,CharacterName,CharacterAge,CharacterNationality,CurrentCareer.Club,CharacterPosition,CharacterRole,Enum.Parse<CharacterType>(CharacterType));Refresh();Status=$"Added {CharacterName}";CharacterName="";}
    public void ProcessMatch(){RequireCareer();if(string.IsNullOrWhiteSpace(MatchOpponent))throw new InvalidOperationException("Opponent is required.");var input=new MatchInput(MatchDate,MatchCompetition,MatchOpponent,MatchHome,TeamScore,OpponentScore,Started,Minutes,Goals,Assists,Rating,YellowCard,RedCard,PenaltyScored,PenaltyMissed,MatchNotes,string.IsNullOrWhiteSpace(MatchNextOpponent)?null:MatchNextOpponent,IsDerby,IsMajor);var result=_careers.ProcessMatch(CurrentCareer!.Id,input);if(AutoNews||AutoSocial)new MediaService(_db).GenerateDeterministic(CurrentCareer.Id,result.Events,AutoNews,AutoSocial);if(_pendingFifa?.LatestMatch is not null){_db.RecordProviderImport(CurrentCareer.Id,"FIFA 18 Save",_pendingFifa.LatestMatch.EventKey,_pendingFifa.SourcePath,_pendingFifa.FileFingerprint,_pendingFifa.CapturedAt,JsonSerializer.Serialize(_pendingFifa.State));_db.UpdateCareerFromProvider(CurrentCareer.Id,_pendingFifa.PlayerName,_pendingFifa.NationalityName,_pendingFifa.ClubName,_pendingFifa.LeagueName,_pendingFifa.CurrentDate,_pendingFifa.Position,_pendingFifa.ShirtNumber);_db.SetSetting($"career:{CurrentCareer.Id}:fifa_player_id",_pendingFifa.PlayerId.ToString());FifaSyncStatus="FIFA match imported. Watching for the next completed save.";_pendingFifa=null;NotifyFifaState();ReloadCareers();_currentCareer=Careers.FirstOrDefault(x=>x.Id==CurrentCareer.Id)??CurrentCareer;OnChanged(nameof(CurrentCareer));}MatchResultSummary=$"Saved. Generated {result.Events.Count} events, selected {result.Reactions.Count} relevant reactions"+(result.Narratives.Count>0?$", active narratives: {string.Join(", ",result.Narratives)}.":".");Status="World updated";Refresh();}

    public async Task<Fifa18ScanResult> ScanFifaSaveAsync(bool automatic=false,CancellationToken ct=default)
    {
        FifaSyncStatus=automatic?"FIFA save changed; reading a stable snapshot...":"Scanning the newest FIFA 18 career save...";
        var provider=new Fifa18SaveCareerDataProvider(_db);var parsed=await provider.ParseLatestAsync(CurrentCareer?.Id,FifaSettingsDirectory,ct);_pendingFifa=parsed;NotifyFifaState();
        if(CurrentCareer is null){FifaSyncStatus=$"Found {parsed.PlayerName} at {parsed.ClubName}. Create a linked career to import it.";return new(Fifa18ScanDisposition.NoCareerSelected,parsed,FifaSyncStatus);}
        var linkedId=_db.GetSetting($"career:{CurrentCareer.Id}:fifa_player_id");var linked=linkedId==parsed.PlayerId.ToString();var sameIdentity=CurrentCareer.PlayerName.Contains(parsed.PlayerName,StringComparison.OrdinalIgnoreCase)||parsed.PlayerName.Contains(CurrentCareer.PlayerName,StringComparison.OrdinalIgnoreCase);var sameClub=string.Equals(CurrentCareer.Club,parsed.ClubName,StringComparison.OrdinalIgnoreCase);
        if(!linked&&(!sameIdentity||!sameClub)){FifaSyncStatus=$"Save belongs to {parsed.PlayerName} at {parsed.ClubName}; active companion career is {CurrentCareer.PlayerName} at {CurrentCareer.Club}.";return new(Fifa18ScanDisposition.CareerMismatch,parsed,FifaSyncStatus);}
        _db.SetSetting($"career:{CurrentCareer.Id}:fifa_player_id",parsed.PlayerId.ToString());var supporting=SyncSupportingFacts(CurrentCareer.Id,parsed);var supportingMessage=DescribeSupportingSync(supporting,parsed);RecordFifaSync(parsed);
        if(parsed.LatestMatch is null){_pendingFifa=null;NotifyFifaState();FifaSyncStatus=$"Save synchronized{supportingMessage}; no player match is available to import.";return new(Fifa18ScanDisposition.NoNewMatch,parsed,FifaSyncStatus);}
        if(_db.HasProviderImport(CurrentCareer.Id,"FIFA 18 Save",parsed.LatestMatch.EventKey)){_pendingFifa=null;NotifyFifaState();FifaSyncStatus=$"Save synchronized{supportingMessage}; its latest match has already been imported.";return new(Fifa18ScanDisposition.NoNewMatch,parsed,FifaSyncStatus);}
        if(!automatic)PopulateMatch(parsed.LatestMatch);FifaSyncStatus=$"New match detected: {parsed.LatestMatch.TeamScore}-{parsed.LatestMatch.OpponentScore} vs {parsed.LatestMatch.Opponent} ({parsed.LatestMatch.Confidence}% confidence). Review and import. Supporting facts synchronized{supportingMessage}.";NotifyFifaState();return new(Fifa18ScanDisposition.MatchDetected,parsed,FifaSyncStatus);
    }

    public void CreateCareerFromPendingFifa()
    {
        if(_pendingFifa is null)throw new InvalidOperationException("Scan a FIFA save first.");var p=_pendingFifa;var id=_db.CreateCareer(Path.GetFileName(p.SourcePath),p.PlayerName,p.NationalityName,p.Age,p.ClubName,p.LeagueName,p.Season,p.Position,p.ShirtNumber);_db.SetSetting($"career:{id}:fifa_player_id",p.PlayerId.ToString());ReloadCareers();CurrentCareer=Careers.Single(x=>x.Id==id);var supporting=SyncSupportingFacts(id,p);RecordFifaSync(p);if(p.LatestMatch is not null)PopulateMatch(p.LatestMatch);FifaSyncStatus=$"Linked career created for {p.PlayerName}{DescribeSupportingSync(supporting,p)}. Review the detected match before importing.";NotifyFifaState();
    }

    private Fifa18SupportingSyncResult SyncSupportingFacts(long careerId,Fifa18ParsedCareer parsed){var result=new Fifa18ImportService(_db).SyncSupportingFacts(careerId,parsed,AutoFifaSquadSync);Refresh();return result;}
    private static string DescribeSupportingSync(Fifa18SupportingSyncResult result,Fifa18ParsedCareer parsed){var parts=new List<string>();if(result.Squad is { } squad)parts.Add($"{parsed.Squad.Count} teammates synced ({squad.Added} new, {squad.MarkedInactive} departed)");if(result.FixtureUpdated&&parsed.NextFixture is { } fixture)parts.Add($"next fixture: {(fixture.IsHome?"vs":"at")} {fixture.Opponent}");return parts.Count==0?"":" · "+string.Join(" · ",parts);}

    public void PopulatePendingMatchForReview(){if(_pendingFifa?.LatestMatch is null)throw new InvalidOperationException("No FIFA match is waiting for review.");PopulateMatch(_pendingFifa.LatestMatch);}
    public bool PrepareConversation(CharacterType type,SceneType scene)
    {
        var character=Characters.FirstOrDefault(x=>x.Type==type&&SquadMemberView.From(x).Active)??Characters.FirstOrDefault(x=>x.Type==type);
        if(character is null)return false;
        SelectedCharacter=character;SelectedScene=scene.ToString();MessageText=type==CareerCompanion.Core.Domain.CharacterType.Manager?$"I want to discuss the plan for {PreMatchOpponent}.":$"How are you feeling before the match against {PreMatchOpponent}?";
        OnChanged(nameof(SelectedScene));return true;
    }
    public void MarkFifaSyncFailure(string message){FifaSyncStatus="FIFA synchronization failed: "+message;if(CurrentCareer is not null)_db.SetSetting($"career:{CurrentCareer.Id}:fifa_last_error",message);NotifyFifaState();}
    private void RecordFifaSync(Fifa18ParsedCareer parsed)
    {
        if(CurrentCareer is null)return;
        _db.SetSetting($"career:{CurrentCareer.Id}:fifa_last_sync",$"Last synchronized {DateTime.Now:dd MMM yyyy, HH:mm}");
        _db.SetSetting($"career:{CurrentCareer.Id}:fifa_source",Path.GetFileName(parsed.SourcePath));
        _db.SetSetting($"career:{CurrentCareer.Id}:fifa_last_error","");NotifyFifaState();
    }
    private void NotifyFifaState(){OnChanged(nameof(HasPendingFifaMatch));OnChanged(nameof(FifaReviewVisibility));OnChanged(nameof(FifaSyncBadge));OnChanged(nameof(LastFifaSyncText));OnChanged(nameof(FifaSourceText));}
    private void RebuildSquadViews()
    {
        var selectedId=SelectedSquadMember?.Id;SquadMembers.Clear();FormerTeammates.Clear();OtherCharacters.Clear();
        var teammates=Characters.Where(x=>x.Type==CareerCompanion.Core.Domain.CharacterType.Teammate).Select(SquadMemberView.From).ToList();
        foreach(var player in teammates.Where(x=>!x.Active))FormerTeammates.Add(player);
        IEnumerable<SquadMemberView> active=teammates.Where(x=>x.Active);
        active=SelectedSquadFilter switch
        {
            "Goalkeepers"=>active.Where(x=>x.Position=="GK"),
            "Defenders"=>active.Where(x=>new[]{"CB","LB","RB","LWB","RWB","SW"}.Contains(x.Position)),
            "Midfielders"=>active.Where(x=>new[]{"CDM","CM","CAM","LM","RM"}.Contains(x.Position)),
            "Forwards"=>active.Where(x=>new[]{"LW","RW","CF","ST"}.Contains(x.Position)),
            "Injured"=>active.Where(x=>x.Injured),
            _=>active
        };
        foreach(var player in active.OrderBy(x=>x.Position).ThenByDescending(x=>x.Overall))SquadMembers.Add(player);
        foreach(var character in Characters.Where(x=>x.Type!=CareerCompanion.Core.Domain.CharacterType.Teammate))OtherCharacters.Add(character);
        SelectedSquadMember=selectedId is null?null:SquadMembers.FirstOrDefault(x=>x.Id==selectedId);
        OnChanged(nameof(PreMatchAvailability));OnChanged(nameof(PreMatchKeyPlayers));
    }

    private void PopulateMatch(Fifa18DetectedMatch m){MatchDate=m.Date;MatchCompetition=m.Competition;MatchOpponent=m.Opponent;MatchHome=m.IsHome;TeamScore=m.TeamScore;OpponentScore=m.OpponentScore;Started=m.Started;Minutes=m.Minutes;Goals=m.Goals;Assists=m.Assists;Rating=m.Rating;YellowCard=m.YellowCard;RedCard=m.RedCard;MatchNotes=m.Evidence;foreach(var name in new[]{nameof(MatchDate),nameof(MatchCompetition),nameof(MatchOpponent),nameof(MatchHome),nameof(TeamScore),nameof(OpponentScore),nameof(Started),nameof(Minutes),nameof(Goals),nameof(Assists),nameof(Rating),nameof(YellowCard),nameof(RedCard),nameof(MatchNotes)})OnChanged(name);}
    public async Task SendMessageAsync(){RequireCareer();if(SelectedCharacter is null)throw new InvalidOperationException("Select a character first.");if(string.IsNullOrWhiteSpace(MessageText))return;GenerationStatus="Generating in the background…";var key=GetApiKey();using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(45)};ILlmProvider provider=string.IsNullOrWhiteSpace(key)?new OfflineLlmProvider():new OpenAIProvider(http,()=>key);try{var service=new ConversationService(_db,provider);var result=await service.SendAsync(CurrentCareer!.Id,SelectedCharacter.Id,Enum.Parse<SceneType>(SelectedScene),MessageText,DefaultModel);MessageText="";GenerationStatus=$"Reply received · {result.InputTokens+result.OutputTokens} tokens";LoadMessages();OnChanged(nameof(SelectedRelationship));}catch(LlmUnavailableException e){GenerationStatus=e.Message;}catch(LlmRateLimitException e){GenerationStatus=e.Message;}}
    public void RecordPress(){RequireCareer();if(string.IsNullOrWhiteSpace(PressAnswer))throw new InvalidOperationException("Enter a response first.");var journalist=Characters.FirstOrDefault(c=>c.Type==CareerCompanion.Core.Domain.CharacterType.Journalist);if(journalist is null){var id=_db.AddCharacter(CurrentCareer!.Id,"Press Pool",35,"",CurrentCareer.Club,"Media","Journalist",CareerCompanion.Core.Domain.CharacterType.Journalist);Refresh();journalist=Characters.Single(c=>c.Id==id);}var activeJournalist=journalist!;var conv=_db.StartConversation(CurrentCareer!.Id,activeJournalist.Id,SceneType.PressConference,JsonSerializer.Serialize(new{question=PressQuestion}));_db.AddMessage(conv,"journalist",PressQuestion);_db.AddMessage(conv,"user",PressAnswer);_db.AddMemory(CurrentCareer.Id,activeJournalist.Id,null,$"Public statement: {PressAnswer}",55,0,"press statement");PressAnswer="";Status="Public statement recorded";}
    public void SaveSettings(string? key){if(!string.IsNullOrWhiteSpace(key))_db.SetSetting("openai_api_key",SecretStore.Protect(key));foreach(var p in new Dictionary<string,string>{{"default_model",DefaultModel},{"premium_model",PremiumModel},{"premium_routing",PremiumRouting.ToString()},{"auto_teammates",AutoTeammates.ToString()},{"auto_manager",AutoManager.ToString()},{"auto_news",AutoNews.ToString()},{"auto_social",AutoSocial.ToString()},{"auto_press",AutoPress.ToString()},{"debug_mode",DebugMode.ToString()},{"auto_fifa_watch",AutoFifaWatch.ToString()},{"auto_fifa_squad_sync",AutoFifaSquadSync.ToString()}})_db.SetSetting(p.Key,p.Value);Status="Settings saved";}
    public void Backup(string path){_db.Backup(path);Status=$"Backup saved: {path}";}
    public void Restore(string path){_db.Restore(path);ReloadCareers();CurrentCareer=Careers.FirstOrDefault();Status="Backup restored";}
    private string GetApiKey(){var env=Environment.GetEnvironmentVariable("OPENAI_API_KEY");if(!string.IsNullOrWhiteSpace(env))return env;var x=_db.GetSetting("openai_api_key");return string.IsNullOrWhiteSpace(x)?"":SecretStore.Unprotect(x);}
    private void LoadSettings(){DefaultModel=_db.GetSetting("default_model")??Environment.GetEnvironmentVariable("OPENAI_DEFAULT_MODEL")??"gpt-5.6-luna";PremiumModel=_db.GetSetting("premium_model")??Environment.GetEnvironmentVariable("OPENAI_PREMIUM_MODEL")??"gpt-5.6-sol";PremiumRouting=B("premium_routing",true);AutoTeammates=B("auto_teammates",true);AutoManager=B("auto_manager",true);AutoNews=B("auto_news",true);AutoSocial=B("auto_social",true);AutoPress=B("auto_press",true);DebugMode=B("debug_mode",false);AutoFifaWatch=B("auto_fifa_watch",true);AutoFifaSquadSync=B("auto_fifa_squad_sync",true);}
    private bool B(string key,bool fallback)=>bool.TryParse(_db.GetSetting(key),out var x)?x:fallback;
    private void RequireCareer(){if(CurrentCareer is null)throw new InvalidOperationException("Create or open a career first.");}
    private void LoadMessages(){Messages.Clear();if(CurrentCareer is null||SelectedCharacter is null)return;foreach(var m in _db.GetMessages(CurrentCareer.Id,SelectedCharacter.Id))Messages.Add(m);}
    public void Refresh(){Characters.Clear();Fixtures.Clear();Events.Clear();News.Clear();Social.Clear();DashboardItems.Clear();if(CurrentCareer is null){RebuildSquadViews();NotifyFifaState();return;}foreach(var x in _db.GetCharacters(CurrentCareer.Id))Characters.Add(x);foreach(var x in _db.GetFixtures(CurrentCareer.Id))Fixtures.Add(x);RebuildSquadViews();if(SelectedCharacter is not null)SelectedCharacter=Characters.FirstOrDefault(x=>x.Id==SelectedCharacter.Id);foreach(var x in _db.GetEvents(CurrentCareer.Id))Events.Add(x);foreach(var x in _db.GetNews(CurrentCareer.Id))News.Add(x);foreach(var x in _db.GetSocial(CurrentCareer.Id))Social.Add(x);var matches=_db.GetMatches(CurrentCareer.Id);var recent=matches.LastOrDefault();RecentMatchSummary=recent is null?"No matches logged":$"{recent.Input.TeamScore}-{recent.Input.OpponentScore} vs {recent.Input.Opponent}";CurrentForm=matches.Count==0?"No form":string.Join("  ",matches.TakeLast(5).Select(m=>m.Result));foreach(var e in Events.Take(4))DashboardItems.Add($"{e.Type.Replace('_',' ')}: {e.Summary}");if(DashboardItems.Count==0)DashboardItems.Add("No activity yet. Log a match to bring the world alive.");var top=Events.FirstOrDefault();PressQuestion=top is null?"No major recent event. Log a match to open a contextual press topic.":top.Type switch{"PLAYER_HATTRICK"=>"A hat trick today. Was that your best performance for the club?","PLAYER_RED_CARD"=>"What is your response to the sending-off?","MATCH_LOST"=>"How does the side respond to this defeat?","PLAYER_BENCHED"=>"Were you surprised not to start?",_=>$"How do you assess {top.Summary.ToLowerInvariant()}"};DebugText=BuildDebug(matches);OnChanged(nameof(RecentMatchSummary));OnChanged(nameof(CurrentForm));OnChanged(nameof(NextFixture));OnChanged(nameof(PlayerSummary));OnChanged(nameof(PressQuestion));OnChanged(nameof(DebugText));OnChanged(nameof(SelectedCharacterSummary));OnChanged(nameof(SelectedRelationship));OnChanged(nameof(PreMatchOpponent));OnChanged(nameof(PreMatchMeta));OnChanged(nameof(PreMatchForm));OnChanged(nameof(PreMatchAvailability));OnChanged(nameof(PreMatchKeyPlayers));NotifyFifaState();}
    private string BuildDebug(IReadOnlyList<CareerMatch> matches){if(CurrentCareer is null)return"No active career.";var sb=new StringBuilder();sb.AppendLine("RAW CAREER STATE").AppendLine(JsonSerializer.Serialize(CurrentCareer,new JsonSerializerOptions{WriteIndented=true})).AppendLine().AppendLine($"MATCHES: {matches.Count}  CHARACTERS: {Characters.Count}  EVENTS: {Events.Count}").AppendLine("\nRECENT EVENT IMPORTANCE / CLASSIFICATION");foreach(var e in Events.Take(20))sb.AppendLine($"{e.Importance,3} {e.Type,-24} {e.Classification} | {e.Summary}");sb.AppendLine("\nREACTION RULES: event >=25; news >=48; social >=55; press >=62; character relevance >=72.");if(SelectedCharacter is not null){var ranked=new CareerCompanion.Core.Simulation.MemoryRanker().Rank(_db.GetMemories(SelectedCharacter.Id),"recent match",DateTime.UtcNow);sb.AppendLine($"\nRETRIEVED MEMORIES FOR {SelectedCharacter.Name.ToUpperInvariant()}");foreach(var m in ranked)sb.AppendLine($"{m.Importance,3} {m.Topic}: {m.Text}");}return sb.ToString();}
}
