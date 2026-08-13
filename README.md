# Touchline - FIFA 18 AI Career Companion

Touchline is a Windows desktop companion for FIFA 18 Player Career Mode. FIFA remains the authority for matches, statistics, transfers, tables, and selection; Touchline reads Player Career save files without modifying them, stages detected results for review, and builds a persistent world of teammates, managers, press, news, social reactions, relationships, memories, and emerging narratives around imported facts.

## Screenshots

![Career briefing dashboard](screenshots/screenshot2.png)

![Squad synchronization view](screenshots/screenshot1.png)

## Technology and architecture

The application uses .NET 9 and WPF for a native Windows experience, SQLite for a durable local career world, and the OpenAI Responses API for optional structured character dialogue. This choice keeps installation straightforward, permits future Windows-process integration, and provides clear separation between UI, application services, domain simulation, data providers, and persistence.

```text
CareerCompanion.App (WPF UI)
        |
Application services / deterministic simulation / LLM orchestration
        |
ICareerDataProvider -> ManualCareerDataProvider / Fifa18SaveCareerDataProvider
        |
SQLite career-world.db
```

The app owns facts, state, chronology, relationships, memory, bounds, and event significance. The model supplies language and interpretation. Stored information is classified as `HistoricalFact`, `SaveFact`, or `SimulatedInterpretation`; model output cannot rewrite a match or promote fiction to a FIFA save fact.

## Included V1 systems

- First-run fictional demonstration career and manual career creation
- Read-only FIFA 18 Player Career save discovery, including OneDrive-redirected Documents folders
- Startup synchronization and a self-healing save-folder watcher with career routing by FIFA player ID
- Persistent match-review inbox, career linking, dismissal history, chronological safety checks, and idempotent provider match storage
- Automatic teammate reconciliation by stable FIFA player ID, with transfers and departures preserved in character history
- Automatic verified manager and agent creation, replacement reconciliation, FIFA Wire news import, international call-up detection, progression snapshots, and next-fixture detection
- Quick squad/manager/journalist entry; structured profile editing and JSON import/export
- Fast post-match form and deterministic win/loss/draw, scoring, card, streak, derby, major-fixture, heavy-defeat, and late-winner events
- Importance heuristics, selective reaction targeting, silence for minor events, and narrative emergence/decay
- Multi-dimensional bounded relationships and validated model deltas
- Per-character memory persistence, relevance ranking, and conservative compression architecture
- Automatic incoming teammate and manager reactions, stateful characters, an unread World Updates inbox, and sender-aware navigation
- Persistent automatic-generation jobs that show an immediate offline fallback and rewrite it with character-aware LLM dialogue when an API key is available
- Scene-aware messages, manager and agent conversations, conditional multi-question post-match interviews with grounded AI follow-ups and offline fallback, and consequential public statements
- Differentiated offline news outlets and social personas
- Career dashboard, chronological timeline, settings, debug inspector, and DPAPI-protected API keys
- Consistent SQLite export/restore backup
- OpenAI Responses API provider with strict JSON Schema, timeout/rate-limit/network/malformed-output handling
- Usage token logging, configurable model routing and generation switches
- A deterministic bulk simulator and automated tests

## Requirements

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) for development; a self-contained publish does not require a separate runtime

## Development and run

```powershell
dotnet restore RPSystem.slnx
dotnet run --project src/CareerCompanion.App/CareerCompanion.App.csproj
```

On first launch, a small career named **Touchline Demo (Fictional)** is created so all offline screens can be evaluated immediately. Create your real save under Career.

## FIFA 18 automatic sync

Open **Career** and use the **Automatic FIFA 18 Sync** card. Touchline discovers the newest `Career*` file under the FIFA 18 settings directory. Enable the watcher to scan again after FIFA writes a save, or use **Scan newest save** at any time.

On the first scan, Touchline stages the detected FIFA save as a pending link. During automatic startup or watcher scans, it creates and links the companion career automatically when the fictional demo is the only local career. During a manual scan, it asks before linking. The link includes the FIFA player, club, season, position, shirt number, current career date, teammates, verified club manager, named agent, FIFA news, progression baseline, and the next confirmed fixture.

A detected result is stored in the match-review inbox and survives restarts. The first result and every ambiguous result stay editable because cumulative statistics or unsupported fields may be uncertain. Once Touchline has a chronological baseline, a result is imported automatically only when the save proves exactly one new appearance and a matching FIFA review proves the opponent and score. Dismissed detections are remembered. Provider-linked match storage, events, reactions, and media are deduplicated so a retry cannot create another match.

Teammates are reconciled using their stable FIFA player IDs. A later scan updates factual details such as club, position, shirt number, overall rating, form, and injury state without replacing personalities, relationships, conversations, or memories. Players missing from a later club squad are retained as former teammates rather than deleted. Real-player display names come from the embedded offline FIFA 18 ID index; edited and generated-player names come from the save.

The fixture view stores confirmed FIFA preview records, surfaces FIFA's own match briefing and availability notes, completes a matching played fixture, and supersedes the previous upcoming fixture when a newer preview appears. FIFA's complete future-season schedule is not present in the tested career save, so Touchline does not invent unconfirmed calendar entries.

Each distinct save snapshot updates verified identity and career facts and records progression such as overall rating, club, position, and shirt-number changes. FIFA news appears as a dated FIFA Wire feed. Important matches can create incoming teammate or manager messages, press duties, news, social reactions, memories, relationship changes, and persistent character mood. These world effects use the in-career date rather than the computer's current date.

Save files are opened read-only after their size and timestamp settle. Touchline writes only to its own SQLite database. Each imported FIFA event has a stable provider key and a source fingerprint, so rescanning the same result does not create a duplicate. Keep using FIFA's normal save and backup practices; no third-party tool can guarantee recovery from a corrupted game save.

## OpenAI configuration

The application remains useful without an API key. Career entry, SQLite, deterministic events, squad management, offline media, timeline, press statement storage, debug tools, and backup remain available.

For character messages, journalist follow-ups, and richer automatic incoming reactions:

1. Open Settings.
2. Paste an OpenAI API key and choose default/premium models.
3. Save settings. The key is encrypted with Windows DPAPI for the current Windows user and is never logged.

For development, `OPENAI_API_KEY`, `OPENAI_DEFAULT_MODEL`, and `OPENAI_PREMIUM_MODEL` environment variables are also recognized; see `.env.example`. `TOUCHLINE_DATA_DIR` can point a development or smoke-test run at an isolated database directory. No dotenv file is automatically loaded. The REST contract follows the official [Responses API structured-output format](https://developers.openai.com/api/docs/guides/structured-outputs).

Model availability and pricing change. Models are settings, not hard-coded business rules. The default configuration uses a cost-sensitive model for routine dialogue and a premium model for high-importance scenes; premium routing can be disabled.

## Database, backup, and restore

The authoritative database is:

```text
%LOCALAPPDATA%\TouchlineCareerCompanion\career-world.db
```

Use **Settings > Export Backup** for an online-consistent SQLite copy. **Restore Backup** validates the migration table and requires confirmation before replacing local data. Back up before restoring.

Schema creation is versioned through `schema_migrations`; future migrations should be additive and registered with a new version. FIFA imports are audited in `provider_imports`; stable squad links live in `provider_entities`; confirmed previews live in `fixtures`.

## Tests, build, and simulation

```powershell
dotnet test RPSystem.slnx -c Release
dotnet build RPSystem.slnx -c Release
dotnet run --project tools/CareerCompanion.Simulator -- 100
dotnet run --project tools/CareerCompanion.Simulator -- --probe-fifa18 "C:\path\to\Career..."
```

The Windows executable built by the repo is:

```text
src\CareerCompanion.App\bin\Release\net9.0-windows\CareerCompanion.App.exe
```

If you want a self-contained publish under `artifacts\win-x64`, add a Windows runtime identifier to the project or restore for `win-x64` before publishing.

The simulator accepts a match count and optional output database path. It generates a repeatable career containing varied outcomes, goals, a red card, derbies, and cup fixtures, then reports event/media volume for spam and narrative inspection. `--probe-fifa18` parses and prints a save snapshot without importing it or changing the source file.

## Project structure

```text
src/CareerCompanion.Core/
  Domain/          normalized records and fact classifications
  Providers/       manual and read-only FIFA 18 save providers
  Persistence/     SQLite schema, migrations, backup, repositories
  Simulation/      events, relevance, relationships, memory, compression
  LLM/             provider interface, OpenAI Responses provider, prompts
  Services/        application orchestration, media, demo seed, secrets
src/CareerCompanion.App/
  WPF UI and external prompt templates
tests/CareerCompanion.Tests/
tools/CareerCompanion.Simulator/
```

## Debugging and logs

Enable Developer/debug mode in Settings. The hidden Debug navigation then shows raw career state, event importance/classification, reaction thresholds, and ranked memories for the selected character. Operational diagnostics are persisted in `debug_log`; API keys and full authorization headers are never written.

## Limitations

- The save parser currently targets Player Career. It imports identity, club, season and date, position and number, reliable career overall, cumulative player statistics, teammates, manager, agent, FIFA news, and the latest match and preview records. Tables and trophy state still require manual entry.
- Only fixtures confirmed by a FIFA-generated preview are synchronized. The tested save contains no populated full-season `fixtures` table.
- The first detected result is marked for review because the save contains cumulative statistics without a prior Touchline baseline. Later saves can calculate stat deltas from the last imported snapshot.
- Starter status, penalties, derby classification, suspensions, and the career player's exact injury state are not reliably exposed by the currently validated tables. They remain review inputs or are described as unknown; Touchline does not infer a bench appearance from minutes played.
- FIFA 18 save structures are undocumented and can vary with platform, title update, mode, or mod. Unknown or malformed layouts fail closed and are not modified.
- Generated AI dialogue requires the user's OpenAI account, model access, and network connection. No live-key request is made by the test suite.
- News and social generation has a deterministic offline fallback; the current UI does not expose advanced pricing-table editing or a visual generation-job queue monitor.
- Profile editing deliberately exposes JSON for the detailed trait payloads; this favors accuracy/extensibility over a large slider form in V1.

## FIFA integration boundary

The implementation is a file-based `ICareerDataProvider` in `src/CareerCompanion.Core/Providers/Fifa18`. It normalizes parsed facts into the same `CareerSnapshot` contract used by the rest of the app, keeping event detection, relationships, memory, narratives, UI, and LLM orchestration independent of FIFA's binary representation.

It does not attach to `FIFA18.exe`, inject code, automate gameplay, write game data, or rely on process-memory offsets. See `THIRD_PARTY_NOTICES.md` for research attribution.

## Roadmap

- **Current:** automatic FIFA 18 synchronization, safe match import, international call-ups, world updates, incoming character reactions, interviews, media, and progression tracking
- **Next:** deeper narratives and verified award, trophy, and transfer-detail events
- **Later:** opponent scouting and richer competition context from additional validated save facts

The best next step is to use the current build through a real 10 to 20 match run, inspect reaction volume and chronology in Debug, and tune importance and character profiles from observed play.
