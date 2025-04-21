using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class Routes
    {
        [Key]
        public int Id { get; set; }

        public string RouteCode { get; set; }

        public string Name { get; set; }

        public ICollection<Delivery> Deliveries { get; set; }
    }
}
