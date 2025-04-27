using Hleb.Classes;
using Hleb.Database;
using Hleb.Dto;
using Microsoft.AspNetCore.SignalR;
using Sprache;
using System.Text.Json;

namespace Hleb.SignalR
{
    public class WorkHub : Hub
    {
        private readonly AppDbContext _context;

        public WorkHub(AppDbContext context)
        {
            _context = context;
        }

        public async Task BackNext(string workerId, int page, DateTime date, int uploadedFileId)
        {
            var workerIntId = int.Parse(workerId);
            var selectedDate = date.Date == default ? DateTime.Now : date.Date;

            var lastShipmentLog = _context.ShipmentLogs
                .Where(s => s.WorkerId == workerIntId)
                .OrderByDescending(s => s.ShipmentDate)
                .FirstOrDefault();

            if (lastShipmentLog == null)
            {
                await SendEmptyResponse(workerIntId, "Не найдены отгрузки для данного сборщика.");
                return;
            }

            var barcode = lastShipmentLog.Barcode;
            var product = _context.Products.FirstOrDefault(p => p.Barcode == barcode);
            if (product == null)
            {
                await SendEmptyResponse(workerIntId, "Продукт не найден");
                return;
            }

            var takenByAnother = _context.ShipmentLogs
                .FirstOrDefault(s => s.Barcode == barcode && s.WorkerId != workerIntId);

            if (takenByAnother != null)
            {
                await SendEmptyResponse(workerIntId, $"Этот продукт уже собирается другим сборщиком (ID: {takenByAnother.WorkerId})");
                return;
            }

            var today = selectedDate.Date;

            var deliveriesQuery = _context.Deliveries
                .Where(d => d.ProductId == product.Id && d.CreateDate.Date == today);

            if (uploadedFileId > 0)
                deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == uploadedFileId);

            var deliveries = deliveriesQuery
                .OrderBy(d => d.ClientId)
                .ToList();

            var fullGrouped = deliveries
                .GroupBy(d => d.ClientId)
                .Select(g =>
                {
                    var client = _context.Clients.FirstOrDefault(c => c.Id == g.Key);

                    var shipped = g.Sum(d => _context.ShipmentLogs
                        .Where(s => s.ClientId == g.Key && s.WorkerId == workerIntId && s.Barcode == barcode)
                        .Sum(s => (int?)s.QuantityShipped) ?? 0);

                    var totalQty = g.Sum(d => d.Quantity);
                    var remaining = Math.Max(0, totalQty - shipped);

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
                .Where(s => allClientIds.Contains(s.ClientId) && s.ShipmentDate.Date == today && s.Barcode == barcode && s.WorkerId == workerIntId)
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
                    data = EmptyData(workerIntId),
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

            var totalPlanned = fullGrouped.Sum(g => g.TotalQuantity);
            var totalRemaining = fullGrouped.Sum(g => g.Remaining);

            var shipmentLog = _context.ShipmentLogs
                .FirstOrDefault(s => s.WorkerId == workerIntId && s.Barcode == barcode && s.ShipmentDate.Date == today && s.ClientId == current.ClientId);

            if (shipmentLog != null)
            {
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
                    Remaining = totalRemaining,
                    Notes = "Товар сканирован и отгружен",
                    DeliveryId = deliveries.FirstOrDefault(d => d.ClientId == current.ClientId)?.Id ?? 0
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
                    clientCode = current.Client?.Id,
                    quantityToShip = current.TotalQuantity,
                },
                next = next != null ? new
                {
                    clientName = next.Client?.Name,
                    clientCode = next.Client?.Id,
                    quantityToShip = next.TotalQuantity
                } : null,
                previous = previous != null ? new
                {
                    clientName = previous.Client?.Name,
                    clientCode = previous.Client?.Id,
                    quantityToShip = previous.TotalQuantity
                } : null,
                page = page,
                totalPages = totalPages,
                totalPlanned = totalPlanned,
                totalRemaining = shipmentLog.Remaining
            };

            var message = new
            {
                message = "",
                status = true,
                workerId = workerIntId,
                isComplete = false,
                data = send,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string prettyJson = JsonSerializer.Serialize(message, options);
            Console.WriteLine(prettyJson);
            await Clients.Caller.SendAsync("ReceiveDeliveryInfo", message);
        }

        // Вспомогательные методы для чистоты:
        private async Task SendEmptyResponse(int workerId, string errorMessage)
        {
            var errorResponse = new
            {
                status = false,
                message = errorMessage,
                isComplete = false,
                data = EmptyData(workerId),
                workerId = workerId
            };
            await Clients.Caller.SendAsync("ReceiveDeliveryInfo", errorResponse);
        }

        private object EmptyData(int workerId)
        {
            return new
            {
                workerId = workerId,
                productName = "",
                current = new { clientName = "", clientCode = "", quantityToShip = 0 },
                next = new { clientName = "", clientCode = "", quantityToShip = 0 },
                previous = new { clientName = "", clientCode = "", quantityToShip = 0 },
                page = 0,
                totalPages = 0,
                totalPlanned = 0,
                totalRemaining = 0
            };
        }
    }
}
