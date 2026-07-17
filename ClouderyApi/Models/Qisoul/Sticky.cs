using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Qisoul
{
    public class Sticky
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [MaxLength(500)]
        public string Content { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Icon { get; set; }

        [MaxLength(50)]
        public string? Color { get; set; } // 颜色值

        public int Likes { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }
    }

}
