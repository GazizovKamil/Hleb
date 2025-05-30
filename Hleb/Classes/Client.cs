﻿using System.ComponentModel.DataAnnotations;

namespace Hleb.Classes
{
    public class Client
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public string ClientCode { get; set; }
        public string RouteCode { get; set; }
        public string DeliveryAddress { get; set; }

        public ICollection<Delivery> Deliveries { get; set; }
    }
}
