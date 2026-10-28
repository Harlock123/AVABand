# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers the game, its features, tilesets,
sound, modding and controls in detail; this file covers how the code is put together and the rules that keep it
working.

## What this is

A cross-platform clone of the roguelike Angband (4.2.5), in C# on .NET 10 and Avalonia 12. It is turn-based, with
a pure engine, JSON game data converted from Angband's `lib/gamedata`, OpenAL sound and SDL2 gamepads. One
self-contained executable per platform. Data derived from Angband carries Angband's licence terms (see README).

## Commands

```bash
dotnet build AVABand.sln -warnaserror             # .NET 10 SDK (no global.json; CI pins 10.0.401)
dotnet test AVABand.sln                           # all tests run headless: no window opens
dotnet run --project src/Angband.Avalonia         # play from source
tools/screenshots.sh                              # refresh screenshots/ (AVABAND_README_SHOTS, fixed seeds)
dotnet run -c Release --project tools/balance soak   # what CI's soak job runs; other modes: play, one, quests, ...
python3 tools/compare_with_angband.py <angband-4.2.5>/lib/gamedata --fail-on-diff   # data drift check
```

- Tests read a few optional env vars: `AVABAND_README_SHOTS`, `AVABAND_SCREENSHOTS`, `AVABAND_TRAILER_FRAMES`
  (picture output folders), `AVABAND_DUMP` and `RICH_FIXTURE_OUT` (fixture output).
- `tools/balance` reads `BOT_DEPTHS`, `BOT_TRACE`, `BOT_PLAIN`, `QUEST_LEVEL` and others; its modes are described
  at the top of `tools/balance/Program.cs`. `tools/perf` is a deep-dungeon speed check.

## Layout

- `src/Angband.Core/`: the pure engine, no UI.
  - `Game/GameSession*.cs` (many partial files) holds the game rules; `GameEvents.cs` the event bus records.
  - `Randomness/GameRandom.cs` (xoshiro256**), `Generation/` (levels), `World/`, `Monsters/`, `Items/`,
    `Magic/`, `Combat/`, `Sight/`, `Quests/`, `Records/` (feats, scores).
  - `Persistence/`: `SaveGame.cs` and `SaveFormat.cs` (gzipped JSON, ids not indexes), `Replay.cs`.
- `src/Angband.Data/`: `data/*.json` game content and `DataLoader` with mod layering; tileset manifests.
- `src/Angband.Audio/`: OpenAL Soft engine, Ogg/MP3/WAV decoders, sound packs, `SoundDirector` (events to sounds).
- `src/Angband.Input/`: input actions, rebindable bindings, `SdlGamepadProvider`.
- `src/Angband.Avalonia/`: MVVM front end (CommunityToolkit.Mvvm). `ViewModels/MainWindowViewModel.*.cs`,
  `Views/`, `Services/` (save, record and settings stores), `Assets/Help/*.md` (in-game help),
  `tilesets/`, `soundpacks/`, `art/` (shipped as loose files).
- `tests/Angband.Tests/`: xUnit tests of the core, with `Saves/` (old-version saves that must keep loading) and
  `Replays/` (bot games that must replay to the same end).
- `tests/Angband.Avalonia.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`, driving the real windows.
- `tools/`: Python importers and tileset builders, `balance/`, `perf/`, `old-saves/`, `screenshots.sh`,
  `trailer.sh`.
- `docs/`: balance notes and the 4.2.5 data-drift report.

## Rules that keep it working

- **Determinism.** All game randomness comes from `GameRandom`. A `GameSession` with the same seed and commands
  replays identically, and the replay fixtures and soak depend on it. Don't use `Random.Shared` in the engine.
- **Dependencies flow one way:** `Avalonia -> Audio/Input/Data -> Core`. Keep UI types out of `Angband.Core`.
- **Saves stay loadable.** Add new save fields as nullable or defaulted so older saves load unchanged;
  `tests/Angband.Tests/Saves` checks this.
- **Data matches 4.2.5.** CI fails if converted data drifts from Angband 4.2.5. Use the importers in `tools/`
  rather than hand-editing converted entries; AVABand's own content lives in its own `ava_*` files.
- **Audio.** `Directory.Build.targets` swaps in the official Windows `soft_oal.dll` from
  `src/Angband.Audio/native/`. In a virtual machine `OpenAlConfig` writes a larger mixing period to
  `ALSOFT_CONF`.
- **Player files.** Saves, scores, settings, bindings and mods live under `ApplicationData/AVABand`. Tests pass
  their own temporary folders to `SaveStore`, `RecordStore` and the like; keep it that way.
- **UI timers are held by tests.** Clocks that move things on by themselves can be held or set by a test (see the
  README's Determinism section). New timers need the same hook.
- **Keep the Debug menu** in `Views/MainWindow.axaml`.
- **Never commit the `.claude/` folder.** It is not in `.gitignore`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass and the build must have no warnings.
- Every user-facing change gets a dated line in `src/Angband.Avalonia/Assets/Help/11-whats-new.md`, newest first.
- If it changes how bot games play out, re-record the replays with
  `dotnet run -c Release --project tools/balance record-replays`.
- If it shows on screen, run `tools/screenshots.sh` and look at the pictures. Update `README.md` and the in-game
  help to match.

## CI and releases

`.github/workflows/build.yml` runs on every push and pull request:

- `test`: builds with `-warnaserror` and runs all tests.
- `soak`: long bot games, recorded and replayed; failing replays are kept as an artifact.
- `drift`: compares the data with Angband 4.2.5 (fetched and checksum-checked).
- `publish`: self-contained single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm,
  linux-musl-x64/arm64 and osx-x64/arm64.

Pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
