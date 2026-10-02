using System.Text.Json;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;

namespace ClouderyApi.UseCases.Cloudery.Domain;

/// <summary>
/// 试卷判分领域逻辑（纯函数，无 I/O）。逻辑自 ExamPapersController.Grade 原样迁出，
/// 算法与逐字行为保持不变：题型缺省按有无 Options 推断、多选比较忽略顺序、
/// 简答题只计数不计分、必答题未作答一律算错。
/// </summary>
public static class ExamPaperGrader
{
    /// <summary>逐题判分；不修改入参 <paramref name="paper"/>。</summary>
    public static ExamGradeResult Grade(ExamPaper paper, GradeRequest request)
    {
        var results = new List<ExamGradeItem>();
        int scorable = 0, essay = 0, correctCount = 0;
        double totalPoints = 0, earned = 0;

        for (int s = 0; s < paper.Sections.Count; s++)
        {
            var section = paper.Sections[s];
            for (int q = 0; q < section.Questions.Count; q++)
            {
                var question = section.Questions[q];
                var key = $"{s}-{q}";
                var type = question.Type ?? (question.Options != null ? "single" : "judge");
                var points = section.PointsPerQuestion ?? 1.0;
                totalPoints += points;

                bool answered = request.Answers.TryGetValue(key, out var raw) && raw.ValueKind != JsonValueKind.Null;
                string[]? userMulti = null;
                string? userSingle = null;
                if (answered)
                {
                    if (raw.ValueKind == JsonValueKind.Array)
                        userMulti = raw.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    else if (raw.ValueKind == JsonValueKind.String)
                        userSingle = raw.GetString();
                    else
                        answered = false;
                }

                bool correct = false;
                if (type == "essay")
                {
                    essay++;
                }
                else
                {
                    scorable++;
                    if (type == "multiple")
                    {
                        var std = string.Concat(question.Answer.OrderBy(c => c));
                        var userArr = userMulti ?? (userSingle != null ? new[] { userSingle } : Array.Empty<string>());
                        var user = string.Concat(userArr.OrderBy(x => x));
                        correct = answered && user == std;
                    }
                    else
                    {
                        correct = answered && userSingle != null && userSingle.Trim() == question.Answer.Trim();
                    }
                }

                if (correct) { correctCount++; earned += points; }
                results.Add(new ExamGradeItem
                {
                    Key = key,
                    Type = type,
                    Correct = correct,
                    Answered = answered,
                    Points = points,
                    Earned = correct ? points : 0,
                    StandardAnswer = question.Answer,
                    Note = question.Note,
                });
            }
        }

        var accuracy = scorable == 0 ? 0 : (int)Math.Round((double)correctCount / scorable * 100);

        return new ExamGradeResult
        {
            TotalCount = paper.Sections.Sum(x => x.Questions.Count),
            ScorableCount = scorable,
            EssayCount = essay,
            CorrectCount = correctCount,
            TotalPoints = totalPoints,
            Earned = earned,
            Accuracy = accuracy,
            Results = results,
        };
    }
}
