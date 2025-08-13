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
using System.Text.Json;

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
        public async Task<IActionResult> ImportExcel([FromForm] ImportExcel dto)
        {
            if (dto.file == null || dto.file.Length == 0)
                return BadRequest("Файл не выбран");

            var deliveries = new List<Delivery>();
            var emptyRows = new List<int>();
            var errorRows = new List<string>();

            var uploadedFile = new UploadedFile
            {
                FileName = dto.file.FileName,
            };
            _context.UploadedFiles.Add(uploadedFile);
            await _context.SaveChangesAsync();

            using (var stream = new MemoryStream())
            {
                await dto.file.CopyToAsync(stream);
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1);

                // Сортируем строки по RouteCode
                var sortedRows = rows.OrderBy(row => row.Cell(8).GetValue<string>()).ToList();

                foreach (var row in sortedRows)
                {
                    int rowNumber = row.WorksheetRow().RowNumber();

                    try
                    {
                        if (Enumerable.Range(1, 12).Any(i => string.IsNullOrWhiteSpace(row.Cell(i).GetValue<string>())))
                        {
                            emptyRows.Add(rowNumber);
                            continue;
                        }

                        string article = row.Cell(1).GetValue<string>();
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

                        var client = await _context.Clients.FirstOrDefaultAsync(c => c.ClientCode == clientCode && c.RouteCode == route.RouteCode);
                        if (client == null)
                        {
                            client = new Client
                            {
                                Name = clientName,
                                ClientCode = clientCode,
                                RouteCode = route.RouteCode,
                                DeliveryAddress = address
                            };
                            _context.Clients.Add(client);
                        }

                        await _context.SaveChangesAsync();

                        var delivery = new Delivery
                        {
                            ProductId = product.Id,
                            ClientId = client.Id,
                            RouteId = route.Id,
                            Quantity = quantity,
                            Weight = weight,
                            DeliveryAddress = address,
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
            // Загружаем только нужные поля из Deliveries
            var deliveriesQuery = _context.Deliveries
                .Select(d => new {
                    d.Id,
                    d.ProductId,
                    ProductName = d.Product.Name,
                    ClientId = d.Client.Id,
                    ClientName = d.Client.Name,
                    d.Client.ClientCode,
                    d.Route.RouteCode,
                    d.DeliveryAddress,
                    d.Quantity,
                    d.UploadedFileId
                });

            if (dto.fileId > 0)
                deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == dto.fileId);

            var deliveries = await deliveriesQuery.OrderBy(x => x.ClientId).ToListAsync();

            if (deliveries.Count == 0)
            {
                return Ok(new
                {
                    message = $"Нет данных!",
                    status = false,
                    data = new object[0],
                });
            }

            var shipmentLogs = await _context.ShipmentLogs
                .Where(s => s.FileId == dto.fileId)
                .Select(s => new {
                    s.DeliveryId,
                    s.QuantityShipped
                })
                .ToListAsync();

            var shippedDict = shipmentLogs
                .GroupBy(s => s.DeliveryId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityShipped));

            var clientIds = deliveries
                .Select(d => d.ClientId)
                .Distinct()
                .ToList();

            // Получаем клиентов с порядком по Id и их данными
            var clients = await _context.Clients
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.ClientCode, c.RouteCode, c.DeliveryAddress })
                .OrderBy(c => c.Id)
                .ToListAsync();

            // Делаем словарь для быстрого доступа к порядку и данным
            var clientDataDict = clients
                .ToDictionary(x => x.Id, x => x);

            var iteration = 0;

            var pivot = deliveries
                .GroupBy(d => new { d.ProductId, d.ProductName })
                .Select(g =>
                {
                    var totalQty = g.Sum(d => d.Quantity);
                    var shipped = g.Sum(d => shippedDict.TryGetValue(d.Id, out var qty) ? qty : 0);
                    var remaining = totalQty - shipped;

                    List<object> clientsList;

                    if (iteration == 0)
                    {
                        // Первая итерация — добавить всех клиентов
                        clientsList = clients
                            .Select(client =>
                            {
                                var clientDeliveries = g.Where(d => d.ClientId == client.Id).ToList();
                                var totalQuantity = clientDeliveries.Sum(d => d.Quantity);

                                return new
                                {
                                    ClientId = client.Id,
                                    Name = client.Name,
                                    Code = client.ClientCode,
                                    TotalQuantity = totalQuantity,
                                    RouteCode = client.RouteCode,
                                    Address = client.DeliveryAddress  // <--- вот здесь поменяли
                                };
                            })
                            .OrderBy(c => c.ClientId)
                            .Cast<object>()
                            .ToList();
                    }
                    else
                    {
                        // Со второй итерации — только те, у кого есть доставка по этому продукту
                        clientsList = g
                            .Select(d => new
                            {
                                ClientId = d.ClientId,
                                Name = d.ClientName,
                                Code = d.ClientCode,
                                TotalQuantity = d.Quantity,
                                RouteCode = d.RouteCode,
                                Address = d.DeliveryAddress  // <--- и тут поменяли
                            })
                            .GroupBy(c => c.ClientId)
                            .Select(cg => new
                            {
                                ClientId = cg.Key,
                                Name = cg.First().Name,
                                Code = cg.First().Code,
                                TotalQuantity = cg.Sum(x => x.TotalQuantity),
                                RouteCode = cg.First().RouteCode,
                                Address = cg.First().Address  // <--- и тут поменяли
                            })
                            .OrderBy(c => c.ClientId)
                            .Cast<object>()
                            .ToList();
                    }

                    iteration++;

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
                data = pivot
            });
        }

        [HttpPost("clear_by_document")]
        public async Task<IActionResult> ClearByDocument([FromBody] Clear dto)
        {
            if (dto.fileId <= 0)
            {
                return Ok(new { message = "Некорректный идентификатор документа", status = false });
            }

            var file = await _context.UploadedFiles.FirstOrDefaultAsync(x => x.Id == dto.fileId);
            if (file == null)
            {
                return Ok(new { message = "Такого документа нет!", status = false });
            }

            var deliveryIds = await _context.Deliveries
                .Where(d => d.UploadedFileId == dto.fileId)
                .Select(d => d.Id)
                .ToListAsync();

            if (deliveryIds.Count == 0)
            {
                return Ok(new { message = "Нет доставок для удаления по этому документу", status = false });
            }

            // Удаляем логи отгрузки
            await _context.ShipmentLogs
                .Where(l => deliveryIds.Contains(l.DeliveryId))
                .ExecuteDeleteAsync();

            // Удаляем доставки
            await _context.Deliveries
                .Where(d => d.UploadedFileId == dto.fileId)
                .ExecuteDeleteAsync();

            await _context.UploadedFiles.ExecuteDeleteAsync();
            await _context.Products.ExecuteDeleteAsync();
            await _context.SaveChangesAsync();

            await _context.Database.ExecuteSqlRawAsync(@"DELETE FROM Clients");

            await _context.Database.ExecuteSqlRawAsync(@"DELETE FROM Products");

            await _context.Database.ExecuteSqlRawAsync("ALTER TABLE Clients AUTO_INCREMENT = 1;");
            await _context.Database.ExecuteSqlRawAsync("ALTER TABLE Products AUTO_INCREMENT = 1;");

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Удалено {deliveryIds.Count} доставок и все логи отгрузки по документу {dto.fileId}",
                status = true
            });
        }


        [HttpGet("get_documents")]
        public async Task<IActionResult> GetUploadedFilesByDate()
        {
            var files = await _context.UploadedFiles
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
                .Where(s => s.WorkerId == workerIntId && s.Barcode != dto.barcode && s.FileId == dto.fileId)
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
                .FirstOrDefault(s => s.Barcode == dto.barcode && s.WorkerId != workerIntId && s.FileId == dto.fileId);

            if (takenByAnother != null)
            {
                return Ok(new
                {
                    message = $"Этот продукт уже собран или собирается другим сборщиком (ID: {takenByAnother.WorkerId})",
                    status = false,
                });
            }

            var deliveriesQuery = _context.Deliveries
                .Where(d => d.ProductId == product.Id && d.UploadedFileId == dto.fileId);

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
                        .Where(s => s.ClientId == g.Key && s.WorkerId == workerIntId && s.Barcode == dto.barcode && s.FileId == dto.fileId)
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
                .Where(s => s.WorkerId == workerIntId && s.Barcode == dto.barcode && s.FileId == dto.fileId)
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
                                     && s.FileId == dto.fileId
                                     && s.ClientId == currentClientId);

            var clientDelivery = deliveries.FirstOrDefault(d => d.ClientId == current.ClientId);
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
                        FileId = dto.fileId,
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
                totalRemaining = totalRemaining
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
            var logsQuery = _context.ShipmentLogs
                .Where(s => s.FileId == dto.fileId && s.WorkerId >= 1 && s.WorkerId <= dto.workerCount);

            if (dto.fileId > 0)
            {
                logsQuery = logsQuery
                    .Where(s => _context.Deliveries.Any(d => d.Id == s.DeliveryId && d.UploadedFileId == dto.fileId));
            }

            var latestLogs = await logsQuery
                .GroupBy(s => s.WorkerId)
                .Select(g => g.OrderByDescending(x => x.ShipmentDate).FirstOrDefault())
                .ToListAsync();

            var result = new List<dynamic>();

            foreach (var log in latestLogs)
            {
                var workerId = log.WorkerId;
                var barcode = log.Barcode;

                var product = await _context.Products.FirstOrDefaultAsync(p => p.Barcode == barcode);
                if (product == null)
                    continue;

                var deliveriesQuery = _context.Deliveries.Where(d => d.ProductId == product.Id);
                if (dto.fileId > 0)
                {
                    deliveriesQuery = deliveriesQuery.Where(d => d.UploadedFileId == dto.fileId);
                }

                var deliveries = await deliveriesQuery.OrderBy(d => d.ClientId).ToListAsync();
                if (deliveries.Count == 0)
                    continue;

                var grouped = new List<dynamic>();
                var deliveryGroups = deliveries.GroupBy(d => d.ClientId);

                foreach (var g in deliveryGroups)
                {
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == g.Key);
                    var deliveryIds = g.Select(d => d.Id).ToList();

                    var shipped = await _context.ShipmentLogs
                        .Where(s => deliveryIds.Contains(s.DeliveryId)
                            && s.WorkerId == workerId
                            && s.Barcode == barcode
                            && s.FileId == dto.fileId)
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
                            && s.FileId == dto.fileId
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

            // Добавляем пустые слоты для недостающих воркеров
            var existingWorkerIds = result.Select(r => (int)r.workerId).ToHashSet();
            for (int workerId = 1; workerId <= dto.workerCount; workerId++)
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
                workerCount = result.Count,
                data = result
            });
        }


        [HttpPost("BackNext")]
        public async Task<IActionResult> BackNext([FromBody] BackNext dto)
        {
            var workerIntId = dto.workerId;

            var lastShipmentLog = _context.ShipmentLogs
                .Where(s => s.WorkerId == workerIntId)
                .OrderByDescending(s => s.ShipmentDate)
                .FirstOrDefault();

            if (lastShipmentLog == null)
            {
                var send1 = await SendEmptyResponse(workerIntId, "Не найдены отгрузки для данного сборщика.");
                return Ok(send1);
            }

            var barcode = lastShipmentLog.Barcode;
            var product = _context.Products.FirstOrDefault(p => p.Barcode == barcode);
            if (product == null)
            {
                var send2 = await SendEmptyResponse(workerIntId, "Продукт не найден");
                return Ok(send2);
            }

            var takenByAnother = await _context.ShipmentLogs
                .FirstOrDefaultAsync(s => s.Barcode == barcode && s.WorkerId != workerIntId && s.FileId == dto.fileId);

            if (takenByAnother != null)
            {
                await SendEmptyResponse(workerIntId, $"Этот продукт уже собирается другим сборщиком (ID: {takenByAnother.WorkerId})");
            }

            var deliveriesQuery = _context.Deliveries
                .Where(d => d.ProductId == product.Id && d.UploadedFileId == dto.fileId);

            var deliveries = deliveriesQuery
                .OrderBy(d => d.ClientId)
                .ToList();

            var fullGrouped = deliveries
                .GroupBy(d => d.ClientId)
                .Select(g =>
                {
                    var client = _context.Clients.FirstOrDefault(c => c.Id == g.Key);

                    var shipped = g.Sum(d => _context.ShipmentLogs
                        .Where(s => s.ClientId == g.Key && s.WorkerId == workerIntId && s.Barcode == barcode && s.FileId == dto.fileId)
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
                .Where(s => allClientIds.Contains(s.ClientId) && s.FileId == dto.fileId && s.Barcode == barcode && s.WorkerId == workerIntId)
                .Select(s => s.ClientId)
                .Distinct()
                .ToList();

            var allClientsShipped = allClientIds.All(id => shippedClientIds.Contains(id));

            //if (page < 0 || page >= totalPages)
            //    page = 0;

            var current = fullGrouped.Skip(dto.page).FirstOrDefault();

            if (current == null)
            {
                var confirmResponse = new
                {
                    status = false,
                    isComplete = true,
                    workerId = workerIntId,
                    data = EmptyData(workerIntId),
                    message = $"Все товары отгружены для продукта {product.Name}"
                };

                return Ok(confirmResponse);
            }

            var next = (dto.page + 1 < totalPages) ? fullGrouped[dto.page + 1] : null;
            var previous = (dto.page - 1 >= 0) ? fullGrouped[dto.page - 1] : null;

            var totalPlanned = fullGrouped.Sum(g => g.TotalQuantity);
            var totalRemaining = fullGrouped.Sum(g => g.Remaining);

            var shipmentLog = _context.ShipmentLogs
                .FirstOrDefault(s => s.WorkerId == workerIntId && s.Barcode == barcode && s.FileId == dto.fileId && s.ClientId == current.ClientId);

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
                    FileId = dto.fileId,
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
                page = dto.page,
                totalPages = totalPages + 1,
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
            return Ok(message);
        }

        [NonAction]
        public async Task<Object> SendEmptyResponse(int workerId, string errorMessage)
        {
            var errorResponse = new
            {
                status = false,
                message = errorMessage,
                isComplete = false,
                data = EmptyData(workerId),
                workerId = workerId
            };
            return errorResponse;
        }

        [NonAction]
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
