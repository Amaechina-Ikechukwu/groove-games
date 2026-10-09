using Microsoft.AspNetCore.Authorization;
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
    private readonly IGameEngineService _gameEngine;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        ITriviaDataService dataService,
        IExportService exportService,
        IGameEngineService gameEngine,
        ILogger<LeaderboardController> logger)
    {
        _dataService = dataService;
        _exportService = exportService;
        _gameEngine = gameEngine;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> GetLeaderboard(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 100)
    {
        var players = await _dataService.GetPersistentLeaderboardAsync(organizationId, hostId, search, Math.Clamp(limit, 1, 500));
        if (User.CanHost())
        {
            return Ok(players);
        }

        // Public view: the identifier is often an email or student ID, so it is never exposed anonymously.
        return Ok(players.Select(p => new
        {
            fullName = p.FullName,
            totalPointsAllTime = p.TotalPointsAllTime,
            quizzesPlayed = p.QuizzesPlayed,
            questionsAnswered = p.QuestionsAnswered,
            correctAnswersCount = p.CorrectAnswersCount,
            highestStreak = p.HighestStreak,
            lastActive = p.LastActive,
            accuracyPercentage = p.AccuracyPercentage
        }));
    }

    [HttpGet("tournaments")]
    public ActionResult GetTournaments()
    {
        var sessions = _gameEngine.GetAllActiveSessions();
        var tournaments = sessions
            .Where(s => !string.IsNullOrEmpty(s.TournamentId))
            .GroupBy(s => s.TournamentId)
            .Select(g => new
            {
                tournamentId = g.Key,
                tournamentName = g.First().TournamentName,
                sessionCount = g.Count(),
                totalPlayers = g.SelectMany(s => s.Players.Values).Select(p => p.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                pins = g.Select(s => s.Pin).ToList(),
                createdAt = g.Min(s => s.CreatedAt)
            })
            .OrderByDescending(t => t.createdAt)
            .ToList();

        return Ok(tournaments);
    }

    [HttpGet("tournament/{tournamentId}")]
    public ActionResult GetTournamentLeaderboard(string tournamentId)
    {
        var sessions = _gameEngine.GetSessionsByTournament(tournamentId);
        if (sessions.Count == 0)
        {
            return NotFound(new { message = $"Tournament '{tournamentId}' not found or has concluded." });
        }

        var playerAggregates = new Dictionary<string, (string FullName, int TotalScore, int SessionsPlayed, int CorrectAnswers, int TotalAnswers, int HighestStreak)>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            foreach (var p in session.Players.Values)
            {
                var key = p.FullName.Trim();
                if (playerAggregates.TryGetValue(key, out var agg))
                {
                    playerAggregates[key] = (
                        FullName: p.FullName,
                        TotalScore: agg.TotalScore + p.Score,
                        SessionsPlayed: agg.SessionsPlayed + 1,
                        CorrectAnswers: agg.CorrectAnswers + p.CorrectAnswers,
                        TotalAnswers: agg.TotalAnswers + p.TotalAnswers,
                        HighestStreak: Math.Max(agg.HighestStreak, p.HighestStreak)
                    );
                }
                else
                {
                    playerAggregates[key] = (
                        FullName: p.FullName,
                        TotalScore: p.Score,
                        SessionsPlayed: 1,
                        CorrectAnswers: p.CorrectAnswers,
                        TotalAnswers: p.TotalAnswers,
                        HighestStreak: p.HighestStreak
                    );
                }
            }
        }

        var leaderboard = playerAggregates.Values
            .OrderByDescending(p => p.TotalScore)
            .ThenByDescending(p => p.CorrectAnswers)
            .Select((p, idx) => new
            {
                rank = idx + 1,
                fullName = p.FullName,
                totalScore = p.TotalScore,
                sessionsPlayed = p.SessionsPlayed,
                correctAnswers = p.CorrectAnswers,
                totalAnswers = p.TotalAnswers,
                accuracyPercentage = p.TotalAnswers > 0 ? (int)Math.Round((double)p.CorrectAnswers / p.TotalAnswers * 100) : 0,
                highestStreak = p.HighestStreak
            })
            .ToList();

        return Ok(new
        {
            tournamentId,
            tournamentName = sessions[0].TournamentName,
            totalSessions = sessions.Count,
            sessions = sessions.Select(s => new
            {
                pin = s.Pin,
                sessionNumber = s.SessionNumber,
                totalSessions = s.TotalSessions,
                state = s.State.ToString(),
                quizTitle = s.Quiz.Title,
                playerCount = s.Players.Count
            }),
            leaderboard
        });
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("reset")]
    public async Task<ActionResult> ResetLeaderboard(
        [FromQuery] string? organizationId = null,
        [FromQuery] string? hostId = null)
    {
        await _dataService.ResetLeaderboardAsync(organizationId, hostId);
        return Ok(new { message = "Leaderboard reset successfully." });
    }

    [Authorize(Policy = "HostOnly")]
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

    [Authorize(Policy = "HostOnly")]
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
