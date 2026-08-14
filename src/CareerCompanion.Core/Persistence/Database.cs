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
            CREATE TABLE IF NOT EXISTS player_states(career_id INTEGER PRIMARY KEY REFERENCES careers(id) ON DELETE CASCADE,
              mood TEXT NOT NULL DEFAULT 'steady', confidence INTEGER NOT NULL DEFAULT 55, pressure INTEGER NOT NULL DEFAULT 25,
              fatigue INTEGER NOT NULL DEFAULT 15, isolation INTEGER NOT NULL DEFAULT 10, resilience INTEGER NOT NULL DEFAULT 55,
              last_trigger TEXT NOT NULL DEFAULT 'Career beginning', needs_support INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS matches(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              date TEXT NOT NULL, competition TEXT NOT NULL, opponent TEXT NOT NULL, is_home INTEGER NOT NULL, team_score INTEGER NOT NULL,
              opponent_score INTEGER NOT NULL, started INTEGER NOT NULL, minutes INTEGER NOT NULL, goals INTEGER NOT NULL, assists INTEGER NOT NULL,
              rating REAL NOT NULL, yellow_card INTEGER NOT NULL, red_card INTEGER NOT NULL, penalty_scored INTEGER NOT NULL,
              penalty_missed INTEGER NOT NULL, notes TEXT NOT NULL, next_opponent TEXT, is_derby INTEGER NOT NULL, is_major INTEGER NOT NULL,
              result TEXT NOT NULL, created_at TEXT NOT NULL, started_known INTEGER NOT NULL DEFAULT 1,
              team_context TEXT NOT NULL DEFAULT 'Club', representing_team TEXT NOT NULL DEFAULT '', score_known INTEGER NOT NULL DEFAULT 1);
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
              error TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, payload_json TEXT NOT NULL DEFAULT '{}');
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
            CREATE TABLE IF NOT EXISTS provider_match_links(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, event_key TEXT NOT NULL, match_id INTEGER NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
              status TEXT NOT NULL DEFAULT 'Processing', created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
              UNIQUE(career_id,provider,event_key), UNIQUE(match_id));
            CREATE TABLE IF NOT EXISTS provider_entities(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, entity_type TEXT NOT NULL, external_id TEXT NOT NULL, local_character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
              active INTEGER NOT NULL DEFAULT 1, payload_json TEXT NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
              UNIQUE(career_id,provider,entity_type,external_id));
            CREATE INDEX IF NOT EXISTS idx_provider_entities_career ON provider_entities(career_id,provider,entity_type,active);
            CREATE TABLE IF NOT EXISTS fixtures(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, event_key TEXT NOT NULL, date TEXT NOT NULL, competition TEXT NOT NULL, opponent TEXT NOT NULL,
              is_home INTEGER NOT NULL, status TEXT NOT NULL, confidence INTEGER NOT NULL, evidence TEXT NOT NULL,
              source_fingerprint TEXT NOT NULL, updated_at TEXT NOT NULL, team_context TEXT NOT NULL DEFAULT 'Club',
              representing_team TEXT NOT NULL DEFAULT '', availability TEXT NOT NULL DEFAULT 'Unknown', UNIQUE(career_id,provider,event_key));
            CREATE INDEX IF NOT EXISTS idx_fixtures_career_date ON fixtures(career_id,status,date);
            CREATE TABLE IF NOT EXISTS match_reviews(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              provider TEXT NOT NULL, event_key TEXT NOT NULL, source_path TEXT NOT NULL, file_fingerprint TEXT NOT NULL,
              captured_at TEXT NOT NULL, match_json TEXT NOT NULL, snapshot_json TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Pending',
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(career_id,provider,event_key));
            CREATE INDEX IF NOT EXISTS idx_match_reviews_career_status ON match_reviews(career_id,status,created_at DESC);
            CREATE TABLE IF NOT EXISTS post_match_interviews(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              match_id INTEGER NOT NULL REFERENCES matches(id) ON DELETE CASCADE, trigger_type TEXT NOT NULL, importance INTEGER NOT NULL,
              questions_json TEXT NOT NULL, answers_json TEXT NOT NULL DEFAULT '[]', current_question INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL DEFAULT 'Pending', created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(career_id,match_id));
            CREATE INDEX IF NOT EXISTS idx_interviews_career_status ON post_match_interviews(career_id,status,created_at DESC);
            CREATE TABLE IF NOT EXISTS notifications(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              kind TEXT NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL, action TEXT NOT NULL DEFAULT '', priority INTEGER NOT NULL DEFAULT 25,
              is_read INTEGER NOT NULL DEFAULT 0, dedupe_key TEXT NOT NULL, created_at TEXT NOT NULL, UNIQUE(career_id,dedupe_key));
            CREATE INDEX IF NOT EXISTS idx_notifications_career_unread ON notifications(career_id,is_read,created_at DESC);
            CREATE TABLE IF NOT EXISTS career_progress_snapshots(id INTEGER PRIMARY KEY, career_id INTEGER NOT NULL REFERENCES careers(id) ON DELETE CASCADE,
              captured_at TEXT NOT NULL, career_date TEXT NOT NULL, club TEXT NOT NULL, league TEXT NOT NULL, position TEXT NOT NULL,
              shirt_number INTEGER NOT NULL, overall INTEGER NOT NULL, form INTEGER NOT NULL, injured INTEGER NOT NULL,
              appearances INTEGER NOT NULL, goals INTEGER NOT NULL, assists INTEGER NOT NULL, yellow_cards INTEGER NOT NULL,
              red_cards INTEGER NOT NULL, source_fingerprint TEXT NOT NULL, UNIQUE(career_id,source_fingerprint));
            CREATE INDEX IF NOT EXISTS idx_progress_career_time ON career_progress_snapshots(career_id,captured_at DESC,id DESC);
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(1,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(2,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(3,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(4,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(5,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(6,datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(7,datetime('now'));
            """;
        cmd.ExecuteNonQuery();
        EnsureColumn(db,"matches","started_known","INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(db,"matches","team_context","TEXT NOT NULL DEFAULT 'Club'");
        EnsureColumn(db,"matches","representing_team","TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db,"matches","score_known","INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(db,"fixtures","team_context","TEXT NOT NULL DEFAULT 'Club'");
        EnsureColumn(db,"fixtures","representing_team","TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db,"fixtures","availability","TEXT NOT NULL DEFAULT 'Unknown'");
        EnsureColumn(db,"generation_jobs","payload_json","TEXT NOT NULL DEFAULT '{}'");
        using var v8=db.CreateCommand();v8.CommandText="INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(8,datetime('now'))";v8.ExecuteNonQuery();
        using var v9=db.CreateCommand();v9.CommandText="INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(9,datetime('now'))";v9.ExecuteNonQuery();
        using var v10=db.CreateCommand();v10.CommandText="INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(10,datetime('now'))";v10.ExecuteNonQuery();
        using var v11=db.CreateCommand();v11.CommandText="INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(11,datetime('now'))";v11.ExecuteNonQuery();
        using var v12=db.CreateCommand();v12.CommandText="INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(12,datetime('now'))";v12.ExecuteNonQuery();
    }
    private static void EnsureColumn(SqliteConnection db,string table,string column,string definition){using var info=db.CreateCommand();info.CommandText=$"PRAGMA table_info([{table}])";using var reader=info.ExecuteReader();var found=false;while(reader.Read())if(string.Equals(reader.GetString(1),column,StringComparison.OrdinalIgnoreCase)){found=true;break;}reader.Close();if(found)return;using var alter=db.CreateCommand();alter.CommandText=$"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition}";alter.ExecuteNonQuery();}

    public long CreateCareer(string saveName, string playerName, string nationality, int age, string club,
        string league, string season, string position, int shirtNumber,string? currentDate=null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO careers(save_name,player_name,nationality,age,club,league,season,current_date,position,shirt_number,created_at,updated_at) " +
            "VALUES($save,$player,$nation,$age,$club,$league,$season,$date,$position,$number,$now,$now); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$save", saveName); cmd.Parameters.AddWithValue("$player", playerName);
        cmd.Parameters.AddWithValue("$nation", nationality); cmd.Parameters.AddWithValue("$age", age);
        cmd.Parameters.AddWithValue("$club", club); cmd.Parameters.AddWithValue("$league", league);
        cmd.Parameters.AddWithValue("$season", season); cmd.Parameters.AddWithValue("$date", currentDate??DateTime.Today.ToString("yyyy-MM-dd"));
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
                    using var insert=db.CreateCommand();insert.Transaction=tx;insert.CommandText="INSERT INTO characters(career_id,name,age,nationality,club,position,squad_role,type,facts_json,personality_json,communication_json) VALUES($career,$name,$age,$nation,$club,$position,$role,$type,$facts,$personality,$communication); SELECT last_insert_rowid();";insert.Parameters.AddWithValue("$career",careerId);insert.Parameters.AddWithValue("$name",fact.Name);insert.Parameters.AddWithValue("$age",fact.Age);insert.Parameters.AddWithValue("$nation",fact.Nationality);insert.Parameters.AddWithValue("$club",fact.Club);insert.Parameters.AddWithValue("$position",fact.Position);insert.Parameters.AddWithValue("$role",fact.SquadRole);insert.Parameters.AddWithValue("$type",fact.Type.ToString());insert.Parameters.AddWithValue("$facts",fact.FactsJson);insert.Parameters.AddWithValue("$personality",JsonSerializer.Serialize(StablePersonality(fact.ExternalId)));insert.Parameters.AddWithValue("$communication",JsonSerializer.Serialize(StableCommunication(fact.ExternalId)));characterId=(long)insert.ExecuteScalar()!;Exec(db,tx,"INSERT INTO relationships(character_id) VALUES($id)",( "$id",characterId.Value));Exec(db,tx,"INSERT INTO character_states(character_id,updated_at) VALUES($id,$now)",( "$id",characterId.Value),( "$now",now));added++;
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
    private static Personality StablePersonality(string seed){var hash=StableHash(seed);int V(int shift,int min=30,int span=50)=>min+(hash>>shift&0x7fffffff)%span;return new(V(0),V(2),V(4),V(6),V(8),V(10),V(12),V(14),V(16),V(18),V(20),V(22,50,40));}
    private static CommunicationStyle StableCommunication(string seed){var hash=StableHash(seed);return new(hash%3==0?"very brief":hash%3==1?"brief":"moderate",35+hash%45,hash%35,15+(hash>>3)%50,25+(hash>>5)%50,25+(hash>>7)%55,10+(hash>>9)%40,(hash>>11)%35);}
    private static int StableHash(string seed){unchecked{uint hash=2166136261;foreach(var c in seed){hash^=c;hash*=16777619;}return(int)(hash&0x7fffffff);}}

    public void UpdateCharacterProfile(long id,string factsJson,string personalityJson,string communicationJson,string historicalNotes,bool isPublic)
    {
        JsonDocument.Parse(factsJson).Dispose();JsonDocument.Parse(personalityJson).Dispose();JsonDocument.Parse(communicationJson).Dispose();
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE characters SET facts_json=$f,personality_json=$p,communication_json=$c,historical_notes=$h,is_public=$public WHERE id=$id";cmd.Parameters.AddWithValue("$f",factsJson);cmd.Parameters.AddWithValue("$p",personalityJson);cmd.Parameters.AddWithValue("$c",communicationJson);cmd.Parameters.AddWithValue("$h",historicalNotes);cmd.Parameters.AddWithValue("$public",isPublic);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
    }
    public void UpdateProviderStaff(long id,string provider,string club,string role,bool active)
    {
        using var db=Open();string existing="{}";using(var read=db.CreateCommand()){read.CommandText="SELECT facts_json FROM characters WHERE id=$id";read.Parameters.AddWithValue("$id",id);existing=Convert.ToString(read.ExecuteScalar())??"{}";}var merged=MergeJson(existing,"{}",new Dictionary<string,object?>{{"provider",provider},{"providerActive",active},{"classification",FactClassification.SaveFact.ToString()}});using var cmd=db.CreateCommand();cmd.CommandText="UPDATE characters SET club=$club,squad_role=$role,facts_json=$facts WHERE id=$id";cmd.Parameters.AddWithValue("$club",club);cmd.Parameters.AddWithValue("$role",role);cmd.Parameters.AddWithValue("$facts",merged);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
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
    public CharacterState GetCharacterState(long characterId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT mood,concerns,ambitions,satisfaction,reaction_state,updated_at FROM character_states WHERE character_id=$id";cmd.Parameters.AddWithValue("$id",characterId);using var r=cmd.ExecuteReader();return r.Read()?new(characterId,r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetString(4),DateTime.Parse(r.GetString(5))):new(characterId);}
    public PlayerState GetPlayerState(long careerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT mood,confidence,pressure,fatigue,isolation,resilience,last_trigger,needs_support,updated_at FROM player_states WHERE career_id=$career";cmd.Parameters.AddWithValue("$career",careerId);using var r=cmd.ExecuteReader();return r.Read()?new(careerId,r.GetString(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetString(6),r.GetBoolean(7),DateTime.Parse(r.GetString(8))):new(careerId);}
    public void SavePlayerState(PlayerState state){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO player_states(career_id,mood,confidence,pressure,fatigue,isolation,resilience,last_trigger,needs_support,updated_at) VALUES($career,$mood,$confidence,$pressure,$fatigue,$isolation,$resilience,$trigger,$support,$updated) ON CONFLICT(career_id) DO UPDATE SET mood=excluded.mood,confidence=excluded.confidence,pressure=excluded.pressure,fatigue=excluded.fatigue,isolation=excluded.isolation,resilience=excluded.resilience,last_trigger=excluded.last_trigger,needs_support=excluded.needs_support,updated_at=excluded.updated_at";cmd.Parameters.AddWithValue("$career",state.CareerId);cmd.Parameters.AddWithValue("$mood",state.Mood);cmd.Parameters.AddWithValue("$confidence",Math.Clamp(state.Confidence,0,100));cmd.Parameters.AddWithValue("$pressure",Math.Clamp(state.Pressure,0,100));cmd.Parameters.AddWithValue("$fatigue",Math.Clamp(state.Fatigue,0,100));cmd.Parameters.AddWithValue("$isolation",Math.Clamp(state.Isolation,0,100));cmd.Parameters.AddWithValue("$resilience",Math.Clamp(state.Resilience,0,100));cmd.Parameters.AddWithValue("$trigger",state.LastTrigger);cmd.Parameters.AddWithValue("$support",state.NeedsSupport);cmd.Parameters.AddWithValue("$updated",(state.UpdatedAt==default?DateTime.UtcNow:state.UpdatedAt).ToString("O"));cmd.ExecuteNonQuery();}
    public bool SavePlayerStateOnce(long careerId,long eventId,string dedupeKey,PlayerState state)
    {
        using var db=Open();using var tx=db.BeginTransaction();var now=DateTime.UtcNow.ToString("O");using(var marker=db.CreateCommand()){marker.Transaction=tx;marker.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,event_id,kind,dedupe_key,status,created_at,updated_at) VALUES($career,$event,'player_psychology',$key,'complete',$now,$now)";marker.Parameters.AddWithValue("$career",careerId);marker.Parameters.AddWithValue("$event",eventId);marker.Parameters.AddWithValue("$key",dedupeKey);marker.Parameters.AddWithValue("$now",now);if(marker.ExecuteNonQuery()!=1){tx.Rollback();return false;}}
        using var save=db.CreateCommand();save.Transaction=tx;save.CommandText="INSERT INTO player_states(career_id,mood,confidence,pressure,fatigue,isolation,resilience,last_trigger,needs_support,updated_at) VALUES($career,$mood,$confidence,$pressure,$fatigue,$isolation,$resilience,$trigger,$support,$updated) ON CONFLICT(career_id) DO UPDATE SET mood=excluded.mood,confidence=excluded.confidence,pressure=excluded.pressure,fatigue=excluded.fatigue,isolation=excluded.isolation,resilience=excluded.resilience,last_trigger=excluded.last_trigger,needs_support=excluded.needs_support,updated_at=excluded.updated_at";save.Parameters.AddWithValue("$career",state.CareerId);save.Parameters.AddWithValue("$mood",state.Mood);save.Parameters.AddWithValue("$confidence",state.Confidence);save.Parameters.AddWithValue("$pressure",state.Pressure);save.Parameters.AddWithValue("$fatigue",state.Fatigue);save.Parameters.AddWithValue("$isolation",state.Isolation);save.Parameters.AddWithValue("$resilience",state.Resilience);save.Parameters.AddWithValue("$trigger",state.LastTrigger);save.Parameters.AddWithValue("$support",state.NeedsSupport);save.Parameters.AddWithValue("$updated",state.UpdatedAt.ToString("O"));save.ExecuteNonQuery();tx.Commit();return true;
    }
    public bool SavePlayerChoiceOnce(long careerId,long matchId,string choice,PlayerState state)
    {
        using var db=Open();using var tx=db.BeginTransaction();var now=DateTime.UtcNow.ToString("O");using(var marker=db.CreateCommand()){marker.Transaction=tx;marker.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,event_id,kind,dedupe_key,status,payload_json,created_at,updated_at) VALUES($career,$match,'player_recovery_choice',$key,'complete',$payload,$now,$now)";marker.Parameters.AddWithValue("$career",careerId);marker.Parameters.AddWithValue("$match",matchId);marker.Parameters.AddWithValue("$key",$"player-recovery:{careerId}:{matchId}");marker.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(new{choice}));marker.Parameters.AddWithValue("$now",now);if(marker.ExecuteNonQuery()!=1){tx.Rollback();return false;}}
        using var save=db.CreateCommand();save.Transaction=tx;save.CommandText="INSERT INTO player_states(career_id,mood,confidence,pressure,fatigue,isolation,resilience,last_trigger,needs_support,updated_at) VALUES($career,$mood,$confidence,$pressure,$fatigue,$isolation,$resilience,$trigger,$support,$updated) ON CONFLICT(career_id) DO UPDATE SET mood=excluded.mood,confidence=excluded.confidence,pressure=excluded.pressure,fatigue=excluded.fatigue,isolation=excluded.isolation,resilience=excluded.resilience,last_trigger=excluded.last_trigger,needs_support=excluded.needs_support,updated_at=excluded.updated_at";save.Parameters.AddWithValue("$career",state.CareerId);save.Parameters.AddWithValue("$mood",state.Mood);save.Parameters.AddWithValue("$confidence",Math.Clamp(state.Confidence,0,100));save.Parameters.AddWithValue("$pressure",Math.Clamp(state.Pressure,0,100));save.Parameters.AddWithValue("$fatigue",Math.Clamp(state.Fatigue,0,100));save.Parameters.AddWithValue("$isolation",Math.Clamp(state.Isolation,0,100));save.Parameters.AddWithValue("$resilience",Math.Clamp(state.Resilience,0,100));save.Parameters.AddWithValue("$trigger",state.LastTrigger);save.Parameters.AddWithValue("$support",state.NeedsSupport);save.Parameters.AddWithValue("$updated",state.UpdatedAt.ToString("O"));save.ExecuteNonQuery();tx.Commit();return true;
    }
    public void SaveCharacterState(CharacterState state){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO character_states(character_id,mood,concerns,ambitions,satisfaction,reaction_state,updated_at) VALUES($id,$mood,$concerns,$ambitions,$satisfaction,$reaction,$now) ON CONFLICT(character_id) DO UPDATE SET mood=excluded.mood,concerns=excluded.concerns,ambitions=excluded.ambitions,satisfaction=excluded.satisfaction,reaction_state=excluded.reaction_state,updated_at=excluded.updated_at";cmd.Parameters.AddWithValue("$id",state.CharacterId);cmd.Parameters.AddWithValue("$mood",state.Mood);cmd.Parameters.AddWithValue("$concerns",state.Concerns);cmd.Parameters.AddWithValue("$ambitions",state.Ambitions);cmd.Parameters.AddWithValue("$satisfaction",Math.Clamp(state.Satisfaction,0,100));cmd.Parameters.AddWithValue("$reaction",state.ReactionState);cmd.Parameters.AddWithValue("$now",(state.UpdatedAt==default?DateTime.UtcNow:state.UpdatedAt).ToString("O"));cmd.ExecuteNonQuery();}
    public bool SaveCharacterStatesOnce(long careerId,string dedupeKey,IEnumerable<CharacterState> states)
    {
        using var db=Open();using var tx=db.BeginTransaction();var now=DateTime.UtcNow.ToString("O");using(var job=db.CreateCommand()){job.Transaction=tx;job.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,kind,dedupe_key,status,created_at,updated_at) VALUES($career,'character_state',$key,'complete',$now,$now)";job.Parameters.AddWithValue("$career",careerId);job.Parameters.AddWithValue("$key",dedupeKey);job.Parameters.AddWithValue("$now",now);if(job.ExecuteNonQuery()!=1){tx.Rollback();return false;}}
        foreach(var state in states){using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO character_states(character_id,mood,concerns,ambitions,satisfaction,reaction_state,updated_at) VALUES($id,$mood,$concerns,$ambitions,$satisfaction,$reaction,$updated) ON CONFLICT(character_id) DO UPDATE SET mood=excluded.mood,concerns=excluded.concerns,ambitions=excluded.ambitions,satisfaction=excluded.satisfaction,reaction_state=excluded.reaction_state,updated_at=excluded.updated_at";cmd.Parameters.AddWithValue("$id",state.CharacterId);cmd.Parameters.AddWithValue("$mood",state.Mood);cmd.Parameters.AddWithValue("$concerns",state.Concerns);cmd.Parameters.AddWithValue("$ambitions",state.Ambitions);cmd.Parameters.AddWithValue("$satisfaction",Math.Clamp(state.Satisfaction,0,100));cmd.Parameters.AddWithValue("$reaction",state.ReactionState);cmd.Parameters.AddWithValue("$updated",(state.UpdatedAt==default?DateTime.UtcNow:state.UpdatedAt).ToString("O"));cmd.ExecuteNonQuery();}tx.Commit();return true;
    }
    public void ApplyNarrativesOnce(long careerId,long matchId,IEnumerable<string> active)
    {
        using var db=Open();using var tx=db.BeginTransaction();var now=DateTime.UtcNow.ToString("O");using(var job=db.CreateCommand()){job.Transaction=tx;job.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,event_id,kind,dedupe_key,status,created_at,updated_at) VALUES($career,$match,'narratives',$key,'complete',$now,$now)";job.Parameters.AddWithValue("$career",careerId);job.Parameters.AddWithValue("$match",matchId);job.Parameters.AddWithValue("$key",$"narratives:{careerId}:{matchId}");job.Parameters.AddWithValue("$now",now);if(job.ExecuteNonQuery()!=1){tx.Rollback();return;}}
        Exec(db,tx,"UPDATE narratives SET strength=max(0,strength-5),status=CASE WHEN strength-5<=15 THEN 'faded' ELSE status END WHERE career_id=$career",("$career",careerId));foreach(var type in active){using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO narratives(career_id,type,strength,status,last_updated,evidence_json) VALUES($career,$type,50,'active',$now,$evidence) ON CONFLICT(career_id,type) DO UPDATE SET strength=min(100,strength+10),status='active',last_updated=$now,evidence_json=$evidence";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$type",type);cmd.Parameters.AddWithValue("$now",now);cmd.Parameters.AddWithValue("$evidence",JsonSerializer.Serialize(new{matchId}));cmd.ExecuteNonQuery();}tx.Commit();
    }
    public bool ApplyStatementConsequencesOnce(long careerId,string dedupeKey,long eventId,IEnumerable<Relationship> relationships,string memoryText,int importance,int valence,DateTime timestamp)
    {
        using var db=Open();using var tx=db.BeginTransaction();var now=DateTime.UtcNow.ToString("O");using(var job=db.CreateCommand()){job.Transaction=tx;job.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,event_id,kind,dedupe_key,status,created_at,updated_at) VALUES($career,$event,'statement_consequences',$key,'complete',$now,$now)";job.Parameters.AddWithValue("$career",careerId);job.Parameters.AddWithValue("$event",eventId);job.Parameters.AddWithValue("$key",dedupeKey);job.Parameters.AddWithValue("$now",now);if(job.ExecuteNonQuery()!=1){tx.Rollback();return false;}}
        foreach(var relationship in relationships){using(var update=db.CreateCommand()){update.Transaction=tx;update.CommandText="UPDATE relationships SET score=$score,trust=$trust,respect=$respect,friendliness=$friendly,rivalry=$rivalry,tension=$tension,familiarity=$familiarity WHERE character_id=$character";update.Parameters.AddWithValue("$score",relationship.Score);update.Parameters.AddWithValue("$trust",relationship.Trust);update.Parameters.AddWithValue("$respect",relationship.Respect);update.Parameters.AddWithValue("$friendly",relationship.Friendliness);update.Parameters.AddWithValue("$rivalry",relationship.Rivalry);update.Parameters.AddWithValue("$tension",relationship.Tension);update.Parameters.AddWithValue("$familiarity",relationship.Familiarity);update.Parameters.AddWithValue("$character",relationship.CharacterId);update.ExecuteNonQuery();}using var memory=db.CreateCommand();memory.Transaction=tx;memory.CommandText="INSERT INTO memories(career_id,character_id,event_id,text,timestamp,importance,valence,topic) VALUES($career,$character,$event,$text,$timestamp,$importance,$valence,'public statement')";memory.Parameters.AddWithValue("$career",careerId);memory.Parameters.AddWithValue("$character",relationship.CharacterId);memory.Parameters.AddWithValue("$event",eventId);memory.Parameters.AddWithValue("$text",memoryText);memory.Parameters.AddWithValue("$timestamp",timestamp.ToString("O"));memory.Parameters.AddWithValue("$importance",Math.Clamp(importance,1,100));memory.Parameters.AddWithValue("$valence",Math.Clamp(valence,-100,100));memory.ExecuteNonQuery();}tx.Commit();return true;
    }

    public long SaveMatch(long careerId, MatchInput m)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO matches(career_id,date,competition,opponent,is_home,team_score,opponent_score,started,minutes,goals,assists,rating,yellow_card,red_card,penalty_scored,penalty_missed,notes,next_opponent,is_derby,is_major,result,created_at,started_known,team_context,representing_team,score_known) " +
          "VALUES($career,$date,$comp,$opp,$home,$ts,$os,$started,$min,$goals,$assists,$rating,$yellow,$red,$ps,$pm,$notes,$next,$derby,$major,$result,$now,$startedKnown,$context,$team,$scoreKnown); SELECT last_insert_rowid();";
        var values = new Dictionary<string,object?> { ["$career"]=careerId,["$date"]=m.Date,["$comp"]=m.Competition,["$opp"]=m.Opponent,["$home"]=m.IsHome,["$ts"]=m.TeamScore,["$os"]=m.OpponentScore,["$started"]=m.Started,["$min"]=m.Minutes,["$goals"]=m.Goals,["$assists"]=m.Assists,["$rating"]=m.Rating,["$yellow"]=m.YellowCard,["$red"]=m.RedCard,["$ps"]=m.PenaltyScored,["$pm"]=m.PenaltyMissed,["$notes"]=m.Notes,["$next"]=m.NextOpponent,["$derby"]=m.IsDerby,["$major"]=m.IsMajorFixture,["$result"]=m.ScoreKnown?m.TeamScore>m.OpponentScore?"W":m.TeamScore<m.OpponentScore?"L":"D":"U",["$now"]=DateTime.UtcNow.ToString("O"),["$startedKnown"]=m.StartedKnown,["$context"]=m.TeamContext,["$team"]=m.RepresentingTeam,["$scoreKnown"]=m.ScoreKnown };
        foreach(var p in values) cmd.Parameters.AddWithValue(p.Key,p.Value ?? DBNull.Value); return (long)cmd.ExecuteScalar()!;
    }
    public (long MatchId,bool Created) SaveProviderMatch(long careerId,string provider,string eventKey,MatchInput m)
    {
        using var db=Open();using var tx=db.BeginTransaction();using(var existing=db.CreateCommand()){existing.Transaction=tx;existing.CommandText="SELECT match_id FROM provider_match_links WHERE career_id=$career AND provider=$provider AND event_key=$event";existing.Parameters.AddWithValue("$career",careerId);existing.Parameters.AddWithValue("$provider",provider);existing.Parameters.AddWithValue("$event",eventKey);var value=existing.ExecuteScalar();if(value is not null){tx.Commit();return(Convert.ToInt64(value),false);}}
        using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO matches(career_id,date,competition,opponent,is_home,team_score,opponent_score,started,minutes,goals,assists,rating,yellow_card,red_card,penalty_scored,penalty_missed,notes,next_opponent,is_derby,is_major,result,created_at,started_known,team_context,representing_team,score_known) VALUES($career,$date,$comp,$opp,$home,$ts,$os,$started,$min,$goals,$assists,$rating,$yellow,$red,$ps,$pm,$notes,$next,$derby,$major,$result,$now,$startedKnown,$context,$team,$scoreKnown); SELECT last_insert_rowid();";var now=DateTime.UtcNow.ToString("O");var values=new Dictionary<string,object?>{{"$career",careerId},{"$date",m.Date},{"$comp",m.Competition},{"$opp",m.Opponent},{"$home",m.IsHome},{"$ts",m.TeamScore},{"$os",m.OpponentScore},{"$started",m.Started},{"$min",m.Minutes},{"$goals",m.Goals},{"$assists",m.Assists},{"$rating",m.Rating},{"$yellow",m.YellowCard},{"$red",m.RedCard},{"$ps",m.PenaltyScored},{"$pm",m.PenaltyMissed},{"$notes",m.Notes},{"$next",m.NextOpponent},{"$derby",m.IsDerby},{"$major",m.IsMajorFixture},{"$result",m.ScoreKnown?m.TeamScore>m.OpponentScore?"W":m.TeamScore<m.OpponentScore?"L":"D":"U"},{"$now",now},{"$startedKnown",m.StartedKnown},{"$context",m.TeamContext},{"$team",m.RepresentingTeam},{"$scoreKnown",m.ScoreKnown}};foreach(var p in values)cmd.Parameters.AddWithValue(p.Key,p.Value??DBNull.Value);var matchId=(long)cmd.ExecuteScalar()!;using var link=db.CreateCommand();link.Transaction=tx;link.CommandText="INSERT INTO provider_match_links(career_id,provider,event_key,match_id,status,created_at,updated_at) VALUES($career,$provider,$event,$match,'Processing',$now,$now)";link.Parameters.AddWithValue("$career",careerId);link.Parameters.AddWithValue("$provider",provider);link.Parameters.AddWithValue("$event",eventKey);link.Parameters.AddWithValue("$match",matchId);link.Parameters.AddWithValue("$now",now);link.ExecuteNonQuery();tx.Commit();return(matchId,true);
    }
    public void CompleteProviderMatch(long careerId,string provider,string eventKey){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE provider_match_links SET status='Complete',updated_at=$now WHERE career_id=$career AND provider=$provider AND event_key=$event";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$provider",provider);cmd.Parameters.AddWithValue("$event",eventKey);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}

    public IReadOnlyList<CareerMatch> GetMatches(long careerId, int limit = 100)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT * FROM (SELECT * FROM matches WHERE career_id=$id ORDER BY date DESC,id DESC LIMIT $limit) ORDER BY date,id"; cmd.Parameters.AddWithValue("$id",careerId); cmd.Parameters.AddWithValue("$limit",limit);
        using var r=cmd.ExecuteReader(); var list=new List<CareerMatch>(); while(r.Read())list.Add(ReadMatch(r));return list;
    }
    public CareerMatch GetMatch(long careerId,long matchId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM matches WHERE career_id=$career AND id=$id";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$id",matchId);using var r=cmd.ExecuteReader();if(!r.Read())throw new KeyNotFoundException("Match not found.");return ReadMatch(r);}
    public bool IsProviderMatch(long careerId,long matchId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT EXISTS(SELECT 1 FROM provider_match_links WHERE career_id=$career AND match_id=$match)";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$match",matchId);return Convert.ToInt32(cmd.ExecuteScalar())==1;}
    public void UpdateMatch(long careerId,long matchId,MatchInput m)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""
            UPDATE matches SET date=$date,competition=$comp,opponent=$opp,is_home=$home,team_score=$ts,opponent_score=$os,
              started=$started,minutes=$min,goals=$goals,assists=$assists,rating=$rating,yellow_card=$yellow,red_card=$red,
              penalty_scored=$ps,penalty_missed=$pm,notes=$notes,next_opponent=$next,is_derby=$derby,is_major=$major,
              result=$result,started_known=$startedKnown,team_context=$context,representing_team=$team,score_known=$scoreKnown WHERE career_id=$career AND id=$match
            """;
        var values=new Dictionary<string,object?>{{"$career",careerId},{"$match",matchId},{"$date",m.Date},{"$comp",m.Competition},{"$opp",m.Opponent},{"$home",m.IsHome},{"$ts",m.TeamScore},{"$os",m.OpponentScore},{"$started",m.Started},{"$min",m.Minutes},{"$goals",m.Goals},{"$assists",m.Assists},{"$rating",m.Rating},{"$yellow",m.YellowCard},{"$red",m.RedCard},{"$ps",m.PenaltyScored},{"$pm",m.PenaltyMissed},{"$notes",m.Notes},{"$next",m.NextOpponent},{"$derby",m.IsDerby},{"$major",m.IsMajorFixture},{"$result",m.ScoreKnown?m.TeamScore>m.OpponentScore?"W":m.TeamScore<m.OpponentScore?"L":"D":"U"},{"$startedKnown",m.StartedKnown},{"$context",m.TeamContext},{"$team",m.RepresentingTeam},{"$scoreKnown",m.ScoreKnown}};
        foreach(var p in values)cmd.Parameters.AddWithValue(p.Key,p.Value??DBNull.Value);if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Match not found.");
    }
    public void ClearGeneratedMatchWorld(long careerId,long matchId)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        using(var interview=db.CreateCommand()){interview.Transaction=tx;interview.CommandText="SELECT answers_json FROM post_match_interviews WHERE career_id=$career AND match_id=$match";interview.Parameters.AddWithValue("$career",careerId);interview.Parameters.AddWithValue("$match",matchId);var answers=interview.ExecuteScalar() as string;if(!string.IsNullOrWhiteSpace(answers)&&answers!="[]")throw new InvalidOperationException("This match has a completed interview. Its public consequences must remain attached to the original result.");}
        Exec(db,tx,"DELETE FROM notifications WHERE career_id=$career AND (dedupe_key IN ('news:'||$match,'social:'||$match,'interview:'||$match) OR EXISTS(SELECT 1 FROM career_events e WHERE e.match_id=$match AND dedupe_key LIKE 'reaction:'||e.id||':%'))",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM conversations WHERE career_id=$career AND EXISTS(SELECT 1 FROM career_events e WHERE e.match_id=$match AND context_json LIKE '%\"eventId\":'||e.id||',%')",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM memories WHERE career_id=$career AND event_id IN (SELECT id FROM career_events WHERE career_id=$career AND match_id=$match)",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM generation_jobs WHERE career_id=$career AND (event_id IN (SELECT id FROM career_events WHERE career_id=$career AND match_id=$match) OR (kind IN ('narratives','player_recovery_choice') AND event_id=$match))",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM news WHERE career_id=$career AND event_id IN (SELECT id FROM career_events WHERE career_id=$career AND match_id=$match)",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM social_posts WHERE career_id=$career AND event_id IN (SELECT id FROM career_events WHERE career_id=$career AND match_id=$match)",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM post_match_interviews WHERE career_id=$career AND match_id=$match",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM career_events WHERE career_id=$career AND match_id=$match",("$career",careerId),("$match",matchId));
        Exec(db,tx,"DELETE FROM narratives WHERE career_id=$career AND evidence_json LIKE '%\"matchId\":'||$match||'%'",("$career",careerId),("$match",matchId));
        tx.Commit();
    }
    public void DeleteManualMatch(long careerId,long matchId)
    {
        if(IsProviderMatch(careerId,matchId))throw new InvalidOperationException("FIFA synchronized matches cannot be deleted. Correct the reviewed fields instead so the import baseline remains safe.");
        ClearGeneratedMatchWorld(careerId,matchId);using var db=Open();using var tx=db.BeginTransaction();Exec(db,tx,"UPDATE fixtures SET status='Upcoming',updated_at=$now WHERE career_id=$career AND status='Completed' AND EXISTS(SELECT 1 FROM matches m WHERE m.id=$match AND m.date=fixtures.date AND lower(m.opponent)=lower(fixtures.opponent))",("$now",DateTime.UtcNow.ToString("O")),("$career",careerId),("$match",matchId));Exec(db,tx,"DELETE FROM matches WHERE career_id=$career AND id=$match",("$career",careerId),("$match",matchId));tx.Commit();
    }
    private static CareerMatch ReadMatch(SqliteDataReader r){int O(string name)=>r.GetOrdinal(name);var careerId=r.GetInt64(O("career_id"));var m=new MatchInput(r.GetString(O("date")),r.GetString(O("competition")),r.GetString(O("opponent")),r.GetBoolean(O("is_home")),r.GetInt32(O("team_score")),r.GetInt32(O("opponent_score")),r.GetBoolean(O("started")),r.GetInt32(O("minutes")),r.GetInt32(O("goals")),r.GetInt32(O("assists")),r.GetDouble(O("rating")),r.GetBoolean(O("yellow_card")),r.GetBoolean(O("red_card")),r.GetBoolean(O("penalty_scored")),r.GetBoolean(O("penalty_missed")),r.GetString(O("notes")),r.IsDBNull(O("next_opponent"))?null:r.GetString(O("next_opponent")),r.GetBoolean(O("is_derby")),r.GetBoolean(O("is_major")),r.GetBoolean(O("started_known")),r.GetString(O("team_context")),r.GetString(O("representing_team")),r.GetBoolean(O("score_known")));return new(r.GetInt64(O("id")),careerId,m,r.GetString(O("result")),DateTime.Parse(r.GetString(O("created_at"))));}

    public void UpsertFixture(long careerId,string provider,string eventKey,string date,string competition,string opponent,
        bool isHome,int confidence,string evidence,string sourceFingerprint,string teamContext="Club",string representingTeam="",string availability="Unknown")
    {
        using var db=Open();using var tx=db.BeginTransaction();
        Exec(db,tx,"UPDATE fixtures SET status='Superseded',updated_at=$now WHERE career_id=$c AND provider=$p AND status='Upcoming' AND event_key<>$event",("$now",DateTime.UtcNow.ToString("O")),("$c",careerId),("$p",provider),("$event",eventKey));
        using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO fixtures(career_id,provider,event_key,date,competition,opponent,is_home,status,confidence,evidence,source_fingerprint,updated_at,team_context,representing_team,availability) VALUES($c,$p,$event,$date,$competition,$opponent,$home,'Upcoming',$confidence,$evidence,$fingerprint,$now,$context,$team,$availability) ON CONFLICT(career_id,provider,event_key) DO UPDATE SET date=excluded.date,competition=excluded.competition,opponent=excluded.opponent,is_home=excluded.is_home,status='Upcoming',confidence=excluded.confidence,evidence=excluded.evidence,source_fingerprint=excluded.source_fingerprint,updated_at=excluded.updated_at,team_context=excluded.team_context,representing_team=excluded.representing_team,availability=excluded.availability";
        cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$event",eventKey);cmd.Parameters.AddWithValue("$date",date);cmd.Parameters.AddWithValue("$competition",competition);cmd.Parameters.AddWithValue("$opponent",opponent);cmd.Parameters.AddWithValue("$home",isHome);cmd.Parameters.AddWithValue("$confidence",confidence);cmd.Parameters.AddWithValue("$evidence",evidence);cmd.Parameters.AddWithValue("$fingerprint",sourceFingerprint);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$context",teamContext);cmd.Parameters.AddWithValue("$team",representingTeam);cmd.Parameters.AddWithValue("$availability",string.IsNullOrWhiteSpace(availability)?"Unknown":availability);cmd.ExecuteNonQuery();tx.Commit();
    }

    public IReadOnlyList<CareerFixture> GetFixtures(long careerId,int limit=50)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,career_id,provider,event_key,date,competition,opponent,is_home,status,confidence,evidence,updated_at,team_context,representing_team,availability FROM fixtures WHERE career_id=$c ORDER BY CASE status WHEN 'Upcoming' THEN 0 ELSE 1 END,date DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var result=new List<CareerFixture>();while(r.Read())result.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetBoolean(7),r.GetString(8),r.GetInt32(9),r.GetString(10),DateTime.Parse(r.GetString(11)),r.GetString(12),r.GetString(13),r.GetString(14)));return result;
    }
    public void CompleteMatchingFixture(long careerId,string date,string opponent){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE fixtures SET status='Completed',updated_at=$now WHERE career_id=$c AND status IN ('Upcoming','Superseded') AND date=$date AND lower(opponent)=lower($opponent)";cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$date",date);cmd.Parameters.AddWithValue("$opponent",opponent);cmd.ExecuteNonQuery();}

    public long SaveEvent(CareerEvent e)
    {
        using var db=Open();using(var existing=db.CreateCommand()){existing.CommandText=e.MatchId is null?"SELECT id FROM career_events WHERE career_id=$career AND match_id IS NULL AND type=$type AND summary=$summary AND metadata_json=$metadata LIMIT 1":"SELECT id FROM career_events WHERE career_id=$career AND match_id=$match AND type=$type AND summary=$summary LIMIT 1";existing.Parameters.AddWithValue("$career",e.CareerId);if(e.MatchId is not null)existing.Parameters.AddWithValue("$match",e.MatchId.Value);existing.Parameters.AddWithValue("$type",e.Type);existing.Parameters.AddWithValue("$summary",e.Summary);if(e.MatchId is null)existing.Parameters.AddWithValue("$metadata",e.MetadataJson);var value=existing.ExecuteScalar();if(value is not null)return Convert.ToInt64(value);}using var cmd=db.CreateCommand(); cmd.CommandText="""INSERT INTO career_events(career_id,match_id,type,timestamp,importance,entities_json,metadata_json,summary,classification) VALUES($c,$m,$t,$ts,$i,$en,$me,$s,$cl); SELECT last_insert_rowid();""";
        object?[] v=[e.CareerId,e.MatchId,e.Type,e.Timestamp.ToString("O"),e.Importance,e.EntitiesJson,e.MetadataJson,e.Summary,e.Classification.ToString()]; string[] n=["$c","$m","$t","$ts","$i","$en","$me","$s","$cl"]; for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value); return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<CareerEvent> GetEvents(long careerId,int limit=100)
    {
        using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT * FROM career_events WHERE career_id=$id ORDER BY timestamp DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var list=new List<CareerEvent>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),DateTime.Parse(r.GetString(4)),r.GetInt32(5),r.GetString(6),r.GetString(7),r.GetString(8),Enum.Parse<FactClassification>(r.GetString(9))));return list;
    }
    public CareerEvent GetEvent(long careerId,long eventId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM career_events WHERE career_id=$career AND id=$id";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$id",eventId);using var r=cmd.ExecuteReader();if(!r.Read())throw new KeyNotFoundException("Career event not found.");return new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),DateTime.Parse(r.GetString(4)),r.GetInt32(5),r.GetString(6),r.GetString(7),r.GetString(8),Enum.Parse<FactClassification>(r.GetString(9)));}

    public long AddMemory(long careerId,long characterId,long? eventId,string text,int importance,int valence,string topic,bool compressed=false,DateTime? timestamp=null)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""INSERT INTO memories(career_id,character_id,event_id,text,timestamp,importance,valence,topic,is_compressed) VALUES($c,$ch,$e,$t,$ts,$i,$v,$topic,$comp); SELECT last_insert_rowid();""";
        object?[] v=[careerId,characterId,eventId,text,(timestamp??DateTime.UtcNow).ToString("O"),importance,valence,topic,compressed];string[] n=["$c","$ch","$e","$t","$ts","$i","$v","$topic","$comp"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<Memory> GetMemories(long characterId,int limit=200)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM memories WHERE character_id=$id ORDER BY timestamp DESC LIMIT $limit";cmd.Parameters.AddWithValue("$id",characterId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var l=new List<Memory>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3),r.GetString(4),DateTime.Parse(r.GetString(5)),r.GetInt32(6),r.GetInt32(7),r.GetString(8),r.GetBoolean(9),r.IsDBNull(10)?null:DateTime.Parse(r.GetString(10)),r.GetBoolean(11)));return l;
    }

    public IReadOnlyList<NewsItem> GetNews(long careerId,int limit=50){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM news WHERE career_id=$id ORDER BY published_at DESC LIMIT $n";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<NewsItem>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt32(7),DateTime.Parse(r.GetString(8))));return l;}
    public IReadOnlyList<SocialPost> GetSocial(long careerId,int limit=50){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM social_posts WHERE career_id=$id ORDER BY published_at DESC LIMIT $n";cmd.Parameters.AddWithValue("$id",careerId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<SocialPost>();while(r.Read())l.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),DateTime.Parse(r.GetString(6))));return l;}

    public long AddNews(long careerId,long? eventId,string outlet,string headline,string body,string sentiment,int importance,DateTime? timestamp=null){using var db=Open();if(eventId is not null){using var existing=db.CreateCommand();existing.CommandText="SELECT id FROM news WHERE career_id=$career AND event_id=$event AND outlet=$outlet AND headline=$headline LIMIT 1";existing.Parameters.AddWithValue("$career",careerId);existing.Parameters.AddWithValue("$event",eventId.Value);existing.Parameters.AddWithValue("$outlet",outlet);existing.Parameters.AddWithValue("$headline",headline);var value=existing.ExecuteScalar();if(value is not null)return Convert.ToInt64(value);}using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO news(career_id,event_id,outlet,headline,body,sentiment,importance,published_at) VALUES($c,$e,$o,$h,$b,$s,$i,$t); SELECT last_insert_rowid();";object?[]v=[careerId,eventId,outlet,headline,body,sentiment,importance,(timestamp??DateTime.UtcNow).ToString("O")];string[]n=["$c","$e","$o","$h","$b","$s","$i","$t"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;}
    public bool AddProviderNews(long careerId,string providerKey,string headline,string body,int importance,string careerDate,bool notify=false){using var db=Open();using var tx=db.BeginTransaction();var timestamp=DateTime.TryParse(careerDate,out var date)?date:DateTime.UtcNow;using var marker=db.CreateCommand();marker.Transaction=tx;marker.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,kind,dedupe_key,status,created_at,updated_at) VALUES($c,'provider_news',$key,'complete',$now,$now)";marker.Parameters.AddWithValue("$c",careerId);marker.Parameters.AddWithValue("$key",$"provider-news:{careerId}:{providerKey}");marker.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));var added=marker.ExecuteNonQuery()==1;if(added){using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO news(career_id,event_id,outlet,headline,body,sentiment,importance,published_at) VALUES($c,NULL,'FIFA Wire',$h,$b,'neutral',$i,$t)";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$h",headline);cmd.Parameters.AddWithValue("$b",body);cmd.Parameters.AddWithValue("$i",importance);cmd.Parameters.AddWithValue("$t",timestamp.ToString("O"));cmd.ExecuteNonQuery();}if(notify){using var notice=db.CreateCommand();notice.Transaction=tx;notice.CommandText="INSERT OR IGNORE INTO notifications(career_id,kind,title,body,action,priority,dedupe_key,created_at) VALUES($career,'FIFA News',$title,$body,'News',$importance,$dedupe,$timestamp)";notice.Parameters.AddWithValue("$career",careerId);notice.Parameters.AddWithValue("$title",headline);notice.Parameters.AddWithValue("$body",body);notice.Parameters.AddWithValue("$importance",Math.Clamp(importance,1,100));notice.Parameters.AddWithValue("$dedupe","fifa-news:"+providerKey);notice.Parameters.AddWithValue("$timestamp",timestamp.ToString("O"));notice.ExecuteNonQuery();}tx.Commit();return added;}
    public long AddSocial(long careerId,long? eventId,string author,string persona,string content,DateTime? timestamp=null){using var db=Open();if(eventId is not null){using var existing=db.CreateCommand();existing.CommandText="SELECT id FROM social_posts WHERE career_id=$career AND event_id=$event AND author=$author AND content=$content LIMIT 1";existing.Parameters.AddWithValue("$career",careerId);existing.Parameters.AddWithValue("$event",eventId.Value);existing.Parameters.AddWithValue("$author",author);existing.Parameters.AddWithValue("$content",content);var value=existing.ExecuteScalar();if(value is not null)return Convert.ToInt64(value);}using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO social_posts(career_id,event_id,author,persona,content,published_at) VALUES($c,$e,$a,$p,$x,$t); SELECT last_insert_rowid();";object?[]v=[careerId,eventId,author,persona,content,(timestamp??DateTime.UtcNow).ToString("O")];string[]n=["$c","$e","$a","$p","$x","$t"];for(int i=0;i<n.Length;i++)cmd.Parameters.AddWithValue(n[i],v[i]??DBNull.Value);return(long)cmd.ExecuteScalar()!;}
    public long StartConversation(long careerId,long characterId,SceneType scene,string context="{}",DateTime? timestamp=null){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO conversations(career_id,character_id,scene,timestamp,context_json) VALUES($c,$ch,$s,$t,$x); SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$ch",characterId);cmd.Parameters.AddWithValue("$s",scene.ToString());cmd.Parameters.AddWithValue("$t",(timestamp??DateTime.UtcNow).ToString("O"));cmd.Parameters.AddWithValue("$x",context);return(long)cmd.ExecuteScalar()!;}
    public void AddMessage(long conversationId,string role,string content,DateTime? timestamp=null){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO messages(conversation_id,role,content,timestamp) VALUES($c,$r,$x,$t)";cmd.Parameters.AddWithValue("$c",conversationId);cmd.Parameters.AddWithValue("$r",role);cmd.Parameters.AddWithValue("$x",content);cmd.Parameters.AddWithValue("$t",(timestamp??DateTime.UtcNow).ToString("O"));cmd.ExecuteNonQuery();}
    public IReadOnlyList<ConversationMessage> GetMessages(long careerId,long characterId,int limit=40){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""SELECT m.conversation_id,m.role,m.content,m.timestamp,c.scene,c.timestamp,(SELECT p.content FROM messages p WHERE p.conversation_id=m.conversation_id AND p.role='user' AND p.id<m.id ORDER BY p.id DESC LIMIT 1) FROM messages m JOIN conversations c ON c.id=m.conversation_id WHERE c.career_id=$c AND c.character_id=$ch ORDER BY m.timestamp DESC,m.id DESC LIMIT $n""";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$ch",characterId);cmd.Parameters.AddWithValue("$n",limit);using var r=cmd.ExecuteReader();var l=new List<ConversationMessage>();while(r.Read())l.Add(new(r.GetString(1),r.GetString(2),DateTime.Parse(r.GetString(3)),r.GetInt64(0),r.GetString(4),DateTime.Parse(r.GetString(5)),r.IsDBNull(6)?null:r.GetString(6)));l.Reverse();return l;}

    public string? GetSetting(string key){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);return cmd.ExecuteScalar() as string;}
    public long? FindCareerIdByFifaPlayerId(int playerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT key FROM settings WHERE key LIKE 'career:%:fifa_player_id' AND value=$player LIMIT 1";cmd.Parameters.AddWithValue("$player",playerId.ToString());var key=cmd.ExecuteScalar() as string;if(key is null)return null;var parts=key.Split(':');return parts.Length>=3&&long.TryParse(parts[1],out var id)?id:null;}
    public void SetSetting(string key,string value){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$k",key);cmd.Parameters.AddWithValue("$v",value);cmd.ExecuteNonQuery();}
    public void Log(string category,string detail){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO debug_log(category,detail,created_at) VALUES($c,$d,$t)";cmd.Parameters.AddWithValue("$c",category);cmd.Parameters.AddWithValue("$d",detail);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void AddUsage(string provider,string model,int inputTokens,int outputTokens,double? cost=null){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO usage_log(provider,model,input_tokens,output_tokens,cost,created_at) VALUES($p,$m,$i,$o,$c,$t)";cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$m",model);cmd.Parameters.AddWithValue("$i",inputTokens);cmd.Parameters.AddWithValue("$o",outputTokens);cmd.Parameters.AddWithValue("$c",cost is null?DBNull.Value:cost);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public bool HasProviderImport(long careerId,string provider,string eventKey){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT 1 FROM provider_imports WHERE career_id=$c AND provider=$p AND event_key=$e LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);return cmd.ExecuteScalar() is not null;}
    public string? GetLatestProviderPayload(long careerId,string provider){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT payload_json FROM provider_imports WHERE career_id=$c AND provider=$p ORDER BY imported_at DESC,id DESC LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);return cmd.ExecuteScalar() as string;}
    public void RecordProviderImport(long careerId,string provider,string eventKey,string sourcePath,string fingerprint,DateTime capturedAt,string payload){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO provider_imports(career_id,provider,event_key,source_path,file_fingerprint,captured_at,payload_json,imported_at) VALUES($c,$p,$e,$s,$f,$captured,$payload,$now)";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);cmd.Parameters.AddWithValue("$s",sourcePath);cmd.Parameters.AddWithValue("$f",fingerprint);cmd.Parameters.AddWithValue("$captured",capturedAt.ToString("O"));cmd.Parameters.AddWithValue("$payload",payload);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void StageMatchReview(long careerId,string provider,string eventKey,string sourcePath,string fingerprint,DateTime capturedAt,string matchJson,string snapshotJson)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="""
            INSERT INTO match_reviews(career_id,provider,event_key,source_path,file_fingerprint,captured_at,match_json,snapshot_json,status,created_at,updated_at)
            VALUES($c,$p,$e,$source,$fingerprint,$captured,$match,$snapshot,'Pending',$now,$now)
            ON CONFLICT(career_id,provider,event_key) DO UPDATE SET source_path=excluded.source_path,file_fingerprint=excluded.file_fingerprint,
              captured_at=excluded.captured_at,match_json=excluded.match_json,snapshot_json=excluded.snapshot_json,updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);cmd.Parameters.AddWithValue("$source",sourcePath);cmd.Parameters.AddWithValue("$fingerprint",fingerprint);cmd.Parameters.AddWithValue("$captured",capturedAt.ToString("O"));cmd.Parameters.AddWithValue("$match",matchJson);cmd.Parameters.AddWithValue("$snapshot",snapshotJson);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<MatchReview> GetMatchReviews(long careerId,string? status=null)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM match_reviews WHERE career_id=$c"+(status is null?"":" AND status=$status")+" ORDER BY captured_at DESC,id DESC";cmd.Parameters.AddWithValue("$c",careerId);if(status is not null)cmd.Parameters.AddWithValue("$status",status);using var r=cmd.ExecuteReader();var list=new List<MatchReview>();while(r.Read())list.Add(new(r.GetInt64(r.GetOrdinal("id")),r.GetInt64(r.GetOrdinal("career_id")),r.GetString(r.GetOrdinal("provider")),r.GetString(r.GetOrdinal("event_key")),r.GetString(r.GetOrdinal("source_path")),r.GetString(r.GetOrdinal("file_fingerprint")),DateTime.Parse(r.GetString(r.GetOrdinal("captured_at"))),r.GetString(r.GetOrdinal("match_json")),r.GetString(r.GetOrdinal("snapshot_json")),r.GetString(r.GetOrdinal("status")),DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),DateTime.Parse(r.GetString(r.GetOrdinal("updated_at")))));return list;
    }
    public string? GetMatchReviewStatus(long careerId,string provider,string eventKey){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT status FROM match_reviews WHERE career_id=$c AND provider=$p AND event_key=$e";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);return cmd.ExecuteScalar() as string;}
    public void SetMatchReviewStatus(long reviewId,string status){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE match_reviews SET status=$status,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$status",status);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",reviewId);if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Match review not found.");}
    public void SetMatchReviewStatus(long careerId,string provider,string eventKey,string status){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE match_reviews SET status=$status,updated_at=$now WHERE career_id=$c AND provider=$p AND event_key=$e";cmd.Parameters.AddWithValue("$status",status);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$e",eventKey);cmd.ExecuteNonQuery();}
    public PostMatchInterview CreatePostMatchInterview(long careerId,long matchId,string triggerType,int importance,string questionsJson)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO post_match_interviews(career_id,match_id,trigger_type,importance,questions_json,created_at,updated_at) VALUES($c,$m,$trigger,$importance,$questions,$now,$now)";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$m",matchId);cmd.Parameters.AddWithValue("$trigger",triggerType);cmd.Parameters.AddWithValue("$importance",importance);cmd.Parameters.AddWithValue("$questions",questionsJson);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();return GetPostMatchInterview(careerId,matchId)??throw new InvalidOperationException("Could not create the post-match interview.");
    }
    public PostMatchInterview? GetPendingPostMatchInterview(long careerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM post_match_interviews WHERE career_id=$c AND status='Pending' ORDER BY created_at DESC,id DESC LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);using var r=cmd.ExecuteReader();return r.Read()?ReadPostMatchInterview(r):null;}
    public PostMatchInterview? GetLatestPostMatchInterview(long careerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM post_match_interviews WHERE career_id=$c ORDER BY updated_at DESC,id DESC LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);using var r=cmd.ExecuteReader();return r.Read()?ReadPostMatchInterview(r):null;}
    public PostMatchInterview? GetPostMatchInterview(long careerId,long matchId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM post_match_interviews WHERE career_id=$c AND match_id=$m";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$m",matchId);using var r=cmd.ExecuteReader();return r.Read()?ReadPostMatchInterview(r):null;}
    public void UpdatePostMatchInterview(long id,string answersJson,int currentQuestion,string status){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE post_match_interviews SET answers_json=$answers,current_question=$question,status=$status,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$answers",answersJson);cmd.Parameters.AddWithValue("$question",currentQuestion);cmd.Parameters.AddWithValue("$status",status);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",id);if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Post-match interview not found.");}
    public void UpdatePostMatchInterviewDialogue(long id,string questionsJson,string answersJson,int currentQuestion,string status){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE post_match_interviews SET questions_json=$questions,answers_json=$answers,current_question=$question,status=$status,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$questions",questionsJson);cmd.Parameters.AddWithValue("$answers",answersJson);cmd.Parameters.AddWithValue("$question",currentQuestion);cmd.Parameters.AddWithValue("$status",status);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",id);if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("Post-match interview not found.");}
    private static PostMatchInterview ReadPostMatchInterview(SqliteDataReader r)=>new(r.GetInt64(r.GetOrdinal("id")),r.GetInt64(r.GetOrdinal("career_id")),r.GetInt64(r.GetOrdinal("match_id")),r.GetString(r.GetOrdinal("trigger_type")),r.GetInt32(r.GetOrdinal("importance")),r.GetString(r.GetOrdinal("questions_json")),r.GetString(r.GetOrdinal("answers_json")),r.GetInt32(r.GetOrdinal("current_question")),r.GetString(r.GetOrdinal("status")),DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),DateTime.Parse(r.GetString(r.GetOrdinal("updated_at"))));
    public bool AddNotification(long careerId,string kind,string title,string body,string action,int priority,string dedupeKey,DateTime? timestamp=null)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO notifications(career_id,kind,title,body,action,priority,dedupe_key,created_at) VALUES($c,$kind,$title,$body,$action,$priority,$dedupe,$now)";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$kind",kind);cmd.Parameters.AddWithValue("$title",title);cmd.Parameters.AddWithValue("$body",body);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$priority",Math.Clamp(priority,1,100));cmd.Parameters.AddWithValue("$dedupe",dedupeKey);cmd.Parameters.AddWithValue("$now",(timestamp??DateTime.UtcNow).ToString("O"));return cmd.ExecuteNonQuery()==1;
    }
    public bool AddAutomaticReaction(long careerId,long characterId,long eventId,SceneType scene,string context,string text,int importance,int valence,string topic,string notificationKind,string title,string notificationAction,int priority,string dedupeKey,DateTime timestamp,bool queueLlm=true)
    {
        using var db=Open();using var tx=db.BeginTransaction();var when=timestamp.ToString("O");
        using(var notice=db.CreateCommand()){notice.Transaction=tx;notice.CommandText="INSERT OR IGNORE INTO notifications(career_id,kind,title,body,action,priority,dedupe_key,created_at) VALUES($c,$kind,$title,$body,$action,$priority,$dedupe,$now)";notice.Parameters.AddWithValue("$c",careerId);notice.Parameters.AddWithValue("$kind",notificationKind);notice.Parameters.AddWithValue("$title",title);notice.Parameters.AddWithValue("$body",text);notice.Parameters.AddWithValue("$action",notificationAction);notice.Parameters.AddWithValue("$priority",Math.Clamp(priority,1,100));notice.Parameters.AddWithValue("$dedupe",dedupeKey);notice.Parameters.AddWithValue("$now",when);if(notice.ExecuteNonQuery()!=1){tx.Rollback();return false;}}
        long conversationId;using(var conversation=db.CreateCommand()){conversation.Transaction=tx;conversation.CommandText="INSERT INTO conversations(career_id,character_id,scene,timestamp,context_json) VALUES($career,$character,$scene,$now,$context); SELECT last_insert_rowid();";conversation.Parameters.AddWithValue("$career",careerId);conversation.Parameters.AddWithValue("$character",characterId);conversation.Parameters.AddWithValue("$scene",scene.ToString());conversation.Parameters.AddWithValue("$now",when);conversation.Parameters.AddWithValue("$context",context);conversationId=(long)conversation.ExecuteScalar()!;}
        using(var message=db.CreateCommand()){message.Transaction=tx;message.CommandText="INSERT INTO messages(conversation_id,role,content,timestamp) VALUES($conversation,'assistant',$text,$now)";message.Parameters.AddWithValue("$conversation",conversationId);message.Parameters.AddWithValue("$text",text);message.Parameters.AddWithValue("$now",when);message.ExecuteNonQuery();}
        using(var memory=db.CreateCommand()){memory.Transaction=tx;memory.CommandText="INSERT INTO memories(career_id,character_id,event_id,text,timestamp,importance,valence,topic) VALUES($career,$character,$event,$text,$now,$importance,$valence,$topic)";memory.Parameters.AddWithValue("$career",careerId);memory.Parameters.AddWithValue("$character",characterId);memory.Parameters.AddWithValue("$event",eventId);memory.Parameters.AddWithValue("$text",text);memory.Parameters.AddWithValue("$now",when);memory.Parameters.AddWithValue("$importance",Math.Clamp(importance,1,100));memory.Parameters.AddWithValue("$valence",Math.Clamp(valence,-100,100));memory.Parameters.AddWithValue("$topic",topic);memory.ExecuteNonQuery();}
        if(queueLlm){using var job=db.CreateCommand();job.Transaction=tx;job.CommandText="INSERT OR IGNORE INTO generation_jobs(career_id,event_id,kind,dedupe_key,status,payload_json,created_at,updated_at) VALUES($career,$event,'automatic_reaction_llm',$key,'Pending',$payload,$now,$now)";job.Parameters.AddWithValue("$career",careerId);job.Parameters.AddWithValue("$event",eventId);job.Parameters.AddWithValue("$key",$"reaction-llm:{careerId}:{eventId}:{characterId}");job.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(new{conversationId,characterId,eventId,scene=scene.ToString(),notificationDedupeKey=dedupeKey}));job.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));job.ExecuteNonQuery();}
        tx.Commit();return true;
    }
    public IReadOnlyList<CareerNotification> GetNotifications(long careerId,int limit=100)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,career_id,kind,title,body,action,priority,is_read,dedupe_key,created_at FROM notifications WHERE career_id=$c ORDER BY is_read,priority DESC,created_at DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var list=new List<CareerNotification>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt32(6),r.GetBoolean(7),r.GetString(8),DateTime.Parse(r.GetString(9))));return list;
    }
    public void MarkNotificationRead(long id){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE notifications SET is_read=1 WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}
    public void MarkNotificationRead(long careerId,string dedupeKey){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE notifications SET is_read=1 WHERE career_id=$career AND dedupe_key=$dedupe";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$dedupe",dedupeKey);cmd.ExecuteNonQuery();}
    public void MarkMessageNotificationsRead(long careerId,long characterId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE notifications SET is_read=1 WHERE career_id=$career AND is_read=0 AND kind IN ('Message','Manager') AND action=$action";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$action",$"Messages:{characterId}");cmd.ExecuteNonQuery();}
    public void MarkAllNotificationsRead(long careerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE notifications SET is_read=1 WHERE career_id=$c";cmd.Parameters.AddWithValue("$c",careerId);cmd.ExecuteNonQuery();}
    public int ClearReadNotifications(long careerId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="DELETE FROM notifications WHERE career_id=$career AND is_read=1";cmd.Parameters.AddWithValue("$career",careerId);return cmd.ExecuteNonQuery();}
    public void ResolveObsoleteNotifications(long careerId)
    {
        using var db=Open();using var tx=db.BeginTransaction();Exec(db,tx,"UPDATE notifications SET is_read=1 WHERE career_id=$career AND action='Review' AND NOT EXISTS(SELECT 1 FROM match_reviews r WHERE r.career_id=$career AND r.status='Pending' AND notifications.dedupe_key='review:'||r.event_key)",("$career",careerId));Exec(db,tx,"UPDATE notifications SET is_read=1 WHERE career_id=$career AND action='Press' AND NOT EXISTS(SELECT 1 FROM post_match_interviews i WHERE i.career_id=$career AND i.status='Pending' AND notifications.dedupe_key='interview:'||i.match_id)",("$career",careerId));tx.Commit();
    }
    public bool AddCareerProgressSnapshot(CareerProgressSnapshot snapshot)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="INSERT OR IGNORE INTO career_progress_snapshots(career_id,captured_at,career_date,club,league,position,shirt_number,overall,form,injured,appearances,goals,assists,yellow_cards,red_cards,source_fingerprint) VALUES($career,$captured,$date,$club,$league,$position,$number,$overall,$form,$injured,$apps,$goals,$assists,$yellow,$red,$fingerprint)";cmd.Parameters.AddWithValue("$career",snapshot.CareerId);cmd.Parameters.AddWithValue("$captured",snapshot.CapturedAt.ToString("O"));cmd.Parameters.AddWithValue("$date",snapshot.CareerDate);cmd.Parameters.AddWithValue("$club",snapshot.Club);cmd.Parameters.AddWithValue("$league",snapshot.League);cmd.Parameters.AddWithValue("$position",snapshot.Position);cmd.Parameters.AddWithValue("$number",snapshot.ShirtNumber);cmd.Parameters.AddWithValue("$overall",snapshot.Overall);cmd.Parameters.AddWithValue("$form",snapshot.Form);cmd.Parameters.AddWithValue("$injured",snapshot.Injured);cmd.Parameters.AddWithValue("$apps",snapshot.Appearances);cmd.Parameters.AddWithValue("$goals",snapshot.Goals);cmd.Parameters.AddWithValue("$assists",snapshot.Assists);cmd.Parameters.AddWithValue("$yellow",snapshot.YellowCards);cmd.Parameters.AddWithValue("$red",snapshot.RedCards);cmd.Parameters.AddWithValue("$fingerprint",snapshot.SourceFingerprint);return cmd.ExecuteNonQuery()==1;
    }
    public CareerProgressSnapshot? GetLatestCareerProgressSnapshot(long careerId)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM career_progress_snapshots WHERE career_id=$c ORDER BY captured_at DESC,id DESC LIMIT 1";cmd.Parameters.AddWithValue("$c",careerId);using var r=cmd.ExecuteReader();return r.Read()?ReadProgressSnapshot(r):null;
    }
    public IReadOnlyList<CareerProgressSnapshot> GetCareerProgressSnapshots(long careerId,int limit=100)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT * FROM career_progress_snapshots WHERE career_id=$c ORDER BY captured_at DESC,id DESC LIMIT $limit";cmd.Parameters.AddWithValue("$c",careerId);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var list=new List<CareerProgressSnapshot>();while(r.Read())list.Add(ReadProgressSnapshot(r));return list;
    }
    private static CareerProgressSnapshot ReadProgressSnapshot(SqliteDataReader r)=>new(r.GetInt64(r.GetOrdinal("id")),r.GetInt64(r.GetOrdinal("career_id")),DateTime.Parse(r.GetString(r.GetOrdinal("captured_at"))),r.GetString(r.GetOrdinal("career_date")),r.GetString(r.GetOrdinal("club")),r.GetString(r.GetOrdinal("league")),r.GetString(r.GetOrdinal("position")),r.GetInt32(r.GetOrdinal("shirt_number")),r.GetInt32(r.GetOrdinal("overall")),r.GetInt32(r.GetOrdinal("form")),r.GetBoolean(r.GetOrdinal("injured")),r.GetInt32(r.GetOrdinal("appearances")),r.GetInt32(r.GetOrdinal("goals")),r.GetInt32(r.GetOrdinal("assists")),r.GetInt32(r.GetOrdinal("yellow_cards")),r.GetInt32(r.GetOrdinal("red_cards")),r.GetString(r.GetOrdinal("source_fingerprint")));
    public IReadOnlyList<GenerationJob> GetPendingGenerationJobs(long careerId,string kind,int limit=4){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,career_id,event_id,kind,dedupe_key,status,attempts,error,payload_json,created_at,updated_at FROM generation_jobs WHERE career_id=$career AND kind=$kind AND status='Pending' ORDER BY created_at,id LIMIT $limit";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$kind",kind);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();var jobs=new List<GenerationJob>();while(r.Read())jobs.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt32(6),r.IsDBNull(7)?null:r.GetString(7),r.GetString(8),DateTime.Parse(r.GetString(9)),DateTime.Parse(r.GetString(10))));return jobs;}
    public IReadOnlyList<GenerationJob> GetGenerationJobs(long careerId,string kind){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,career_id,event_id,kind,dedupe_key,status,attempts,error,payload_json,created_at,updated_at FROM generation_jobs WHERE career_id=$career AND kind=$kind ORDER BY created_at,id";cmd.Parameters.AddWithValue("$career",careerId);cmd.Parameters.AddWithValue("$kind",kind);using var r=cmd.ExecuteReader();var jobs=new List<GenerationJob>();while(r.Read())jobs.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt32(6),r.IsDBNull(7)?null:r.GetString(7),r.GetString(8),DateTime.Parse(r.GetString(9)),DateTime.Parse(r.GetString(10))));return jobs;}
    public void CompleteGenerationJob(long id){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE generation_jobs SET status='Complete',error=NULL,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void FailGenerationJob(long id,string error,int maxAttempts=3){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE generation_jobs SET attempts=attempts+1,status=CASE WHEN attempts+1 >= $max THEN 'Failed' ELSE 'Pending' END,error=$error,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$max",maxAttempts);cmd.Parameters.AddWithValue("$error",error.Length>500?error[..500]:error);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.ExecuteNonQuery();}
    public void ReplaceAutomaticReactionText(long careerId,long conversationId,long characterId,long eventId,string notificationDedupeKey,string text)
    {
        using var db=Open();using var tx=db.BeginTransaction();Exec(db,tx,"UPDATE messages SET content=$text WHERE conversation_id=$conversation AND role='assistant'",("$text",text),("$conversation",conversationId));Exec(db,tx,"UPDATE notifications SET body=$text WHERE career_id=$career AND dedupe_key=$dedupe",("$text",text),("$career",careerId),("$dedupe",notificationDedupeKey));Exec(db,tx,"UPDATE memories SET text=$text WHERE career_id=$career AND character_id=$character AND event_id=$event AND topic='automatic post-match reaction'",("$text",text),("$career",careerId),("$character",characterId),("$event",eventId));tx.Commit();
    }
    public void UpdateCareerFromProvider(long id,string playerName,string nationality,int age,string club,string league,string season,string currentDate,string position,int shirtNumber){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE careers SET player_name=$player,nationality=$nation,age=$age,club=$club,league=$league,season=$season,current_date=$date,position=$position,shirt_number=$number,updated_at=$now WHERE id=$id";cmd.Parameters.AddWithValue("$player",playerName);cmd.Parameters.AddWithValue("$nation",nationality);cmd.Parameters.AddWithValue("$age",age);cmd.Parameters.AddWithValue("$club",club);cmd.Parameters.AddWithValue("$league",league);cmd.Parameters.AddWithValue("$season",season);cmd.Parameters.AddWithValue("$date",currentDate);cmd.Parameters.AddWithValue("$position",position);cmd.Parameters.AddWithValue("$number",shirtNumber);cmd.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}

    public void Backup(string destination){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);using var source=Open();using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination}.ToString());target.Open();source.BackupDatabase(target);}
    public void Restore(string source)
    {
        using(var check=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly}.ToString())){check.Open();using var cmd=check.CreateCommand();cmd.CommandText="SELECT count(*) FROM schema_migrations";_ = cmd.ExecuteScalar() ?? throw new InvalidDataException("Not a Touchline backup.");}
        File.Copy(source,Path,true);
    }
    private static void Exec(SqliteConnection db,SqliteTransaction tx,string sql,params(string,object)[] values){using var c=db.CreateCommand();c.Transaction=tx;c.CommandText=sql;foreach(var p in values)c.Parameters.AddWithValue(p.Item1,p.Item2);c.ExecuteNonQuery();}
}
