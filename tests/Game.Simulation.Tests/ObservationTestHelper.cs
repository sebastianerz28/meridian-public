using Game.Contracts;
using Game.Simulation.State;

namespace Game.Simulation.Tests;

/// <summary>
/// Parses an ObservationProperties-formatted property key back into the ground-truth
/// value it describes, for test assertions only.
/// </summary>
public static class ObservationTestHelper
{
    public static double TruthFor(WorldState w, string property)
    {
        var arrow = property.IndexOf("->", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            var dot = property.IndexOf('.', arrow);
            var src = Enum.Parse<CountryId>(property[..arrow]);
            var dst = Enum.Parse<CountryId>(property[(arrow + 2)..dot]);
            var kind = property[(dot + 1)..];
            var rel = w.Rel(src, dst);
            return kind switch
            {
                "Trust" => rel.Trust,
                "Threat" => rel.Threat,
                _ => throw new ArgumentException($"Unknown relation property '{property}'"),
            };
        }

        var parts = property.Split('.');
        var country = Enum.Parse<CountryId>(parts[0]);
        var cs = w.Countries[country];
        return (parts[1], parts[2]) switch
        {
            ("Economy", "Gdp") => cs.Gdp,
            ("Economy", "Inflation") => cs.Inflation,
            ("Economy", "Treasury") => cs.Treasury,
            ("Resource", "Production") => cs.ResourceProduction,
            ("Resource", "Dependence") => cs.ResourceDependence,
            ("Military", "Strength") => cs.MilitaryStrength,
            ("Military", "Readiness") => cs.Readiness,
            ("Military", "ForcesInStrait") => cs.ForcesInStrait,
            ("Politics", "Approval") => cs.Approval,
            _ => throw new ArgumentException($"Unknown property '{property}'"),
        };
    }
}
