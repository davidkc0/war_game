# PLAN.md — Procedural RTS Game (Working Title: TBD)

> **Brief for AI coding agents (Cursor / Antigravity / Claude Code).** This document is the source of truth for the project. Read it fully before generating code. When in doubt, prefer the choices documented here over your own defaults. Flag deviations explicitly to the human operator.

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

- **Plains** — no modifier
- **Forest** — light units fight at +bonus, heavy at -penalty
- **Mountain** — impassable to heavy, slow for light
- **Water** — impassable
- **Road** — speed bonus to all units, +supply throughput
- **City** — produces units, supply node
- **Capital** — like a city, but losing it = losing the game
- **Fortification (built)** — +defense bonus to occupant, +power projection

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

#### 3a. Supply lines (1 week)
- Each unit has a "supply path" to nearest connected friendly city
- Path computed via flood fill on friendly-controlled tiles
- Out-of-supply units lose HP per tick
- Visualize supply paths in UI (toggleable)

**Human checkpoint:** does cutting supply lines feel impactful? If not, adjust starvation rate.

#### 3b. Fog of war (3–4 days)
- Tile visibility based on friendly unit/city vision radius
- Three states per tile per player: hidden / explored (last-seen) / visible
- Renderer respects per-player visibility (critical for MP and replays)

**Human checkpoint:** does FoW feel right at this map size, or are maps too big/small?

#### 3c. Fortifications (3–4 days)
- New command: `BuildFortification(tile, builderUnit)`
- 30-second build time, costs ECO
- Provides defense bonus + power projection while occupied

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
