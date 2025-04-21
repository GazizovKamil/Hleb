using Hleb.Classes;
using Hleb.Database;
using Hleb.Dto;
using Microsoft.AspNetCore.SignalR;
using Sprache;

namespace Hleb.SignalR
{
    public class WorkHub : Hub
    {
        private readonly AppDbContext _context;

        public WorkHub(AppDbContext context)
        {
            _context = context;
        }

        public async Task BackNext(string workerId, int page)
        {
            var workerIntId = int.Parse(workerId);

            var lastShipmentLog = _context.ShipmentLogs
                .Where(s => s.WorkerId == workerIntId)
                .OrderByDescending(s => s.ShipmentDate)
                .FirstOrDefault();

            if (lastShipmentLog == null)
            {
                var errorResponse = new { status = false, message = "Не найдены отгрузки для данного сборщика.",
                    isComplete = false,
                    data = new
                    {
                        workerId = workerIntId,
                        productName = "",
                        current = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        next = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        previous = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        page = 0,
                        totalPages = 0,
                        totalPlanned = 0,
                        totalRemaining = 0
                    },
                    workerId = workerIntId
                };
                await Clients.Caller.SendAsync("ReceiveDeliveryInfo", errorResponse);
                return;
            }

            var barcode = lastShipmentLog.Barcode;

            var product = _context.Products.FirstOrDefault(p => p.Barcode == barcode);
            if (product == null)
            {
                var errorResponse = new { status = false, message = "Продукт не найден",
                    isComplete = false,
                    data = new
                    {
                        workerId = workerIntId,
                        productName = "",
                        current = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        next = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        previous = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        page = 0,
                        totalPages = 0,
                        totalPlanned = 0,
                        totalRemaining = 0
                    },
                    workerId = workerIntId
                };
                await Clients.Caller.SendAsync("ReceiveDeliveryInfo", errorResponse);
                return;
            }

            var takenByAnother = _context.ShipmentLogs
                .FirstOrDefault(s => s.Barcode == barcode && s.WorkerId != workerIntId);

            if (takenByAnother != null)
            {
                var errorResponse = new { status = false, message = $"Этот продукт уже собирается другим сборщиком (ID: {takenByAnother.WorkerId})",
                    isComplete = false,
                    data = new
                    {
                        workerId = workerIntId,
                        productName = "",
                        current = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        next = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        previous = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        page = 0,
                        totalPages = 0,
                        totalPlanned = 0,
                        totalRemaining = 0
                    },
                    workerId = workerIntId
                };
                await Clients.Caller.SendAsync("ReceiveDeliveryInfo", errorResponse);
                return;
            }

            var today = DateTime.Today;

            var deliveries = _context.Deliveries
                 .Where(d => d.ProductId == product.Id && d.CreateDate.Date == today)
                 .OrderBy(d => d.ClientId)
                 .ToList();

            var fullGrouped = deliveries
                .GroupBy(d => d.ClientId)
                .Select(g =>
                {
                    var client = _context.Clients.FirstOrDefault(c => c.Id == g.Key);

                    var shipped = g.Sum(d => _context.ShipmentLogs
                        .Where(s => s.ClientId == g.Key)
                        .Sum(s => (int?)s.QuantityShipped) ?? 0);

                    var totalQty = g.Sum(d => d.Quantity);
                    var remaining = totalQty - shipped;

                    return new
                    {
                        ClientId = g.Key,
                        Client = client,
                        TotalQuantity = totalQty,
                        Shipped = shipped,
                        Remaining = remaining,
                    };
                })
                .OrderBy(g => g.ClientId)
                .ToList();

            var totalPages = fullGrouped.Count;
            var allClientIds = fullGrouped.Select(g => g.ClientId).ToList();

            var shippedClientIds = _context.ShipmentLogs
                .Where(s => allClientIds.Contains(s.ClientId) && s.ShipmentDate.Date == today && s.Barcode == barcode)
                .Select(s => s.ClientId)
                .Distinct()
                .ToList();

            var allClientsShipped = allClientIds.All(id => shippedClientIds.Contains(id));

            if (allClientsShipped)
            {
                var confirmResponse = new
                {
                    status = false,
                    isComplete = true,
                    workerId = workerIntId,
                    data = new
                    {
                        workerId = workerIntId,
                        productName = "",
                        current = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        next = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        previous = new
                        {
                            clientName = "",
                            clientCode = "",
                            quantityToShip = 0
                        },
                        page = 0,
                        totalPages = 0,
                        totalPlanned = 0,
                        totalRemaining = 0
                    },
                    message = $"Все товары отгружены для продукта {product.Name}"
                };
                await Clients.Caller.SendAsync("ReceiveDeliveryInfo", confirmResponse);
                return;
            }


            if (page < 0 || page >= totalPages)
                page = 0;

            var current = fullGrouped.Skip(page).First();
            var next = (page + 1 < totalPages) ? fullGrouped[page + 1] : null;
            var previous = (page - 1 >= 0) ? fullGrouped[page - 1] : null;

            var totalShippedBefore = fullGrouped
                .Take(page)
                .Sum(g => g.Shipped);

            var totalPlanned = fullGrouped.Sum(g => g.TotalQuantity);
            var totalShipped = fullGrouped.Sum(g => g.Shipped);
            var totalRemaining = totalPlanned - fullGrouped
            .Skip(page) 
            .Sum(g => g.Shipped);


            var shipmentLog = _context.ShipmentLogs
                .FirstOrDefault(s => s.WorkerId == workerIntId && s.Barcode == barcode && s.ShipmentDate.Date == DateTime.Now.Date && s.ClientId == current.ClientId);

            if (shipmentLog != null)
            {
                //shipmentLog.QuantityShipped = current.Remaining;
                shipmentLog.ShipmentDate = DateTime.Now;
            }
            else
            {
                shipmentLog = new ShipmentLog
                {
                    WorkerId = workerIntId,
                    Barcode = barcode,
                    ClientId = current.ClientId,
                    QuantityShipped = current.TotalQuantity,
                    ShipmentDate = DateTime.Now,
                    Notes = "Товар сканирован и отгружен",
                    DeliveryId = deliveries.FirstOrDefault().Id
                };

                _context.ShipmentLogs.Add(shipmentLog);
            }

            await _context.SaveChangesAsync();

            var send = new 
            {
                workerId = workerIntId,
                productName = product.Name,
                current = new 
                {
                    clientName = current.Client?.Name,
                    clientCode = current.Client?.ClientCode,
                    quantityToShip = current.TotalQuantity,
                },
                next = next != null ? new 
                {
                    clientName = next.Client?.Name,
                    clientCode = next.Client?.ClientCode,
                    quantityToShip = next.TotalQuantity
                } : null,
                previous = previous != null ? new 
                {
                    clientName = previous.Client?.Name,
                    clientCode = previous.Client?.ClientCode,
                    quantityToShip = previous.Shipped
                } : null,
                page = page,
                totalPages = totalPages,
                totalPlanned = totalPlanned,
                totalRemaining = totalRemaining
            };

            var message = new
            {
                message = "",
                status = true,
                workerId = workerIntId,
                isComplete = false,
                data = send,
            };

            await Clients.Caller.SendAsync("ReceiveDeliveryInfo", message);
        }
    }
}
