using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Signal.Server.Data.Dtos;
using Signal.Server.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SignalTrack.Api.Controllers;

[ApiController]
[Route("api/equipment")]
public class EquipmentController(EquipmentService equipmentService, AuditLogService auditLogService) : ControllerBase
{
    private readonly EquipmentService equipmentService = equipmentService;
    private readonly AuditLogService auditLogService = auditLogService;

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue("sub");
        return int.TryParse(sub, out int id) ? id : null;
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    [Authorize]
    [HttpGet("{serialNumber}/logs")]
    public async Task<IActionResult> GetEquipmentLogs(
        [FromRoute] string serialNumber,
        [FromQuery] string period = "ALL_TIME",
        [FromQuery] string? identifier = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var result = await equipmentService.GetEquipmentLogsAsync(serialNumber, period, identifier, page, pageSize);
        return Ok(result);
    }

    [Authorize]
    [HttpGet("{serialNumber}/graph")]
    public async Task<IActionResult> GetGraphData([FromRoute] string serialNumber, [FromQuery] string period = "ALL_TIME")
    {
        var points = await equipmentService.GetFullGraphDataAsync(serialNumber, period);
        return Ok(points);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetEquipment()
    {
        var devices = await equipmentService.GetEquipmentAsync();
        return Ok(devices);
    }

    [HttpGet("admin-panel")]
    public async Task<IActionResult> GetEquipment([FromQuery] int page, [FromQuery] int pageSize = 10)
    {
        var devices = await equipmentService.GetEquipmentAsync(page, pageSize);
        return Ok(devices);
    }

    [HttpGet("reading-types")]
    public async Task<IActionResult> GetReadingTypes([FromQuery] int page, [FromQuery] int pageSize = 10)
    {
        var readingTypes = await equipmentService.GetReadingTypesAsync(page, pageSize);
        return Ok(readingTypes);
    }

    [HttpPost("logs")]
    public async Task<IActionResult> Create([FromBody] CreateReadingLogDto dto)
    {
        try
        {
            var result = await equipmentService.AddReadingLogAsync(dto);
            return StatusCode(201, new { result.ReadingLogId, result.StatusId, result.WriteDateTime });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch
        {
            return BadRequest();
        }
    }

    [HttpPost("logs/batch")]
    public async Task<IActionResult> CreateBatch([FromBody] List<CreateReadingLogDto> dtos)
    {
        try
        {
            int processed = await equipmentService.AddReadingLogsBatchAsync(dtos);
            return StatusCode(201, new { processed });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("agent-config")]
    public async Task<ActionResult<List<AgentDeviceConfigDto>>> GetAgentConfig()
    {
        var config = await equipmentService.ExportAgentConfigAsync();
        return Ok(config);
    }

    [HttpPost("agent-config")]
    public async Task<IActionResult> SaveAgentConfig([FromBody] List<AgentDeviceConfigDto> devices)
    {
        int updated = await equipmentService.ImportAgentConfigAsync(devices);
        await auditLogService.LogActionAsync(
            GetCurrentUserId(),
            "IMPORT_AGENT_CONFIG",
            "equipment_command",
            null,
            new { devicesCount = devices?.Count ?? 0, updatedDevices = updated },
            GetClientIp());

        return Ok(new { updated });
    }

    [Authorize]
    [HttpGet("alerts/unread")]
    public async Task<ActionResult<List<UserAlertDto>>> GetUnreadAlerts()
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var alerts = await equipmentService.GetUnreadUserAlertsAsync(userId.Value);
        return Ok(alerts);
    }

    [Authorize]
    [HttpPost("alerts/{userAlertId}/read")]
    public async Task<IActionResult> MarkAlertRead(int userAlertId)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var ok = await equipmentService.MarkAlertAsReadAsync(userId.Value, userAlertId);
        if (!ok) return NotFound();
        return Ok(new { message = "Уведомление прочитано" });
    }

    [Authorize]
    [HttpGet("fleet-summary")]
    public async Task<ActionResult<FleetSummaryDto>> GetFleetSummary()
    {
        var summary = await equipmentService.GetFleetSummaryAsync();
        return Ok(summary);
    }

    [Authorize]
    [HttpGet("{serialNumber}/latest")]
    public async Task<ActionResult<IEnumerable<LatestReadingResponse>>> GetLatestReadings(string serialNumber)
    {
        var exists = await equipmentService.CheckEquipmentExistsAsync(serialNumber);
        if (!exists)
        {
            return NotFound($"Прибор с серийным номером '{serialNumber}' не найден.");
        }

        var readings = await equipmentService.GetLatestReadingsAsync(serialNumber);
        return Ok(readings);
    }

    [Authorize(Roles = "Администратор")]
    [HttpPost]
    public async Task<IActionResult> CreateEquipment([FromBody] EquipmentDto dto)
    {
        try
        {
            var createdId = await equipmentService.CreateEquipmentAsync(dto);
            if (createdId is null)
                return BadRequest(new { message = "Ошибка добавления оборудования" });

            await auditLogService.LogActionAsync(
                GetCurrentUserId(),
                "CREATE_EQUIPMENT",
                "equipment",
                createdId.Value,
                dto with { EquipmentId = createdId.Value },
                GetClientIp());

            return StatusCode(201, new { equipmentId = createdId.Value, message = "Оборудование успешно добавлено" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "Администратор")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEquipment(int id)
    {
        var result = await equipmentService.DeleteEquipmentAsync(id);
        if (!result) return NotFound(new { message = "Оборудование не найдено" });

        await auditLogService.LogActionAsync(
            GetCurrentUserId(),
            "DELETE_EQUIPMENT",
            "equipment",
            id,
            new { equipmentId = id, softDeleted = true },
            GetClientIp());

        return NoContent();
    }

    [Authorize(Roles = "Администратор")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEquipment(int id, [FromBody] EquipmentDto dto)
    {
        try
        {
            var result = await equipmentService.UpdateEquipmentAsync(id, dto);
            if (!result) return NotFound(new { message = "Оборудование не найдено" });

            await auditLogService.LogActionAsync(
                GetCurrentUserId(),
                "UPDATE_EQUIPMENT",
                "equipment",
                id,
                dto,
                GetClientIp());

            return Ok(new { message = "Данные успешно обновлены" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "Администратор")]
    [HttpDelete("reading-types/{id}")]
    public async Task<IActionResult> DeleteReadingType(int id)
    {
        var result = await equipmentService.DeleteReadingTypeAsync(id);
        if (!result) return NotFound(new { message = "Тип показания не найден" });

        await auditLogService.LogActionAsync(
            GetCurrentUserId(),
            "DELETE_READING_TYPE",
            "reading_type",
            id,
            new { readingTypeId = id },
            GetClientIp());

        return NoContent();
    }

    [Authorize(Roles = "Администратор")]
    [HttpPut("reading-types/{id}")]
    public async Task<IActionResult> UpdateReadingType(int id, [FromBody] ReadingTypeDto dto)
    {
        try
        {
            var result = await equipmentService.UpdateReadingTypeAsync(id, dto);
            if (!result) return NotFound(new { message = "Тип показания не найден" });

            await auditLogService.LogActionAsync(
                GetCurrentUserId(),
                "UPDATE_READING_TYPE",
                "reading_type",
                id,
                dto,
                GetClientIp());

            return Ok(new { message = "Данные успешно обновлены" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}