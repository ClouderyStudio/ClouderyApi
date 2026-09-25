using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

public class AssessmentIn
{
    [JsonPropertyName("assessment_type")] public string AssessmentType { get; set; } = string.Empty;

    [JsonPropertyName("answers")] public Dictionary<string, int> Answers { get; set; } = new();

    [JsonPropertyName("free_text")] public string FreeText { get; set; } = string.Empty;

    [JsonPropertyName("save_to_cloud")] public bool SaveToCloud { get; set; }
}

public class AssessmentOut
{
    public int? Id { get; set; }

    public string AssessmentType { get; set; } = string.Empty;

    public int? Score { get; set; }

    public string Level { get; set; } = string.Empty;

    public string LevelCode { get; set; } = string.Empty;

    public bool Crisis { get; set; }

    public string AiResult { get; set; } = string.Empty;

    public bool SavedCloud { get; set; }

    public DateTime? CreatedAt { get; set; }
}
