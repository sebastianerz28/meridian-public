using System.Globalization;
using Game.Contracts;

namespace Game.Headless;

/// <summary>
/// Human-driven actor for <c>--actors play</c>: always <see cref="SchedulingPolicy.EveryTurn"/>.
/// Each turn it prints the same categories of information an <c>LlmActor</c>'s prompt would
/// contain — own exact stats, bounded intelligence estimates of the others, and recent
/// relevant events rendered as news — then reads one line of stdin shaped
/// <c>action [target] [magnitude] [statement...]</c> ("pass" is a shortcut for
/// <see cref="ActionType.Pass"/> with no target/magnitude/statement). Invalid input re-prompts
/// rather than throwing or silently passing. Never reflects: memories are an LLM-agent concept
/// and the player has none to write.
/// </summary>
public sealed class ConsolePlayerActor : IActor
{
    public Task<ActionIntent> DecideAsync(DecisionContext ctx, CancellationToken ct)
    {
        PrintTurnBriefing(ctx);

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                // No stdin available (EOF/non-interactive) — degrade to Pass rather than spin.
                Console.WriteLine("(no input available; passing)");
                return Task.FromResult(ActionIntent.Pass(ctx.Self));
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (string.Equals(trimmed, "pass", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(ActionIntent.Pass(ctx.Self));

            if (TryParse(ctx.Self, trimmed, out var intent, out var error))
                return Task.FromResult(intent!);

            Console.WriteLine($"Invalid input ({error}). Format: action [target] [magnitude] [statement...] (or 'pass').");
        }
    }

    public Task ReflectAsync(ReflectionContext ctx, CancellationToken ct) => Task.CompletedTask;

    private static bool TryParse(CountryId self, string input, out ActionIntent? intent, out string? error)
    {
        intent = null;
        error = null;

        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = "empty input";
            return false;
        }

        if (!Enum.TryParse<ActionType>(tokens[0], ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            error = $"unknown action '{tokens[0]}'";
            return false;
        }

        var idx = 1;
        CountryId? target = null;
        if (idx < tokens.Length
            && Enum.TryParse<CountryId>(tokens[idx], ignoreCase: true, out var parsedTarget)
            && Enum.IsDefined(parsedTarget))
        {
            target = parsedTarget;
            idx++;
        }

        var magnitude = 0.0;
        if (idx < tokens.Length
            && double.TryParse(tokens[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMagnitude))
        {
            magnitude = Math.Clamp(parsedMagnitude, 0.0, 1.0);
            idx++;
        }

        var statement = idx < tokens.Length ? string.Join(' ', tokens[idx..]) : string.Empty;

        intent = new ActionIntent(self, type, target, magnitude, statement);
        return true;
    }

    private static void PrintTurnBriefing(DecisionContext ctx)
    {
        var own = ctx.View.Own;

        Console.WriteLine();
        Console.WriteLine($"=== Turn {ctx.Turn}: {ctx.Self}'s decision ===");

        Console.WriteLine("-- Your country (exact) --");
        Console.WriteLine($"GDP: {Num(own.Gdp)}  Growth: {Num(own.GrowthAnnual)}  Inflation: {Num(own.Inflation)}  Treasury: {Num(own.Treasury)}");
        Console.WriteLine($"Resource production: {Num(own.ResourceProduction)}  stockpile: {Num(own.ResourceStockpile)}  dependence: {Num(own.ResourceDependence)}");
        Console.WriteLine($"Military strength: {Num(own.MilitaryStrength)}  readiness: {Num(own.Readiness)}  forces in strait: {Num(own.ForcesInStrait)}");
        Console.WriteLine($"Approval: {Num(own.Approval)}  Election countdown: {own.ElectionCountdown.ToString(CultureInfo.InvariantCulture)}  Government: {own.GovernmentTemplateId}  In shortage: {(own.InShortage ? "yes" : "no")}");

        Console.WriteLine("-- Global situation --");
        Console.WriteLine($"Rare-earth price: {Num(ctx.View.Public.RareEarthPrice)}  Strait tension: {Num(ctx.View.Public.StraitTension)}");
        Console.WriteLine("Pacts: " + (ctx.View.Public.Pacts.IsDefaultOrEmpty
            ? "none"
            : string.Join(", ", ctx.View.Public.Pacts.Select(p => $"{p.A}-{p.B}"))));

        Console.WriteLine("-- Intelligence estimates on others --");
        if (ctx.View.ForeignEstimates.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            foreach (var (property, estimate) in ctx.View.ForeignEstimates)
                Console.WriteLine(DescribeEstimate(property, estimate));
        }

        Console.WriteLine("-- Recent news --");
        var news = NewsRenderer.Render(ctx.RelevantEvents, ctx.Self);
        Console.WriteLine(news.Length == 0 ? "(nothing new)" : news);
        Console.WriteLine();
    }

    private static string DescribeEstimate(string property, Estimate estimate)
    {
        var label = DescribeProperty(property);
        if (estimate.Confidence == Confidence.Unknown)
            return $"{label}: unknown";

        return $"{label}: {Num(estimate.Low)}–{Num(estimate.High)} " +
               $"(confidence: {estimate.Confidence}, observed T{estimate.ObservedTurn.ToString(CultureInfo.InvariantCulture)}, source: {estimate.Source})";
    }

    // Mirrors ContextBuilder.DescribeProperty (Game.Agents) so the player sees estimates
    // labeled the same way an LLM agent's prompt would; duplicated here rather than shared
    // because that method is private to the prompt-building type.
    private static string DescribeProperty(string property)
    {
        var arrowIdx = property.IndexOf("->", StringComparison.Ordinal);
        if (arrowIdx >= 0)
        {
            var src = property[..arrowIdx];
            var rest = property[(arrowIdx + 2)..]; // "Target.Field"
            var dotIdx = rest.IndexOf('.');
            var target = dotIdx >= 0 ? rest[..dotIdx] : rest;
            var field = dotIdx >= 0 ? rest[(dotIdx + 1)..] : "";
            var noun = field switch
            {
                "Trust" => "trust in",
                "Threat" => "perceived threat from",
                _ => field.ToLowerInvariant(),
            };
            return $"{src} {noun} {target}";
        }

        var parts = property.Split('.');
        var country = parts.Length > 0 ? parts[0] : property;
        var fieldName = parts.Length > 2 ? parts[2] : parts[^1];
        var description = fieldName switch
        {
            "Gdp" => "gdp",
            "Inflation" => "inflation",
            "Treasury" => "treasury",
            "Production" => "resource production",
            "Dependence" => "resource dependence",
            "Strength" => "military strength",
            "Readiness" => "military readiness",
            "ForcesInStrait" => "forces in strait",
            "Approval" => "approval",
            _ => fieldName.ToLowerInvariant(),
        };
        return $"{country} {description}";
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
