using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Signal.Server.Data.Dtos
{
    public class CreateReadingLogDto
    {
        public string IpAddress { get; set; } = null!;
        public string ReadingTypeIdentifier { get; set; } = null!;
        public string Value { get; set; } = null!;
        public DateTime? WriteDateTime { get; set; }
    }

    public class AgentParamConfigDto
    {
        [JsonPropertyName("Command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("Name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("Uncorrect")]
        public string? Uncorrect { get; set; }

        [JsonPropertyName("PollIntervalSeconds")]
        public int PollIntervalSeconds { get; set; } = 30;

        [JsonPropertyName("Annotations")]
        public Dictionary<string, string>? Annotations { get; set; }
    }

    public class AgentDeviceConfigDto
    {
        [JsonPropertyName("Ip")]
        public string Ip { get; set; } = string.Empty;

        [JsonPropertyName("Login")]
        public string? Login { get; set; }

        [JsonPropertyName("Password")]
        public string? Password { get; set; }

        [JsonPropertyName("Params")]
        public List<AgentParamConfigDto> Params { get; set; } = new();
    }

    public record AdminAuditLogDto(
        [property: JsonPropertyName("auditLogId")] int AuditLogId,
        [property: JsonPropertyName("userId")] int? UserId,
        [property: JsonPropertyName("userLogin")] string? UserLogin,
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("entityName")] string EntityName,
        [property: JsonPropertyName("entityId")] int? EntityId,
        [property: JsonPropertyName("details")] string? Details,
        [property: JsonPropertyName("ipAddress")] string? IpAddress,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt
    );

    public class PagedAuditLogsResponse
    {
        [JsonPropertyName("logs")]
        public List<AdminAuditLogDto> Logs { get; set; } = new();

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
    }

    public record UserAlertDto(
        [property: JsonPropertyName("userAlertId")] int UserAlertId,
        [property: JsonPropertyName("alertId")] int AlertId,
        [property: JsonPropertyName("equipmentId")] int EquipmentId,
        [property: JsonPropertyName("serialNumber")] string SerialNumber,
        [property: JsonPropertyName("equipmentName")] string EquipmentName,
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("isRead")] bool IsRead,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt
    );

    public record FleetSummaryDto(
        [property: JsonPropertyName("totalDevices")] int TotalDevices,
        [property: JsonPropertyName("onlineDevices")] int OnlineDevices,
        [property: JsonPropertyName("warningDevices")] int WarningDevices,
        [property: JsonPropertyName("errorDevices")] int ErrorDevices,
        [property: JsonPropertyName("activeAlertsCount")] int ActiveAlertsCount,
        [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt
    );
}