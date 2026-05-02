using System.Collections.Generic;

namespace WarGame.Sim.State;

// Persistent road/bridge construction order. The path is stored so the
// preview, replay, and eventual construction all use the same deterministic
// route even if territory shifts while the unit is working.
public struct RoadOrder
{
    public int UnitId;
    public PlayerId Owner;
    public int TargetX;
    public int TargetY;
    public List<int> Path;
    public int CurrentPathIndex;
    public int TicksRemainingOnTile;

    public static RoadOrder Create(int unitId, PlayerId owner, int targetX, int targetY, List<int> path)
    {
        return new RoadOrder
        {
            UnitId = unitId,
            Owner = owner,
            TargetX = targetX,
            TargetY = targetY,
            Path = path,
            CurrentPathIndex = 0,
            TicksRemainingOnTile = 0,
        };
    }
}
