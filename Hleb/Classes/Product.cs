using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class Product
    {
        [Key]
        public int Id { get; set; }

        public int Article { get; set; }

        public string Barcode { get; set; }

        public string Name { get; set; }

        public string Unit { get; set; }

        public int PackingRate { get; set; }

        public ICollection<Delivery> Deliveries { get; set; }
    }
}
