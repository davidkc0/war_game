namespace WarGame.Sim.State;

// PLAN.md §3 fixes Phase 1 to two unit types. Adding a third here is a
// scope-creep red flag; if it ever feels needed, the answer is no until v1
// ships. (See PLAN.md §8.)
public enum UnitType : byte
{
    Light = 0,
    Heavy = 1,
}
