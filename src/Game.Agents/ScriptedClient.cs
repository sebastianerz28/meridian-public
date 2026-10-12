namespace Game.Agents;

public sealed class ScriptedClient(Queue<string> responses) : ILlmClient
{
    public List<(string System, string User)> Calls { get; } = new();

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        Calls.Add((systemPrompt, userPrompt));

        if (!responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("No more scripted responses available.");
        }

        return Task.FromResult(response);
    }
}
