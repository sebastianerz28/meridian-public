using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Game.Contracts;

namespace Game.Agents;

/// <summary>
/// Builds the two prompts sent to an LLM for a country's turn: a stable system prompt
/// (identity, objectives, persona, action schema, output contract) and a per-turn
/// decision prompt built exclusively from <see cref="DecisionContext"/>. Because the
/// decision prompt only ever reads from <see cref="ActorView"/> (own exact snapshot,
/// bounded estimates of others, exact outgoing relations, public facts) and the
/// engine-filtered event/memory data already handed to the agent, there is no code path
/// here that can leak another country's ground truth — the type system only gives this
/// class what the observer itself is entitled to see.
/// </summary>
public static class ContextBuilder
{
    private static readonly (ActionType Type, string Description)[] ActionDescriptions =
    {
        (ActionType.Pass, "Take no action this turn."),
        (ActionType.Trade, "Expand YOUR OWN export flow of rare earths toward the target. Export-side only: you can grow what you sell, you cannot force the target to buy."),
        (ActionType.Sanction, "Impose economic sanctions against the target."),
        (ActionType.ExportControl, "Restrict YOUR OWN rare-earth exports to the target."),
        (ActionType.Subsidize, "Subsidize your domestic resource industry."),
        (ActionType.Reassure, "Publicly reassure the target of your peaceful intent."),
        (ActionType.Threaten, "Publicly threaten the target."),
        (ActionType.SignPact, "Propose or sign a pact with the target."),
        (ActionType.BreakPact, "Break an existing pact with the target."),
        (ActionType.Mobilize, "Mobilize your armed forces, raising your own readiness."),
        (ActionType.DeployToRegion, "Deploy your forces into the contested strait region."),
        (ActionType.Withdraw, "Withdraw your forces from the contested strait region."),
        (ActionType.Spy, "Conduct a covert espionage operation against the target."),
    };

    public static string BuildSystemPrompt(AgentState s)
    {
        var sb = new StringBuilder();

        sb.Append("You are the government of ").Append(s.Country).Append(".\n\n");

        sb.Append("## Objectives\n").Append(s.ObjectivesText.Trim()).Append("\n\n");
        sb.Append("## Persona\n").Append(s.PersonaText.Trim()).Append("\n\n");

        sb.Append("## Available actions\n");
        foreach (var (type, description) in ActionDescriptions)
        {
            sb.Append("- ").Append(type).Append(": ").Append(description).Append('\n');
        }
        sb.Append('\n');

        sb.Append("## Output format\n");
        sb.Append("Respond with exactly one JSON object, and nothing else, shaped like this:\n");
        sb.Append("""
            {
              "assessment": "string",
              "beliefs_update": ["optional full replacement list of political beliefs"],
              "action": {"type": "ExportControl", "target": "Alderia", "magnitude": 0.6},
              "public_statement": "string"
            }
            """);
        sb.Append("\n\n");

        sb.Append("\"type\" must be one of the action names listed above. \"target\" is a country name and is ")
          .Append("omitted or ignored for actions that do not take one. \"magnitude\" is a number from 0 to 1. ")
          .Append("\"beliefs_update\", if present, fully replaces your political beliefs list.\n\n");

        sb.Append("You decide intent only; the world simulation determines all consequences. Output ONLY the JSON object.");

        return sb.ToString();
    }

    public static string BuildDecisionPrompt(AgentState s, DecisionContext ctx)
    {
        var view = ctx.View;
        var sb = new StringBuilder();

        sb.Append("Current turn: T").Append(ctx.Turn.ToString(CultureInfo.InvariantCulture)).Append("\n\n");

        sb.Append("## Your country (exact)\n");
        AppendOwnSnapshot(sb, view.Own);
        sb.Append('\n');

        sb.Append("## Global situation\n");
        sb.Append("Rare-earth price: ").Append(Num(view.Public.RareEarthPrice)).Append('\n');
        sb.Append("Strait tension: ").Append(Num(view.Public.StraitTension)).Append('\n');
        sb.Append("Pacts: ");
        sb.Append(view.Public.Pacts.IsDefaultOrEmpty
            ? "none"
            : string.Join(", ", view.Public.Pacts.Select(p => $"{p.A}-{p.B}")));
        sb.Append("\n\n");

        sb.Append("## Intelligence estimates\n");
        if (view.ForeignEstimates.Count == 0)
        {
            sb.Append("(none)\n");
        }
        else
        {
            foreach (var (property, estimate) in view.ForeignEstimates)
            {
                sb.Append(DescribeEstimate(property, estimate)).Append('\n');
            }
        }
        sb.Append('\n');

        sb.Append("## Your relationships\n");
        if (view.OutgoingRelations.IsDefaultOrEmpty)
        {
            sb.Append("(none)\n");
        }
        else
        {
            foreach (var rel in view.OutgoingRelations)
            {
                sb.Append(s.Country).Append(" -> ").Append(rel.Target)
                  .Append(": trust ").Append(Num(rel.Trust))
                  .Append(", threat ").Append(Num(rel.Threat))
                  .Append('\n');
            }
        }
        sb.Append('\n');

        sb.Append("## Recent relevant events\n");
        sb.Append(RenderEventLines(ctx.RelevantEvents, ctx.Self));
        sb.Append('\n');

        sb.Append("## Your memories\n");
        var memoryCount = s.Memories.Count;
        if (memoryCount == 0)
        {
            sb.Append("(none)\n");
        }
        else
        {
            // Newest first, at most 10.
            var take = Math.Min(10, memoryCount);
            for (var i = 0; i < take; i++)
            {
                var memory = s.Memories[memoryCount - 1 - i];
                sb.Append("- ").Append(Bounds.Truncate(memory, Bounds.MaxMemory)).Append('\n');
            }
        }
        sb.Append('\n');

        sb.Append("## Your political beliefs\n");
        if (s.PoliticalBeliefs.Count == 0)
        {
            sb.Append("(none)\n");
        }
        else
        {
            foreach (var belief in s.PoliticalBeliefs)
            {
                sb.Append("- ").Append(belief).Append('\n');
            }
        }
        sb.Append('\n');

        sb.Append("## Your previous assessment\n");
        sb.Append(string.IsNullOrEmpty(s.LatestAssessment) ? "(none)" : s.LatestAssessment).Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// Renders a list of events exactly as the decision prompt's "Recent relevant
    /// events" section does — one line per event, newest-first order preserved as
    /// given, "(none)" when empty. Shared by <see cref="BuildDecisionPrompt"/> and
    /// by LlmActor's reflection prompt so both surfaces describe events identically.
    /// </summary>
    public static string RenderEventLines(ImmutableArray<DomainEvent> events, CountryId self)
    {
        var sb = new StringBuilder();
        if (events.IsDefaultOrEmpty)
        {
            sb.Append("(none)\n");
        }
        else
        {
            foreach (var e in events)
            {
                sb.Append('T').Append(e.Turn.ToString(CultureInfo.InvariantCulture))
                  .Append(": ").Append(DescribeEvent(e, self)).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void AppendOwnSnapshot(StringBuilder sb, CountrySnapshot own)
    {
        sb.Append("GDP: ").Append(Num(own.Gdp)).Append('\n');
        sb.Append("Growth (annual): ").Append(Num(own.GrowthAnnual)).Append('\n');
        sb.Append("Inflation: ").Append(Num(own.Inflation)).Append('\n');
        sb.Append("Treasury: ").Append(Num(own.Treasury)).Append('\n');
        sb.Append("Resource production: ").Append(Num(own.ResourceProduction)).Append('\n');
        sb.Append("Resource stockpile: ").Append(Num(own.ResourceStockpile)).Append('\n');
        sb.Append("Resource dependence: ").Append(Num(own.ResourceDependence)).Append('\n');
        sb.Append("Military strength: ").Append(Num(own.MilitaryStrength)).Append('\n');
        sb.Append("Readiness: ").Append(Num(own.Readiness)).Append('\n');
        sb.Append("Forces in strait: ").Append(Num(own.ForcesInStrait)).Append('\n');
        sb.Append("Approval: ").Append(Num(own.Approval)).Append('\n');
        sb.Append("Election countdown: ").Append(own.ElectionCountdown.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("Government: ").Append(own.GovernmentTemplateId).Append('\n');
        sb.Append("In shortage: ").Append(own.InShortage ? "yes" : "no").Append('\n');
    }

    private static string DescribeEstimate(string property, Estimate estimate)
    {
        var label = DescribeProperty(property);
        if (estimate.Confidence == Confidence.Unknown)
        {
            return $"{label}: unknown";
        }

        var provenance = $"(confidence: {estimate.Confidence}, observed T{estimate.ObservedTurn.ToString(CultureInfo.InvariantCulture)}, source: {estimate.Source})";
        return $"{label}: {Num(estimate.Low)}–{Num(estimate.High)} {provenance}";
    }

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

    private static string DescribeEvent(DomainEvent e, CountryId self) => e switch
    {
        ExportRestrictionSet x => $"{x.Exporter} restricts rare-earth exports to {x.Importer} ({Pct(x.Level)}) — \"{x.Statement}\"",
        SanctionLevelSet x => $"{x.Source} sets sanctions on {x.Target} to {Pct(x.Level)} — \"{x.Statement}\"",
        TradeFlowChanged x => $"{x.Exporter} adjusts trade flow to {x.Importer} ({Signed(x.Delta)}) — \"{x.Statement}\"",
        PactSigned x => $"{x.A} and {x.B} sign a pact — \"{x.Statement}\"",
        PactBroken x => $"{x.A} breaks its pact with {x.B} — \"{x.Statement}\"",
        SubsidyEnacted x => $"{x.Actor} enacts subsidies (effective {Pct(x.Effective)}) — \"{x.Statement}\"",
        ThreatIssued x => $"{x.Actor} threatens {x.Target} — \"{x.Statement}\"",
        ReassuranceGiven x => $"{x.Actor} reassures {x.Target} — \"{x.Statement}\"",
        ForcesDeployedNotice x => $"{x.Actor} deploys forces near {x.Target} — \"{x.Statement}\"",
        ShortageStarted x => $"Shortage begins in {x.Country} (severity {Pct(x.Severity)})",
        ShortageEnded x => $"Shortage ends in {x.Country}",
        StraitIncident => "Incident reported in the Strait",
        SpySucceeded x when x.Actor == self => $"Your covert operation against {x.Target} succeeds",
        SpyCaught x => $"{x.Actor}'s agent is caught spying on {x.Target}",
        ElectionHeld x => $"Election held in {x.Country} — incumbent {(x.IncumbentRetained ? "retained" : "ousted")}",
        GovernmentChanged x => $"{x.Country} installs a new government ({x.NewTemplateId})",
        IntentRejected x when x.IntentActor == self => $"Your {x.Type} was rejected — {x.Reason}",

        // Defense in depth: this class is the ground-truth-leak boundary, so an event
        // type/case not explicitly handled above (including a self-guarded case like
        // SpySucceeded/IntentRejected falling through for a non-participant) must never
        // dump its raw record fields into the prompt — only the type name is safe.
        _ => e.GetType().Name,
    };

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double value) => $"{(value * 100).ToString("0", CultureInfo.InvariantCulture)}%";

    private static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
}
