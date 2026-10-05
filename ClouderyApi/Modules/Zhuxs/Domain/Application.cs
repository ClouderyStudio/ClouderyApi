using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Zhuxs.Domain;

public class Sharable
{
    public required string Question { get; set; }
    public required string Answer { get; set; }
}

public class Application
{
    public required string Id { get; set; }
    public required bool Passed { get; set; }
    public required DateTime SubmissionDate { get; set; }
    [Column(TypeName = "json")] public List<Sharable>? Sharables { get; set; }

    /// <summary>新增一条报名：主键与提交时间由服务端生成，Passed 恒为 false（需管理员 PUT 审核通过，防 over-posting 绕过审核）。</summary>
    public static Application Create(List<Sharable>? sharables) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Passed = false,
        SubmissionDate = DateTime.UtcNow,
        Sharables = sharables,
    };
}