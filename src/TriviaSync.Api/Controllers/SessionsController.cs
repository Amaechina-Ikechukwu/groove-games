using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Controllers;

public class CreateSessionRequest
{
    public string? QuizId { get; set; }
    public List<string>? QuizIds { get; set; }
    public Quiz? Quiz { get; set; }
    public string TournamentName { get; set; } = "";
    public string SessionType { get; set; } = "Single"; // "Single" or "MultiSession"
    public int SessionCount { get; set; } = 1;
    public bool AutoAdvance { get; set; } = true;
}

public class SessionSummaryDto
{
    public string Pin { get; set; } = string.Empty;
    public string QuizId { get; set; } = string.Empty;
    public string QuizTitle { get; set; } = string.Empty;
    public int SessionNumber { get; set; } = 1;
    public int TotalSessions { get; set; } = 1;
    public string State { get; set; } = "Lobby";
    public int QuestionCount { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly IGameEngineService _gameEngine;
    private readonly ITriviaDataService _dataService;
    private readonly IExportService _exportService;
    private readonly IHubContext<QuizHub, IQuizClient> _hub;
    private readonly ITournamentService _tournaments;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        IGameEngineService gameEngine,
        ITriviaDataService dataService,
        IExportService exportService,
        IHubContext<QuizHub, IQuizClient> hub,
        ITournamentService tournaments,
        ILogger<SessionsController> logger)
    {
        _gameEngine = gameEngine;
        _dataService = dataService;
        _exportService = exportService;
        _hub = hub;
        _tournaments = tournaments;
        _logger = logger;
    }

    [Authorize(Policy = "HostOnly")]
    [HttpPost]
    public async Task<ActionResult> CreateSession([FromBody] CreateSessionRequest request)
    {
        // Sessions are always owned by the caller; never trust a host id from the request body.
        var hostId = User.UserEmail()!;
        var tournamentId = $"tourn_{Guid.NewGuid().ToString("N")[..8]}";
        var tournamentName = string.IsNullOrWhiteSpace(request.TournamentName) 
            ? $"Tournament {DateTime.UtcNow:MMM d}"
            : request.TournamentName.Trim();

        // 1. Check if Multi-Session Tournament was requested
        if (request.SessionType.Equals("MultiSession", StringComparison.OrdinalIgnoreCase) || 
            (request.QuizIds != null && request.QuizIds.Count > 1) || 
            request.SessionCount > 1)
        {
            var createdSessions = new List<SessionSummaryDto>();
            var quizList = new List<Quiz>();

            if (request.QuizIds != null && request.QuizIds.Count > 0)
            {
                foreach (var qId in request.QuizIds)
                {
                    var q = await _dataService.GetQuizByIdAsync(qId);
                    if (q != null && q.Questions.Count > 0)
                    {
                        quizList.Add(q);
                    }
                }
            }

            if (quizList.Count == 0)
            {
                Quiz? baseQuiz = request.Quiz;
                if (baseQuiz == null && !string.IsNullOrWhiteSpace(request.QuizId))
                {
                    baseQuiz = await _dataService.GetQuizByIdAsync(request.QuizId);
                }

                if (baseQuiz != null)
                {
                    int count = Math.Max(1, request.SessionCount);
                    for (int i = 0; i < count; i++)
                    {
                        quizList.Add(baseQuiz);
                    }
                }
            }

            if (quizList.Count == 0)
            {
                return BadRequest(new { message = "At least one valid quiz is required to launch multi-session tournament." });
            }

            int total = quizList.Count;
            for (int i = 0; i < total; i++)
            {
                var q = quizList[i];
                var session = _gameEngine.CreateSession(
                    quiz: q,
                    hostId: hostId,
                    autoAdvance: request.AutoAdvance,
                    tournamentId: tournamentId,
                    tournamentName: $"{tournamentName} - Session {i + 1}",
                    sessionNumber: i + 1,
                    totalSessions: total,
                    sessionType: "MultiSession"
                );

                createdSessions.Add(new SessionSummaryDto
                {
                    Pin = session.Pin,
                    QuizId = session.QuizId,
                    QuizTitle = session.Quiz.Title,
                    SessionNumber = session.SessionNumber,
                    TotalSessions = session.TotalSessions,
                    State = session.State.ToString(),
                    QuestionCount = session.Quiz.Questions.Count
                });
            }

            return Ok(new
            {
                tournamentId = tournamentId,
                tournamentName = tournamentName,
                sessionType = "MultiSession",
                hostId = hostId,
                totalSessions = total,
                pin = createdSessions[0].Pin, // First session active PIN
                sessions = createdSessions
            });
        }

        // 2. Single Session Match
        Quiz? singleQuiz = request.Quiz;
        if (singleQuiz == null && !string.IsNullOrWhiteSpace(request.QuizId))
        {
            singleQuiz = await _dataService.GetQuizByIdAsync(request.QuizId);
        }

        if (singleQuiz == null || singleQuiz.Questions.Count == 0)
        {
            return BadRequest(new { message = "Valid quiz with at least one question is required to launch session." });
        }

        var singleSession = _gameEngine.CreateSession(
            quiz: singleQuiz,
            hostId: hostId,
            autoAdvance: request.AutoAdvance,
            tournamentId: tournamentId,
            tournamentName: string.IsNullOrWhiteSpace(request.TournamentName) ? singleQuiz.Title : tournamentName,
            sessionNumber: 1,
            totalSessions: 1,
            sessionType: "Single"
        );

        return Ok(new
        {
            pin = singleSession.Pin,
            quizId = singleSession.QuizId,
            quizTitle = singleSession.Quiz.Title,
            tournamentId = singleSession.TournamentId,
            tournamentName = singleSession.TournamentName,
            sessionType = "Single",
            sessionNumber = 1,
            totalSessions = 1,
            questionCount = singleSession.Quiz.Questions.Count,
            state = singleSession.State.ToString(),
            hostId = singleSession.HostId,
            sessions = new[]
            {
                new SessionSummaryDto
                {
                    Pin = singleSession.Pin,
                    QuizId = singleSession.QuizId,
                    QuizTitle = singleSession.Quiz.Title,
                    SessionNumber = 1,
                    TotalSessions = 1,
                    State = singleSession.State.ToString(),
                    QuestionCount = singleSession.Quiz.Questions.Count
                }
            }
        });
    }

    /// <summary>Active sessions the caller can run: their own, or every session for admins.</summary>
    [Authorize(Policy = "HostOnly")]
    [HttpGet]
    public ActionResult GetActiveSessions([FromQuery] string? hostId = null)
    {
        var query = User.IsAdmin()
            ? (string.IsNullOrWhiteSpace(hostId) ? _gameEngine.GetAllActiveSessions() : _gameEngine.GetSessionsByHost(hostId))
            : _gameEngine.GetSessionsByHost(User.UserEmail()!);

        var sessions = query.OrderByDescending(s => s.CreatedAt).Select(s => new
        {
            pin = s.Pin,
            quizId = s.QuizId,
            quizTitle = s.Quiz.Title,
            tournamentId = s.TournamentId,
            tournamentName = s.TournamentName,
            sessionNumber = s.SessionNumber,
            totalSessions = s.TotalSessions,
            sessionType = s.SessionType,
            hostId = s.HostId,
            state = s.State.ToString(),
            connectedPlayers = s.ConnectedPlayerCount,
            totalPlayers = s.Players.Count,
            currentQuestionIndex = s.CurrentQuestionIndex,
            totalQuestions = s.Quiz.Questions.Count,
            createdAt = s.CreatedAt
        });

        return Ok(sessions);
    }

    [Authorize(Policy = "HostOnly")]
    [HttpGet("host/{hostId}")]
    public ActionResult GetSessionsByHost(string hostId)
    {
        if (!User.IsAdmin() && !string.Equals(hostId, User.UserEmail(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var sessions = _gameEngine.GetSessionsByHost(hostId).Select(s => new
        {
            pin = s.Pin,
            quizTitle = s.Quiz.Title,
            tournamentId = s.TournamentId,
            tournamentName = s.TournamentName,
            sessionNumber = s.SessionNumber,
            totalSessions = s.TotalSessions,
            state = s.State.ToString(),
            connectedPlayers = s.ConnectedPlayerCount
        });

        return Ok(sessions);
    }

    [HttpGet("tournament/{tournamentId}")]
    public ActionResult GetSessionsByTournament(string tournamentId)
    {
        var sessions = _gameEngine.GetSessionsByTournament(tournamentId).Select(s => new
        {
            pin = s.Pin,
            quizTitle = s.Quiz.Title,
            tournamentId = s.TournamentId,
            tournamentName = s.TournamentName,
            sessionNumber = s.SessionNumber,
            totalSessions = s.TotalSessions,
            state = s.State.ToString(),
            connectedPlayers = s.ConnectedPlayerCount
        });

        return Ok(sessions);
    }

    [HttpGet("{pin}")]
    public ActionResult GetSession(string pin)
    {
        var session = _gameEngine.GetSession(pin.ToUpperInvariant());
        if (session == null)
        {
            return NotFound(new { message = $"Session '{pin}' not found." });
        }

        return Ok(new
        {
            pin = session.Pin,
            quizTitle = session.Quiz.Title,
            tournamentId = session.TournamentId,
            tournamentName = session.TournamentName,
            sessionNumber = session.SessionNumber,
            totalSessions = session.TotalSessions,
            sessionType = session.SessionType,
            state = session.State.ToString(),
            currentQuestionIndex = session.CurrentQuestionIndex,
            totalQuestions = session.Quiz.Questions.Count,
            connectedPlayerCount = session.ConnectedPlayerCount,
            canControl = User.CanControl(session),
            players = session.Players.Values.Select(p => new
            {
                playerId = p.PlayerId,
                fullName = p.FullName,
                score = p.Score,
                rank = p.Rank,
                isConnected = p.IsConnected,
                streak = p.CurrentStreak
            })
        });
    }

    [Authorize(Policy = "HostOnly")]
    [HttpDelete("{pin}")]
    public async Task<ActionResult> CloseSession(string pin)
    {
        var cleanPin = pin.ToUpperInvariant();
        var session = _gameEngine.GetSession(cleanPin);
        if (session == null)
        {
            return NotFound(new { message = $"Session '{pin}' not found." });
        }
        if (!User.CanControl(session))
        {
            return Forbid();
        }

        await _hub.Clients.Group(cleanPin).SessionClosed(new { message = "The host ended this game." });
        _gameEngine.CloseSession(cleanPin);
        if (!string.IsNullOrEmpty(session.TournamentSessionId))
        {
            // A finished game already recorded its results; one ended early is discarded so it can be run again.
            _tournaments.EndLive(session.TournamentSessionId, finished: session.State == GameState.GameEnded, Array.Empty<LivePlayerResult>());
        }
        _logger.LogInformation("Session {Pin} closed by {User}", cleanPin, User.UserEmail());
        return Ok(new { message = $"Session '{pin}' closed." });
    }

    [Authorize(Policy = "HostOnly")]
    [HttpGet("{pin}/export/csv")]
    public ActionResult ExportSessionCsv(string pin)
    {
        var session = _gameEngine.GetSession(pin.ToUpperInvariant());
        if (session == null)
        {
            return NotFound(new { message = $"Session '{pin}' not found." });
        }
        if (!User.CanControl(session))
        {
            return Forbid();
        }

        var bytes = _exportService.ExportSessionToCsv(session);
        return File(bytes, "text/csv; charset=utf-8", $"Groove_Session_{pin}.csv");
    }

    [Authorize(Policy = "HostOnly")]
    [HttpGet("{pin}/export/excel")]
    public ActionResult ExportSessionExcel(string pin)
    {
        var session = _gameEngine.GetSession(pin.ToUpperInvariant());
        if (session == null)
        {
            return NotFound(new { message = $"Session '{pin}' not found." });
        }
        if (!User.CanControl(session))
        {
            return Forbid();
        }

        var bytes = _exportService.ExportSessionToExcelXml(session);
        return File(bytes, "application/vnd.ms-excel", $"Groove_Session_{pin}.xls");
    }
}
