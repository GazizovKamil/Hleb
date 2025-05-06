using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class ShipmentLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DeliveryId { get; set; }
        [Required]
        public int FileId { get; set; }

        [Required]
        public int QuantityShipped { get; set; }

        [Required]
        public DateTime ShipmentDate { get; set; } = DateTime.Now;

        public string Notes { get; set; }
        public int Remaining { get; set; }

        public int WorkerId { get; set; } // Добавлено поле идентификатора сборщика
        public int ClientId { get; set; } // Добавлено поле идентификатора сборщика

        public string Barcode { get; set; } // Сохраняем штрихкод для проверки уникальности
    }
}
