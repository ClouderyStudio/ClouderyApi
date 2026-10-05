using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Zhuxs.Domain;

public class TermInfo
{
    public required string Name { get; set; }
    public required string From { get; set; }
    public string? To { get; set; }
    public required string Version { get; set; }
    public required int Modcount { get; set; }
    public required int Playercount { get; set; }
}

public class TermFile
{
    public required string Filename { get; set; }
    public required float Size { get; set; }
    public required string Unit { get; set; }
}

public class Term
{
    public required string Id { get; set; }
    public required string RecordDate { get; set; }
    public required string Description { get; set; }

    [Column(TypeName = "json")] public required TermInfo Information { get; set; }

    [Column(TypeName = "json")] public List<TermFile>? Files { get; set; }

    /// <summary>新增一条进度：主键由服务端生成，客户端不可指定。</summary>
    public static Term Create(string recordDate, string description, TermInfo information, List<TermFile>? files) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        RecordDate = recordDate,
        Description = description,
        Information = information,
        Files = files,
    };
}