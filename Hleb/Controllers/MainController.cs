 using ClosedXML.Excel;
using DotNetEnv;
using Hleb.Classes;
using Hleb.Database;
using Hleb.Dto;
using Hleb.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sprache;
using System.Globalization;

namespace Hleb.Controllers
{
    [ApiController]
    [Route("api")]
    public class MainController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MainController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] Login dto)
        {
            var envPassword = Env.GetString("SECRET_TOKEN");

            if (dto.Password != envPassword)
            {
                return Ok(new { message = "Неверный пароль!", status = false });
            }

            var token = GenerateSimpleToken();
            var deviceId = GenerateSimpleToken(16);

            await _context.Sessions.AddAsync(new Session
            {
                AuthToken = token,
            });

            await _context.SaveChangesAsync();

            return Ok(new { message = "Авторизация успешно выполнена!", token, status = true });
        }

        [HttpPost("login_token")]
        public async Task<IActionResult> LoginToken([FromHeader(Name = "session-token")] string token)
        {
            var envPassword = Env.GetString("SECRET_TOKEN");

            if (token != envPassword)
            {
                return Ok(new { message = "Неверный пароль!", status = false });
            }

            return Ok(new { message = "Авторизация успешно выполнена!", token, status = true });
        }

        private string GenerateSimpleToken(int length = 64)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HttpPost("import_excel")]
        //[CheckSession]
        public async Task<IActionResult> ImportExcel([FromForm] ImportExcel dto)
        {
            if (dto.file == null || dto.file.Length == 0)
                return BadRequest("Файл не выбран");

            var selectedDate = dto.date.Date == default ? DateTime.Now : dto.date.Date;

            var deliveries = new List<Delivery>();
            var emptyRows = new List<int>();      // Строки с пустыми данными
            var errorRows = new List<string>();   // Строки с исключениями

            var uploadedFile = new UploadedFile
            {
                FileName = dto.file.FileName,
                UploadDate = selectedDate
            };
            _context.UploadedFiles.Add(uploadedFile);
            await _context.SaveChangesAsync();


            using (var stream = new MemoryStream())
            {
                await dto.file.CopyToAsync(stream);
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    int rowNumber = row.WorksheetRow().RowNumber();

                    try
                    {
                        // Проверка на пустые ячейки
                        if (Enumerable.Range(1, 12).Any(i => string.IsNullOrWhiteSpace(row.Cell(i).GetValue<string>())))
                        {
                            emptyRows.Add(rowNumber);
                            continue;
                        }

                        int article = int.Parse(row.Cell(1).GetValue<string>());
                        string barcode = row.Cell(2).GetValue<string>();
                        string productName = row.Cell(3).GetValue<string>();
                        string unit = row.Cell(4).GetValue<string>();
                        int packingRate = int.Parse(row.Cell(5).GetValue<string>());

                        var product = await _context.Products.FirstOrDefaultAsync(p => p.Article == article);
                        if (product == null)
                        {
                            product = new Product
                            {
                                Article = article,
                                Barcode = barcode,
                                Name = productName,
                                Unit = unit,
                                PackingRate = packingRate
                            };
                            _context.Products.Add(product);
                            await _context.SaveChangesAsync();
                        }

                        string clientName = row.Cell(6).GetValue<string>();
                        string clientCode = row.Cell(7).GetValue<string>();
                        clientCode = string.Concat(clientCode.TrimStart('0').Where(c => !char.IsWhiteSpace(c)));

                        var client = await _context.Clients.FirstOrDefaultAsync(c => c.ClientCode == clientCode);
                        if (client == null)
                        {
                            client = new Client
                            {
                                Name = clientName,
                                ClientCode = clientCode
                            };
                            _context.Clients.Add(client);
                            await _context.SaveChangesAsync();
                        }

                        string routeCode = row.Cell(8).GetValue<string>();
                        string routeName = row.Cell(9).GetValue<string>();

                        var route = await _context.Routes.FirstOrDefaultAsync(r => r.RouteCode == routeCode);
                        if (route == null)
                        {
                            route = new Routes
                            {
                                RouteCode = routeCode,
                                Name = routeName
                            };
                            _context.Routes.Add(route);
                            await _context.SaveChangesAsync();
                        }

                        int quantity = int.Parse(row.Cell(10).GetValue<string>());
                        double weight = double.Parse(row.Cell(11).GetValue<string>().Replace(",", "."), CultureInfo.InvariantCulture);
                        string address = row.Cell(12).GetValue<string>();

                        var delivery = new Delivery
                        {
                            ProductId = product.Id,
                            ClientId = client.Id,
                            RouteId = route.Id,
                            Quantity = quantity,
                            Weight = weight,
                            DeliveryAddress = address,
                            CreateDate = selectedDate,
                            UploadedFileId = uploadedFile.Id
                        };

                        deliveries.Add(delivery);
                    }
                    catch (Exception ex)
                    {
                        errorRows.Add($"Строка {rowNumber}: {ex.Message}");
                        continue;
                    }
                }
            }

            await _context.Deliveries.AddRangeAsync(deliveries);
            await _context.SaveChangesAsync();

            string message = $"Импортировано {deliveries.Count} доставок.";
            if (emptyRows.Count > 0)
                message += $" Пропущено {emptyRows.Count} строк с пустыми ячейками: {string.Join(", ", emptyRows)}.";
            if (errorRows.Count > 0)
                message += $" Ошибки в строках: {string.Join(" | ", errorRows)}.";

            return Ok(new { message, status = true });
        }

        [HttpPost("build_map")]
        public async Task<IActionResult> BuildMap([FromBody] BuildMap dto)
        {
            var selectedDate = dto.date.Date == default ? DateTime.Now.Date : dto.date.Date;

            // Загружаем только нужные поля из Deliveries
            var deliveriesQuery = _context.Deliveries
                .Where(d => d.CreateDate.Date == selectedDate)
                .Select(d => new {
                    d.Id,
                    d.ProductId,
                    ProductName = d.Product.Name,
                    d.ClientId,
                    ClientName = d.Client.Name,
                    d.Client.ClientCode,
                    d.Quantity,
                    d.UploadedFileId
                });

            if (dto.fileId > 0)
                deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == dto.fileId);

            var deliveries = await deliveriesQuery.ToListAsync();

            if (deliveries.Count == 0)
            {
                return Ok(new
                {
                    message = $"Нет данных на дату {selectedDate:dd.MM.yyyy}",
                    status = false,
                    data = new object[0],
                    date = selectedDate
                });
            }

            // Загружаем ShipmentLogs по дате
            var shipmentLogs = await _context.ShipmentLogs
                .Where(s => s.ShipmentDate.Date == selectedDate)
                .Select(s => new {
                    s.DeliveryId,
                    s.QuantityShipped
                })
                .ToListAsync();

            // Группируем отгрузки по DeliveryId
            var shippedDict = shipmentLogs
                .GroupBy(s => s.DeliveryId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityShipped));

            var clientIds = deliveries
                .Select(d => d.ClientId)
                .Distinct()
                .ToList();

            var clients = await _context.Clients
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.ClientCode })
                .OrderBy(c => c.Id)
                .ToListAsync();

            // Формируем сводку по продуктам
            var pivot = deliveries
                .GroupBy(d => new { d.ProductId, d.ProductName })
                .Select(g =>
                {
                    var totalQty = g.Sum(d => d.Quantity);
                    var shipped = g.Sum(d => shippedDict.TryGetValue(d.Id, out var qty) ? qty : 0);
                    var remaining = totalQty - shipped;

                    var clientsList = clients
                        .Select(client =>
                        {
                            var quantity = g
                                .Where(d => d.ClientId == client.Id)
                                .Sum(d => d.Quantity);

                            return new
                            {
                                ClientId = client.Id,
                                Name = client.Name,
                                Code = client.ClientCode,
                                Quantity = quantity
                            };
                        })
                        .OrderBy(c => c.ClientId)
                        .ToList();

                    return new
                    {
                        Product = g.Key.ProductName,
                        Clients = clientsList,
                        Total = totalQty,
                        Shipped = shipped,
                        Remaining = remaining
                    };
                })
                .ToList();

            return Ok(new
            {
                message = "",
                status = true,
                data = pivot,
                date = selectedDate
            });
        }


        [HttpPost("clear_by_document")]
        //[CheckSession]
        public async Task<IActionResult> ClearByDocument([FromBody] Clear dto)
        {
            if (dto.fileId <= 0)
            {
                return Ok(new
                {
                    message = "Некорректный идентификатор документа",
                    status = false
                });
            }
             
            var file = await _context.UploadedFiles.FirstOrDefaultAsync(x => x.Id == dto.fileId);

            if(file == null)
            {
                return Ok(new
                {
                    message = "Такого документа нет!",
                    status = false
                });
            }

            var deliveries = await _context.Deliveries
                .Where(d => d.UploadedFileId == dto.fileId)
                .ToListAsync();

            if (deliveries.Count == 0)
            {
                return Ok(new
                {
                    message = "Нет доставок для удаления по этому документу",
                    status = false
                });
            }

            var deliveryIds = deliveries.Select(d => d.Id).ToList();

            var shipmentLogs = await _context.ShipmentLogs
                .Where(s => deliveryIds.Contains(s.DeliveryId))
                .ToListAsync();

            var clients = await _context.Clients
                .ToListAsync();

            _context.Clients.RemoveRange(clients);

            _context.ShipmentLogs.RemoveRange(shipmentLogs);

            _context.Deliveries.RemoveRange(deliveries);
            _context.UploadedFiles.Remove(file);

            await _context.SaveChangesAsync();
            await _context.Database.ExecuteSqlRawAsync("ALTER TABLE Clients AUTO_INCREMENT = 1;");


            return Ok(new
            {
                message = $"Удалено {deliveries.Count} доставок и {shipmentLogs.Count} логов отгрузки по документу {dto.fileId}",
                status = true
            });
        }

        [HttpPost("get_documents")]
        public async Task<IActionResult> GetUploadedFilesByDate([FromBody] GetDocument dto)
        {
            var selectedDate = dto.date.Date == default ? DateTime.Now : dto.date.Date;

            var files = await _context.UploadedFiles
                .Where(f => f.UploadDate.Date == selectedDate.Date)
                .Select(s => new
                {
                    fileId = s.Id,
                    fileName = s.FileName,
                })
                .ToListAsync();

            if (files.Count == 0)
            {
                return Ok(new
                {
                    message = "",
                    status = false,
                    data = new object[] { }
                });
            }

            return Ok(new
            {
                message = "",
                status = false,
                data = files
            });
        }

        [HttpPost("GetDeliveryInfo")]
        public async Task<IActionResult> GetDeliveryInfo([FromBody] GetDelivery dto)
        {
            var product = _context.Products.FirstOrDefault(p => p.Barcode == dto.barcode);
            if (product == null)
            {
                return Ok(new
                {
                    message = "Продукт не найден",
                    status = false,
                });
            }

            var workerIntId = dto.workerId;

            var unfinished = _context.ShipmentLogs
                .Where(s => s.WorkerId == workerIntId && s.Barcode != dto.barcode && s.Delivery.UploadedFileId == dto.fileId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefault();

            if (unfinished != null && unfinished.Remaining - unfinished.QuantityShipped > 0)
            {
                return Ok(new
                {
                    message = $"Невозможно отсканировать новый товар. Завершите отгрузку предыдущего продукта (штрихкод: {unfinished.Barcode}, клиент: {unfinished.ClientId})",
                    status = false,
                });
            }

            var takenByAnother = _context.ShipmentLogs
                .FirstOrDefault(s => s.Barcode == dto.barcode && s.WorkerId != workerIntId && s.Delivery.UploadedFileId == dto.fileId);

            if (takenByAnother != null)
            {
                return Ok(new
                {
                    message = $"Этот продукт уже собран или собирается другим сборщиком (ID: {takenByAnother.WorkerId})",
                    status = false,
                });
            }

            var deliveriesQuery = _context.Deliveries
                .Where(d => d.ProductId == product.Id && d.CreateDate.Date == dto.date.Date);

            if (dto.fileId > 0)
            {
                deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == dto.fileId);
            }

            var deliveries = deliveriesQuery
                .OrderBy(d => d.ClientId)
                .ToList();

            var grouped = deliveries
                .GroupBy(d => d.ClientId)
                .Select(g =>
                {
                    var client = _context.Clients.FirstOrDefault(c => c.Id == g.Key);

                    var shipped = g.Sum(d => _context.ShipmentLogs
                        .Where(s => s.ClientId == g.Key && s.WorkerId == workerIntId && s.Barcode == dto.barcode && s.Delivery.UploadedFileId == dto.fileId)
                        .Sum(s => (int?)s.QuantityShipped) ?? 0);

                    var totalQty = g.Sum(d => d.Quantity);
                    var remaining = Math.Max(0, totalQty - shipped);

                    return new
                    {
                        ClientId = g.Key,
                        Client = client,
                        TotalQuantity = totalQty,
                        Shipped = shipped,
                        Remaining = remaining
                    };
                })
                .OrderBy(x => x.ClientId)
                .ToList();

            var finish = _context.ShipmentLogs
                .Where(s => s.WorkerId == workerIntId && s.Barcode == dto.barcode && s.Delivery.UploadedFileId == dto.fileId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefault();

            if (finish != null && finish.Remaining - finish.QuantityShipped == 0)
            {
                return Ok(new
                {
                    message = $"Все товары отгружены для продукта {product.Name}",
                    status = false,
                });
            }

            var currentIndex = 0;
            var current = grouped.ElementAtOrDefault(currentIndex);

            if (current == null) {
                return Ok(new
                {
                    message = $"Нет доставок для продукта {product.Name}",
                    status = false,
                });
            }

            var next = grouped.ElementAtOrDefault(currentIndex + 1);
            var previous = grouped.ElementAtOrDefault(currentIndex - 1);

            var totalShipped = grouped.Sum(x => x.Shipped);
            var totalRemaining = grouped.Sum(x => x.Remaining);
            var totalPlanned = grouped.Sum(x => x.TotalQuantity);

            var currentClientId = (int)current.ClientId;

            var shipmentLog = _context.ShipmentLogs
                .FirstOrDefault(s => s.WorkerId == workerIntId
                                     && s.Barcode == dto.barcode
                                     && s.Delivery.UploadedFileId == dto.fileId
                                     && s.ClientId == currentClientId);

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
                    Barcode = dto.barcode,
                    ClientId = currentClientId,
                    QuantityShipped = current.TotalQuantity,
                    ShipmentDate = DateTime.Now,
                    Remaining = totalRemaining,
                    Notes = "Товар сканирован и отгружен",
                    DeliveryId = deliveries.FirstOrDefault(d => d.ClientId == currentClientId)?.Id ?? 0
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
                    quantityToShip = current.TotalQuantity
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
                    quantityToShip = previous.Shipped
                } : null,
                page = currentIndex,
                totalPages = grouped.Count,
                totalPlanned = totalPlanned,
                totalRemaining = shipmentLog.Remaining
            };

            return Ok(new
            {
                message = "",
                status = true,
                data = send,
            });
        }



        [HttpPost("GetCurrentAssignments")]
        public async Task<IActionResult> GetCurrentAssignments([FromBody] GetInfo dto)
        {
            var today = dto.date.Date == default ? DateTime.Now.Date : dto.date.Date;

            var logsQuery = _context.ShipmentLogs
                .Where(s => s.ShipmentDate.Date == today);

            if (dto.fileId > 0)
            {
                logsQuery = logsQuery
                    .Where(s => _context.Deliveries.Any(d => d.Id == s.DeliveryId && d.UploadedFileId == dto.fileId));
            }

            var latestLogs = await logsQuery
                .GroupBy(s => s.WorkerId)
                .Select(g => g.OrderByDescending(x => x.ShipmentDate).FirstOrDefault())
                .ToListAsync();

            // Определяем максимальное количество сборщиков
            var activeWorkerIds = await _context.ShipmentLogs
                .Where(s => s.ShipmentDate.Date == today && s.Delivery.UploadedFileId == dto.fileId)
                .Select(s => s.WorkerId)
                .Distinct()
                .ToListAsync();

            int maxWorkerCount = activeWorkerIds.Any(id => id >= 4) ? 6 : 3;

            if(maxWorkerCount < dto.workerCount)
            {
                maxWorkerCount = dto.workerCount;
            }

            var result = new List<dynamic>();

            foreach (var log in latestLogs)
            {
                var workerId = log.WorkerId;
                var barcode = log.Barcode;

                var product = await _context.Products.FirstOrDefaultAsync(p => p.Barcode == barcode);
                if (product == null)
                    continue;

                var deliveriesQuery = _context.Deliveries
                    .Where(d => d.ProductId == product.Id && d.CreateDate.Date == today);

                if (dto.fileId > 0)
                {
                    deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == dto.fileId);
                }

                var deliveries = await deliveriesQuery
                    .OrderBy(d => d.ClientId)
                    .ToListAsync();

                if (deliveries.Count == 0)
                    continue;

                var grouped = new List<dynamic>();
                var deliveryGroups = deliveries.GroupBy(d => d.ClientId);

                foreach (var g in deliveryGroups)
                {
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == g.Key);
                    var deliveryIds = g.Select(d => d.Id).ToList();

                    var shipped = await _context.ShipmentLogs
                        .Where(s => deliveryIds.Contains(s.DeliveryId) && s.WorkerId == workerId && s.Barcode == barcode && s.Delivery.UploadedFileId == dto.fileId)
                        .SumAsync(s => (int?)s.QuantityShipped) ?? 0;

                    var totalQty = g.Sum(d => d.Quantity);
                    var remaining = Math.Max(0, totalQty - shipped);

                    grouped.Add(new
                    {
                        ClientId = g.Key,
                        Client = client,
                        TotalQuantity = totalQty,
                        Shipped = shipped,
                        Remaining = remaining
                    });
                }

                grouped = grouped.ToList();

                var totalRemaining = grouped.Sum(g => g.Remaining);
                var allClientIds = grouped.Select(g => g.ClientId).ToList();

                var shippedClientIds = await _context.ShipmentLogs
                    .Where(s => allClientIds.Contains(s.ClientId)
                                && s.Delivery.UploadedFileId == dto.fileId
                                && s.Barcode == barcode
                                && s.WorkerId == workerId)
                    .Select(s => s.ClientId)
                    .Distinct()
                    .ToListAsync();

                var allClientsShipped = allClientIds.All(id => shippedClientIds.Contains(id));

                if (allClientsShipped)
                {
                    result.Add(new
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
                    });
                    continue;
                }

                var currentClientId = log.ClientId;
                var currentIndex = grouped.FindIndex(g => g.ClientId == currentClientId);
                if (currentIndex == -1)
                    currentIndex = 0;

                var current = grouped[currentIndex];
                var next = (currentIndex + 1 < grouped.Count) ? grouped[currentIndex + 1] : null;
                var previous = (currentIndex - 1 >= 0) ? grouped[currentIndex - 1] : null;

                var totalPlanned = grouped.Sum(g => g.TotalQuantity);

                var deliveryIdsForProduct = deliveries.Select(d => d.Id).ToList();

                var shipmentLog = await _context.ShipmentLogs
                    .Where(s => s.WorkerId == workerId
                                && s.Barcode == barcode
                                && s.Delivery.UploadedFileId == dto.fileId
                                && deliveryIdsForProduct.Contains(s.DeliveryId)
                                && s.ClientId == currentClientId)
                    .OrderByDescending(s => s.ShipmentDate)
                    .FirstOrDefaultAsync();

                var send = new
                {
                    workerId = workerId,
                    productName = product.Name,
                    current = new
                    {
                        clientName = current.Client?.Name,
                        clientCode = current.Client?.Id,
                        quantityToShip = current.TotalQuantity
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
                        quantityToShip = previous.Shipped
                    } : null,
                    page = currentIndex,
                    totalPages = grouped.Count,
                    totalPlanned = totalPlanned,
                    totalRemaining = shipmentLog?.Remaining ?? (current.TotalQuantity - current.Shipped)
                };

                result.Add(send);
            }

            var existingWorkerIds = result.Select(r => (int)r.workerId).ToHashSet();
            for (int workerId = 1; workerId <= maxWorkerCount; workerId++)
            {
                if (!existingWorkerIds.Contains(workerId))
                {
                    result.Add(new
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
                    });
                }
            }

            result = result.OrderBy(r => (int)r.workerId).ToList();

            return Ok(new
            {
                message = "",
                status = true,
                data = result,
            });
        }
    }
}
