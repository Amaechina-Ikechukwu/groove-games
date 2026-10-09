using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Data;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Controllers;

public class TournamentDetailsRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class JoinTournamentRequest
{
    public string Code { get; set; } = string.Empty;
}

public class UpdateSessionRequest
{
    public string Title { get; set; } = string.Empty;
    /// <summary>Omit to only rename the session.</summary>
    public List<TriviaSync.Api.Models.Question>? Questions { get; set; }
}

public class CreateTournamentSessionRequest
{
    /// <summary>Leave empty to start with no questions and write your own.</summary>
    public string QuizId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Mode { get; set; } = SessionModes.Live;
}

public class OpenSessionRequest
{
    public DateTime ClosesAt { get; set; }
}

/// <summary>Turns TournamentException into the matching HTTP response.</summary>
public class TournamentExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is TournamentException ex)
        {
            context.Result = new ObjectResult(new { message = ex.Message }) { StatusCode = ex.StatusCode };
            context.ExceptionHandled = true;
        }
    }
}

[ApiController]
[Authorize]
[Route("api/tournaments")]
[TypeFilter(typeof(TournamentExceptionFilter))]
public class TournamentsController : ControllerBase
{
    private readonly ITournamentService _tournaments;
    private readonly ITriviaDataService _data;
    private readonly IGameEngineService _engine;
    private readonly IAuthService _auth;
    private readonly IHubContext<QuizHub, IQuizClient> _hub;

    public TournamentsController(ITournamentService tournaments, ITriviaDataService data, IGameEngineService engine,
        IAuthService auth, IHubContext<QuizHub, IQuizClient> hub)
    {
        _tournaments = tournaments;
        _data = data;
        _engine = engine;
        _auth = auth;
        _hub = hub;
    }

    private string Email => User.UserEmail()!;

    private bool CanManage(TournamentEntity t) =>
        User.IsAdmin() || (User.CanHost() && t.HostEmail == Email);

    private TournamentEntity RequireTournament(string id) =>
        _tournaments.Get(id) ?? throw TournamentException.NotFound("Tournament");

    private TournamentEntity RequireManage(string id)
    {
        var t = RequireTournament(id);
        if (!CanManage(t)) throw TournamentException.Forbidden("Only this tournament's host can do that.");
        return t;
    }

    private TournamentEntity RequireView(string id)
    {
        var t = RequireTournament(id);
        if (!CanManage(t) && !_tournaments.IsMember(id, Email))
            throw TournamentException.Forbidden("Join this tournament to see it.");
        return t;
    }

    private TournamentSessionEntity RequireSession(TournamentEntity t, string sessionId)
    {
        var s = _tournaments.GetSession(sessionId);
        if (s == null || s.TournamentId != t.Id) throw TournamentException.NotFound("Session");
        return s;
    }

    private object Summary(TournamentEntity t)
    {
        var sessions = _tournaments.Sessions(t.Id);
        var host = _auth.FindUser(t.HostEmail);
        return new
        {
            id = t.Id,
            name = t.Name,
            description = t.Description,
            hostName = host?.DisplayName ?? t.HostEmail,
            hostEmail = CanManage(t) ? t.HostEmail : null,
            createdAt = t.CreatedAt,
            memberCount = _tournaments.Members(t.Id).Count,
            sessionCount = sessions.Count,
            openCount = sessions.Count(s => _tournaments.EffectiveStatus(s) is SessionStatuses.Open or SessionStatuses.Live),
            canManage = CanManage(t)
        };
    }

    private object SessionDto(TournamentEntity t, TournamentSessionEntity s, bool canManage, bool isMember)
    {
        var status = _tournaments.EffectiveStatus(s);
        var attempt = isMember ? _tournaments.AttemptFor(s.Id, Email) : null;
        return new
        {
            id = s.Id,
            title = s.Title,
            createdAt = s.CreatedAt,
            openedAt = s.OpenedAt,
            closedAt = s.ClosedAt,
            code = canManage ? s.Code : null,
            questionsLocked = canManage ? _tournaments.QuestionsLocked(s) : (bool?)null,
            mode = s.Mode,
            status,
            closesAt = s.ClosesAt,
            questionCount = _tournaments.QuizFor(s).Questions.Count,
            livePin = status == SessionStatuses.Live ? s.LivePin : null,
            attemptCount = canManage ? _tournaments.Attempts(s.Id).Count : (int?)null,
            myAttempt = attempt == null ? null : new
            {
                score = attempt.Score,
                correct = attempt.CorrectCount,
                answered = attempt.AnsweredCount,
                questionCount = attempt.QuestionCount,
                completed = attempt.CompletedAt != null
            }
        };
    }

    // -------------------------------------------------------------------------
    // Tournaments
    // -------------------------------------------------------------------------

    /// <summary>Tournaments the caller runs. Admins get every tournament.</summary>
    [Authorize(Policy = "HostOnly")]
    [HttpGet("hosting")]
    public ActionResult Hosting()
    {
        var list = User.IsAdmin() ? _tournaments.All() : _tournaments.HostedBy(Email);
        return Ok(list.Select(Summary));
    }

    /// <summary>Tournaments the caller has joined as a player.</summary>
    [HttpGet("joined")]
    public ActionResult Joined() => Ok(_tournaments.JoinedBy(Email).Select(Summary));

    [Authorize(Policy = "HostOnly")]
    [HttpPost]
    public ActionResult Create([FromBody] TournamentDetailsRequest request)
    {
        var t = _tournaments.Create(Email, request.Name, request.Description);
        return Ok(Summary(t));
    }

    [HttpGet("{id}")]
    public ActionResult Get(string id)
    {
        var t = RequireView(id);
        var canManage = CanManage(t);
        var isMember = _tournaments.IsMember(id, Email);
        return Ok(new
        {
            tournament = Summary(t),
            joinCode = canManage ? t.JoinCode : null,
            isMember,
            sessions = _tournaments.Sessions(id).Select(s => SessionDto(t, s, canManage, isMember))
        });
    }

    [HttpPut("{id}")]
    public ActionResult Update(string id, [FromBody] TournamentDetailsRequest request)
    {
        RequireManage(id);
        return Ok(Summary(_tournaments.Update(id, request.Name, request.Description)));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        RequireManage(id);
        foreach (var s in _tournaments.Sessions(id).Where(s => !string.IsNullOrEmpty(s.LivePin)))
        {
            await _hub.Clients.Group(s.LivePin!).SessionClosed(new { message = "The host deleted this tournament." });
            _engine.CloseSession(s.LivePin!);
        }
        _tournaments.Delete(id);
        return Ok(new { message = "Tournament deleted." });
    }

    /// <summary>Public list of tournaments for the standings page. No join codes or emails.</summary>
    [AllowAnonymous]
    [HttpGet("public")]
    public ActionResult PublicList()
    {
        return Ok(_tournaments.All().Select(t => new
        {
            id = t.Id,
            name = t.Name,
            hostName = _auth.FindUser(t.HostEmail)?.DisplayName ?? "Host",
            playerCount = _tournaments.Standings(t.Id).Count,
            createdAt = t.CreatedAt
        }));
    }

    /// <summary>Tournament standings are public. Emails are only included for the tournament's host and admins.</summary>
    [AllowAnonymous]
    [HttpGet("{id}/standings")]
    public ActionResult Standings(string id)
    {
        var t = RequireTournament(id);
        var me = User.UserEmail();
        var canManage = me != null && CanManage(t);
        var sessions = _tournaments.Sessions(id);
        return Ok(new
        {
            tournament = new
            {
                id = t.Id,
                name = t.Name,
                description = t.Description,
                createdAt = t.CreatedAt,
                hostName = _auth.FindUser(t.HostEmail)?.DisplayName ?? "Host",
                sessionCount = sessions.Count,
                playedCount = sessions.Count(s => _tournaments.EffectiveStatus(s) == SessionStatuses.Closed),
                activeCount = sessions.Count(s => _tournaments.EffectiveStatus(s) is SessionStatuses.Open or SessionStatuses.Live)
            },
            standings = _tournaments.Standings(id).Select(r => new
            {
                rank = r.Rank,
                displayName = r.DisplayName,
                email = canManage ? r.Email : null,
                isMe = me != null && r.Email == me,
                totalScore = r.TotalScore,
                sessionsPlayed = r.SessionsPlayed,
                correct = r.Correct,
                answered = r.Answered
            })
        });
    }

    /// <summary>Every tournament session the caller has played, newest first.</summary>
    [HttpGet("history")]
    public ActionResult History()
    {
        return Ok(_tournaments.AttemptsBy(Email).Select(a =>
        {
            var s = _tournaments.GetSession(a.SessionId);
            var t = _tournaments.Get(a.TournamentId);
            var everyone = _tournaments.Attempts(a.SessionId);
            return new
            {
                tournamentId = a.TournamentId,
                tournamentName = t?.Name ?? "Deleted tournament",
                sessionId = a.SessionId,
                sessionTitle = s?.Title ?? "Deleted session",
                mode = s?.Mode,
                status = s == null ? null : _tournaments.EffectiveStatus(s),
                score = a.Score,
                correct = a.CorrectCount,
                answered = a.AnsweredCount,
                questionCount = a.QuestionCount,
                completed = a.CompletedAt != null,
                rank = everyone.FindIndex(x => x.Id == a.Id) + 1,
                playerCount = everyone.Count,
                playedAt = a.CompletedAt ?? a.StartedAt
            };
        }));
    }

    // -------------------------------------------------------------------------
    // Membership
    // -------------------------------------------------------------------------
    [HttpPost("join")]
    public ActionResult Join([FromBody] JoinTournamentRequest request)
    {
        var user = _auth.FindUser(Email) ?? throw TournamentException.Forbidden("Sign in again.");
        var t = _tournaments.Join(request.Code, user.Email, user.DisplayName);
        return Ok(Summary(t));
    }

    [HttpDelete("{id}/members/me")]
    public ActionResult Leave(string id)
    {
        RequireTournament(id);
        if (!_tournaments.RemoveMember(id, Email))
            throw TournamentException.NotFound("Membership");
        return Ok(new { message = "You left the tournament." });
    }

    [HttpGet("{id}/members")]
    public ActionResult Members(string id)
    {
        RequireManage(id);
        var standings = _tournaments.Standings(id).ToDictionary(r => r.Email);
        return Ok(_tournaments.Members(id).Select(m => new
        {
            email = m.UserEmail,
            displayName = m.DisplayName,
            joinedAt = m.JoinedAt,
            totalScore = standings.GetValueOrDefault(m.UserEmail)?.TotalScore ?? 0,
            sessionsPlayed = standings.GetValueOrDefault(m.UserEmail)?.SessionsPlayed ?? 0
        }));
    }

    [HttpDelete("{id}/members/{email}")]
    public ActionResult RemoveMember(string id, string email)
    {
        RequireManage(id);
        if (!_tournaments.RemoveMember(id, email))
            throw TournamentException.NotFound("Member");
        return Ok(new { message = "Member removed." });
    }

    // -------------------------------------------------------------------------
    // Sessions
    // -------------------------------------------------------------------------
    [HttpPost("{id}/sessions")]
    public async Task<ActionResult> CreateSession(string id, [FromBody] CreateTournamentSessionRequest request)
    {
        var t = RequireManage(id);
        TriviaSync.Api.Models.Quiz quiz;
        if (string.IsNullOrWhiteSpace(request.QuizId))
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                throw TournamentException.Invalid("Give the session a name.");
            quiz = new TriviaSync.Api.Models.Quiz { Id = $"custom_{Guid.NewGuid():N}"[..15], Title = request.Title.Trim(), CreatedBy = Email };
        }
        else
        {
            quiz = await _data.GetQuizByIdAsync(request.QuizId) ?? throw TournamentException.NotFound("Quiz");
        }
        var s = _tournaments.CreateSession(id, request.Title, quiz, request.Mode);
        return Ok(SessionDto(t, s, true, false));
    }

    /// <summary>Full session for editing, including the correct answers. Managers only.</summary>
    [HttpGet("{id}/sessions/{sessionId}")]
    public ActionResult GetSessionForEdit(string id, string sessionId)
    {
        var t = RequireManage(id);
        var s = RequireSession(t, sessionId);
        return Ok(new
        {
            session = SessionDto(t, s, true, false),
            questions = _tournaments.QuizFor(s).Questions
        });
    }

    [HttpPut("{id}/sessions/{sessionId}")]
    public ActionResult UpdateSession(string id, string sessionId, [FromBody] UpdateSessionRequest request)
    {
        var t = RequireManage(id);
        RequireSession(t, sessionId);
        var s = _tournaments.UpdateSession(sessionId, request.Title, request.Questions);
        return Ok(new
        {
            session = SessionDto(t, s, true, false),
            questions = _tournaments.QuizFor(s).Questions
        });
    }

    [HttpDelete("{id}/sessions/{sessionId}")]
    public async Task<ActionResult> DeleteSession(string id, string sessionId)
    {
        var t = RequireManage(id);
        var s = RequireSession(t, sessionId);
        if (!string.IsNullOrEmpty(s.LivePin))
        {
            await _hub.Clients.Group(s.LivePin).SessionClosed(new { message = "The host removed this session." });
            _engine.CloseSession(s.LivePin);
        }
        _tournaments.DeleteSession(sessionId);
        return Ok(new { message = "Session deleted." });
    }

    [HttpPost("{id}/sessions/{sessionId}/open")]
    public ActionResult OpenSession(string id, string sessionId, [FromBody] OpenSessionRequest request)
    {
        var t = RequireManage(id);
        RequireSession(t, sessionId);
        return Ok(SessionDto(t, _tournaments.OpenSession(sessionId, request.ClosesAt), true, false));
    }

    [HttpPost("{id}/sessions/{sessionId}/close")]
    public ActionResult CloseSession(string id, string sessionId)
    {
        var t = RequireManage(id);
        RequireSession(t, sessionId);
        return Ok(SessionDto(t, _tournaments.CloseSession(sessionId), true, false));
    }

    /// <summary>Starts (or returns the already running) live game for a live session.</summary>
    [HttpPost("{id}/sessions/{sessionId}/live")]
    public ActionResult StartLive(string id, string sessionId, [FromQuery] bool autoAdvance = true)
    {
        var t = RequireManage(id);
        var s = RequireSession(t, sessionId);
        if (s.Mode != SessionModes.Live)
            throw TournamentException.Invalid("This session is self-paced. Open it with a deadline instead.");
        if (s.Status == SessionStatuses.Closed)
            throw TournamentException.Conflict("This session has already been played.");

        if (!string.IsNullOrEmpty(s.LivePin) && _engine.GetSession(s.LivePin) != null)
            return Ok(new { pin = s.LivePin });
        _tournaments.RequirePlayableContent(s);

        var game = _engine.CreateSession(
            quiz: _tournaments.QuizFor(s),
            hostId: t.HostEmail,
            autoAdvance: autoAdvance,
            tournamentId: t.Id,
            tournamentName: t.Name,
            sessionType: "Tournament",
            tournamentSessionId: s.Id,
            preferredPin: s.Code);
        _tournaments.SetLive(s.Id, game.Pin);
        return Ok(new { pin = game.Pin });
    }

    [HttpGet("{id}/sessions/{sessionId}/results")]
    public ActionResult Results(string id, string sessionId)
    {
        var t = RequireManage(id);
        var s = RequireSession(t, sessionId);
        return Ok(new
        {
            session = SessionDto(t, s, true, false),
            attempts = _tournaments.Attempts(sessionId).Select(a => new
            {
                displayName = a.DisplayName,
                email = a.UserEmail,
                score = a.Score,
                correct = a.CorrectCount,
                answered = a.AnsweredCount,
                questionCount = a.QuestionCount,
                completed = a.CompletedAt != null,
                startedAt = a.StartedAt,
                completedAt = a.CompletedAt
            })
        });
    }
}

public class AnswerRequest
{
    public int QuestionIndex { get; set; }
    public int ChoiceIndex { get; set; }
}

/// <summary>Self-paced play. The server serves one question at a time and times each answer.</summary>
[ApiController]
[Authorize]
[Route("api/play")]
[TypeFilter(typeof(TournamentExceptionFilter))]
public class PlayController : ControllerBase
{
    private readonly ITournamentService _tournaments;
    private readonly IAuthService _auth;

    public PlayController(ITournamentService tournaments, IAuthService auth)
    {
        _tournaments = tournaments;
        _auth = auth;
    }

    /// <summary>
    /// Tells the join screen what an access code is for, so a self-paced code can send the player to the quiz
    /// and a live code can say the host hasn't started yet. Reveals nothing beyond the session's name and status.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("code/{code}")]
    public ActionResult ResolveCode(string code)
    {
        var s = _tournaments.FindSessionByCode(code);
        if (s == null) return NotFound(new { message = "No session uses that code." });
        return Ok(new
        {
            sessionId = s.Id,
            title = s.Title,
            mode = s.Mode,
            status = _tournaments.EffectiveStatus(s)
        });
    }

    /// <summary>Describes a session for the intro screen without starting the attempt or any timer.</summary>
    [HttpGet("{sessionId}")]
    public ActionResult Describe(string sessionId)
    {
        var email = User.UserEmail()!;
        var s = _tournaments.GetSession(sessionId) ?? throw TournamentException.NotFound("Session");
        var t = _tournaments.Get(s.TournamentId) ?? throw TournamentException.NotFound("Tournament");
        if (!_tournaments.IsMember(t.Id, email))
            throw TournamentException.Forbidden("Join this tournament to play its sessions.");
        var attempt = _tournaments.AttemptFor(sessionId, email);
        return Ok(new
        {
            sessionId = s.Id,
            title = s.Title,
            mode = s.Mode,
            tournamentId = t.Id,
            tournamentName = t.Name,
            status = _tournaments.EffectiveStatus(s),
            closesAt = s.ClosesAt,
            totalQuestions = _tournaments.QuizFor(s).Questions.Count,
            started = attempt != null,
            done = attempt?.CompletedAt != null,
            answered = attempt?.AnsweredCount ?? 0,
            score = attempt?.Score ?? 0,
            correctCount = attempt?.CorrectCount ?? 0
        });
    }

    [HttpPost("{sessionId}/start")]
    public ActionResult Start(string sessionId)
    {
        var user = _auth.FindUser(User.UserEmail()!) ?? throw TournamentException.Forbidden("Sign in again.");
        return Ok(_tournaments.StartOrResume(sessionId, user.Email, user.DisplayName));
    }

    [HttpPost("{sessionId}/answer")]
    public ActionResult Answer(string sessionId, [FromBody] AnswerRequest request)
    {
        var (result, state) = _tournaments.Answer(sessionId, User.UserEmail()!, request.QuestionIndex, request.ChoiceIndex);
        return Ok(new { result, state });
    }
}
