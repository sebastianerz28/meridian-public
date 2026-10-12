namespace Game.Agents;

/// <summary>
/// Code-enforced limits on LLM-produced (and agent-persisted) text. These are not
/// suggestions embedded in a prompt — <see cref="DecisionParser"/> and (later)
/// LlmActor apply <see cref="Truncate"/> unconditionally, so no path from model
/// output to game state can exceed them regardless of what the model was told.
/// </summary>
public static class Bounds
{
    public const int MaxAssessment = 400;
    public const int MaxStatement = 200;
    public const int MaxBeliefs = 8;
    public const int MaxBeliefLength = 200;
    public const int MaxMemory = 300;

    /// <summary>Truncates <paramref name="value"/> to at most <paramref name="maxLength"/> characters.</summary>
    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
