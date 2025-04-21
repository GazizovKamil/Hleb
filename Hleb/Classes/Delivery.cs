using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class Delivery
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public Product Product { get; set; }

        [Required]
        public int ClientId { get; set; }

        [ForeignKey("ClientId")]
        public Client Client { get; set; }

        [Required]
        public int RouteId { get; set; }

        [ForeignKey("RouteId")]
        public Routes Route { get; set; }

        public int Quantity { get; set; }

        public double Weight { get; set; }

        public string DeliveryAddress { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.Now;

        public virtual ICollection<ShipmentLog> ShipmentLogs { get; set; }

    }
}
