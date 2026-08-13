using CareerCompanion.Core.Domain;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CareerCompanion.Core.Persistence;

public sealed class Database(string path)
{
    public string Path { get; } = path;
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true }.ToString();
    public SqliteConnection Open() { var c = new SqliteConnection(ConnectionString); c.Open(); return c; }

    public void Migrate()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS careers(id INTEGER PRIMARY KEY, save_name TEXT NOT NULL, player_name TEXT NOT NULL,
              nationality TEXT NOT NULL, age INTEGER NOT NULL, club TEXT NOT NULL, league TEXT NOT NULL, season TEXT NOT NULL,
              current_date TEXT NOT NULL, position TEXT NOT NULL, shirt_number INTEGER NOT NULL, next_opponent TEXT,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS clubs(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              name TEXT NOT NULL, league TEXT NOT NULL, country TEXT NOT NULL DEFAULT '', reputation INTEGER NOT NULL DEFAULT 50);
            CREATE TABLE IF NOT EXISTS characters(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              name TEXT NOT NULL, age INTEGER NOT NULL, nationality TEXT NOT NULL, club TEXT NOT NULL, position TEXT NOT NULL,
              squad_role TEXT NOT NULL, type TEXT NOT NULL, facts_json TEXT NOT NULL DEFAULT '{}', personality_json TEXT NOT NULL,
              communication_json TEXT NOT NULL, historical_notes TEXT NOT NULL DEFAULT '', is_public INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS relationships(character_id INTEGER PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
              score INTEGER NOT NULL DEFAULT 0, trust INTEGER NOT NULL DEFAULT 0, respect INTEGER NOT NULL DEFAULT 0,
              friendliness INTEGER NOT NULL DEFAULT 0, rivalry INTEGER NOT NULL DEFAULT 0, tension INTEGER NOT NULL DEFAULT 0,
              familiarity INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS character_states(character_id INTEGER PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
              mood TEXT NOT NULL DEFAULT 'neutral', concerns TEXT NOT NULL DEFAULT '', ambitions TEXT NOT NULL DEFAULT '',
              satisfaction INTEGER NOT NULL DEFAULT 50, reaction_state TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS matches(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              date TEXT NOT NULL, competition TEXT NOT NULL, opponent TEXT NOT NULL, is_home INTEGER NOT NULL, team_score INTEGER NOT NULL,
              opponent_score INTEGER NOT NULL, started INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL, assists INTEGER NOT NULL,
              rating REAL NOT NULL, yellow_card INTEGER NOT NULL, red_card INTEGER NOT NULL, penalty_scored INTEGER NOT NULL,
              penalty_missed INTEGER NOT NULL, notes TEXT NOT NULL, next_opponent TEXT, is_derby INTEGER NOT NULL, is_major INTEGER NOT NULL,
              result TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS career_events(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              match_id INTEGER REFERENCES matches(id) ON DELETE SET NULL, type TEXT NOT NULL, timestamp TEXT NOT NULL, importance INTEGER NOT NULL,
              entities_json TEXT NOT NULL, metadata_json TEXT NOT NULL, summary TEXT NOT NULL, classification TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_events_career_time ON career_events(career_id,timestamp DESC);
            CREATE TABLE IF NOT EXISTS memories(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE, event_id INTEGER REFERENCES career_events(id),
              text TEXT NOT NULL, timestamp TEXT NOT NULL, importance INTEGER NOT NULL, valence INTEGER NOT NULL, topic TEXT NOT NULL,
              resolved INTEGER NOT NULL DEFAULT 0, last_recalled TEXT, is_compressed INTEGER NOT NULL DEFAULT 0,
              classification TEXT NOT NULL DEFAULT 'SimulatedInterpretation');
            CREATE INDEX IF NOT EXISTS idx_memories_character ON memories(character_id,timestamp DESC);
            CREATE TABLE IF NOT EXISTS conversations(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              character_id INTEGER NOT NULL REFERENCES characters(id), scene TEXT NOT NULL, timestamp TEXT NOT NULL,
              context_json TEXT NOT NULL DEFAULT '{}', result_json TEXT NOT NULL DEFAULT '{}');
            CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY, conversation_id INTEGER NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
              role TEXT NOT NULL, content TEXT NOT NULL, timestamp TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS news(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              event_id INTEGER REFERENCES career_events(id), outlet TEXT NOT NULL, headline TEXT NOT NULL, body TEXT NOT NULL,
              sentiment TEXT NOT NULL, importance INTEGER NOT NULL, published_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS social_posts(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              event_id INTEGER REFERENCES career_events(id), author TEXT NOT NULL, persona TEXT NOT NULL, content TEXT NOT NULL, published_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS narratives(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              type TEXT NOT NULL, strength INTEGER NOT NULL, status TEXT NOT NULL, last_updated TEXT NOT NULL, evidence_json TEXT NOT NULL,
              UNIQUE(career_id,type));
            CREATE TABLE IF NOT EXISTS generation_jobs(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              event_id INTEGER, kind TEXT NOT NULL, dedupe_key TEXT NOT NULL UNIQUE, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
              error TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS usage_log(id INTEGER PRIMARY KEY, provider TEXT NOT NULL, model TEXT NOT NULL, input_tokens INTEGER NOT NULL,
              output_tokens INTEGER NOT NULL, cost REAL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS debug_log(id INTEGER PRIMARY KEY, category TEXT NOT NULL, detail TEXT NOT NULL,
              created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS provider_imports(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, event_key TEXT NOT NULL, source_path TEXT NOT NULL, file_fingerprint TEXT NOT NULL,
              captured_at TEXT NOT NULL, payload_json TEXT NOT NULL, imported_at TEXT NOT NULL,
              UNIQUE(career_id,provider,event_key));
            CREATE INDEX IF NOT EXISTS idx_provider_imports_career ON provider_imports(career_id,provider,imported_at DESC);
            CREATE TABLE IF NOT EXISTS provider_entities(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, entity_type TEXT NOT NULL, external_id TEXT NOT NULL, local_character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
              active INTEGER NOT NULL DEFAULT 1, payload_json TEXT NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
              UNIQUE(career_id,provider,entity_type,external_id));
            CREATE INDEX IF NOT EXISTS idx_provider_entities_career ON provider_entities(career_id,provider,entity_type,active);
            CREATE TABLE IF NOT EXISTS fixtures(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, event_key TEXT NOT NULL, date TEXT NOT NULL, competition TEXT NOT NULL, opponent TEXT NOT NULL,
              is_home INTEGER NOT NULL, status TEXT NOT NULL, confidence INTEGER NOT NULL, evidence TEXT NOT NULL,
              source_fingerprint TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(career_id,provider,event_key));
            CREATE INDEX IF NOT EXISTS idx_fixtures_career_date ON fixtures(career_id,status,date);
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(1,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(2,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(3,datetime('now'));
            """;
        cmd.ExecuteNonQuery();
    }

    public long CreateCareer(string saveName, string playerName, string nationality, int age, string club,
        string league, string season, string position, int shirtNumber)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO careers(save_name,player_name,nationality,age,club,league,season,current_date,position,shirt_number,created_at,updated_at) " +
            "VALUES($save,$player,$nation,$age,$club,$league,$season,$date,$position,$number,$now,$now); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$save", saveName); cmd.Parameters.AddWithValue("$player", playerName);
        cmd.Parameters.AddWithValue("$nation", nationality); cmd.Parameters.AddWithValue("$age", age);
        cmd.Parameters.AddWithValue("$club", club); cmd.Parameters.AddWithValue("$league", league);
        cmd.Parameters.AddWithValue("$season", season); cmd.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$position", position); cmd.Parameters.AddWithValue("$number", shirtNumber);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Career> GetCareers()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM careers ORDER BY updated_at DESC";
        using var r = cmd.ExecuteReader(); var list = new List<Career>();
        while (r.Read()) list.Add(ReadCareer(r)); return list;
    }

    public Career GetCareer(long id)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM careers WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader(); if (!r.Read()) throw new KeyNotFoundException("Career not found."); return ReadCareer(r);
    }

    private static Career ReadCareer(SqliteDataReader r) => new(r.GetInt64(r.GetOrdinal("id")), r.GetString(r.GetOrdinal("save_name")),
        r.GetString(r.GetOrdinal("player_name")), r.GetString(r.GetOrdinal("nationality")), r.GetInt32(r.GetOrdinal("age")),
        r.GetString(r.GetOrdinal("club")), r.GetString(r.GetOrdinal("league")), r.GetString(r.GetOrdinal("season")),
        r.GetString(r.GetOrdinal("current_date")), r.GetString(r.GetOrdinal("position")), r.GetInt32(r.GetOrdinal("shirt_number")),
        DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))), DateTime.Parse(r.GetString(r.GetOrdinal("updated_at"))),
        r.IsDBNull(r.GetOrdinal("next_opponent")) ? null : r.GetString(r.GetOrdinal("next_opponent")));

    public long AddCharacter(long careerId, string name, int age, string nationality, string club, string position,
        string role, CharacterType type, Personality? personality = null, CommunicationStyle? communication = null)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO characters(career_id,name,age,nationality,club,position,squad_role,type,personality_json,communication_json) " +
          "VALUES($career,$name,$age,$nation,$club,$pos,$role,$type,$personality,$communication); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$career", careerId); cmd.Parameters.AddWithValue("$name", name); cmd.Parameters.AddWithValue("$age", age);
        cmd.Parameters.AddWithValue("$nation", nationality); cmd.Parameters.AddWithValue("$club", club); cmd.Parameters.AddWithValue("$pos", position);
        cmd.Parameters.AddWithValue("$role", role); cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.Parameters.AddWithValue("$personality", JsonSerializer.Serialize(personality ?? Personality.Balanced));
        cmd.Parameters.AddWithValue("$communication", JsonSerializer.Serialize(communication ?? CommunicationStyle.Balanced));
        var id = (long)cmd.ExecuteScalar()!;
        Exec(db, tx, "INSERT INTO relationships(character_id) VALUES($id)", ("$id", id));
        Exec(db, tx, "INSERT INTO character_states(character_id,updated_at) VALUES($id,$now)", ("$id", id), ("$now", DateTime.UtcNow.ToString("O")));
        tx.Commit(); return id;
    }

    public IReadOnlyList<Character> GetCharacters(long careerId)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM characters WHERE career_id=$id ORDER BY type,name"; cmd.Parameters.AddWithValue("$id", careerId);
        using var r = cmd.ExecuteReader(); var list = new List<Character>(); while (r.Read()) list.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), Enum.Parse<CharacterType>(r.GetString(8)), r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12), r.GetBoolean(13))); return list;
    }

    public ProviderCharacterSyncResult SyncProviderCharacters(long careerId,string provider,IEnumerable<ProviderCharacterFact> facts)
    {
        var incoming=facts.ToList();var seen=incoming.Select(x=>x.ExternalId).ToHashSet(StringComparer.Ordinal);
        var added=0;var updated=0;var inactive=0;var now=DateTime.UtcNow.ToString("O");
        using var db=Open();using var tx=db.BeginTransaction();
        foreach(var fact in incoming)
        {
            long? characterId=null;using(var find=db.CreateCommand()){find.Transaction=tx;find.CommandText="SELECT local_character_id FROM provider_entities WHERE career_id=$c AND provider=$p AND entity_type='character' AND external_id=$e";find.Parameters.AddWithValue("$c",careerId);find.Parameters.AddWithValue("$p",provider);find.Parameters.AddWithValue("$e",fact.ExternalId);var value=find.ExecuteScalar();if(value is not null)characterId=Convert.ToInt64(value);}
            if(characterId is null)
            {
                using var match=db.CreateCommand();match.Transaction=tx;match.CommandText="SELECT c.id FROM characters c WHERE c.career_id=$career AND c.name=$name AND c.type=$type AND NOT EXISTS(SELECT 1 FROM provider_entities p WHERE p.local_character_id=c.id) LIMIT 1";match.Parameters.AddWithValue("$career",careerId);match.Parameters.AddWithValue("$name",fact.Name);match.Parameters.AddWithValue("$type",fact.Type.ToString());var existing=match.ExecuteScalar();
                if(existing is not null)characterId=Convert.ToInt64(existing);
                else
                {
                    using var insert=db.CreateCommand();insert.Transaction=tx;insert.CommandText="INSERT INTO characters(career_id,name,age,nationality,club,position,squad_role,type,facts_json,personality_json,communication_json) VALUES($career,$name,$age,$nation,$club,$position,$role,$type,$facts,$personality,$communication); SELECT last_insert_rowid();";insert.Parameters.AddWithValue("$career",careerId);insert.Parameters.AddWithValue("$name",fact.Name);insert.Parameters.AddWithValue("$age",fact.Age);insert.Parameters.AddWithValue("$nation",fact.Nationality);insert.Parameters.AddWithValue("$club",fact.Club);insert.Parameters.AddWithValue("$position",fact.Position);insert.Parameters.AddWithValue("$role",fact.SquadRole);insert.Parameters.AddWithValue("$type",fact.Type.ToString());insert.Parameters.AddWithValue("$facts",fact.FactsJson);insert.Parameters.AddWithValue("$personality",JsonSerializer.Serialize(Personality.Balanced));insert.Parameters.AddWithValue("$communication",JsonSerializer.Serialize(CommunicationStyle.Balanced));characterId=(long)insert.ExecuteScalar()!;Exec(db,tx,"INSERT INTO relationships(character_id) VALUES($id)",( "$id",characterId.Value));Exec(db,tx,"INSERT INTO character_states(character_id,updated_at) VALUES($id,$now)",( "$id",characterId.Value),( "$now",now));added++;
                }
            }
            else updated++;
            string existingFacts="{}";using(var read=db.CreateCommand()){read.Transaction=tx;read.CommandText="SELECT facts_json FROM characters WHERE id=$id";read.Parameters.AddWithValue("$id",characterId.Value);existingFacts=Convert.ToString(read.ExecuteScalar())??"{}";}
            var merged=MergeJson(existingFacts,fact.FactsJson,new Dictionary<string,object?>{{"providerActive",true}});
            using(var update=db.CreateCommand()){update.Transaction=tx;update.CommandText="UPDATE characters SET name=$name,age=$age,nationality=$nation,club=$club,position=$position,squad_role=$role,type=$type,facts_json=$facts WHERE id=$id";update.Parameters.AddWithValue("$name",fact.Name);update.Parameters.AddWithValue("$age",fact.Age);update.Parameters.AddWithValue("$nation",fact.Nationality);update.Parameters.AddWithValue("$club",fact.Club);update.Parameters.AddWithValue("$position",fact.Position);update.Parameters.AddWithValue("$role",fact.SquadRole);update.Parameters.AddWithValue("$type",fact.Type.ToString());update.Parameters.AddWithValue("$facts",merged);update.Parameters.AddWithValue("$id",characterId.Value);update.ExecuteNonQuery();}
            using(var entity=db.CreateCommand()){entity.Transaction=tx;entity.CommandText="INSERT INTO provider_entities(career_id,provider,entity_type,external_id,local_character_id,active,payload_json,first_seen,last_seen) VALUES($c,$p,'character',$e,$local,1,$payload,$now,$now) ON CONFLICT(career_id,provider,entity_type,external_id) DO UPDATE SET local_character_id=excluded.local_character_id,active=1,payload_json=excluded.payload_json,last_seen=excluded.last_seen";entity.Parameters.AddWithValue("$c",careerId);entity.Parameters.AddWithValue("$p",provider);entity.Parameters.AddWithValue("$e",fact.ExternalId);entity.Parameters.AddWithValue("$local",characterId.Value);entity.Parameters.AddWithValue("$payload",fact.PayloadJson);entity.Parameters.AddWithValue("$now",now);entity.ExecuteNonQuery();}
        }
        var active=new List<(string ExternalId,long CharacterId)>();using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="SELECT external_id,local_character_id FROM provider_entities WHERE career_id=$c AND provider=$p AND entity_type='character' AND active=1";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);using var reader=cmd.ExecuteReader();while(reader.Read())active.Add((reader.GetString(0),reader.GetInt64(1)));}
        foreach(var missing in active.Where(x=>!seen.Contains(x.ExternalId)))
        {
            Exec(db,tx,"UPDATE provider_entities SET active=0,last_seen=$now WHERE career_id=$c AND provider=$p AND entity_type='character' AND external_id=$e",("$now",now),("$c",careerId),("$p",provider),("$e",missing.ExternalId));
            string existingFacts="{}";using(var read=db.CreateCommand()){read.Transaction=tx;read.CommandText="SELECT facts_json FROM characters WHERE id=$id";read.Parameters.AddWithValue("$id",missing.CharacterId);existingFacts=Convert.ToString(read.ExecuteScalar())??"{}";}
            Exec(db,tx,"UPDATE characters SET squad_role='Former teammate',facts_json=$facts WHERE id=$id",("$facts",MergeJson(existingFacts,"{}",new Dictionary<string,object?>{{"providerActive",false}})),("$id",missing.CharacterId));inactive++;
        }
        tx.Commit();return new(added,updated,inactive);
    }

    private static string MergeJson(string existing,string imported,IReadOnlyDictionary<string,object?> additions)
    {
        JsonObject root;try{root=JsonNode.Parse(existing) as JsonObject??new();}catch(JsonException){root=new();}
        try{if(JsonNode.Parse(imported) is JsonObject source)foreach(var item in source)root[item.Key]=item.Value?.DeepClone();}catch(JsonException){}
        foreach(var item in additions)root[item.Key]=JsonValue.Create(item.Value);return root.ToJsonString();
    }

    public void UpdateCharacterProfile(long id,string factsJson,string personalityJson,string communicationJson,string historicalNotes,bool isPublic)
    {
        JsonDocument.Parse(factsJson).Dispose();JsonDocument.Parse(personalityJson).Dispose();JsonDocument.Parse(communicationJson).Dispose();
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE characters SET facts_json=$f,personality_json=$p,communication_json=$c,historical_notes=$h,is_public=$public WHERE id=$id";cmd.Parameters.AddWithValue("$f",factsJson);cmd.Parameters.AddWithValue("$p",personalityJson);cmd.Parameters.AddWithValue("$c",communicationJson);cmd.Parameters.AddWithValue("$h",historicalNotes);cmd.Parameters.AddWithValue("$public",isPublic);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
    }

    public Relationship GetRelationship(long characterId)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM relationships WHERE character_id=$id"; cmd.Parameters.AddWithValue("$id", characterId);
        using var r = cmd.ExecuteReader(); if (!r.Read()) return new(characterId); return new(characterId, r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7));
    }

    public void SaveRelationship(Relationship x)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = """UPDATE relationships SET score=$s,trust=$tr,respect=$r,friendliness=$f,rivalry=$rv,tension=$te,familiarity=$fa WHERE character_id=$id""";
        foreach (var p in new[] { ("$s",x.Score),("$tr",x.Trust),("$r",x.Respect),("$f",x.Friendliness),("$rv",x.Rivalry),("$te",x.Tension),("$fa",x.Familiarity),("$id",(int)x.CharacterId) }) cmd.Parameters.AddWithValue(p.Item1,p.Item2);
        cmd.ExecuteNonQuery();
    }

    public long SaveMatch(long careerId, MatchInput m)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO matches(career_id,date,competition,opponent,is_home,team_score,opponent_score,started,minutes,goals,assists,rating,yellow_card,red_card,penalty_scored,penalty_missed,notes,next_opponent,is_derby,is_major,result,created_at) " +
          "VALUES($career,$date,$comp,$opp,$home,$ts,$os,$started,$min,$goals,$assists,$rating,$yellow,$red,$ps,$pm,$notes,$next,$derby,$major,$result,$now); SELECT last_insert_rowid();";
        var values = new Dictionary<string,object?> { ["$career"]=careerId,["$date"]=m.Date,["$comp"]=m.Competition,["$opp"]=m.Opponent,["$home"]=m.IsHome,["$ts"]=m.TeamScore,["$os"]=m.OpponentScore,["$started"]=m.Started,["$min"]=m.Minutes,["$goals"]=m.Goals,["$assists"]=m.Assists,["$rating"]=m.Rating,["$yellow"]=m.YellowCard,["$red"]=m.RedCard,["$ps"]=m.PenaltyScored,["$pm"]=m.PenaltyMissed,["$notes"]=m.Notes,["$next"]=m.NextOpponent,["$derby"]=m.IsDerby,["$major"]=m.IsMajorFixture,["$result"]=m.TeamScore>m.OpponentScore?"W":m.TeamScore<m.OpponentScore?"L":"D",["$now"]=DateTime.UtcNow.ToString("O") };
        foreach(var p in values) cmd.Parameters.AddWithValue(p.Key,p.Value ?? DBNull.Value); return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<CareerMatch> GetMatches(long careerId, int limit = 100)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM matches WHERE career_id=$id ORDER BY date,id LIMIT $limit"; cmd.Parameters.AddWithValue("$id",careerId); cmd.Parameters.AddWithValue("$limit",limit);
        using var r=cmd.ExecuteReader(); var list=new List<CareerMatch>(); while(r.Read()){ var m=new MatchInput(r.GetString(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetInt32(6),r.GetInt32(7),r.GetBoolean(8),r.GetInt32(9),r.GetInt32(10),r.GetInt32(11),r.GetDouble(12),r.GetBoolean(13),r.GetBoolean(14),r.GetBoolean(15),r.GetBoolean(16),r.GetString(17),r.IsDBNull(18)?null:r.GetString(18),r.GetBoolean(19),r.GetBoolean(20)); list.Add(new(r.GetInt64(0),careerId,m,r.GetString(21),DateTime.Parse(r.GetString(22)))); } return list;
    }

    public void UpsertFixture(long careerId,string provider,string eventKey,string date,string competition,string opponent,
        bool isHome,int confidence,string evidence,string sourceFingerprint)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        Exec(db,tx,"UPDATE fixtures SET status='Superseded',updated_at=$now WHERE career_id=$c AND provider=$p AND status='Upcoming' AND event_key<>$event",("$now",DateTime.UtcNow.ToString("O")),("$c",careerId),("$p",provider),("$event",eventKey));
        using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO fixtures(career_id,provider,event_key,date,competition,opponent,is_home,status,confidence,evidence,source_fingerprint,updated_at) VALUES($c,$p,$event,$date,$competition,$opponent,$home,'Upcoming',$confidence,$evidence,$fingerprint,$now) ON CONFLICT(career_id,provider,event_key) DO UPDATE SET date=excluded.date,competition=excluded.competition,opponent=excluded.opponent,is_home=excluded.is_home,status='Upcoming',confidence=excluded.confidence,evidence=excluded.evidence,source_fingerprint=excluded.source_fingerprint,updated_at=excluded.updated_at";
        cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$event",eventKey);cmd.Parameters.AddWithValue("$date",date);cmd.Parameters.AddWithValue("$competition",competition);cmd.Parameters.AddWithValue("$opponent",opponent);cmd.Parameters.AddWithValue("$home",isHome);cmd.Parameters.AddWithValue("$confidence",confidence);cmd.Parameters.AddWithValue("$evidence",evidence);cmd.Parameters.AddWithValue("$fingerprint",sourceFingerprint);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();tx.Commit();
    }

    public IReadOnlyList<CareerFixture> GetFixtures(long careerId,int limit=50)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,career_id,provider,event_key,date,competition,opponent,is_home,status,confidence,evidence,updated_at FROM fixtures WHERE career_id=$c ORDER BY CASE status WHEN 'Upcoming' THEN 0 ELSE 1 END,date DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var result=new List<CareerFixture>();while(r.Read())result.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetBoolean(7),r.GetString(8),r.GetInt32(9),r.GetString(10),DateTime.Parse(r.GetString(11))));return result;
    }

    public long SaveEvent(CareerEvent e)
    {
        using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText="""INSERT INTO career_events(career_id,match_id,type,timestamp,importance,entities_json,metadata_json,summary,classification) VALUES($c,$m,$t,$ts,$i,$en,$me,$s,$cl); SELECT last_insert_rowid();""";
        object?[] v=[e.CareerId,e.MatchId,e.Type,e.Timestamp.ToString("O"),e.Importance,e.EntitiesJson,e.MetadataJson,e.Summary,e.Classification.ToString()]; string[] n=["$c","$m","$t","$ts","$i","$en","$me","$s","$cl"]; for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value); return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<CareerEvent> GetEvents(long careerId,int limit=100)
    {
        using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT * FROM career_events WHERE career_id=$id ORDER BY timestamp DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var list=new List<CareerEvent>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),DateTime.Parse(r.GetString(4)),r.GetInt32(5),r.GetString(6),r.GetString(7),r.GetString(8),Enum.Parse<FactClassification>(r.GetString(9))));return list;
    }

    public long AddMemory(long careerId,long characterId,long? eventId,string text,int importance,int valence,string topic,bool compressed=false)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""INSERT INTO memories(career_id,character_id,event_id,text,timestamp,importance,valence,topic,is_compressed) VALUES($c,$ch,$e,$t,$ts,$i,$v,$topic,$comp); SELECT last_insert_rowid();""";
        object?[] v=[careerId,characterId,eventId,text,DateTime.UtcNow.ToString("O"),importance,valence,topic,compressed];string[] n=["$c","$ch","$e","$t","$ts","$i","$v","$topic","$comp"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Memory> GetMemories(long characterId,int limit=200)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM memories WHERE character_id=$id ORDER BY timestamp DESC LIMIT $limit";cmd.Parameters.AddWithValue("$id",characterId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var l=new List<Memory>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3),r.GetString(4),DateTime.Parse(r.GetString(5)),r.GetInt32(6),r.GetInt32(7),r.GetString(8),r.GetBoolean(9),r.IsDBNull(10)?null:DateTime.Parse(r.GetString(10)),r.GetBoolean(11)));return l;
    }

    public IReadOnlyList<NewsItem> GetNews(long careerId,int limit=50){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM news WHERE career_id=$id ORDER BY published_at DESC LIMIT $n";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<NewsItem>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt32(7),DateTime.Parse(r.GetString(8))));return l;}
    public IReadOnlyList<SocialPost> GetSocial(long careerId,int limit=50){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM social_posts WHERE career_id=$id ORDER BY published_at DESC LIMIT $n";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<SocialPost>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),DateTime.Parse(r.GetString(6))));return l;}

    public long AddNews(long careerId,long? eventId,string outlet,string headline,string body,string sentiment,int importance){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO news(career_id,event_id,outlet,headline,body,sentiment,importance,published_at) VALUES($c,$e,$o,$h,$b,$s,$i,$t); SELECT last_insert_rowid();";object?[]v=[careerId,eventId,outlet,headline,body,sentiment,importance,DateTime.UtcNow.ToString("O")];string[]n=["$c","$e","$o","$h","$b","$s","$i","$t"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;}
    public long AddSocial(long careerId,long? eventId,string author,string persona,string content){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO social_posts(career_id,event_id,author,persona,content,published_at) VALUES($c,$e,$a,$p,$x,$t); SELECT last_insert_rowid();";object?[]v=[careerId,eventId,author,persona,content,DateTime.UtcNow.ToString("O")];string[]n=["$c","$e","$a","$p","$x","$t"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;}
    public long StartConversation(long careerId,long characterId,SceneType scene,string context="{}"){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO conversations(career_id,character_id,scene,timestamp,context_json) VALUES($c,$ch,$s,$t,$x); SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$ch",characterId);cmd.Parameters.AddWithValue("$s",scene.ToString());cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$x",context);return(long)cmd.ExecuteScalar()!;}
    public void AddMessage(long conversationId,string role,string content){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO messages(conversation_id,role,content,timestamp) VALUES($c,$r,$x,$t)";cmd.Parameters.AddWithValue("$c",conversationId);cmd.Parameters.AddWithValue("$r",role);cmd.Parameters.AddWithValue("$x",content);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public IReadOnlyList<ConversationMessage> GetMessages(long careerId,long characterId,int limit=40){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""SELECT m.role,m.content,m.timestamp FROM messages m JOIN conversations c ON c.id=m.conversation_id WHERE c.career_id=$c AND c.character_id=$ch ORDER BY m.timestamp DESC LIMIT $n""";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$ch",characterId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<ConversationMessage>();while(r.Read())l.Add(new(r.GetString(0),r.GetString(1),DateTime.Parse(r.GetString(2))));l.Reverse();return l;}

    public string? GetSetting(string key){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);return cmd.ExecuteScalar() as string;}
    public void SetSetting(string key,string value){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$k",key);cmd.Parameters.AddWithValue("$v",value);cmd.ExecuteNonQuery();}
    public void Log(string category,string detail){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO debug_log(category,detail,created_at) VALUES($c,$d,$t)";cmd.Parameters.AddWithValue("$c",category);cmd.Parameters.AddWithValue("$d",detail);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void AddUsage(string provider,string model,int inputTokens,int outputTokens,double? cost=null){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO usage_log(provider,model,input_tokens,output_tokens,cost,created_at) VALUES($p,$m,$i,$o,$c,$t)";cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$m",model);cmd.Parameters.AddWithValue("$i",inputTokens);cmd.Parameters.AddWithValue("$o",outputTokens);cmd.Parameters.AddWithValue("$c",cost is null?DBNull.Value:cost);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public bool HasProviderImport(long careerId,string provider,string eventKey){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT 1 FROM provider_imports WHERE career_id=$c AND provider=$p AND event_key=$e LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);return cmd.ExecuteScalar() is not null;}
    public string? GetLatestProviderPayload(long careerId,string provider){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT payload_json FROM provider_imports WHERE career_id=$c AND provider=$p ORDER BY imported_at DESC,id DESC LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);return cmd.ExecuteScalar() as string;}
    public void RecordProviderImport(long careerId,string provider,string eventKey,string sourcePath,string fingerprint,DateTime capturedAt,string payload){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO provider_imports(career_id,provider,event_key,source_path,file_fingerprint,captured_at,payload_json,imported_at) VALUES($c,$p,$e,$s,$f,$captured,$payload,$now)";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);cmd.Parameters.AddWithValue("$s",sourcePath);cmd.Parameters.AddWithValue("$f",fingerprint);cmd.Parameters.AddWithValue("$captured",capturedAt.ToString("O"));cmd.Parameters.AddWithValue("$payload",payload);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void UpdateCareerFromProvider(long id,string playerName,string nationality,string club,string league,string currentDate,string position,int shirtNumber){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE careers SET player_name=$player,nationality=$nation,club=$club,league=$league,current_date=$date,position=$position,shirt_number=$number,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$player",playerName);cmd.Parameters.AddWithValue("$nation",nationality);cmd.Parameters.AddWithValue("$club",club);cmd.Parameters.AddWithValue("$league",league);cmd.Parameters.AddWithValue("$date",currentDate);cmd.Parameters.AddWithValue("$position",position);cmd.Parameters.AddWithValue("$number",shirtNumber);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}

    public void Backup(string destination){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);using var source=Open();using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination}.ToString());target.Open();source.BackupDatabase(target);}
    public void Restore(string source)
    {
        using(var check=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly}.ToString())){check.Open();using var cmd=check.CreateCommand();cmd.CommandText="SELECT count(*) FROM schema_migrations";_ = cmd.ExecuteScalar() ?? throw new InvalidDataException("Not a Touchline backup.");}
        File.Copy(source,Path,true);
    }
    private static void Exec(SqliteConnection db,SqliteTransaction tx,string sql,params(string,object)[] values){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText=sql;foreach(var p in values)c.Parameters.AddWithValue(p.Item1,p.Item2);c.ExecuteNonQuery();}
}
