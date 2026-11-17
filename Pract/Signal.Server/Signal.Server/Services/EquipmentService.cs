using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Signal.Server.Data;
using Signal.Server.Data.Dtos;
using Signal.Server.Data.Models;

namespace Signal.Server.Services
{
    public class EquipmentService(ApplicationDbContext context)
    {
        // Константа безопасности: максимальное число уведомлений, которое клиентское устройство получает за один раз
        public const int MaxNotificationsPerBatch = 5;

        private readonly ApplicationDbContext context = context;

        public async Task<PagedLogsResponse> GetEquipmentLogsAsync(
            string serialNumber,
            string periodType,
            string? readingIdentifier,
            int page = 1,
            int pageSize = 20)
        {
            var now = DateTime.UtcNow;
            DateTime? startDate = null;
            DateTime? endDate = null;

            ApplyPeriod(periodType, now, ref startDate, ref endDate);

            var availableIdentifiers = await context.ReadingLogs
                .Where(rl => rl.Equipment.SerialNumber == serialNumber && !rl.Equipment.IsDeleted)
                .Select(rl => rl.ReadingType.Identifier)
                .Distinct()
                .ToListAsync();

            var query = context.ReadingLogs
                .Where(rl => rl.Equipment.SerialNumber == serialNumber
                    && !rl.Equipment.IsDeleted
                    && rl.Status.Name.ToLower() != "ok");

            if (startDate.HasValue) query = query.Where(rl => rl.WriteDateTime >= startDate.Value);
            if (endDate.HasValue) query = query.Where(rl => rl.WriteDateTime < endDate.Value);

            if (!string.IsNullOrEmpty(readingIdentifier))
            {
                query = query.Where(rl => rl.ReadingType.Identifier == readingIdentifier);
            }

            int totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(rl => rl.WriteDateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(rl => new ReadingLogResponse(
                    rl.ReadingType.Name,
                    rl.Value,
                    rl.ReadingType.Unit,
                    rl.Status.Name,
                    rl.WriteDateTime
                ))
                .ToListAsync();

            return new PagedLogsResponse(logs, availableIdentifiers, totalCount);
        }

        public async Task<List<EquipmentDto>> GetEquipmentAsync()
        {
            await SyncAlertsWithDatabaseStateAsync();

            return await context.Equipment
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.Model)
                .ThenBy(e => e.Name)
                .Select(e => new EquipmentDto(
                    e.EquipmentId,
                    e.SerialNumber,
                    e.Name,
                    e.Model,
                    e.Status.Name,
                    e.IpAddress,
                    e.IsDeleted,
                    e.PollEnabled
                )).ToListAsync();
        }

        public async Task<EquipmentResponce> GetEquipmentAsync(int page = 1, int pageSize = 10)
        {
            await SyncAlertsWithDatabaseStateAsync();

            var query = context.Equipment.Where(e => !e.IsDeleted);
            var equipmentCount = await query.CountAsync();
            var equipment = await query
                .OrderBy(e => e.Model)
                .ThenBy(e => e.Name)
                .Select(e => new EquipmentDto(
                    e.EquipmentId,
                    e.SerialNumber,
                    e.Name,
                    e.Model,
                    e.Status.Name,
                    e.IpAddress,
                    e.IsDeleted,
                    e.PollEnabled
                )).Skip(pageSize * (page - 1)).Take(pageSize).ToListAsync();

            return new EquipmentResponce
            {
                Equipment = equipment,
                TotalEquipment = equipmentCount
            };
        }

        public async Task<List<GraphPointResponse>> GetFullGraphDataAsync(string serialNumber, string periodType)
        {
            var now = DateTime.UtcNow;
            DateTime? startDate = null;
            DateTime? endDate = null;

            ApplyPeriod(periodType, now, ref startDate, ref endDate);

            var query = context.ReadingLogs
                .Where(rl => rl.Equipment.SerialNumber == serialNumber && !rl.Equipment.IsDeleted);

            if (startDate.HasValue) query = query.Where(rl => rl.WriteDateTime >= startDate.Value);
            if (endDate.HasValue) query = query.Where(rl => rl.WriteDateTime < endDate.Value);

            return await query
                .OrderBy(rl => rl.WriteDateTime)
                .Select(rl => new GraphPointResponse(
                    rl.ReadingType.Identifier,
                    rl.Value,
                    rl.WriteDateTime
                ))
                .ToListAsync();
        }

        public async Task<bool> CheckEquipmentExistsAsync(string serialNumber)
        {
            return await context.Equipment
                .AnyAsync(e => e.SerialNumber == serialNumber && !e.IsDeleted);
        }

        public async Task<IEnumerable<LatestReadingResponse>> GetLatestReadingsAsync(string serialNumber)
        {
            await SyncAlertsWithDatabaseStateAsync();

            return await context.ViewLatestEquipmentReadings
                .Where(r => r.SerialNumber == serialNumber)
                .Select(r => new LatestReadingResponse(
                    r.SerialNumber,
                    r.MetricKey ?? "",
                    r.Label ?? "",
                    r.Value ?? "",
                    r.Unit,
                    r.Status ?? "ok",
                    r.Datetime,
                    r.MinValue,
                    r.MaxValue
                ))
                .ToListAsync();
        }

        public async Task SyncAlertsWithDatabaseStateAsync()
        {
            try
            {
                var userIds = await context.Users.Select(u => u.UserId).ToListAsync();
                if (userIds.Count == 0) return;

                var equipments = await context.Equipment
                    .Include(e => e.Status)
                    .Where(e => !e.IsDeleted)
                    .ToListAsync();

                var allLatestReadings = await context.ViewLatestEquipmentReadings.ToListAsync();

                var recentAbnormalLogs = await context.ReadingLogs
                    .Include(rl => rl.ReadingType)
                    .Include(rl => rl.Status)
                    .Where(rl => !rl.Equipment.IsDeleted && rl.Status.Name.ToLower() != "ok")
                    .OrderByDescending(rl => rl.WriteDateTime)
                    .ThenByDescending(rl => rl.ReadingLogId)
                    .Take(200)
                    .ToListAsync();

                var unresolvedAlerts = await context.Alerts
                    .Where(a => !a.IsResolved)
                    .OrderByDescending(a => a.CreatedAt)
                    .ThenByDescending(a => a.AlertId)
                    .ToListAsync();

                foreach (var eq in equipments)
                {
                    var eqUnresolvedAlerts = unresolvedAlerts
                        .Where(a => a.EquipmentId == eq.EquipmentId)
                        .ToList();

                    var primaryAlert = eqUnresolvedAlerts.FirstOrDefault();
                    if (eqUnresolvedAlerts.Count > 1)
                    {
                        foreach (var dup in eqUnresolvedAlerts.Skip(1))
                        {
                            dup.IsResolved = true;
                            dup.ResolvedAt = DateTime.UtcNow;
                        }
                        await context.SaveChangesAsync();
                    }

                    var abnormalLatest = allLatestReadings
                        .Where(r => r.SerialNumber == eq.SerialNumber
                            && !string.IsNullOrWhiteSpace(r.Status)
                            && !r.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var eqAbnormalLogs = recentAbnormalLogs
                        .Where(l => l.EquipmentId == eq.EquipmentId)
                        .ToList();

                    int? newestAbnormalLogId = eqAbnormalLogs.FirstOrDefault()?.ReadingLogId;
                    bool isDeviceStatusAbnormal = !eq.Status.Name.Equals("ok", StringComparison.OrdinalIgnoreCase);

                    if (abnormalLatest.Count == 0 && !isDeviceStatusAbnormal)
                    {
                        if (primaryAlert != null)
                        {
                            primaryAlert.IsResolved = true;
                            primaryAlert.ResolvedAt = DateTime.UtcNow;
                            await context.SaveChangesAsync();
                        }
                        continue;
                    }

                    var metricParts = new List<string>();
                    bool hasError = eq.Status.Name.Equals("error", StringComparison.OrdinalIgnoreCase);

                    if (abnormalLatest.Count > 0)
                    {
                        foreach (var m in abnormalLatest)
                        {
                            if (string.Equals(m.Status, "error", StringComparison.OrdinalIgnoreCase))
                                hasError = true;

                            string label = !string.IsNullOrWhiteSpace(m.Label) ? m.Label : (m.MetricKey ?? "Параметр");
                            string unit = string.IsNullOrWhiteSpace(m.Unit) ? "" : $" {m.Unit}";
                            metricParts.Add($"{label} ({m.Value}{unit})");
                        }
                    }
                    else if (eqAbnormalLogs.Count > 0)
                    {
                        var groupedLogs = eqAbnormalLogs
                            .GroupBy(l => l.ReadingTypeId)
                            .Select(g => g.First())
                            .ToList();

                        foreach (var l in groupedLogs)
                        {
                            if (l.Status.Name.Equals("error", StringComparison.OrdinalIgnoreCase))
                                hasError = true;

                            string label = !string.IsNullOrWhiteSpace(l.ReadingType.Name)
                                ? l.ReadingType.Name
                                : l.ReadingType.Identifier;
                            string unit = string.IsNullOrWhiteSpace(l.ReadingType.Unit) ? "" : $" {l.ReadingType.Unit}";
                            metricParts.Add($"{label} ({l.Value}{unit})");
                        }
                    }

                    string severity = hasError ? "CRITICAL" : "WARNING";
                    string message = metricParts.Count > 0
                        ? $"{eq.Name}: вне нормы {string.Join(", ", metricParts)}"
                        : $"{eq.Name}: нештатный статус ({eq.Status.Name})";

                    if (message.Length > 255)
                        message = message.Substring(0, 252) + "...";

                    if (primaryAlert == null)
                    {
                        primaryAlert = new Alert
                        {
                            EquipmentId = eq.EquipmentId,
                            ReadingLogId = newestAbnormalLogId,
                            Severity = severity,
                            Message = message,
                            IsResolved = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        context.Alerts.Add(primaryAlert);
                        await context.SaveChangesAsync();

                        foreach (var uid in userIds)
                        {
                            context.UserAlerts.Add(new UserAlert
                            {
                                AlertId = primaryAlert.AlertId,
                                UserId = uid,
                                IsRead = false
                            });
                        }
                        await context.SaveChangesAsync();
                    }
                    else
                    {
                        bool messageOrSeverityChanged = primaryAlert.Message != message || primaryAlert.Severity != severity;
                        if (messageOrSeverityChanged)
                        {
                            primaryAlert.Message = message;
                            primaryAlert.Severity = severity;
                            primaryAlert.ReadingLogId = newestAbnormalLogId;
                            primaryAlert.CreatedAt = DateTime.UtcNow;

                            var existingUserAlerts = await context.UserAlerts
                                .Where(ua => ua.AlertId == primaryAlert.AlertId)
                                .ToListAsync();

                            foreach (var ua in existingUserAlerts)
                            {
                                ua.IsRead = false;
                                ua.ReadAt = null;
                            }

                            foreach (var uid in userIds)
                            {
                                if (!existingUserAlerts.Any(ua => ua.UserId == uid))
                                {
                                    context.UserAlerts.Add(new UserAlert
                                    {
                                        AlertId = primaryAlert.AlertId,
                                        UserId = uid,
                                        IsRead = false
                                    });
                                }
                            }

                            await context.SaveChangesAsync();
                        }
                    }
                }

                var activeAlertIds = await context.Alerts
                    .Where(a => !a.IsResolved)
                    .Select(a => a.AlertId)
                    .ToListAsync();

                if (activeAlertIds.Count > 0)
                {
                    var existingPairs = await context.UserAlerts
                        .Where(ua => activeAlertIds.Contains(ua.AlertId))
                        .Select(ua => new { ua.AlertId, ua.UserId })
                        .ToListAsync();

                    bool addedAny = false;
                    foreach (var alertId in activeAlertIds)
                    {
                        foreach (var uid in userIds)
                        {
                            if (!existingPairs.Any(p => p.AlertId == alertId && p.UserId == uid))
                            {
                                context.UserAlerts.Add(new UserAlert
                                {
                                    AlertId = alertId,
                                    UserId = uid,
                                    IsRead = false
                                });
                                addedAny = true;
                            }
                        }
                    }

                    if (addedAny)
                    {
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncAlerts] Ошибка синхронизации уведомлений: {ex.Message}");
            }
        }

        public async Task<ReadingLog> AddReadingLogAsync(CreateReadingLogDto dto)
        {
            var statuses = await context.Statuses.ToListAsync();
            var okStatus = statuses.FirstOrDefault(s => s.Name.Equals("ok", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Статус Ok не найден");
            var warningStatus = statuses.FirstOrDefault(s => s.Name.Equals("warning", StringComparison.OrdinalIgnoreCase)) ?? okStatus;
            var errorStatus = statuses.FirstOrDefault(s => s.Name.Equals("error", StringComparison.OrdinalIgnoreCase)) ?? okStatus;

            var equipment = await context.Equipment
                .FirstOrDefaultAsync(e => e.IpAddress == dto.IpAddress && !e.IsDeleted);

            if (equipment is null)
            {
                equipment = new Equipment
                {
                    StatusId = okStatus.StatusId,
                    IpAddress = dto.IpAddress,
                    SerialNumber = Guid.NewGuid().ToString(),
                    Name = "New Equipment",
                    Model = "Other",
                    IsDeleted = false,
                    PollEnabled = true
                };

                await context.Equipment.AddAsync(equipment);
                await context.SaveChangesAsync();
            }

            var readingType = await context.ReadingTypes
                .FirstOrDefaultAsync(rt => rt.Identifier == dto.ReadingTypeIdentifier);

            if (readingType is null)
            {
                readingType = new ReadingType
                {
                    Identifier = dto.ReadingTypeIdentifier,
                    Name = dto.ReadingTypeIdentifier
                };
                await context.ReadingTypes.AddAsync(readingType);
                await context.SaveChangesAsync();
            }

            int computedStatusId = okStatus.StatusId;
            string rawTrimmed = (dto.Value ?? string.Empty).Trim();

            if (rawTrimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
                rawTrimmed.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                rawTrimmed.Equals("fail", StringComparison.OrdinalIgnoreCase))
            {
                computedStatusId = errorStatus.StatusId;
            }
            else if (readingType.MinValue.HasValue || readingType.MaxValue.HasValue)
            {
                string normalizedVal = rawTrimmed.Replace(",", ".").TrimStart('+');
                if (!decimal.TryParse(normalizedVal, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numericVal))
                {
                    computedStatusId = errorStatus.StatusId;
                }
                else if ((readingType.MinValue.HasValue && numericVal < readingType.MinValue.Value) ||
                         (readingType.MaxValue.HasValue && numericVal > readingType.MaxValue.Value))
                {
                    computedStatusId = warningStatus.StatusId;
                }
            }

            return await CretaeEquipmentLogAsync(dto, equipment, readingType, computedStatusId);
        }

        private async Task<ReadingLog> CretaeEquipmentLogAsync(CreateReadingLogDto dto, Equipment equipment, ReadingType readingType, int computedStatusId)
        {
            var newLog = new ReadingLog
            {
                EquipmentId = equipment.EquipmentId,
                ReadingTypeId = readingType.ReadingTypeId,
                StatusId = computedStatusId,
                Value = dto.Value,
                WriteDateTime = dto.WriteDateTime?.ToUniversalTime() ?? DateTime.UtcNow
            };

            await context.ReadingLogs.AddAsync(newLog);
            equipment.StatusId = computedStatusId;
            await context.SaveChangesAsync();

            await context.Entry(newLog).ReloadAsync();

            await SyncAlertsWithDatabaseStateAsync();

            return newLog;
        }

        public async Task<int> AddReadingLogsBatchAsync(List<CreateReadingLogDto> dtos)
        {
            if (dtos == null || dtos.Count == 0) return 0;

            var ordered = dtos.OrderBy(d => d.WriteDateTime ?? DateTime.UtcNow).ToList();
            int count = 0;
            foreach (var dto in ordered)
            {
                await AddReadingLogAsync(dto);
                count++;
            }
            return count;
        }

        public async Task<List<AgentDeviceConfigDto>> ExportAgentConfigAsync()
        {
            var equipments = await context.Equipment
                .Include(e => e.EquipmentCommands)
                .ThenInclude(ec => ec.ReadingType)
                .Where(e => !e.IsDeleted && e.PollEnabled && !string.IsNullOrEmpty(e.IpAddress))
                .OrderBy(e => e.EquipmentId)
                .ToListAsync();

            var result = new List<AgentDeviceConfigDto>();
            foreach (var eq in equipments)
            {
                var devDto = new AgentDeviceConfigDto
                {
                    Ip = eq.IpAddress!,
                    Params = eq.EquipmentCommands.Select(cmd => new AgentParamConfigDto
                    {
                        Command = cmd.CommandString,
                        Name = cmd.ReadingType.Identifier,
                        Uncorrect = cmd.InvalidValue,
                        PollIntervalSeconds = cmd.PollIntervalSeconds
                    }).ToList()
                };
                result.Add(devDto);
            }
            return result;
        }

        public async Task<int> ImportAgentConfigAsync(List<AgentDeviceConfigDto> devices)
        {
            if (devices == null) return 0;

            var okStatus = await context.Statuses.FirstOrDefaultAsync(s => s.Name.ToLower() == "ok");
            int statusId = okStatus?.StatusId ?? 1;
            int updatedDevices = 0;

            foreach (var dev in devices.Where(d => !string.IsNullOrWhiteSpace(d.Ip)))
            {
                var equipment = await context.Equipment
                    .Include(e => e.EquipmentCommands)
                    .FirstOrDefaultAsync(e => e.IpAddress == dev.Ip);

                if (equipment is null)
                {
                    equipment = new Equipment
                    {
                        IpAddress = dev.Ip,
                        SerialNumber = Guid.NewGuid().ToString(),
                        Name = $"Device {dev.Ip}",
                        Model = "Other",
                        StatusId = statusId,
                        IsDeleted = false,
                        PollEnabled = true
                    };
                    context.Equipment.Add(equipment);
                    await context.SaveChangesAsync();
                }

                foreach (var param in dev.Params)
                {
                    if (string.IsNullOrWhiteSpace(param.Command) || string.IsNullOrWhiteSpace(param.Name))
                        continue;

                    var readingType = await context.ReadingTypes
                        .FirstOrDefaultAsync(rt => rt.Identifier == param.Name);

                    if (readingType is null)
                    {
                        readingType = new ReadingType
                        {
                            Identifier = param.Name,
                            Name = param.Name
                        };
                        context.ReadingTypes.Add(readingType);
                        await context.SaveChangesAsync();
                    }

                    var existingCmd = equipment.EquipmentCommands
                        .FirstOrDefault(c => c.CommandString == param.Command);

                    if (existingCmd is null)
                    {
                        context.EquipmentCommands.Add(new EquipmentCommand
                        {
                            EquipmentId = equipment.EquipmentId,
                            ReadingTypeId = readingType.ReadingTypeId,
                            CommandString = param.Command,
                            InvalidValue = param.Uncorrect,
                            PollIntervalSeconds = param.PollIntervalSeconds > 0 ? param.PollIntervalSeconds : 30
                        });
                    }
                    else
                    {
                        existingCmd.ReadingTypeId = readingType.ReadingTypeId;
                        existingCmd.InvalidValue = param.Uncorrect;
                        if (param.PollIntervalSeconds > 0)
                            existingCmd.PollIntervalSeconds = param.PollIntervalSeconds;
                    }
                }

                await context.SaveChangesAsync();
                updatedDevices++;
            }

            return updatedDevices;
        }

        public async Task<List<UserAlertDto>> GetUnreadUserAlertsAsync(int userId)
        {
            await SyncAlertsWithDatabaseStateAsync();

            var rawUnread = await context.UserAlerts
                .Include(ua => ua.Alert)
                .ThenInclude(a => a.Equipment)
                .Where(ua => ua.UserId == userId && !ua.IsRead && !ua.Alert.IsResolved)
                .OrderByDescending(ua => ua.Alert.CreatedAt)
                .ThenByDescending(ua => ua.UserAlertId)
                .ToListAsync();

            var groupedByDevice = new List<UserAlertDto>();
            var duplicateUserAlertsToMarkRead = new List<UserAlert>();

            foreach (var group in rawUnread.GroupBy(ua => ua.Alert.EquipmentId))
            {
                var latestForDevice = group.First();

                foreach (var olderDuplicate in group.Skip(1))
                {
                    olderDuplicate.IsRead = true;
                    olderDuplicate.ReadAt = DateTime.UtcNow;
                    duplicateUserAlertsToMarkRead.Add(olderDuplicate);
                }

                groupedByDevice.Add(new UserAlertDto(
                    latestForDevice.UserAlertId,
                    latestForDevice.AlertId,
                    latestForDevice.Alert.EquipmentId,
                    latestForDevice.Alert.Equipment.SerialNumber,
                    latestForDevice.Alert.Equipment.Name,
                    latestForDevice.Alert.Severity,
                    latestForDevice.Alert.Message,
                    latestForDevice.IsRead,
                    latestForDevice.Alert.CreatedAt
                ));
            }

            if (duplicateUserAlertsToMarkRead.Count > 0)
            {
                await context.SaveChangesAsync();
            }

            return groupedByDevice
                .Take(MaxNotificationsPerBatch)
                .ToList();
        }

        public async Task<bool> MarkAlertAsReadAsync(int userId, int userAlertId)
        {
            var userAlert = await context.UserAlerts
                .FirstOrDefaultAsync(ua => ua.UserAlertId == userAlertId && ua.UserId == userId);

            if (userAlert is null) return false;

            userAlert.IsRead = true;
            userAlert.ReadAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<FleetSummaryDto> GetFleetSummaryAsync()
        {
            await SyncAlertsWithDatabaseStateAsync();

            var activeEquipment = await context.Equipment
                .Include(e => e.Status)
                .Where(e => !e.IsDeleted)
                .ToListAsync();

            int total = activeEquipment.Count;
            int online = activeEquipment.Count(e => e.Status.Name.Equals("ok", StringComparison.OrdinalIgnoreCase));
            int warning = activeEquipment.Count(e => e.Status.Name.Equals("warning", StringComparison.OrdinalIgnoreCase));
            int error = activeEquipment.Count(e => e.Status.Name.Equals("error", StringComparison.OrdinalIgnoreCase));
            int activeAlerts = await context.Alerts.CountAsync(a => !a.IsResolved);

            return new FleetSummaryDto(total, online, warning, error, activeAlerts, DateTime.UtcNow);
        }

        public async Task<int?> CreateEquipmentAsync(EquipmentDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.SerialNumber) || string.IsNullOrWhiteSpace(dto.Name))
                return null;

            var statusName = string.IsNullOrWhiteSpace(dto.StatusName) ? "Ok" : dto.StatusName.Trim();
            var status = await context.Statuses
                .FirstOrDefaultAsync(s => s.Name.ToLower() == statusName.ToLower())
                ?? await context.Statuses.FirstOrDefaultAsync(s => s.Name.ToLower() == "ok");

            if (status is null) return null;

            var existing = await context.Equipment
                .FirstOrDefaultAsync(e => e.SerialNumber == dto.SerialNumber.Trim());

            if (existing is not null)
            {
                if (!existing.IsDeleted)
                    throw new InvalidOperationException("Серийный номер уже занят");

                existing.Name = dto.Name.Trim();
                existing.Model = string.IsNullOrWhiteSpace(dto.Model) ? "Other" : dto.Model.Trim();
                existing.StatusId = status.StatusId;
                existing.IpAddress = string.IsNullOrWhiteSpace(dto.IpAddress) ? existing.IpAddress : dto.IpAddress.Trim();
                existing.IsDeleted = false;
                existing.PollEnabled = dto.PollEnabled;

                await context.SaveChangesAsync();
                await SyncAlertsWithDatabaseStateAsync();
                return existing.EquipmentId;
            }

            var equipment = new Equipment
            {
                SerialNumber = dto.SerialNumber.Trim(),
                Name = dto.Name.Trim(),
                Model = string.IsNullOrWhiteSpace(dto.Model) ? "Other" : dto.Model.Trim(),
                StatusId = status.StatusId,
                IpAddress = string.IsNullOrWhiteSpace(dto.IpAddress) ? null : dto.IpAddress.Trim(),
                IsDeleted = false,
                PollEnabled = dto.PollEnabled
            };

            try
            {
                context.Equipment.Add(equipment);
                await context.SaveChangesAsync();
                await SyncAlertsWithDatabaseStateAsync();
                return equipment.EquipmentId;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DeleteEquipmentAsync(int id)
        {
            var equipment = await context.Equipment.FindAsync(id);
            if (equipment is null) return false;

            try
            {
                equipment.IsDeleted = true;
                equipment.PollEnabled = false;
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateEquipmentAsync(int id, EquipmentDto dto)
        {
            var equipment = await context.Equipment.FindAsync(id);
            if (equipment is null) return false;

            if (equipment.SerialNumber != dto.SerialNumber)
            {
                bool serialExists = await context.Equipment.AnyAsync(u => u.SerialNumber == dto.SerialNumber && u.EquipmentId != id);
                if (serialExists) throw new InvalidOperationException("Серийный номер уже занят");
            }

            equipment.SerialNumber = dto.SerialNumber;
            equipment.Name = dto.Name;
            equipment.Model = dto.Model;
            if (dto.IpAddress is not null)
                equipment.IpAddress = dto.IpAddress;
            equipment.PollEnabled = dto.PollEnabled;

            var status = await context.Statuses.FirstOrDefaultAsync(s => s.Name == dto.StatusName);
            if (status is not null)
                equipment.StatusId = status.StatusId;

            try
            {
                await context.SaveChangesAsync();
                await SyncAlertsWithDatabaseStateAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<ReadingTypeResponce> GetReadingTypesAsync(int page, int pageSize)
        {
            var readingTypeCount = await context.ReadingTypes.CountAsync();
            var readingTypes = await context.ReadingTypes
                .OrderBy(rt => rt.ReadingTypeId)
                .Select(rt => new ReadingTypeDto
                {
                    ReadingTypeId = rt.ReadingTypeId,
                    Name = rt.Name,
                    Identifier = rt.Identifier,
                    Unit = rt.Unit,
                    MinValue = rt.MinValue,
                    MaxValue = rt.MaxValue
                }).Skip(pageSize * (page - 1)).Take(pageSize).ToListAsync();

            return new ReadingTypeResponce
            {
                ReadingTypes = readingTypes,
                TotalReadingTypes = readingTypeCount
            };
        }

        public async Task<bool> DeleteReadingTypeAsync(int id)
        {
            var readingType = await context.ReadingTypes.FindAsync(id);
            if (readingType is null) return false;

            try
            {
                context.ReadingTypes.Remove(readingType);
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateReadingTypeAsync(int id, ReadingTypeDto dto)
        {
            var readingType = await context.ReadingTypes.FindAsync(id);
            if (readingType is null) return false;

            if (readingType.Identifier != dto.Identifier)
            {
                bool exists = await context.ReadingTypes.AnyAsync(u => u.Identifier == dto.Identifier && u.ReadingTypeId != id);
                if (exists) throw new InvalidOperationException("Идентификатор уже занят");
            }

            readingType.Identifier = dto.Identifier;
            readingType.Name = dto.Name;
            readingType.Unit = dto.Unit;
            readingType.MinValue = dto.MinValue;
            readingType.MaxValue = dto.MaxValue;

            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyPeriod(string periodType, DateTime now, ref DateTime? startDate, ref DateTime? endDate)
        {
            switch (periodType.ToUpper())
            {
                case "HOUR_1":
                    startDate = now.AddHours(-1);
                    break;
                case "TODAY":
                    startDate = now.Date;
                    break;
                case "YESTERDAY":
                    startDate = now.Date.AddDays(-1);
                    endDate = now.Date;
                    break;
                case "CURRENT_WEEK":
                    int diff = (7 + (now.Date.DayOfWeek - DayOfWeek.Monday)) % 7;
                    startDate = now.Date.AddDays(-1 * diff);
                    break;
                case "LAST_WEEK":
                    int lastWeekDiff = (7 + (now.Date.DayOfWeek - DayOfWeek.Monday)) % 7;
                    startDate = now.Date.AddDays(-1 * lastWeekDiff).AddDays(-7);
                    endDate = now.Date.AddDays(-1 * lastWeekDiff);
                    break;
                case "CURRENT_MONTH":
                    startDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    break;
                case "LAST_MONTH":
                    var lastMonth = now.AddMonths(-1);
                    startDate = new DateTime(lastMonth.Year, lastMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    endDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    break;
                case "ALL_TIME":
                default:
                    break;
            }
        }
    }
}