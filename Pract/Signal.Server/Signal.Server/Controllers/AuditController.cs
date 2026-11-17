using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Signal.Server.Data.Dtos;
using Signal.Server.Services;

namespace SignalTrack.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Администратор")]
public class AuditController(AuditLogService auditLogService) : ControllerBase
{
    private readonly AuditLogService auditLogService = auditLogService;

    [HttpGet]
    public async Task<ActionResult<PagedAuditLogsResponse>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        [FromQuery] string? entityName = null,
        [FromQuery] string? action = null,
        [FromQuery] string? userLogin = null)
    {
        var result = await auditLogService.GetAuditLogsAsync(page, pageSize, entityName, action, userLogin);
        return Ok(result);
    }
}