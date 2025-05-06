namespace Hleb.Dto
{
    public class DeliveryInfoDto
    {
        public string ProductName { get; set; }
        public ClientDeliveryInfo Current { get; set; }
        public ClientDeliveryInfo Next { get; set; }
        public ClientDeliveryInfo Previous { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int TotalPlanned { get; set; }
        public int TotalRemaining { get; set; }

    }

    public class ClientDeliveryInfo
    {
        public string ClientName { get; set; }
        public string ClientCode { get; set; }
        public int QuantityToShip { get; set; }
    }

    public class BackNext
    {
        public int workerId { get; set; }
        public int page { get; set; }
        public int fileId { get; set; }
    }
}
