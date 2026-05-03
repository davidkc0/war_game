# PLAN.md — Procedural RTS Game (Working Title: WarGame)

> **Brief for AI coding agents (Cursor / Antigravity / Claude Code).** This document is the source of truth for the project. Read it fully before generating code. When in doubt, prefer the choices documented here over your own defaults. Flag deviations explicitly to the human operator.

---

## 0.0 Current Status & Resume Guide *(updated 2026-05-03)*

### Where we are

**Phase 0 (Project Skeleton): ✅ COMPLETE.** Mac+Windows determinism CI is green; binary export job still failing (non-blocker).

**Phase 1 (Tactical Core): ✅ COMPLETE.** All 10 acceptance criteria met.

**Phase 1.5 (Tactical Depth): ✅ COMPLETE.** Terrain combat modifiers, attacker penalty, unit maintenance, fortifications, and Advance Wars-style city capture all implemented and tested.

**Phase 2 (Procedural Map Generation): ✅ COMPLETE.** Deterministic integer-only noise → rank-based land terrain assignment → irregular basin/lake carving → mountain-peak spines → river validation/shaping → same-territory road networks → connectivity guarantee → 4-axis BalanceValidator (path symmetry, terrain parity, choke points, connectivity) → reject-and-retry loop with deterministic seed perturbation.

**Phase 3a / 3a.5 (Supply Lines + Road/Bridge Engineering): ✅ COMPLETE.** Supply now computes after power projection; friendly territory carries normal supply, roads/bridges carry road-assisted supply, enemy units interdict road supply, cut-off units pay higher maintenance and cannot heal, road-supplied units pay reduced maintenance, and selected units can build previewed roads/bridges with `B`.

**Phase 3b (Fog of War): ✅ COMPLETE.** Per-player tile visibility now tracks Hidden / Explored / Visible state, remembers last-seen terrain/ownership, filters rendering and hot-seat input by active player, hides enemy units outside vision, and prevents movement/build commands into black fog.

**Next up: Phase 3a polish pass, then Phase 3d (Doctrines).**

### Repo layout (live at `~/Projects/war_game/`)

```
war_game/
  PLAN.md                    ← this file
  WarGame.sln                ← solution (3 projects)
  WarGame.csproj             ← Godot game (Godot.NET.Sdk/4.6.2)
  project.godot              ← 1600x900 default, resizable, no stretch
  Sim/                       ← deterministic C# library, NO Godot deps
    WarGame.Sim.csproj
    Math/      FP.cs, FPVec2.cs, SimRng.cs
    State/     GameState.cs, MapState.cs, TileType.cs, SupplyStatus.cs,
               VisibilityState.cs,
               Unit.cs, City.cs, Player.cs, PlayerId.cs,
               UnitType.cs, UnitStats.cs, FortOrder.cs, RoadOrder.cs
    Commands/  Command.cs (MoveUnit, BuildUnit, BuildFort,
               RazeFort, BuildRoad, CancelRoad, NoOp)
    Systems/   Movement, Combat, Production, Healing,
               CityCapture, FortConstruction, Maintenance,
               PowerProjection, SupplyLines, RoadConstruction,
               FogOfWar,
               WinConditions, Encirclement (legacy instant-kill disabled),
               Pathfinding (A*)
    Generation/ IntegerNoise.cs, MapGenerator.cs, BalanceValidator.cs
    GameSim.cs                ← per-tick orchestrator
    StateSerializer.cs        ← canonical hash/replay serializer
  Sim.Tests/                 ← xUnit, runs via `dotnet test`, 172 tests
    WarGame.Sim.Tests.csproj
  src/                       ← Godot scene scripts
    Main.cs, Main.tscn        ← entry; switches to Game.tscn
    Game.cs, Game.tscn        ← game scene; tick loop + HUD + input wiring
    Render/   MapRenderer.cs, Theme.cs, TestMap.cs (now unused — Game.cs
              calls MapGenerator instead; TestMap kept for ad-hoc debugging)
    UI/       InputController.cs
    Net/, AI/, Audio/, Tools/  (empty)
  test/                      ← Chickensoft GoDotTest scaffold (unused)
  .github/workflows/build.yml ← determinism matrix + (failing) godot-export job
```

### Schema state

`GameState.CurrentVersion = 10`. Schema history:
- v1–v5: Phase 0–1 baseline
- v7: City.CaptureHp (Advance Wars-style capture)
- v8: PendingForts, Fort tile type, terrain defense, maintenance
- v9: TileSupplyOwner, TileRoadSupplyOwner, UnitSupplyStatus, PendingRoads, Bridge tile type
- v10: TileVisibility, LastSeenTileType, LastSeenTileOwner

Determinism golden hash pinned in `Sim.Tests/DeterminismTests.cs`. Bump version + repin whenever the byte layout changes.

### Sim tick order (fixed, in GameSim.Step)

```
Movement → Combat → CityCapture → FortConstruction →
RoadConstruction → Production → PowerProjection →
SupplyLines → Healing → Maintenance → FogOfWar → WinConditions
```

### What's actually playable today

A hot-seat 1v1 RTS on a procedurally generated 60×60 map (`Sim/Generation/MapGenerator.cs`, runtime entry: `Game.cs` calls `MapGenerator.Generate(seed)`). Two players share keyboard/mouse; **Tab** swaps active seat. Real systems running:

- **Tile grid** — Plains, Forest, Mountain, MountainPeak, Water, River, Road, Bridge, City, Capital, Fort
- **Two unit types** (Light, Heavy) with PLAN.md §3 stat baseline
- **Deterministic A* pathfinding** with terrain costs
- **Movement** — sub-tile interpolation, friendly pass-through, enemy blocking
- **Combat** — adjacent engagement, two-pass simultaneous damage, **three stacking modifiers**:
  - Concentration-of-force (+15% per additional ally attacking same target)
  - Terrain defense (forest 30%, mountain 50%, city 40%, fort 55% damage reduction)
  - Moving-attacker penalty (15% less damage while in motion)
- **City capture** — Advance Wars-style HP-based capture (cities 100 HP, capitals 200 HP; on-tile units deal 3 dmg/tick, adjacent 1 dmg/tick)
- **Fortifications** — built via `F` key on Plains in owned territory; 50 ECO, 10 sec build; 55% damage reduction, base-25/radius-6 projection, +2 supply; CaptureHp 80; razeable via `R` key; max 3 per player; cancelled if territory lost during construction
- **Supply lines** — owned cities/capitals/forts seed supply; friendly territory carries normal supply; roads/bridges carry road-assisted supply outside friendly territory; enemy units on roads/bridges interdict the road bonus/path
- **Unit maintenance** — 0.02 ECO/tick per unsheltered unit; road-supplied units pay 50%; cut-off units pay 150%; units on friendly cities or forts exempt; no ECO = starvation damage
- **City + capital production** (1 ECO/sec, 3 ECO/sec; build orders for Light/Heavy; cancel order)
- **Healing** on friendly-controlled owned cities/forts only (0.5 HP/tick at 0.05 ECO/tick cost); road supply never enables healing
- **Power projection** (additive linear-falloff; fort-aware: fort base=25, radius=6)
- **Win conditions** (capture enemy capital OR hold ≥80% of cities for 30 consecutive seconds)
- **Procedural maps** — `MapGenerator.Generate(seed)`: layered integer-noise elevation → rank-based land terrain assignment (plains/forest/foothill/mountain) → irregular major lowland basins + small inland lakes → mountain-water buffer enforcement → component-based tiny-water cleanup → saddle-point pass cutting through ridges → mountain-peak spine promotion along sufficiently large range interiors → one narrow river on 60×60 maps, starting in mountain country, meandering through lowlands, and flowing toward larger water bodies → city placement that keeps owned cities clustered near capitals → same-territory road networks only (no free road between enemy capitals) → BFS-based connectivity guarantee with `PunchPath` last resort
- **Map balance scoring** — 4-axis `BalanceValidator` (path symmetry / terrain parity / choke points / connectivity), threshold ≥250/400, reject-and-retry up to 10 attempts with deterministic xorshift seed perturbation
- **Road/bridge engineering** — selected unit + `B` enters road-build mode; hover previews the deterministic engineering path; click commits `BuildRoadCommand`; land segments cost 2 ECO/30 ticks; bridges over rivers or 1-tile-wide waterways cost 8 ECO/90 ticks; broad water, mountain peaks, and skinny land causeways between water are blocked
- **Fog of war** — each hot-seat player has separate Hidden / Explored / Visible tile state; friendly light units reveal radius 5, heavy units radius 4, and owned cities/capitals/forts radius 8; explored tiles show dim last-seen terrain/ownership/structures but no units; hidden tiles show no information
- **Visual layer**: Teal/Coral palette, Inter fallback, terrain tones, drop shadows, borders, HP bars, star icons, stack fanning, hostile-territory ring, supply status rings, victory banner, fort diamonds (amber), road/bridge previews, build progress bars, capture HP bars

Performance: **1.6 ms/tick avg** with 200 units on a 60×60 map. ~18× headroom under the 30 ms budget for 30 Hz sim.

### Known design deviations from PLAN.md (intentional)

- **Encirclement system**: built in Phase 1 but **still disabled** in `GameSim.Step`. Phase 3a replaced the instant-kill behavior with supply status, no-heal cutoffs, and maintenance pressure. `Encirclement.cs` remains as historical reference only unless rewritten around supply notifications.
- **Fortifications pulled forward from Phase 3c to Phase 1.5**: built early based on user request. PLAN.md originally slated forts for Phase 3c with a 30-sec build time; we used 10 sec and added capture/raze mechanics.
- **No-stacking** (one unit per tile): added based on playtest feedback.
- **Healing on friendly cities**: not in original PLAN.md; added based on playtest.
- **Tile size 32 px / window 1600×900**: chosen to fit 30×20 grid with HUD. Phase 3 introduces camera pan/zoom.
- **Unit stats**: Light 60 HP / 8 dps / 4 tiles/sec / supply 1 / 10 ECO; Heavy 150 HP / 20 dps / 1.5 tiles/sec / supply 2 / 30 ECO.

### Controls reference

| Key/Action | Effect |
|---|---|
| Left-click | Select unit / Open city production menu |
| Drag-select | Box-select units |
| Right-click | Issue move order |
| Q | Build Light (when city menu open) |
| W | Build Heavy (when city menu open) |
| F | Build fort at tile under cursor |
| R | Raze fort at tile under cursor |
| B | Enter road/bridge build mode for selected unit; hover previews path, left-click commits |
| Tab | Switch active player (hot-seat) |
| Esc | Clear selection / Close menu |
| F11 | Toggle fullscreen |

### Open issues / non-blockers carried forward

1. **`godot-export` CI job fails** on Linux runner. Determinism CI on Mac+Windows is green. Non-blocker.
2. **No Inter font bundled**. Theme.cs uses SystemFont fallback chain. Bundle `.ttf` in Phase 6.
3. **Editorconfig analyzer warnings** — Chickensoft template default; harmless.
4. **Stat tuning ongoing**. The stats above are starting points.

### How to resume work cold

```bash
cd ~/Projects/war_game
export PATH="$HOME/.dotnet:$PATH"   # .NET 8 SDK in user profile
dotnet test Sim.Tests/              # confirm 172/172 still green
dotnet build WarGame.csproj         # confirm Godot project builds
open project.godot                  # or run via Godot.app, F5 to play
```

To add a new sim system:
1. Add file under `Sim/Systems/`
2. Wire into `GameSim.Step` in fixed order (Movement → Combat → CityCapture → FortConstruction → RoadConstruction → Production → PowerProjection → SupplyLines → Healing → Maintenance → FogOfWar → WinConditions → [your system if it belongs after visibility])
3. If you add to `GameState`: bump `GameState.CurrentVersion`, update `StateSerializer.Write`, update the schema versions comment block in `GameState.cs`, re-pin the golden hash in `Sim.Tests/DeterminismTests.cs`
4. Write xUnit tests in `Sim.Tests/`

### Next session priorities (in order)

1. **Phase 3a polish pass**: playtest supply/fog readability together and tune maintenance multipliers / road build costs if cutoffs feel too weak or too punishing.
2. **Phase 3d — Doctrines**: pre-match selection screen with Maneuver / Attrition / Combined Arms. Each modifies a few stats and unlocks one ability per PLAN.md §3.4.
3. **Phase 3e — Larger Maps + Camera**: bump default to 80×80 (with 120×120 option). Camera pan/zoom already exists; add minimap and performance pass.
4. **Fix `godot-export` CI** — drag a working version pin into `.github/workflows/build.yml`. Determinism CI is solid; binary builds just need a green action.

### Phase 2 design notes (for posterity)

- **Why integer noise, not simplex**: float ops aren't bit-identical across CPU/JIT pairs. `IntegerNoise.cs` uses a Fisher-Yates-shuffled 256-entry permutation table seeded from `SimRng`, with bilinear interpolation via integer-only Hermite smoothstep. Deterministic across Mac+Windows (the determinism CI verifies this).
- **Why water is carved after land terrain**: early versions assigned the lowest terrain ranks as water. That produced statistical water, not geography: oceans became ruler-straight top/right bands and rivers became short drainage cuts. `MapGenerator` now assigns land by rank, then carves irregular lowland basins and smaller inland lakes. Full map-edge oceans are deferred until a better coast/continent model exists.
- **Peaks are promoted as spines, not dots**: `MapGenerator` promotes medial-axis tiles of sufficiently large mountain components, so peaks read as ridgelines along the center of a range rather than isolated edge artifacts.
- **Roads are internal logistics**: generated roads only connect cities owned by the same player. There is intentionally no paved road between both territories; later unit-built roads/bridges make cross-front engineering a player action.
- **Rivers are distinct from lakes/coasts**: `TileType.River` is passable but slow and bad for defense. Rivers must be one tile wide, long enough to read as rivers, visibly touch mountain country at the source, avoid straight canal runs, and reach larger water bodies. Generated roads do not overwrite rivers; player-built engineering now converts river segments to `TileType.Bridge`.
- **Startup territory is precomputed**: `Game._Ready()` calls `PowerProjection.Tick(ref _state)` after procgen initialization so the first rendered frame starts with authoritative contiguous ownership instead of an empty/one-tick-stale `TileOwner` buffer.
- **`IntegerNoise` takes `SimRng` by ref** in its constructor. An earlier version took it by value, which (because `SimRng` is a struct) meant two noise instances built from the same outer rng got identical permutation tables — elevation and moisture noise were perfectly correlated rather than independent channels. Fixed during Phase 2 cleanup.
- **Validator's path-symmetry axis** uses BFS distance, not Euclidean. The score drops smoothly from 100 (≤20% imbalance) to 0 (≥80% imbalance). Most generated maps land in the 70–95 range.
- **Choke-point detection** samples three vertical seams (¼, ½, ¾ width) and counts narrow passable runs (≤2 tiles wide). Target is 2–6 chokes total across all seams. Maps with 0 chokes feel like open fields; >6 feel like mazes.

---

## 0. Project Summary

A minimalist 2D real-time strategy game in the spirit of *War of Dots*, but distinguished by:

1. **Procedurally generated maps** with balance validation, replacing hand-authored maps.
2. **Strategic-layer depth** — supply lines, fortifications, fog of war, simple tech/doctrines, and larger operational maps — turning what is essentially a real-time tactics game into a real-time strategy game.
3. **Polished minimalist art direction** — visually distinct from War of Dots' stark prototype look. Reference points: *Abstractanks* and *Square Wars*. The game should look like a well-designed product, not a programmer's mockup. Geometric primitives, intentional color palettes, subtle depth via gradients and soft shadows, no skeuomorphism, no MS-Paint-tier flatness.
4. **Strategic depth grounded in real strategic theory** — see Section 1.5 for how Freedman's *Strategy: A History* and Greene's *33 Strategies of War* inform specific mechanics.

**Development platform:** macOS (Apple Silicon or Intel). All tools must run natively on Mac.
**Target platforms (release):** Steam on **Windows AND macOS at launch** (both produced from the Mac dev machine via CI). Linux as a stretch goal.
**Multiplayer scope (v1):** 1v1 PvP only. Deterministic lockstep netcode.
**Single-player:** vs AI, multiple difficulty levels. AI must be good enough to teach mechanics and challenge intermediate players.
**Business model:** Free-to-play with **in-app purchases** (cosmetics, doctrine packs, map theme packs — all non-pay-to-win).
**Optimization target:** *Shortest credible path to a Steam release that doesn't embarrass us.*

---

## 1. Tech Stack (Non-Negotiable Unless Flagged)

| Layer | Choice | Rationale |
|---|---|---|
| Engine | **Godot 4.6+ with C#** | Native Apple Silicon + Intel binaries; lightweight 2D; free; good Steam integration |
| Language | C# (.NET 8) | Required for fixed-point determinism libraries; first-class on macOS via .NET SDK |
| Math | **Custom fixed-point (Q32.32)** for all simulation state | Float determinism across CPUs is unreliable; fixed-point is the standard RTS solution |
| Networking | **Steamworks.NET** (pure C# bindings) | Cross-platform from Mac dev machine; no native plugin compilation per OS |
| Project template | **Chickensoft GameTemplate** | Pre-configured Godot+C#+Steamworks.NET cross-platform setup for macOS and Windows |
| IDE | **JetBrains Rider** (preferred) or **VS Code** | Both run natively on Apple Silicon; Rider has best Godot C# integration |
| Lobby/Matchmaking | Steam Lobbies API | Free, robust, no backend needed |
| Persistence | Steam Cloud + local JSON | No DB required for v1 |
| CI/CD | **GitHub Actions** producing Mac + Windows builds | Critical: cross-compiling signed Windows builds from Mac is fragile; let CI do it |
| Distribution (pre-Steam) | itch.io | Free playtester distribution before Steam approval |

### Hard rules for AI coding agents

- **NEVER use floats in the simulation layer.** Floats are fine for rendering and UI only. Any code in `/sim` uses fixed-point types from `Sim/Math/FP.cs`.
- **NEVER call `DateTime.Now`, `Random` (un-seeded), or any non-deterministic API inside simulation code.** All randomness must come from the seeded `SimRng` instance.
- **NEVER store references to `Node` objects inside simulation state.** Sim state must be serializable and engine-agnostic.
- **All simulation steps must be pure functions of (state, inputs, tick).** This is the foundation of lockstep netcode and replay.
- Renderer reads sim state but never writes to it.

If you (the AI agent) think you need to break one of these rules, **stop and ask the human first.**

### macOS development environment setup (do this once, before Phase 0)

```bash
# 1. Install .NET 8 SDK (Apple Silicon or Intel)
brew install --cask dotnet-sdk
# Verify: dotnet --version  (should print 8.x or higher)

# 2. Install Godot 4.6+ .NET version (universal binary)
# Download from https://godotengine.org/download/macos/
# Use the ".NET" build (with C# support), NOT the standard one
# Install to /Applications/Godot.app

# 3. Install JetBrains Rider (recommended) or VS Code
brew install --cask rider     # or: brew install --cask visual-studio-code

# 4. Install GitHub CLI for CI setup later
brew install gh
```

### Mac-specific gotchas the AI agent must know

- **Building Windows .exe from a Mac is fragile.** Don't try to cross-compile signed Windows builds locally. Push to GitHub; let GitHub Actions build Windows + Mac artifacts. This is set up in Phase 0.
- **macOS notarization** is required for non-Steam distribution (e.g., itch.io playtesting). Steam-distributed Mac builds get re-signed by Steam, but pre-Steam testing on itch.io needs an Apple Developer account ($99/year). Plan for this before external playtesting in Phase 5.
- **Steamworks.NET on macOS** requires a `dllmap` entry in `app.config` so it finds the Steam libraries; the Chickensoft template handles this. Don't remove it.
- **Apple Silicon vs Intel:** Godot exports a Universal 2 binary that runs on both. No special handling needed.
- **Path separators and case sensitivity:** macOS default volumes are case-insensitive but case-preserving. Don't rely on case differences in filenames; this will break on Linux CI runners.

---

## 1.5. Design Philosophy & Strategic Theory References

This game's strategic depth must come from real ideas about strategy, not from feature bloat. Two books inform specific design decisions and should be treated as design references throughout development:

### Lawrence Freedman, *Strategy: A History* (2013)

A 750-page intellectual history of strategic thought spanning ancient warfare, modern military doctrine, political revolution, and business. Key takeaways that shape this game's design:

- **Strategy is fluid, not a fixed plan.** Freedman repeatedly argues that real strategy is "governed by the starting point, not the end point" — strategists feel their way through a series of unanticipated states, reappraising as they go. **Game implication:** the game must reward adaptation over plan-locking. Procgen maps support this directly (no memorized openings); fog of war and asymmetric information force constant reappraisal.
- **Strategy is "getting more out of a situation than the starting balance of power would suggest."** (Joseph Nye's distillation.) **Game implication:** map generation must occasionally produce asymmetric starting positions where the "weaker" position is winnable through strategic play. A perfectly balanced game becomes a game about execution speed; a slightly-asymmetric game becomes a game about strategy.
- **The distinction between strategy, operations, and tactics.** Freedman insists on the layered nature of war: tactics win battles, operations connect battles into campaigns, strategy shapes the war itself. **Game implication:** the AI architecture (Phase 4) explicitly mirrors this — a strategic planner sets goals, an operational layer allocates forces, a tactical layer executes. Players experience the same layering through doctrines (strategic), supply lines (operational), and unit micro (tactical).
- **Indirect approach over direct confrontation** (Liddell Hart, B.H., heavily cited by Freedman). Cutting supply, threatening rear areas, and forcing the opponent to react to multiple threats often beats a stronger frontal force. **Game implication:** supply lines are not a flavor system; they are the primary vehicle for indirect strategy. Cutting a supply line should feel as decisive as winning a battle.

### Robert Greene, *The 33 Strategies of War* (2006)

A more accessible (and more controversial — see Section 1.5 caveats below) catalog of 33 named strategies with historical examples. Useful as a vocabulary of strategic *patterns* that should be expressible in our gameplay. Specific strategies that map to game systems:

| Greene strategy | In-game expression |
|---|---|
| **Grand strategy** ("lose battles, but win the war") | Doctrine system, victory by city-percentage holding (not just capital capture), supply-cut campaigns |
| **Indirect approach** | Supply-line cutting; encirclement is rewarded over headlong assault |
| **The blitzkrieg** (overwhelming speed and force) | Maneuver doctrine bonuses; light units' speed advantage on roads |
| **Defensive warfare** ("defenders usually win the war") | Fortifications give *real* defensive multipliers; Attrition doctrine |
| **Maneuver warfare** | Combined Arms doctrine; encirclement mechanics |
| **Counterattack** (let them strike first, then exploit) | Fog of war hides reserves; defenders see attacker commitment before responding |
| **Strike at weakness, not strength** | Power projection makes weak points visible as thin border zones |
| **Loss of central position** (forcing enemy to fight on multiple fronts) | Larger maps + multiple cities create multi-front pressure naturally |
| **Deception and the unexpected** | Fog of war is the entire foundation of this; possible future: feint mechanics |
| **Conserve your forces** (Pyrrhic victories) | Unit production is supply-capped; reckless attacks bleed your supply ceiling |

**Caveats on Greene** (the AI agent should know these): Greene is a self-help author, not a military historian. His book has been criticized — Admiral James G. Stavridis said the book had good breadth, but it lacked depth, and leadership theorist John Adair said Greene "shows a poor grasp of the subject". For our purposes, Greene is useful as a *catalog of recognizable strategic archetypes that players will intuitively understand*, not as a serious strategic doctrine. Where Greene and Freedman conflict, defer to Freedman.

### Design implication: the "Three Doctrines" map directly to strategic archetypes

The three doctrines in Section 3 are not arbitrary unit-stat tweaks; each represents a coherent strategic philosophy:

- **Maneuver Doctrine** = Greene's blitzkrieg + Liddell Hart's indirect approach. Win by speed, mobility, and refusing to let the enemy set the pace.
- **Attrition Doctrine** = Defensive warfare + grand strategy. Win by surviving longer, denying the enemy a decisive engagement, and building unassailable positions.
- **Combined Arms Doctrine** = Counterattack + strike-at-weakness. Win by absorbing the enemy's commitment and then exploiting the seam.

These names and mechanics should feel coherent to anyone who has read either book. If a doctrine ever feels like "just stat changes," the design has failed.

### Design implication: the game must reward *reading the opponent*

Freedman's recurring theme is that strategy is fundamentally about anticipating another mind. Greene devotes a strategy ("Know your enemy") to it. **Game implication:** fog of war is non-negotiable, and information-gathering (scouts, observation posts) should have meaningful cost-benefit decisions. A v2 candidate is a "feint" mechanic — units that *appear* hostile in fog but cost less. We're not building this for v1, but the architecture should not preclude it.

### What this section is NOT

- It is **not** a license for feature creep. Every system listed in Section 3 already exists in the plan; this section explains *why* those systems are there and how they should *feel*. If an AI agent reads this and thinks "we should add diplomacy because Freedman talks about coalitions," the answer is no.
- It is **not** a tutorial. We're not teaching the player Clausewitz. The strategic depth should be *legible through play*, not lectured.

---

## 1.6. Visual Design Direction

The game must look like a polished, intentional product — closer to a well-designed mobile/indie game than to an engineer's prototype. War of Dots' aesthetic is a deliberate **anti-reference**: we are minimalist *and* polished, not minimalist *because* unpolished.

### Reference points (study these before any UI/render work)

- **Abstractanks** — geometric tank shapes, deliberate two-color factions, soft drop shadows, terrain rendered as subtly textured zones rather than tiles. Reads instantly at a glance.
- **Square Wars** — clean grid presentation, restrained palette, clear unit silhouettes, polished UI typography.
- **Mini Metro** (broader inspiration) — the gold standard for "minimalist but polished" — every visual element earns its place, color encodes meaning, motion is deliberate.

### Concrete visual rules

1. **Color palette** — pick a deliberate palette in Phase 1 and *commit*. Two faction colors that are colorblind-safe (avoid pure red/green pairing — use blue/orange, or teal/coral). Terrain colors are desaturated to keep faction colors readable as the visual focus. Document the palette as CSS-style hex values in `Render/Theme.cs`.
2. **Units are geometric primitives, not icons** — light unit = small filled circle, heavy unit = filled hexagon or rounded square. Differentiation is *shape and size*, not detail.
3. **Subtle depth** — soft drop shadows under units (1-2px blur, low opacity), gentle gradients on terrain zones (10-15% lightness variation, not garish). No flat-shading-as-aesthetic. No skeuomorphism either.
4. **Typography** — one good sans-serif, used at 2-3 sizes total. Inter, Manrope, or similar geometric sans. NOT Arial. NOT Comic Sans. NOT Godot's default theme font.
5. **Motion is meaningful** — units move with subtle easing, not linear lerps. Border shifts are smoothly animated. Combat doesn't flash garishly; impact is communicated by a subtle radial pulse and a single particle burst.
6. **No gradients on UI panels** — UI is flat, with restrained use of soft shadows for layering. Buttons have a single accent color from the palette.
7. **No emoji or stock icons in UI** — use a single icon set (Lucide, Phosphor, or similar) rendered at consistent stroke width.
8. **Empty space is a feature** — resist the urge to fill the screen with HUD elements. WoD-tier minimalism in *information density*, Abstractanks-tier polish in *what's actually shown*.

### Hard rules for the AI agent on visual work

- **Do not** use Godot's default theme. Replace it in Phase 1.
- **Do not** ship with placeholder colors past Phase 1. The committed palette should be in place from the moment the first dot renders.
- **Do not** add visual effects that aren't justified by gameplay legibility. A particle effect that tells the player something they need to know is good; a particle effect for "juice" alone is suspect.
- **Do** ask the human before introducing new visual elements (icons, effects, fonts). Visual coherence is easy to break and hard to repair.
- When in doubt, **remove** rather than add. The reference games are restrained.

### Art assets the human will commission later

- Music (original commission, post-Phase 5)
- SFX (original commission OR curated royalty-free, post-Phase 5)
- Steam capsule images and trailer (Phase 6)
- A logotype / wordmark for the game (once name is decided)

For Phases 0-5, all visuals are produced procedurally in code or via free icon sets. No external art dependencies until commissioning starts.

---

## 2. Architecture (Read This Before Writing Any Code)

```
/Game
  /Sim            ← Deterministic simulation. No Godot dependencies. Pure C#.
    /Math         ← Fixed-point types, deterministic random, vectors
    /State        ← GameState, UnitState, CityState, MapState (POCOs/structs)
    /Systems      ← Movement, Combat, Economy, PowerProjection, Supply, FogOfWar
    /Commands     ← Input commands (MoveUnits, BuildFortification, etc.)
    /Generation   ← Procedural map generation (also deterministic)
  /Render         ← Godot scenes, nodes, visual effects. Reads sim state.
  /UI             ← HUD, menus, lobby screens
  /Net            ← Steamworks integration, lockstep, input buffering
  /AI             ← Bot AI. Outputs Commands like a human player would.
  /Audio          ← Sound effects, music
  /Tools          ← Map preview tool, replay viewer, balance test harness
/Tests
  /Sim            ← Unit + integration tests for simulation. MUST be deterministic.
  /Replay         ← Recorded games that must replay identically.
```

**Why this matters:** the sim/render split is the single most important architectural decision. Get this wrong and lockstep netcode + replays + headless AI training all become impossible to retrofit. Every feature must respect this boundary from day one.

---

## 3. Game Design Spec (v1 Scope)

### Units (2 types — keep this discipline; do not add more in v1)

| Unit | Speed | Damage | HP | Terrain Penalty | Supply Cost |
|---|---|---|---|---|---|
| Light | Fast | Low | Low | None | 1 |
| Heavy | Slow | High | High | Severe in forest/mountain | 2 |

### Map Tiles

| Tile | Speed | Defense Multiplier | Notes |
|---|---|---|---|
| Plains | 1.0× | 1.00× (none) | Default |
| Forest | 1.0× light, 0.5× heavy | 0.70× (30% reduction) | Cover from trees |
| Mountain | 0.25× light, impassable heavy | 0.50× (50% reduction) | Extreme high-ground |
| Water | impassable | — | Blocks all movement |
| Road | 1.5× all | 1.10× (10% MORE damage) | Fast but exposed |
| City | 1.0× | 0.60× (40% reduction) | Produces units, supply node |
| Capital | 1.0× | 0.60× (40% reduction) | Like city; losing it = game over |
| Fort | 1.0× | 0.45× (55% reduction) | ✅ IMPLEMENTED. Built via `BuildFortCommand`; 50 ECO, 10 sec build, capturable (80 HP), razeable, max 3/player |

### Economy

- Each city produces 1 ECO/sec; capitals produce 3 ECO/sec
- Each city supplies up to 5 unit-supply-points (light=1, heavy=2)
- Units beyond supply capacity start losing HP each tick (starvation)
- Units must be connected to a supply source via friendly territory (this is the supply line system — NEW vs WoD)
- Cut a supply line, units in the cut-off pocket starve

### Power Projection (port WoD's mechanic)

- Units and cities project influence onto the tile grid
- Tiles owned by whichever side has higher local influence
- Borders form dynamically; encircled units are destroyed
- Power projection algorithm: weighted Voronoi-like field, computed each sim tick at low resolution (e.g., 8x downsampled grid for performance)

### Strategic Layer (NEW vs WoD — this is the differentiator)

1. **Supply lines** (described above)
2. **Fortifications** — build at a city or controlled tile; takes 30 seconds; gives +defense + power projection
3. **Fog of war** — see only tiles within friendly unit/city vision; remember last seen state
4. **Doctrines** — at game start, pick ONE of three doctrines. Each modifies a few unit stats and unlocks one ability. Doctrines map to canonical strategic archetypes (see Section 1.5):
   - **Maneuver** *(blitzkrieg / indirect approach)*: light units +speed, can build forward outposts, faster on roads
   - **Attrition** *(defensive warfare / grand strategy)*: units regen faster, fortifications cheaper and stronger, supply ceiling +1 per city
   - **Combined Arms** *(counterattack / strike-at-weakness)*: heavy units suffer less terrain penalty, can fire while moving, +vision range
5. **Larger maps** — 80x80 to 160x160 tiles, vs WoD's smaller maps

### Win Conditions

- Capture enemy capital, OR
- Control 80% of cities for 30 consecutive seconds, OR
- Opponent disconnect/surrender

---

## 4. Phased Build Plan

Each phase has a **goal**, **deliverables**, **acceptance criteria**, and **human decision points**. AI agents should not start phase N+1 until phase N's acceptance criteria are met *and* the human has signed off on decision points.

---

### Phase 0 — Project Skeleton (Target: 2–3 days)

**Goal:** Empty project that runs, with sim/render split enforced from line one.

**Deliverables:**
- Bootstrap from **Chickensoft GameTemplate** (`dotnet new install Chickensoft.GodotGame` then `dotnet new chickengame`) — gives you Godot+C#+Steamworks.NET preconfigured for Mac and Windows
- Folder structure per Section 2 (add `Sim`, `Render`, `AI`, `Net` subfolders)
- `Sim/Math/FP.cs` — fixed-point Q32.32 type with operators, conversion utilities
- `Sim/Math/SimRng.cs` — seeded deterministic RNG (xorshift or PCG)
- `Sim/Math/FPVec2.cs` — 2D vector using FP
- `Sim/State/GameState.cs` — empty struct with version field
- A "tick loop" that runs `GameState.Step(commands)` at 30 Hz
- One render node that reads sim state and draws a single dot
- Unit test harness (xUnit) running locally via `dotnet test`
- One golden test: same seed + same commands = same final state
- **GitHub Actions workflow that builds Mac (.app) and Windows (.exe) artifacts on every push** — this is non-negotiable; do NOT defer cross-platform CI to later phases

**Acceptance criteria:**
- `dotnet test` passes locally on Mac
- Project runs in Godot editor on Mac, shows a moving dot
- Determinism test: run sim 10000 ticks twice on Mac, hash final state, hashes match
- **Determinism test passes identically on the Windows GitHub Actions runner** — this is the real determinism test, since Mac-only determinism doesn't prove cross-platform determinism
- GitHub Actions produces downloadable Mac and Windows builds

**Human checkpoint:** review folder structure and commit to it before continuing.

---

### Phase 1 — Tactical Core (Target: 1–2 weeks)

**Goal:** A playable single-screen sandbox that reproduces WoD's tactical core.

**Deliverables:**
- Tile grid (start with 60x60)
- Unit movement with pathfinding (A* on tile grid, deterministic tiebreaking)
- Both unit types implemented per stats above
- City placement, unit production, supply capacity
- Power-projection field (downsampled grid, recompute every N ticks)
- Dynamic border rendering
- Encirclement detection and destruction
- Combat resolution
- Click-and-drag to select, right-click to move (basic input)
- Two players (hot-seat or both controlled by mouse) — no AI, no networking yet
- **Commit the visual palette and typography** per Section 1.6 — replace Godot default theme, pick the two faction colors, pick the typeface. Everything from here on uses these values.

**Acceptance criteria:**
- A human can play both sides and finish a match
- Borders shift visibly as units move
- Encircling enemy units actually destroys them
- 1000-tick replay test passes deterministically
- Game runs at 60 FPS with 200 units on screen

**Human checkpoint:** **PLAY THE GAME.** Does it feel like WoD? Is the power projection tuned right? Do not proceed until the tactical core feels good. This is the foundation everything else sits on.

---

### Phase 2 — Procedural Map Generation (Target: 1–2 weeks)

**Goal:** Replace hand-placed maps with a generator that produces balanced, interesting maps.

**Deliverables:**
- `Sim/Generation/MapGenerator.cs` — takes seed + parameters, returns `MapState`
- Terrain generation: layered Perlin/simplex noise → biome assignment
- City placement: Poisson disk sampling → balance pass
- Symmetry option (mirror/rotational) for competitive fairness
- **Balance validator** that scores maps on:
  - Starting position fairness (distance to nearest city, terrain quality)
  - Choke point distribution
  - City reachability from each spawn
  - Connectivity (no isolated regions)
- Reject-and-retry loop: generate, validate, regenerate if score < threshold
- Map preview tool in `/Tools` that renders 16 maps from random seeds at once

**Acceptance criteria:**
- Generate 100 maps, all pass validator
- Visual inspection of 20 random maps: no obvious problems (impossible spawns, isolated cities, all-water maps, etc.)
- Map generation completes in < 500ms
- Same seed = same map (determinism)

**Human checkpoint:** review generated maps. Tune validator thresholds. **This phase usually takes longer than the agent estimates** because "technically valid" and "actually fun" are different things.

---

### Phase 3 — Strategic Layer (Target: 3–4 weeks)

**Goal:** Add the systems that elevate this from tactics to strategy.

**Sub-phases (build and playtest one at a time, in this order):**

#### 3a. Supply lines — ✅ COMPLETE
- `SupplyLines` runs after `PowerProjection` and before `Healing` / `Maintenance`
- Owned cities, capitals, and forts seed supply
- Friendly-controlled tiles carry normal supply
- Roads and bridges carry road-assisted supply outside friendly territory when not blocked by enemy units
- Enemy units standing on roads/bridges interdict that road supply path
- Cut-off units cannot heal and pay 150% maintenance; road-supplied units pay 50% maintenance
- Healing remains restricted to friendly-controlled owned city/fort shelter

**Human checkpoint:** playtest whether supply interdiction feels impactful. Tune maintenance multipliers if cutoffs are too weak or too punishing.

#### 3a.5 Road/bridge engineering — ✅ COMPLETE
- `BuildRoadCommand(unit, target)` / `CancelRoadCommand(unit)`
- Selected unit + `B` enters road-build mode; hover previews deterministic engineering path
- Land road segments cost 2 ECO and 30 ticks; river / 1-tile-wide waterway bridge segments cost 8 ECO and 90 ticks
- Mountain peaks, broad water, and skinny land causeways between water are blocked; forests/mountains are allowed but costly
- Bridges render as darker tan and move/supply like roads

#### 3b. Fog of war (3–4 days)
- Tile visibility based on friendly unit/city vision radius
- Three states per tile per player: hidden / explored (last-seen) / visible
- Renderer respects per-player visibility (critical for MP and replays)

**Human checkpoint:** does FoW feel right at this map size, or are maps too big/small?

#### 3c. Fortifications — ✅ PULLED FORWARD TO PHASE 1.5

Fortifications were implemented early based on user request. See Phase 1.5 notes in Section 0.0. Summary:
- `BuildFortCommand(tile)` / `RazeFortCommand(tile)` — no builder unit needed
- 50 ECO cost, 10-second build (300 ticks), max 3 per player
- 55% damage reduction (strongest in game), base-25/radius-6 projection, +2 supply
- Capturable (80 HP), razeable (reverts tile to Plains)
- Under-construction forts cancelled if territory is lost
- Rendered as amber diamond shapes with build progress bars

#### 3d. Doctrines (3–4 days)
- Doctrine selection screen before match start
- Three doctrines per spec above
- Apply stat modifiers and unlock per-doctrine abilities

#### 3e. Larger maps (2–3 days)
- Bump map size to 120x120
- Performance pass: ensure 500+ units, larger power projection grid still hit 60 FPS
- Camera with zoom + pan (RTS-standard)

**Acceptance criteria for Phase 3:**
- All five subsystems work in single-player sandbox
- 30-minute test match produces interesting decisions, not just unit-blob fights
- All systems remain deterministic (replay test still passes)
- Performance target maintained on larger maps

**Human checkpoint:** **HEAVY PLAYTESTING.** This is where the design either works or doesn't. Cut anything that isn't fun. Better to ship with 3 strategic systems that work than 5 that don't.

---

### Phase 4 — Single-Player AI (Target: 3–5 weeks)

**Goal:** AI good enough that beating it teaches you the game, and the hard difficulty challenges intermediate players.

**Architecture: layered AI**
- **Strategic layer** (slow, every few seconds): sets goals — "expand to that region," "defend this city," "tech to combined arms"
- **Operational layer** (medium, every second): allocates units to goals, plans force compositions
- **Tactical layer** (fast, every tick): executes individual unit movements, reacts to threats

**Deliverables:**
- Three difficulty levels (Easy / Medium / Hard) — differ in strategic awareness, NOT input speed (no APM advantage; that's not strategic)
- AI uses the same `Command` API as human players (no cheating)
- Easy: random-ish but valid play, won't blunder *too* badly
- Medium: solid economy, sensible expansion, basic threat response
- Hard: actively counters player strategy, uses terrain, defends supply lines

**Acceptance criteria:**
- Easy AI: a brand-new player wins ~70% of matches
- Medium AI: a player who's read the tutorial wins ~50% of matches
- Hard AI: requires real strategic thinking to beat
- AI completes its decisions in < 5ms per tick (no frame drops)
- AI never desyncs (it's just generating Commands like a human)

**Human checkpoint:** this is the slowest phase. **Budget extra time.** The AI doesn't have to be world-class; it has to teach the game. If Hard mode is too easy, ship anyway and improve post-launch.

---

### Phase 5 — Lockstep Multiplayer (Target: 3–5 weeks)

**Goal:** Steam-based 1v1 matchmaking with deterministic lockstep netcode.

**Deliverables:**
- GodotSteam integration
- Steam Lobbies for matchmaking (quick match + custom lobby)
- Lockstep simulation: both clients run sim from identical inputs
- Input delay (typical: 2-3 ticks of buffering at 30Hz = ~66-100ms)
- Heartbeat + desync detection (hash sim state every N ticks, compare)
- Reconnection handling (rejoin via state transfer if heartbeat fails)
- Replay system (just a list of timestamped commands + initial seed)
- Surrender + disconnect handling
- Basic ranked matchmaking (Elo-based, no fancy MMR for v1)

**Acceptance criteria:**
- Two clients on same network play full match without desync
- Two clients on different networks (NAT) play full match (Steam relay handles this)
- Replay system produces identical match from recorded inputs
- Matchmaking finds opponent in < 60 seconds (with realistic player count caveat)
- Disconnect during match → other player wins after grace period

**Human checkpoint:** **TEST WITH REAL FRIENDS ON REAL INTERNET.** LAN tests lie. Find at least 3 testers in different regions before declaring this phase done.

**Risk note for AI agents:** Desyncs are the #1 source of pain in lockstep RTS. If you find one, **do not paper over it.** Find the root cause. Common culprits: float usage that snuck into sim, unsorted dictionary iteration, non-deterministic LINQ ordering, hash-set iteration order, threading.

---

### Phase 6 — Polish, Steam Integration, Release Prep (Target: 4–6 weeks)

**Goal:** Ship-ready build.

**Deliverables:**
- Tutorial (interactive, not text walls)
- Main menu + settings (graphics, audio, controls)
- Sound effects + music (royalty-free or commissioned)
- Steam achievements (10–15)
- Steam stats and leaderboards
- Steam rich presence
- **In-app purchase plumbing via Steam Microtransactions** — non-pay-to-win only. v1 SKUs:
  - Cosmetic faction color packs (alternative palettes — cannot conflict with default colorblind-safe set)
  - Map theme packs (alternate terrain palettes — purely visual)
  - "Supporter pack" tier ($5–10) bundling all current cosmetics + a unique color
  - **Strict rule:** no IAP affects gameplay stats, doctrine availability, or unit performance. Steam reviews will eviscerate the game otherwise.
- Localization framework (English only at launch; structure ready for more)
- Crash reporting (Sentry free tier is fine)
- Steam store page assets: trailer, screenshots, capsule images, description
- EULA and privacy policy (use a generator — DO NOT copy WoD's; it has issues per Steam reviews)
- Press kit
- Steam Deck verification (Godot games usually pass easily; budget a day to test — note: Steam Deck runs Linux, so even though Linux isn't a launch target, you'll want to make sure the Linux build works for Steam Deck players)
- **macOS build verified end-to-end on Steam** (you have a Mac, so this is a free win — many WoD competitors ship Windows-only)

**Acceptance criteria:**
- 10 hours of playtesting by external testers without critical bugs
- Steam Deck plays at 60 FPS
- All Steam features actually work (not just stubs)
- Build size < 200 MB

**Human checkpoint:** Steam approval takes 1–2 weeks; submit early.

---

## 5. Aggregate Timeline

| Phase | Duration (LLM-assisted, full-time-equivalent) |
|---|---|
| 0. Skeleton | 2–3 days |
| 1. Tactical core | 1–2 weeks |
| 2. Procedural maps | 1–2 weeks |
| 3. Strategic layer | 3–4 weeks |
| 4. Single-player AI | 3–5 weeks |
| 5. Multiplayer netcode | 3–5 weeks |
| 6. Polish + Steam | 4–6 weeks |
| **Total** | **~4–6 months full-time, ~8–12 months part-time** |

Pad by 25%. You will hit something this plan didn't anticipate.

---

## 6. Standing Instructions for AI Coding Agents

When working on this codebase:

1. **Read the relevant phase section before generating code.** Don't write Phase 4 code while we're in Phase 2.
2. **Respect the sim/render split** (Section 1 hard rules). Violations are bugs.
3. **Write tests for sim code.** Determinism tests are non-negotiable.
4. **Prefer small, reviewable PRs.** A 2000-line generation is a red flag, not a feature.
5. **Flag design questions to the human.** Do not invent game design — ask.
6. **When uncertain about correctness, write a test that would catch the bug.** Then fix the bug.
7. **Keep dependencies minimal.** Every NuGet package is a future maintenance cost. Justify additions.
8. **Comment the *why*, not the *what*.** Especially for fixed-point math, supply line algorithms, and netcode.
9. **Don't optimize prematurely.** First make it correct, then measure, then optimize. Power projection is the most likely real bottleneck — profile before rewriting.
10. **If you find yourself fighting the architecture, stop and ask.** That's a signal something's wrong with the plan, not the code.

---

## 7. Open Questions / Decisions Deferred

These will need answers before their relevant phases:

- **Game name** (needed by Phase 6 for Steam page) — TBD; flag candidates throughout development
- **Specific palette hex values** — pick in Phase 1 and commit
- **Music style and SFX direction** — original commission, decided post-Phase 5 once gameplay is locked
- **IAP price points and pack contents** — decide during Phase 6 based on industry norms (typical: $1.99–4.99 per cosmetic pack, $9.99 supporter)
- **Server-authoritative future** — for now P2P is fine; revisit if we hit cheating problems
- **Apple Developer account** ($99/year) — confirmed yes; needed for itch.io playtesting before Steam approval

### Resolved decisions (locked in)

- Engine: Godot 4.6+ with C# (Section 1)
- Multiplayer: Steamworks.NET via Chickensoft template
- Launch platforms: Windows + macOS
- Multiplayer scope: 1v1 only for v1
- Strategic depth: Medium (supply lines + fog of war + doctrines + larger maps)
- Business model: F2P with cosmetic-only IAP
- Localization: English-only at v1
- Visual direction: polished minimalism per Section 1.6 (Abstractanks / Square Wars reference)
- Strategic design grounded in Freedman + Greene (Section 1.5)

---

## 8. What This Plan Deliberately Excludes

To stay honest about scope, v1 does NOT include:

- Team modes (2v2 etc.) — post-launch
- Custom map editor — post-launch (procgen is the v1 story)
- Mod support / Steam Workshop — post-launch
- Mobile/console ports — much later
- Campaign / story mode — post-launch
- More than 2 unit types — out of scope, would unbalance the minimalism
- More than 3 doctrines — same reasoning (additional doctrines are a post-launch update path, not v1)
- Progression / unlocks that affect gameplay — out of scope for free-to-play v1 (cosmetic IAP only)
- Anti-cheat — P2P with replays is sufficient for v1; revisit if needed
- **Pay-to-win IAP** — explicitly out of scope and out of values. All IAP is cosmetic.

If a feature isn't in this plan, it's not in v1. Adding scope is the #1 reason indie games never ship.
