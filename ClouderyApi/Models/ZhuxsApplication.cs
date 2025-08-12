using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models
{
    public class Sharable
    {
        public required string Question { get; set; }
        public required string Answer { get; set; }
    }
    public class ZhuxsApplication
    {
        public required string Id { get; set; }
        public required bool Passed { get; set; }
        [Column(TypeName = "json")]
        public List<Sharable>? Sharables { get; set; }
    }
}
