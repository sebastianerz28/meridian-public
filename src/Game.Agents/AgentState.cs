using Game.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Agents;

public sealed class AgentState
{
    public required CountryId Country { get; init; }
    public List<string> Memories { get; } = new();
    public string GovernmentTemplateId { get; private set; } = "";
    public string ObjectivesText { get; private set; } = "";
    public string PersonaText { get; private set; } = "";
    public List<string> PoliticalBeliefs { get; } = new();
    public string LatestAssessment { get; set; } = "";

    public static AgentState FromTemplate(CountryId country, string templateId)
    {
        var template = GovernmentTemplates.Get(templateId);

        var state = new AgentState
        {
            Country = country,
            GovernmentTemplateId = templateId,
            ObjectivesText = template.ObjectivesText,
            PersonaText = template.PersonaText,
        };

        // Seed PoliticalBeliefs from InitialBeliefsText, one belief per non-empty line
        var lines = template.InitialBeliefsText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                state.PoliticalBeliefs.Add(trimmed);
            }
        }

        return state;
    }

    public void SwapGovernment(string newTemplateId)
    {
        var template = GovernmentTemplates.Get(newTemplateId);

        GovernmentTemplateId = newTemplateId;
        ObjectivesText = template.ObjectivesText;
        PersonaText = template.PersonaText;
        LatestAssessment = "";

        // Clear and repopulate PoliticalBeliefs
        PoliticalBeliefs.Clear();
        var lines = template.InitialBeliefsText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                PoliticalBeliefs.Add(trimmed);
            }
        }

        // Memories are intentionally kept
    }

    public string ToJson()
    {
        var dto = new AgentStateDto
        {
            Country = Country,
            GovernmentTemplateId = GovernmentTemplateId,
            ObjectivesText = ObjectivesText,
            PersonaText = PersonaText,
            Memories = new List<string>(Memories),
            PoliticalBeliefs = new List<string>(PoliticalBeliefs),
            LatestAssessment = LatestAssessment,
        };

        return JsonSerializer.Serialize(dto);
    }

    public static AgentState FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<AgentStateDto>(json)
            ?? throw new InvalidOperationException("Failed to deserialize AgentState from JSON.");

        var state = new AgentState
        {
            Country = dto.Country,
            GovernmentTemplateId = dto.GovernmentTemplateId,
            ObjectivesText = dto.ObjectivesText,
            PersonaText = dto.PersonaText,
            LatestAssessment = dto.LatestAssessment,
        };

        state.Memories.AddRange(dto.Memories);
        state.PoliticalBeliefs.AddRange(dto.PoliticalBeliefs);

        return state;
    }

    // Internal DTO for JSON serialization
    private sealed class AgentStateDto
    {
        public CountryId Country { get; set; }
        public string GovernmentTemplateId { get; set; } = "";
        public string ObjectivesText { get; set; } = "";
        public string PersonaText { get; set; } = "";
        public List<string> Memories { get; set; } = new();
        public List<string> PoliticalBeliefs { get; set; } = new();
        public string LatestAssessment { get; set; } = "";
    }
}
