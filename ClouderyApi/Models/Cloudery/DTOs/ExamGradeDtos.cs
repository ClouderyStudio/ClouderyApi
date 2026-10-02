using System.Text.Json;

namespace ClouderyApi.Models.Cloudery.DTOs;

/// <summary>判分请求：answers 以 "s-q" 为键，值为 string（单选/判断/简答）或 string[]（多选）</summary>
public class GradeRequest
{
    public Dictionary<string, JsonElement> Answers { get; set; } = new();
}

public class ExamGradeItem
{
    public required string Key { get; set; }
    public string? Type { get; set; }
    public bool Correct { get; set; }
    public bool Answered { get; set; }
    public double Points { get; set; }
    public double Earned { get; set; }
    public string? StandardAnswer { get; set; }
    public string? Note { get; set; }
}

public class ExamGradeResult
{
    public int TotalCount { get; set; }
    public int ScorableCount { get; set; }
    public int EssayCount { get; set; }
    public int CorrectCount { get; set; }
    public double TotalPoints { get; set; }
    public double Earned { get; set; }
    public int Accuracy { get; set; }
    public List<ExamGradeItem>? Results { get; set; }
}
