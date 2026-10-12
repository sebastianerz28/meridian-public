using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Contracts;

namespace Game.Agents;

/// <summary>
/// The result of parsing one LLM decision response. Deliberately narrow: it can only
/// express an intent to submit, a self-assessment, and an optional full replacement of
/// political beliefs. It cannot express estimate/knowledge changes — that mutation
/// boundary does not exist here, and never will via this type. This is the LLM mutation
/// boundary for a turn's decision.
/// </summary>
public sealed record ParsedDecision(
    ActionIntent Intent,
    string Assessment,
    IReadOnlyList<string>? BeliefsUpdate);

/// <summary>
/// Parses free-form LLM text into a <see cref="ParsedDecision"/>. Never throws: any
/// malformed, incomplete, or hostile input yields <c>null</c> rather than an exception,
/// because a misbehaving model is an expected operating condition, not a bug.
/// All bounds from <see cref="Bounds"/> are enforced here in code, not merely requested
/// via prompt — this is the last line of defense before agent/world state.
/// </summary>
public static class DecisionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ParsedDecision? Parse(CountryId actor, string? llmText)
    {
        if (string.IsNullOrWhiteSpace(llmText)) return null;

        try
        {
            var stripped = StripFences(llmText);

            var start = stripped.IndexOf('{');
            var end = stripped.LastIndexOf('}');
            if (start < 0 || end < start) return null;

            var jsonText = stripped[start..(end + 1)];

            var dto = JsonSerializer.Deserialize<DecisionDto>(jsonText, JsonOptions);
            if (dto?.Action is null) return null;

            var typeStr = dto.Action.Type;
            if (string.IsNullOrWhiteSpace(typeStr)) return null;
            if (!Enum.TryParse<ActionType>(typeStr, ignoreCase: true, out var type)) return null;
            if (!Enum.IsDefined(type)) return null;

            CountryId? target = null;
            if (!string.IsNullOrWhiteSpace(dto.Action.Target))
            {
                if (!Enum.TryParse<CountryId>(dto.Action.Target, ignoreCase: true, out var parsedTarget)) return null;
                if (!Enum.IsDefined(parsedTarget)) return null;
                target = parsedTarget;
            }

            var magnitude = Math.Clamp(dto.Action.Magnitude ?? 0.0, 0.0, 1.0);

            var statement = Bounds.Truncate(dto.PublicStatement ?? string.Empty, Bounds.MaxStatement);
            var assessment = Bounds.Truncate(dto.Assessment ?? string.Empty, Bounds.MaxAssessment);

            // actor is ALWAYS the passed-in actor — any "actor" field the model put inside
            // the action object was never even mapped onto DecisionDto/ActionDto above.
            var intent = new ActionIntent(actor, type, target, magnitude, statement);

            List<string>? beliefs = null;
            if (dto.BeliefsUpdate is not null)
            {
                beliefs = dto.BeliefsUpdate
                    .Where(b => b is not null)
                    .Take(Bounds.MaxBeliefs)
                    .Select(b => Bounds.Truncate(b!, Bounds.MaxBeliefLength))
                    .ToList();
            }

            return new ParsedDecision(intent, assessment, beliefs);
        }
        catch (Exception)
        {
            // This method's entire contract with callers is "never throws on untrusted
            // LLM text" — any parsing failure, expected (JsonException) or not, becomes
            // a null result rather than an exception escaping to the caller.
            return null;
        }
    }

    private static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = t.IndexOf('\n');
            t = firstNewline >= 0 ? t[(firstNewline + 1)..] : t[3..];
        }
        if (t.EndsWith("```", StringComparison.Ordinal))
        {
            t = t[..^3];
        }
        return t;
    }

    private sealed class DecisionDto
    {
        public string? Assessment { get; set; }

        [JsonPropertyName("beliefs_update")]
        public List<string?>? BeliefsUpdate { get; set; }

        public ActionDto? Action { get; set; }

        [JsonPropertyName("public_statement")]
        public string? PublicStatement { get; set; }
    }

    private sealed class ActionDto
    {
        public string? Type { get; set; }
        public string? Target { get; set; }
        public double? Magnitude { get; set; }
    }
}
