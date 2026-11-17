using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Signal.Server.Data;
using Signal.Server.Data.Dtos;
using Signal.Server.Data.Models;

namespace Signal.Server.Services
{
    public class AuditLogService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext context = context;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public async Task LogActionAsync(
            int? userId,
            string action,
            string entityName,
            int? entityId,
            object? detailsObj,
            string? ipAddress)
        {
            try
            {
                string? detailsJson = detailsObj != null
                    ? JsonSerializer.Serialize(detailsObj, JsonOptions)
                    : null;

                var entry = new AdminAuditLog
                {
                    UserId = userId,
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    Details = detailsJson,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow
                };

                context.AdminAuditLogs.Add(entry);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка записи в журнал аудита: {ex.Message}");
            }
        }

        public async Task<PagedAuditLogsResponse> GetAuditLogsAsync(
            int page = 1,
            int pageSize = 15,
            string? entityName = null,
            string? actionFilter = null,
            string? userLogin = null)
        {
            var query = context.AdminAuditLogs
                .Include(a => a.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityName))
            {
                query = query.Where(a => a.EntityName.ToLower().Contains(entityName.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(actionFilter))
            {
                query = query.Where(a => a.Action.ToLower().Contains(actionFilter.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(userLogin))
            {
                query = query.Where(a => a.User != null && a.User.Login.ToLower().Contains(userLogin.ToLower()));
            }

            int totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AdminAuditLogDto(
                    a.AuditLogId,
                    a.UserId,
                    a.User != null ? a.User.Login : "system",
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.Details,
                    a.IpAddress,
                    a.CreatedAt
                ))
                .ToListAsync();

            return new PagedAuditLogsResponse
            {
                Logs = logs,
                TotalCount = totalCount
            };
        }
    }
}