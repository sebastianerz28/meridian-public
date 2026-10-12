using System.Globalization;
using System.Text;
using Game.Contracts;

namespace Game.Headless;

/// <summary>
/// Turns a batch of DomainEvents into a human-readable news feed for one viewer.
/// Purely a whitelist: only event types with narrative content produce a line;
/// everything else (bookkeeping deltas, RareEarthPriceChanged, StraitTensionChanged,
/// TurnStarted, ElectionClockTicked, ...) is silently skipped by falling through the
/// switch expression's default arm. No timestamps, no non-deterministic ordering —
/// events are rendered in the order they were supplied.
/// </summary>
public static class NewsRenderer
{
    /// <summary>
    /// Renders every event in <paramref name="events"/> that both (a) is observable by
    /// <paramref name="viewer"/> per <see cref="Visibility.CanObserve"/> and (b) is
    /// newsworthy, as one line each, newline-separated with no trailing newline.
    /// Returns an empty string when nothing qualifies.
    /// </summary>
    public static string Render(IReadOnlyList<DomainEvent> events, CountryId viewer)
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var e in events)
        {
            if (!e.Visibility.CanObserve(viewer)) continue;

            var line = Describe(e, viewer);
            if (line is null) continue;

            if (!first) sb.Append('\n');
            first = false;
            sb.Append("T").Append(e.Turn.ToString(CultureInfo.InvariantCulture)).Append(" BREAKING: ").Append(line);
        }

        return sb.ToString();
    }

    private static string? Describe(DomainEvent e, CountryId viewer) => e switch
    {
        ExportRestrictionSet x =>
            $"{x.Exporter} restricts rare-earth exports to {x.Importer} ({Pct(x.Level)}) — \"{x.Statement}\"",

        SanctionLevelSet x =>
            $"{x.Source} sets sanctions on {x.Target} to {Pct(x.Level)} — \"{x.Statement}\"",

        TradeFlowChanged x =>
            $"{x.Exporter} adjusts trade flow to {x.Importer} ({Signed(x.Delta)}) — \"{x.Statement}\"",

        PactSigned x =>
            $"{x.A} and {x.B} sign a pact — \"{x.Statement}\"",

        PactBroken x =>
            $"{x.A} breaks its pact with {x.B} — \"{x.Statement}\"",

        SubsidyEnacted x =>
            $"{x.Actor} enacts subsidies (effective {Pct(x.Effective)}) — \"{x.Statement}\"",

        ThreatIssued x =>
            $"{x.Actor} threatens {x.Target} — \"{x.Statement}\"",

        ReassuranceGiven x =>
            $"{x.Actor} reassures {x.Target} — \"{x.Statement}\"",

        ForcesDeployedNotice x =>
            $"{x.Actor} deploys forces near {x.Target} — \"{x.Statement}\"",

        ShortageStarted x =>
            $"Shortage begins in {x.Country} (severity {Pct(x.Severity)})",

        ShortageEnded x =>
            $"Shortage ends in {x.Country}",

        StraitIncident =>
            "Incident reported in the Strait",

        SpySucceeded x when viewer == x.Actor =>
            $"{x.Actor}'s covert operation against {x.Target} succeeds",

        SpyCaught x =>
            $"{x.Actor}'s agent is caught spying on {x.Target}",

        ElectionHeld x =>
            $"Election held in {x.Country} — incumbent {(x.IncumbentRetained ? "retained" : "ousted")}",

        GovernmentChanged x =>
            $"{x.Country} installs a new government ({x.NewTemplateId})",

        IntentRejected x when x.IntentActor == viewer =>
            $"{x.IntentActor}'s {x.Type} was rejected — {x.Reason}",

        _ => null,
    };

    // "P0" under InvariantCulture inserts a space before the '%' (e.g. "60 %"); build the
    // string manually so news reads "60%" as in the worked example.
    private static string Pct(double value) => $"{(value * 100).ToString("0", CultureInfo.InvariantCulture)}%";

    private static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
}
