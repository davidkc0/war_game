namespace WarGame;

using WarGame.Sim.AI;

public enum MatchMode : byte
{
    HumanVsHuman = 0,
    HumanVsAi = 1,
}

public static class MatchConfig
{
    public static MatchMode Mode { get; set; } = MatchMode.HumanVsAi;
    public static AiDifficulty AiDifficulty { get; set; } = AiDifficulty.Medium;

    public static bool IsAiMatch => Mode == MatchMode.HumanVsAi;
}
