using Hleb.Dto;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class Session
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        public string AuthToken { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
