using WarGame.Sim.Math;

namespace WarGame.Sim.State;

// Snapshot of the entire simulation at a given tick. This is the *only* state
// that gets fed to GameSim.Step — anything not serializable here cannot
// influence sim outcomes. That guarantee is what enables lockstep netcode and
// replay: a (seed + state + command-stream) tuple fully determines the future.
//
// Bump CurrentVersion whenever the schema changes so we can reject loading
// stale replays/saves.
public struct GameState
{
    public const int CurrentVersion = 1;

    public int Version;
    public int Tick;
    public SimRng Rng;
    public FPVec2 DotPos;
    public FPVec2 DotVel;

    public static GameState Initial(ulong seed)
    {
        return new GameState
        {
            Version = CurrentVersion,
            Tick = 0,
            Rng = new SimRng(seed),
            // Phase 0 dot starts at the origin. Phase 1 replaces this with
            // unit/city state.
            DotPos = FPVec2.Zero,
            // 0.05 units per tick on each axis — chosen so a 10000-tick run
            // lands at a non-trivial position with no overflow.
            DotVel = new FPVec2(
                FP.FromRaw(FP.OneRaw / 20),
                FP.FromRaw(FP.OneRaw / 30)),
        };
    }
}
