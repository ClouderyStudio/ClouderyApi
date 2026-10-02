using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Cloudery.Domain;

public class Social
{
    public required string Type { get; set; }
    public required string Link { get; set; }
}

public class Member
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Position { get; set; }
    public string? Description { get; set; }

    [Column(TypeName = "Json")] public List<Social>? Socials { get; set; }
}