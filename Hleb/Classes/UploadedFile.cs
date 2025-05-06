using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class UploadedFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FileName { get; set; }

        public ICollection<Delivery> Deliveries { get; set; }
    }
}
