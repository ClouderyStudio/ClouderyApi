using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Zhuxs;

public class Sharable
{
    public required string Question { get; set; }
    public required string Answer { get; set; }
}

public class Application
{
    public required string Id { get; set; }
    public required bool Passed { get; set; }

    [Column(TypeName = "json")] public List<Sharable>? Sharables { get; set; }
}