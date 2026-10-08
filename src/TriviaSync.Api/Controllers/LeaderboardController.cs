using Microsoft.AspNetCore.Mvc;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaderboardController : ControllerBase
{
    private readonly ITriviaDataService _dataService;
    private readonly IExportService _exportService;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        ITriviaDataService dataService,
        IExportService exportService,
        ILogger<LeaderboardController> logger)
    {
        _dataService = dataService;
        _exportService = exportService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<PersistentPlayer>>> GetLeaderboard(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 100)
    {
        var players = await _dataService.GetPersistentLeaderboardAsync(organizationId, hostId, search, limit);
        return Ok(players);
    }

    [HttpPost("reset")]
    public async Task<ActionResult> ResetLeaderboard(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null)
    {
        await _dataService.ResetLeaderboardAsync(organizationId, hostId);
        return Ok(new { message = "Leaderboard reset successfully." });
    }

    [HttpGet("export/csv")]
    public async Task<ActionResult> ExportCsv(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null,
        [FromQuery] string? search = null)
    {
        var players = await _dataService.GetPersistentLeaderboardAsync(organizationId, hostId, search, 1000);
        var bytes = _exportService.ExportLeaderboardToCsv(players);
        return File(bytes, "text/csv; charset=utf-8", "Groove_Global_Leaderboard.csv");
    }

    [HttpGet("export/excel")]
    public async Task<ActionResult> ExportExcel(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null,
        [FromQuery] string? search = null)
    {
        var players = await _dataService.GetPersistentLeaderboardAsync(organizationId, hostId, search, 1000);
        var bytes = _exportService.ExportLeaderboardToExcelXml(players);
        return File(bytes, "application/vnd.ms-excel", "Groove_Global_Leaderboard.xls");
    }
}
