using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Zhuxs
{
    [Keyless]
    public class TermInfo
    {
        public required string Name { get; set; }
        public required string From { get; set; }
        public string? To { get; set; }
        public required string Version { get; set; }
        public required int Modcount { get; set; }
        public required int Playercount { get; set; }
    }
    [Keyless]
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
        [Column(TypeName = "json")]
        public required TermInfo Information { get; set; }
        [Column(TypeName = "json")]
        public List<TermFile>? Files { get; set; }
    }
}
