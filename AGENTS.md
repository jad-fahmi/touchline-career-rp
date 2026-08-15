# Touchline project instructions

## Purpose

Touchline is a Windows WPF companion for FIFA 18 Player Career Mode. FIFA is the source of truth for save facts such as matches, scores, players, transfers, selection, dates, and statistics. Touchline stores its own career world in SQLite and adds relationships, memories, events, media, interviews, and deterministic or optional AI dialogue around those facts.

Do not make the app write to or modify a FIFA save. The FIFA provider must remain read-only. Unknown provider data must stay unknown or require review; never invent an opponent, score, injury, starter status, transfer, or table position.

## Architecture

- `src/CareerCompanion.Core/Domain`: normalized records and fact classifications.
- `src/CareerCompanion.Core/Providers/Fifa18`: read-only save parsing and normalization.
- `src/CareerCompanion.Core/Persistence`: SQLite schema, migrations, backup, and repositories.
- `src/CareerCompanion.Core/Simulation`: events, relationships, memory, psychology, relevance, and narratives.
- `src/CareerCompanion.Core/Services`: orchestration, media, imports, dialogue, world updates, and secrets.
- `src/CareerCompanion.Core/LLM`: provider contracts, prompts, response validation, and OpenAI-compatible clients.
- `src/CareerCompanion.App`: WPF views, bindings, navigation, settings, and user actions.
- `tests/CareerCompanion.Tests`: unit and integration tests.
- `tools/CareerCompanion.Simulator`: deterministic simulation and FIFA probe tooling.

Keep provider parsing separate from simulation and UI. New FIFA fields should be normalized into provider models first, then imported through an idempotent service. Do not put binary-save assumptions in the WPF layer.

## FIFA import rules

- Match imports must have a stable provider event key and must be idempotent across rescans.
- The save never records who a match was against. Verified on real saves: only five tables carry dates (transfer offers, loan offers, the rating log, a contract table, and news), there is no schedule or result table, and a fixture's opponent exists solely as prose inside a `career_news` article. `career_news` is a rolling buffer of roughly twenty articles, so that evidence expires within a few matchdays. Scan often, keep the article cache, and treat the stored `fixtures` row as the durable copy of an opponent. Use `--find-fifa18-fixtures`, `--diff-fifa18-rows`, and `--diff-fifa18-team` in the simulator to re-check a save; note that the inspector reads raw bits, so a date field only looks like a date after adding back its `RangeLow` base (usually 20080101).
- When the save cannot name an opponent and FIFA is running, `Fifa18LiveMatchReader` recovers it from the live process. It searches by shape (club id, opponent id, `yyyymmdd` date, then two goal tallies) rather than by pointer path, so it survives ASLR and needs no offsets. It is strictly read-only: `PROCESS_VM_READ` only, no writes to the game or the save, and every failure returns null so save-only behaviour is unchanged. Verified against a live career: `47 v 896 on 2017-09-13, 3-1`.
- The table DB is not the whole save. The file is an `FBCHUNKS` container: three DB sections hold the 75 tables (55% of the file), and a 4.8 MB serialized region after them holds career state the tables do not, including plain `yyyymmdd` dates. Competition state such as standings must live there, since a row-level diff across a played match changes only squad links, attributes, tactics, lineups, ratings, offers, form, and news. That region is undecoded; do not claim a fact is absent from the save on the strength of a table scan alone.
- Preserve chronology. Cumulative FIFA counters require a verified baseline and exactly one new appearance before automatic stat deltas are trusted.
- Resolve named opponents from FIFA match-review or preview context whenever available, including cup competitions such as Supercopa. If the source does not expose a fact, keep it unknown and explain it in diagnostics, not in character dialogue.
- Do not describe parser state as football dialogue. A manager should never say that data “has not come through” or mention FIFA fields. If a result is too incomplete for a grounded reaction, skip the automatic reaction or route it to review.
- International appearances are separate from club statistics and need explicit representing-team context.
- Keep FIFA dates as in-career dates. Do not use the computer's current UTC date for career-world facts unless no career date exists.
- Fixture availability must be conservative. Only claim selected, benched, injured, suspended, or not selected when FIFA evidence supports it.

## Dialogue and AI policy

- Automatic teammate, manager, agent, and pre-match messages are first messages. They must be generated offline and must not spend an LLM request.
- Direct player messages use the offline library only for a bare greeting ("hey", "morning mate"). A greeting carrying a second line, a question, or any other message goes to the model, whether it is a statement or a question.
- A model reply is only abandoned when nothing usable is left. JSON wrappers, code fences, reasoning blocks, speaker labels, and truncated wrappers are repaired, and a reply that leaks the prompt or steps outside the football world is regenerated with a correction before the offline library is used.
- Nothing a character or journalist says may mention FIFA, EA, a save, career mode, data, imports, or software, and no game or system may be described as deciding selection, fitness, or transfers. Event summaries, notifications, and availability text are read back by characters, so they follow the same rule.
- A model response is a response to the player's message. Never use the model to invent the first incoming message.
- OpenAI-compatible providers must not expose system prompts, reasoning text, JSON fragments, or prompt echoes. Normal plain-text dialogue can be accepted when a provider ignores the requested JSON wrapper; salvage a valid `dialogue` field from a broken wrapper where safe.
- Keep relationship and memory deltas bounded and grounded. AI language must not change factual match data.
- Characters write to the player in English by default. A nationality in the prompt is enough to make a model answer in that language, so every system prompt states the language and a reply that comes back in another one is regenerated with a correction. "Let characters speak their own language" in Settings turns the realistic behaviour on, and only then is a foreign reply accepted.
- Offline dialogue should vary by character type, personality, communication style, age gap, relationship, scene, and event. Avoid sending every teammate the same post-match message.
- Two characters must never send the same line. Multi-speaker events share one set of used lines, each speaker seeds from its own identity, and phrases used recently by anyone else in the career are off limits.
- Keep post-match reaction volume intentional. Prefer a small number of distinct voices over a broadcast to the whole squad. Wellbeing support should not be duplicated by generic match reactions.

## UI and copy rules

- Match the existing FIFA 18-inspired palette. Text never sits white on a bright fill. Anything painted with `Interactive` (lime), `Accent`, `Warm` or `Win` takes the `InkOnBright` brush, which is near-black. Use that brush rather than a literal hex.
- The app-wide implicit `TextBlock` style paints near-white and beats inherited `TextElement.Foreground`, so a bright surface that only sets `TextElement.Foreground` still renders white text. Templates that host arbitrary content on a bright fill re-point the implicit style through a `ContentPresenter.Resources` entry (see `ButtonShell` and `ComboBoxItem`).
- Check normal, hover, selected, disabled, and keyboard-focused states. Selected dropdown rows must remain readable.
- Keep sidebar content usable without requiring horizontal scrolling.
- Message views should show sender names, timestamps/context, and clear left/right speaker bubbles where applicable.
- Avoid em dashes in user-facing copy. Use commas, periods, or parentheses.
- Do not expose internal exception text, parser diagnostics, provider names, or raw JSON as character dialogue.

## Persistence and safety

- Database schema changes must be additive, versioned through `schema_migrations`, and covered by tests.
- Use existing dedupe keys and provider identifiers before adding side effects. Automatic world effects should be safe to retry.
- Do not delete or rewrite user career data during normal scans. Match deletion/correction must remain an explicit user action.
- Never log API keys, authorization headers, raw secrets, or full sensitive prompts.
- Preserve unrelated working-tree changes. Use `apply_patch` for source edits.

## Verification

From the repository root:

```powershell
dotnet restore RPSystem.slnx
dotnet test RPSystem.slnx -c Release
dotnet build RPSystem.slnx -c Release
dotnet run --project tools/CareerCompanion.Simulator -- 100
```

For a self-contained Windows build:

```powershell
dotnet publish src/CareerCompanion.App/CareerCompanion.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64 --no-restore
```

If the app is running, its published DLLs may be locked. Publish to `artifacts/win-x64-next` for a safe test build, or ask the user to close the app before replacing the normal output.

When changing FIFA parsing, add a focused parser test and, where possible, a real-save probe that only reads the source. When changing dialogue routing, test both the no-model path and the model path, including malformed compatible-provider output. When changing UI bindings, build the WPF app and verify selected-state colors and text contrast.

