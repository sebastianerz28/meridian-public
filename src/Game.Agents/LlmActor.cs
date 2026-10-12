using System.Diagnostics;
using Game.Contracts;

namespace Game.Agents;

/// <summary>
/// Drives one country via an <see cref="ILlmClient"/>. Every LLM call, valid or not,
/// is telemetried. A malformed reply earns exactly one retry with a corrective prefix;
/// a second malformed reply — or any exception the client throws (transport, provider,
/// etc.) — degrades to <see cref="ActionIntent.Pass"/> rather than propagating, because
/// a single bad turn or a provider outage must never end a multi-turn run.
/// <see cref="OperationCanceledException"/> is the one exception let through uncaught,
/// so callers can still cancel a run.
/// </summary>
public sealed class LlmActor(AgentState state, ILlmClient llm, IAgentTelemetry telemetry) : IActor
{
    private const string RetryPrefix =
        "Your previous reply was not valid. Reply with ONLY the JSON object in the required schema.\n\n";

    public AgentState State => state;

    public async Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct)
    {
        var systemPrompt = ContextBuilder.BuildSystemPrompt(state);
        var userPrompt = ContextBuilder.BuildDecisionPrompt(state, ctx);

        ParsedDecision? parsed;
        try
        {
            parsed = await AttemptDecisionAsync(systemPrompt, userPrompt, ctx.Turn, ct).ConfigureAwait(false);
            if (parsed is null)
            {
                parsed = await AttemptDecisionAsync(systemPrompt, RetryPrefix + userPrompt, ctx.Turn, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            telemetry.LlmFailure(state.Country, ctx.Turn, "decide", ex.Message);
            return ActionIntent.Pass(state.Country);
        }

        if (parsed is null)
        {
            return ActionIntent.Pass(state.Country);
        }

        state.LatestAssessment = parsed.Assessment;
        if (parsed.BeliefsUpdate is not null)
        {
            state.PoliticalBeliefs.Clear();
            state.PoliticalBeliefs.AddRange(parsed.BeliefsUpdate);
        }

        return parsed.Intent;
    }

    public async Task ReflectAsync(ReflectionContext ctx, CancellationToken ct)
    {
        var systemPrompt = ContextBuilder.BuildSystemPrompt(state);
        var eventLines = ContextBuilder.RenderEventLines(ctx.VisibleTurnEvents, ctx.Self);
        var userPrompt =
            "Something strategically significant happened this turn. Events you observed:\n" +
            eventLines +
            "Write a memory of at most 50 words that your institutions will retain — what mattered and why.";

        string response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await llm.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            telemetry.LlmFailure(state.Country, ctx.Turn, "reflect", ex.Message);
            return;
        }
        sw.Stop();

        var memory = Bounds.Truncate(response, Bounds.MaxMemory);
        state.Memories.Add(memory);
        telemetry.MemoryWritten(state.Country, ctx.Turn, memory);
        telemetry.LlmAttempt(state.Country, ctx.Turn, "reflect", userPrompt, response, valid: true, sw.ElapsedMilliseconds);
    }

    public void OnGovernmentChanged(string newTemplateId) => state.SwapGovernment(newTemplateId);

    private async Task<ParsedDecision?> AttemptDecisionAsync(
        string systemPrompt, string userPrompt, int turn, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await llm.CompleteAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        sw.Stop();

        var parsed = DecisionParser.Parse(state.Country, response);
        telemetry.LlmAttempt(state.Country, turn, "decide", userPrompt, response, parsed is not null, sw.ElapsedMilliseconds);
        return parsed;
    }
}
