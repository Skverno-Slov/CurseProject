using System;
using System.Collections.Generic;

namespace Signal.Dtos
{
    public class AuditLogDto
    {
        public int AuditLogId { get; set; }
        public int? UserId { get; set; }
        public string UserLogin { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;
        public int? EntityId { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PagedAuditLogsResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<AuditLogDto> Logs { get; set; } = new();
    }
}