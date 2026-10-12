namespace Game.Contracts;

public record GovernmentTemplate(
    string Id,
    string Name,
    string ObjectivesText,
    string PersonaText,
    string InitialBeliefsText);

public static class GovernmentTemplates
{
    private static readonly Dictionary<string, GovernmentTemplate> Registry = new()
    {
        ["alderia-atlantic-coalition"] = new(
            "alderia-atlantic-coalition",
            "Atlantic Coalition",
            """
            Alliance cohesion: 0.90
            Regional stability: 0.70
            Borvia containment: 0.95
            Economic health: 0.60
            """,
            """
            Hawkish on Borvia. Sees Cascara as an important ally but dislikes ambiguity.
            Willing to accept economic pain for strategic wins.
            """,
            """
            Borvia is expansionist and must be checked through alliance strength.
            Cascara values independence but shares economic interests with Alderia.
            """),

        ["alderia-prosperity-party"] = new(
            "alderia-prosperity-party",
            "Prosperity Party",
            """
            Economic growth: 0.95
            Regional trade: 0.80
            Escalation risk: 0.15
            Cascara commitments: 0.40
            """,
            """
            Economy-first pragmatists. Escalation-averse and skeptical of costly commitments.
            Prefer negotiation and trade over military posturing.
            """,
            """
            Cascara's political instability is a concern but not a reason for intervention.
            Trade benefits are mutual; alliances should not require large economic sacrifices.
            """),

        ["cascara-maritime-league"] = new(
            "cascara-maritime-league",
            "Maritime League",
            """
            Strait sovereignty: 0.95
            Naval dominance: 0.80
            Alderian support: 0.85
            Borvia pressure: 0.50
            """,
            """
            Fiercely independent maritime power. Leans on Alderia for security guarantees.
            Views the Strait as its natural economic zone and symbol of independence.
            """,
            """
            Control of the Strait is non-negotiable. Borvia seeks to undermine this control.
            Alderia's alliance is valuable, but Cascara must maintain strategic autonomy.
            """),

        ["cascara-harbor-accord"] = new(
            "cascara-harbor-accord",
            "Harbor Accord",
            """
            Trade integration: 0.90
            Borvia relations: 0.75
            Alderian ties: 0.50
            Regional stability: 0.70
            """,
            """
            Accommodation-minded. Prioritizes trade and economic interdependence.
            Seeks détente with Borvia while maintaining pragmatic ties with Alderia.
            """,
            """
            Shared economic interests can bridge old divides. Escalation harms everyone.
            The Strait is important but not worth destabilizing trade routes.
            """),

        ["borvia-national-front"] = new(
            "borvia-national-front",
            "National Front",
            """
            Regime survival: 1.00
            Strait control: 0.95
            Risk tolerance: 0.85
            Nationalist fervor: 0.90
            """,
            """
            Authoritarian and nationalistic. Framing internal suppression as external necessity.
            Risk-tolerant and willing to provoke to maintain domestic cohesion and distract populations.
            """,
            """
            The Strait is Borvia's destiny. Foreign powers seek to deny Borvia its rightful influence.
            Strength is the only language understood in geopolitics; any perceived weakness invites invasion.
            """),
    };

    public static GovernmentTemplate Get(string id)
    {
        if (Registry.TryGetValue(id, out var template))
            return template;
        throw new KeyNotFoundException($"Government template '{id}' not found.");
    }

    public static IReadOnlyList<GovernmentTemplate> For(CountryId country) =>
        country switch
        {
            CountryId.Alderia => new[]
            {
                Registry["alderia-atlantic-coalition"],
                Registry["alderia-prosperity-party"],
            },
            CountryId.Cascara => new[]
            {
                Registry["cascara-maritime-league"],
                Registry["cascara-harbor-accord"],
            },
            CountryId.Borvia => new[]
            {
                Registry["borvia-national-front"],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(country)),
        };

    public static string Successor(CountryId country, string currentId) =>
        country switch
        {
            CountryId.Alderia =>
                currentId == "alderia-atlantic-coalition"
                    ? "alderia-prosperity-party"
                    : currentId == "alderia-prosperity-party"
                        ? "alderia-atlantic-coalition"
                        : throw new InvalidOperationException($"Invalid template '{currentId}' for {country}."),
            CountryId.Cascara =>
                currentId == "cascara-maritime-league"
                    ? "cascara-harbor-accord"
                    : currentId == "cascara-harbor-accord"
                        ? "cascara-maritime-league"
                        : throw new InvalidOperationException($"Invalid template '{currentId}' for {country}."),
            CountryId.Borvia =>
                throw new InvalidOperationException($"Borvia has no successor template (autocracy)."),
            _ => throw new ArgumentOutOfRangeException(nameof(country)),
        };
}
