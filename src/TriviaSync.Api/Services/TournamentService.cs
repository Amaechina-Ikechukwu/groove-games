using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TriviaSync.Api.Data;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

/// <summary>A failure that maps directly to an HTTP status code and a message safe to show users.</summary>
public class TournamentException : Exception
{
    public int StatusCode { get; }
    public TournamentException(int statusCode, string message) : base(message) => StatusCode = statusCode;

    public static TournamentException NotFound(string what) => new(404, $"{what} not found.");
    public static TournamentException Forbidden(string message) => new(403, message);
    public static TournamentException Conflict(string message) => new(409, message);
    public static TournamentException Invalid(string message) => new(400, message);
}

public static class SessionModes
{
    public const string Live = "Live";
    public const string SelfPaced = "SelfPaced";
}

public static class SessionStatuses
{
    public const string Draft = "Draft";
    public const string Open = "Open";
    public const string Live = "Live";
    public const string Closed = "Closed";
}

public record LivePlayerResult(string Email, string DisplayName, int Score, int Correct, int Answered);

public record PlayQuestion(int Index, int Number, string Text, List<string> Choices, int TimeLimit, double Remaining, int Points);

public record PlayState(
    string SessionId, string Title, string TournamentId, string TournamentName, DateTime? ClosesAt,
    int TotalQuestions, int Answered, int Score, int CorrectCount, bool Done, PlayQuestion? Question);

public record AnswerResult(bool IsCorrect, bool TimedOut, int CorrectIndex, int PointsEarned);

public record StandingRow(int Rank, string Email, string DisplayName, int TotalScore, int SessionsPlayed, int Correct, int Answered);

public record AttemptAnswer(int Index, int Choice, bool Correct, int Points, int Ms);

public interface ITournamentService
{
    TournamentEntity Create(string hostEmail, string name, string description);
    TournamentEntity? Get(string id);
    TournamentEntity? FindByJoinCode(string code);
    List<TournamentEntity> All();
    List<TournamentEntity> HostedBy(string email);
    List<TournamentEntity> JoinedBy(string email);
    TournamentEntity Update(string id, string name, string description);
    void Delete(string id);

    bool IsMember(string tournamentId, string email);
    List<TournamentMemberEntity> Members(string tournamentId);
    TournamentEntity Join(string code, string email, string displayName);
    void EnsureMember(string tournamentId, string email, string displayName);
    bool RemoveMember(string tournamentId, string email);

    List<TournamentSessionEntity> Sessions(string tournamentId);
    TournamentSessionEntity? GetSession(string sessionId);
    TournamentSessionEntity CreateSession(string tournamentId, string title, Quiz quiz, string mode);
    TournamentSessionEntity UpdateSession(string sessionId, string title, List<Question>? questions);
    TournamentSessionEntity? FindSessionByCode(string code);
    bool CodeInUse(string code);
    bool QuestionsLocked(TournamentSessionEntity session);
    void RequirePlayableContent(TournamentSessionEntity session);
    void DeleteSession(string sessionId);
    TournamentSessionEntity OpenSession(string sessionId, DateTime closesAtUtc);
    TournamentSessionEntity CloseSession(string sessionId);
    void SetLive(string sessionId, string pin);
    void EndLive(string sessionId, bool finished, IEnumerable<LivePlayerResult> results);
    string EffectiveStatus(TournamentSessionEntity session);
    Quiz QuizFor(TournamentSessionEntity session);

    List<SessionAttemptEntity> Attempts(string sessionId);
    List<SessionAttemptEntity> AttemptsBy(string email);
    SessionAttemptEntity? AttemptFor(string sessionId, string email);
    PlayState StartOrResume(string sessionId, string email, string displayName);
    (AnswerResult? Result, PlayState State) Answer(string sessionId, string email, int questionIndex, int choiceIndex);

    List<StandingRow> Standings(string tournamentId);
}

/// <summary>
/// Tournaments, memberships, sessions and attempts. Everything is cached in memory and written
/// through to PostgreSQL when it's available (memory-only otherwise), like UserStore.
/// </summary>
public class TournamentService : ITournamentService
{
    private static readonly TimeSpan AnswerGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxOpenWindow = TimeSpan.FromDays(120);
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<TournamentService>? _logger;
    private readonly bool _usePostgres;
    private readonly object _lock = new();

    private readonly Dictionary<string, TournamentEntity> _tournaments = new();
    private readonly Dictionary<(string, string), TournamentMemberEntity> _members = new();
    private readonly Dictionary<string, TournamentSessionEntity> _sessions = new();
    private readonly Dictionary<string, SessionAttemptEntity> _attempts = new();

    /// <summary>Memory-only (tests, or no database configured).</summary>
    public TournamentService()
    {
    }

    public TournamentService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<TournamentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var connString = config.GetConnectionString("DefaultConnection")
                         ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connString))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            db.Database.EnsureCreated();
            // EnsureCreated does nothing on databases that predate these tables.
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS tournaments (
                    id text PRIMARY KEY, name text NOT NULL, description text NOT NULL,
                    host_email text NOT NULL, join_code text NOT NULL, created_at timestamp with time zone NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_tournaments_join_code ON tournaments (join_code);
                CREATE TABLE IF NOT EXISTS tournament_members (
                    tournament_id text NOT NULL, user_email text NOT NULL, display_name text NOT NULL,
                    joined_at timestamp with time zone NOT NULL, PRIMARY KEY (tournament_id, user_email));
                CREATE TABLE IF NOT EXISTS tournament_sessions (
                    id text PRIMARY KEY, tournament_id text NOT NULL, title text NOT NULL, quiz_id text NOT NULL,
                    quiz_json text NOT NULL, mode text NOT NULL, status text NOT NULL,
                    closes_at timestamp with time zone NULL, live_pin text NULL, code text NOT NULL DEFAULT '',
                    opened_at timestamp with time zone NULL, closed_at timestamp with time zone NULL,
                    created_at timestamp with time zone NOT NULL);
                ALTER TABLE tournament_sessions ADD COLUMN IF NOT EXISTS code text NOT NULL DEFAULT '';
                ALTER TABLE tournament_sessions ADD COLUMN IF NOT EXISTS opened_at timestamp with time zone NULL;
                ALTER TABLE tournament_sessions ADD COLUMN IF NOT EXISTS closed_at timestamp with time zone NULL;
                CREATE TABLE IF NOT EXISTS session_attempts (
                    id text PRIMARY KEY, session_id text NOT NULL, tournament_id text NOT NULL, user_email text NOT NULL,
                    display_name text NOT NULL, started_at timestamp with time zone NOT NULL, completed_at timestamp with time zone NULL,
                    score integer NOT NULL, correct_count integer NOT NULL, answered_count integer NOT NULL,
                    question_count integer NOT NULL, current_index integer NOT NULL,
                    current_served_at timestamp with time zone NULL, answers_json text NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_session_attempts_user ON session_attempts (session_id, user_email);");

            foreach (var t in db.Tournaments.AsNoTracking()) _tournaments[t.Id] = t;
            foreach (var m in db.TournamentMembers.AsNoTracking()) _members[(m.TournamentId, m.UserEmail)] = m;
            foreach (var s in db.TournamentSessions.AsNoTracking()) _sessions[s.Id] = s;
            foreach (var a in db.SessionAttempts.AsNoTracking()) _attempts[a.Id] = a;
            _usePostgres = true;

            foreach (var s in _sessions.Values.Where(s => string.IsNullOrEmpty(s.Code)).ToList())
            {
                s.Code = NewSessionCode();
                Persist(db2 => db2.TournamentSessions.Update(s));
            }

            // Live games only exist in memory, so any that were running before a restart are gone.
            foreach (var s in _sessions.Values.Where(s => s.Status == SessionStatuses.Live).ToList())
            {
                s.Status = SessionStatuses.Draft;
                s.LivePin = null;
                s.OpenedAt = null;
                s.ClosedAt = null;
                Persist(db2 => db2.TournamentSessions.Update(s));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tournament store could not reach PostgreSQL. Tournaments will not survive a restart.");
        }
    }

    private void Persist(Action<TriviaDbContext> apply)
    {
        if (!_usePostgres)
        {
            return;
        }
        using var scope = _scopeFactory!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        apply(db);
        db.SaveChanges();
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // -------------------------------------------------------------------------
    // Tournaments
    // -------------------------------------------------------------------------
    public TournamentEntity Create(string hostEmail, string name, string description)
    {
        var (cleanName, cleanDescription) = ValidateDetails(name, description);
        lock (_lock)
        {
            var t = new TournamentEntity
            {
                Id = $"trn_{Guid.NewGuid():N}"[..14],
                Name = cleanName,
                Description = cleanDescription,
                HostEmail = Normalize(hostEmail),
                JoinCode = NewJoinCode(),
                CreatedAt = DateTime.UtcNow
            };
            Persist(db => db.Tournaments.Add(t));
            _tournaments[t.Id] = t;
            return t;
        }
    }

    private static (string, string) ValidateDetails(string? name, string? description)
    {
        var cleanName = (name ?? string.Empty).Trim();
        var cleanDescription = (description ?? string.Empty).Trim();
        if (cleanName.Length is < 2 or > 80)
            throw TournamentException.Invalid("Tournament name must be between 2 and 80 characters.");
        if (cleanDescription.Length > 300)
            throw TournamentException.Invalid("Description can be at most 300 characters.");
        return (cleanName, cleanDescription);
    }

    private string NewJoinCode()
    {
        while (true)
        {
            var code = string.Concat(Enumerable.Range(0, 6).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]));
            if (!_tournaments.Values.Any(t => t.JoinCode == code))
                return code;
        }
    }

    public TournamentEntity? Get(string id)
    {
        lock (_lock) return _tournaments.GetValueOrDefault(id);
    }

    public TournamentEntity? FindByJoinCode(string code)
    {
        var clean = (code ?? string.Empty).Trim().ToUpperInvariant();
        lock (_lock) return _tournaments.Values.FirstOrDefault(t => t.JoinCode == clean);
    }

    public List<TournamentEntity> All()
    {
        lock (_lock) return _tournaments.Values.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public List<TournamentEntity> HostedBy(string email)
    {
        var e = Normalize(email);
        lock (_lock) return _tournaments.Values.Where(t => t.HostEmail == e).OrderByDescending(t => t.CreatedAt).ToList();
    }

    public List<TournamentEntity> JoinedBy(string email)
    {
        var e = Normalize(email);
        lock (_lock)
        {
            return _members.Values.Where(m => m.UserEmail == e)
                .OrderByDescending(m => m.JoinedAt)
                .Select(m => _tournaments.GetValueOrDefault(m.TournamentId))
                .Where(t => t != null).Cast<TournamentEntity>().ToList();
        }
    }

    public TournamentEntity Update(string id, string name, string description)
    {
        var (cleanName, cleanDescription) = ValidateDetails(name, description);
        lock (_lock)
        {
            var t = _tournaments.GetValueOrDefault(id) ?? throw TournamentException.NotFound("Tournament");
            t.Name = cleanName;
            t.Description = cleanDescription;
            Persist(db => db.Tournaments.Update(t));
            return t;
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            if (!_tournaments.ContainsKey(id))
                throw TournamentException.NotFound("Tournament");

            Persist(db =>
            {
                db.SessionAttempts.RemoveRange(db.SessionAttempts.Where(a => a.TournamentId == id));
                db.TournamentSessions.RemoveRange(db.TournamentSessions.Where(s => s.TournamentId == id));
                db.TournamentMembers.RemoveRange(db.TournamentMembers.Where(m => m.TournamentId == id));
                db.Tournaments.RemoveRange(db.Tournaments.Where(t => t.Id == id));
            });

            foreach (var a in _attempts.Values.Where(a => a.TournamentId == id).ToList()) _attempts.Remove(a.Id);
            foreach (var s in _sessions.Values.Where(s => s.TournamentId == id).ToList()) _sessions.Remove(s.Id);
            foreach (var key in _members.Keys.Where(k => k.Item1 == id).ToList()) _members.Remove(key);
            _tournaments.Remove(id);
        }
    }

    // -------------------------------------------------------------------------
    // Membership
    // -------------------------------------------------------------------------
    public bool IsMember(string tournamentId, string email)
    {
        lock (_lock) return _members.ContainsKey((tournamentId, Normalize(email)));
    }

    public List<TournamentMemberEntity> Members(string tournamentId)
    {
        lock (_lock) return _members.Values.Where(m => m.TournamentId == tournamentId).OrderBy(m => m.DisplayName).ToList();
    }

    public TournamentEntity Join(string code, string email, string displayName)
    {
        var t = FindByJoinCode(code) ?? throw new TournamentException(404, "No tournament uses that code. Check it and try again.");
        if (t.HostEmail == Normalize(email))
            throw TournamentException.Conflict("You host this tournament, so you can't join it as a player.");
        EnsureMember(t.Id, email, displayName);
        return t;
    }

    public void EnsureMember(string tournamentId, string email, string displayName)
    {
        var e = Normalize(email);
        lock (_lock)
        {
            if (_members.ContainsKey((tournamentId, e)))
                return;
            var m = new TournamentMemberEntity { TournamentId = tournamentId, UserEmail = e, DisplayName = displayName, JoinedAt = DateTime.UtcNow };
            Persist(db => db.TournamentMembers.Add(m));
            _members[(tournamentId, e)] = m;
        }
    }

    /// <summary>Removes the membership. Past scores stay in the standings.</summary>
    public bool RemoveMember(string tournamentId, string email)
    {
        var e = Normalize(email);
        lock (_lock)
        {
            if (!_members.TryGetValue((tournamentId, e), out var m))
                return false;
            Persist(db => db.TournamentMembers.Remove(m));
            _members.Remove((tournamentId, e));
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Sessions
    // -------------------------------------------------------------------------
    public List<TournamentSessionEntity> Sessions(string tournamentId)
    {
        lock (_lock) return _sessions.Values.Where(s => s.TournamentId == tournamentId).OrderBy(s => s.CreatedAt).ToList();
    }

    public TournamentSessionEntity? GetSession(string sessionId)
    {
        lock (_lock) return _sessions.GetValueOrDefault(sessionId);
    }

    public TournamentSessionEntity CreateSession(string tournamentId, string title, Quiz quiz, string mode)
    {
        if (mode != SessionModes.Live && mode != SessionModes.SelfPaced)
            throw TournamentException.Invalid("Mode must be Live or SelfPaced.");
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? quiz.Title : title.Trim();
        if (cleanTitle.Length is < 1 or > 100)
            throw TournamentException.Invalid("Give the session a name of up to 100 characters.");

        lock (_lock)
        {
            if (!_tournaments.ContainsKey(tournamentId))
                throw TournamentException.NotFound("Tournament");

            var s = new TournamentSessionEntity
            {
                Id = $"ses_{Guid.NewGuid():N}"[..16],
                TournamentId = tournamentId,
                Title = cleanTitle,
                QuizId = quiz.Id,
                QuizJson = JsonSerializer.Serialize(quiz),
                Code = NewSessionCode(),
                Mode = mode,
                Status = SessionStatuses.Draft,
                CreatedAt = DateTime.UtcNow
            };
            Persist(db => db.TournamentSessions.Add(s));
            _sessions[s.Id] = s;
            return s;
        }
    }

    public void DeleteSession(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.ContainsKey(sessionId))
                throw TournamentException.NotFound("Session");
            Persist(db =>
            {
                db.SessionAttempts.RemoveRange(db.SessionAttempts.Where(a => a.SessionId == sessionId));
                db.TournamentSessions.RemoveRange(db.TournamentSessions.Where(s => s.Id == sessionId));
            });
            foreach (var a in _attempts.Values.Where(a => a.SessionId == sessionId).ToList()) _attempts.Remove(a.Id);
            _sessions.Remove(sessionId);
        }
    }

    public TournamentSessionEntity OpenSession(string sessionId, DateTime closesAtUtc)
    {
        var closesAt = closesAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(closesAtUtc, DateTimeKind.Utc)
            : closesAtUtc.ToUniversalTime();
        if (closesAt <= DateTime.UtcNow.AddMinutes(1))
            throw TournamentException.Invalid("The deadline must be in the future.");
        if (closesAt > DateTime.UtcNow.Add(MaxOpenWindow))
            throw TournamentException.Invalid("The deadline can be at most 120 days away.");

        lock (_lock)
        {
            var s = _sessions.GetValueOrDefault(sessionId) ?? throw TournamentException.NotFound("Session");
            if (s.Mode != SessionModes.SelfPaced)
                throw TournamentException.Invalid("Only self-paced sessions can be opened with a deadline.");
            RequirePlayableContent(s);
            s.OpenedAt ??= DateTime.UtcNow;
            s.ClosedAt = null;
            s.Status = SessionStatuses.Open;
            s.ClosesAt = closesAt;
            Persist(db => db.TournamentSessions.Update(s));
            return s;
        }
    }

    public TournamentSessionEntity CloseSession(string sessionId)
    {
        lock (_lock)
        {
            var s = _sessions.GetValueOrDefault(sessionId) ?? throw TournamentException.NotFound("Session");
            if (s.Mode != SessionModes.SelfPaced)
                throw TournamentException.Invalid("Live sessions end when the host ends the game.");
            s.Status = SessionStatuses.Closed;
            s.ClosedAt = DateTime.UtcNow;
            Persist(db => db.TournamentSessions.Update(s));
            return s;
        }
    }

    public void SetLive(string sessionId, string pin)
    {
        lock (_lock)
        {
            var s = _sessions.GetValueOrDefault(sessionId) ?? throw TournamentException.NotFound("Session");
            s.Status = SessionStatuses.Live;
            s.LivePin = pin;
            s.OpenedAt = DateTime.UtcNow;
            s.ClosedAt = null;
            Persist(db => db.TournamentSessions.Update(s));
        }
    }

    /// <summary>
    /// Called when a live game for this session stops. A finished game records everyone's result
    /// and closes the session; a game ended early is discarded so the host can run it again.
    /// </summary>
    public void EndLive(string sessionId, bool finished, IEnumerable<LivePlayerResult> results)
    {
        lock (_lock)
        {
            var s = _sessions.GetValueOrDefault(sessionId);
            if (s == null)
                return;

            s.LivePin = null;
            s.Status = finished ? SessionStatuses.Closed : SessionStatuses.Draft;
            // A game ended early is discarded, so it goes back to never having run.
            s.ClosedAt = finished ? DateTime.UtcNow : null;
            if (!finished) s.OpenedAt = null;
            Persist(db => db.TournamentSessions.Update(s));

            if (!finished)
                return;

            var questionCount = QuizFor(s).Questions.Count;
            foreach (var r in results.Where(r => !string.IsNullOrWhiteSpace(r.Email)))
            {
                var email = Normalize(r.Email);
                var existing = _attempts.Values.FirstOrDefault(a => a.SessionId == sessionId && a.UserEmail == email);
                var attempt = existing ?? new SessionAttemptEntity
                {
                    SessionId = sessionId,
                    TournamentId = s.TournamentId,
                    UserEmail = email,
                    StartedAt = DateTime.UtcNow
                };
                attempt.DisplayName = r.DisplayName;
                attempt.Score = r.Score;
                attempt.CorrectCount = r.Correct;
                attempt.AnsweredCount = questionCount;
                attempt.QuestionCount = questionCount;
                attempt.CurrentIndex = questionCount;
                attempt.CompletedAt = DateTime.UtcNow;

                Persist(db =>
                {
                    if (existing == null) db.SessionAttempts.Add(attempt);
                    else db.SessionAttempts.Update(attempt);
                });
                _attempts[attempt.Id] = attempt;
            }
        }
    }

    public string EffectiveStatus(TournamentSessionEntity session)
    {
        if (session.Status == SessionStatuses.Open && session.ClosesAt <= DateTime.UtcNow)
            return SessionStatuses.Closed;
        return session.Status;
    }

    public Quiz QuizFor(TournamentSessionEntity session) =>
        JsonSerializer.Deserialize<Quiz>(session.QuizJson, Json) ?? new Quiz();

    // -------------------------------------------------------------------------
    // Editing & access codes
    // -------------------------------------------------------------------------
    public const int MaxQuestions = 100;

    /// <summary>Questions can't change once anyone has played, or while a live game is running.</summary>
    public bool QuestionsLocked(TournamentSessionEntity session)
    {
        lock (_lock)
        {
            return session.Status == SessionStatuses.Live || _attempts.Values.Any(a => a.SessionId == session.Id);
        }
    }

    public void RequirePlayableContent(TournamentSessionEntity session)
    {
        if (QuizFor(session).Questions.Count == 0)
            throw TournamentException.Invalid("Add at least one question first.");
    }

    public TournamentSessionEntity UpdateSession(string sessionId, string title, List<Question>? questions)
    {
        var cleanTitle = (title ?? string.Empty).Trim();
        if (cleanTitle.Length is < 1 or > 100)
            throw TournamentException.Invalid("Give the session a name of up to 100 characters.");

        lock (_lock)
        {
            var s = _sessions.GetValueOrDefault(sessionId) ?? throw TournamentException.NotFound("Session");

            if (questions != null)
            {
                if (s.Status == SessionStatuses.Live)
                    throw TournamentException.Conflict("A live game is running. End it before changing the questions.");
                if (_attempts.Values.Any(a => a.SessionId == sessionId))
                    throw TournamentException.Conflict("Players have already played this session, so its questions can't change. You can still rename it.");

                var cleaned = ValidateQuestions(questions);
                var quiz = QuizFor(s);
                quiz.Questions = cleaned;
                s.QuizJson = JsonSerializer.Serialize(quiz);
            }

            s.Title = cleanTitle;
            Persist(db => db.TournamentSessions.Update(s));
            return s;
        }
    }

    private static List<Question> ValidateQuestions(List<Question> questions)
    {
        if (questions.Count > MaxQuestions)
            throw TournamentException.Invalid($"A session can have at most {MaxQuestions} questions.");

        var result = new List<Question>();
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var n = i + 1;
            var text = (q.Text ?? string.Empty).Trim();
            if (text.Length is < 1 or > 300)
                throw TournamentException.Invalid($"Question {n} needs text of up to 300 characters.");

            var choices = (q.Choices ?? new List<string>()).Select(c => (c ?? string.Empty).Trim()).ToList();
            if (choices.Count is < 2 or > 6)
                throw TournamentException.Invalid($"Question {n} needs between 2 and 6 answers.");
            if (choices.Any(c => c.Length is < 1 or > 200))
                throw TournamentException.Invalid($"Every answer in question {n} needs text of up to 200 characters.");
            if (choices.Select(c => c.ToLowerInvariant()).Distinct().Count() != choices.Count)
                throw TournamentException.Invalid($"Question {n} has two identical answers.");
            if (q.CorrectIndex < 0 || q.CorrectIndex >= choices.Count)
                throw TournamentException.Invalid($"Mark which answer is correct in question {n}.");
            if (q.TimeLimitSeconds is < 5 or > 120)
                throw TournamentException.Invalid($"Question {n}: the time limit must be between 5 and 120 seconds.");
            if (q.Points is < 0 or > 5000)
                throw TournamentException.Invalid($"Question {n}: points must be between 0 and 5,000.");

            result.Add(new Question
            {
                QuestionId = string.IsNullOrWhiteSpace(q.QuestionId) ? Guid.NewGuid().ToString("N") : q.QuestionId,
                Text = text,
                Choices = choices,
                CorrectIndex = q.CorrectIndex,
                TimeLimitSeconds = q.TimeLimitSeconds,
                Points = q.Points
            });
        }
        return result;
    }

    public TournamentSessionEntity? FindSessionByCode(string code)
    {
        var clean = (code ?? string.Empty).Trim();
        lock (_lock) return _sessions.Values.FirstOrDefault(s => s.Code == clean);
    }

    public bool CodeInUse(string code)
    {
        lock (_lock) return _sessions.Values.Any(s => s.Code == code);
    }

    /// <summary>A 6-digit code, the same shape as a live PIN so players type one kind of code.</summary>
    private string NewSessionCode()
    {
        while (true)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            if (!_sessions.Values.Any(s => s.Code == code))
                return code;
        }
    }

    // -------------------------------------------------------------------------
    // Self-paced play
    // -------------------------------------------------------------------------
    public List<SessionAttemptEntity> Attempts(string sessionId)
    {
        lock (_lock) return _attempts.Values.Where(a => a.SessionId == sessionId).OrderByDescending(a => a.Score).ToList();
    }

    public List<SessionAttemptEntity> AttemptsBy(string email)
    {
        var e = Normalize(email);
        lock (_lock) return _attempts.Values.Where(a => a.UserEmail == e).OrderByDescending(a => a.CompletedAt ?? a.StartedAt).ToList();
    }

    public SessionAttemptEntity? AttemptFor(string sessionId, string email)
    {
        var e = Normalize(email);
        lock (_lock) return _attempts.Values.FirstOrDefault(a => a.SessionId == sessionId && a.UserEmail == e);
    }

    private TournamentSessionEntity RequirePlayable(string sessionId, string email)
    {
        var s = _sessions.GetValueOrDefault(sessionId) ?? throw TournamentException.NotFound("Session");
        if (s.Mode != SessionModes.SelfPaced)
            throw TournamentException.Invalid("This session is played live. Join with the PIN on the host's screen.");
        if (!_members.ContainsKey((s.TournamentId, Normalize(email))))
            throw TournamentException.Forbidden("Join this tournament to play its sessions.");
        return s;
    }

    public PlayState StartOrResume(string sessionId, string email, string displayName)
    {
        lock (_lock)
        {
            var s = RequirePlayable(sessionId, email);
            var quiz = QuizFor(s);
            var attempt = _attempts.Values.FirstOrDefault(a => a.SessionId == sessionId && a.UserEmail == Normalize(email));
            var open = EffectiveStatus(s) == SessionStatuses.Open;

            if (attempt == null)
            {
                if (!open)
                    throw TournamentException.Conflict(s.Status == SessionStatuses.Draft ? "This session isn't open yet." : "This session has closed.");
                attempt = new SessionAttemptEntity
                {
                    SessionId = sessionId,
                    TournamentId = s.TournamentId,
                    UserEmail = Normalize(email),
                    DisplayName = displayName,
                    StartedAt = DateTime.UtcNow,
                    QuestionCount = quiz.Questions.Count
                };
                var created = attempt;
                Persist(db => db.SessionAttempts.Add(created));
                _attempts[attempt.Id] = attempt;
            }

            if (attempt.CompletedAt == null && open)
            {
                // A question left unanswered past its time (e.g. the player closed the tab) counts as a miss.
                while (attempt.CurrentServedAt != null && attempt.CurrentIndex < quiz.Questions.Count)
                {
                    var q = quiz.Questions[attempt.CurrentIndex];
                    if (DateTime.UtcNow - attempt.CurrentServedAt.Value <= TimeSpan.FromSeconds(q.TimeLimitSeconds) + AnswerGrace)
                        break;
                    RecordAnswer(attempt, quiz, -1, TimeSpan.FromSeconds(q.TimeLimitSeconds));
                }

                if (attempt.CurrentIndex < quiz.Questions.Count && attempt.CurrentServedAt == null)
                {
                    attempt.CurrentServedAt = DateTime.UtcNow;
                }
                SaveAttempt(attempt);
            }

            return BuildState(s, quiz, attempt);
        }
    }

    public (AnswerResult? Result, PlayState State) Answer(string sessionId, string email, int questionIndex, int choiceIndex)
    {
        lock (_lock)
        {
            var s = RequirePlayable(sessionId, email);
            var quiz = QuizFor(s);
            var attempt = _attempts.Values.FirstOrDefault(a => a.SessionId == sessionId && a.UserEmail == Normalize(email))
                          ?? throw TournamentException.Invalid("Start the session before answering.");

            if (EffectiveStatus(s) != SessionStatuses.Open)
                throw TournamentException.Conflict("This session has closed. Your score so far still counts.");
            if (attempt.CompletedAt != null)
                return (null, BuildState(s, quiz, attempt));
            // Out-of-date or duplicate submission (double tap, two tabs): ignore it and return the current state.
            if (questionIndex != attempt.CurrentIndex || attempt.CurrentServedAt == null)
                return (null, BuildState(s, quiz, attempt));

            var question = quiz.Questions[attempt.CurrentIndex];
            var elapsed = DateTime.UtcNow - attempt.CurrentServedAt.Value;
            var result = RecordAnswer(attempt, quiz, choiceIndex, elapsed);
            SaveAttempt(attempt);
            return (result, BuildState(s, quiz, attempt));
        }
    }

    private static AnswerResult RecordAnswer(SessionAttemptEntity attempt, Quiz quiz, int choiceIndex, TimeSpan elapsed)
    {
        var question = quiz.Questions[attempt.CurrentIndex];
        var limit = TimeSpan.FromSeconds(question.TimeLimitSeconds);
        var timedOut = choiceIndex < 0 || choiceIndex >= question.Choices.Count || elapsed > limit + AnswerGrace;
        var correct = !timedOut && choiceIndex == question.CorrectIndex;
        var points = correct ? Scoring.CalculatePoints(question.Points, elapsed.TotalSeconds, question.TimeLimitSeconds) : 0;

        var answers = JsonSerializer.Deserialize<List<AttemptAnswer>>(attempt.AnswersJson, Json) ?? new();
        answers.Add(new AttemptAnswer(attempt.CurrentIndex, timedOut ? -1 : choiceIndex, correct, points, (int)elapsed.TotalMilliseconds));
        attempt.AnswersJson = JsonSerializer.Serialize(answers);

        attempt.Score += points;
        attempt.CorrectCount += correct ? 1 : 0;
        attempt.AnsweredCount++;
        attempt.CurrentIndex++;
        // The next question's clock starts when the player asks for it, not while they read feedback.
        attempt.CurrentServedAt = null;
        if (attempt.CurrentIndex >= quiz.Questions.Count)
        {
            attempt.CompletedAt = DateTime.UtcNow;
        }

        return new AnswerResult(correct, timedOut, question.CorrectIndex, points);
    }

    private void SaveAttempt(SessionAttemptEntity attempt) => Persist(db => db.SessionAttempts.Update(attempt));

    private PlayState BuildState(TournamentSessionEntity s, Quiz quiz, SessionAttemptEntity attempt)
    {
        var t = _tournaments.GetValueOrDefault(s.TournamentId);
        PlayQuestion? question = null;
        var done = attempt.CompletedAt != null || attempt.CurrentIndex >= quiz.Questions.Count;
        if (!done && attempt.CurrentServedAt != null && EffectiveStatus(s) == SessionStatuses.Open)
        {
            var q = quiz.Questions[attempt.CurrentIndex];
            var remaining = Math.Max(0, q.TimeLimitSeconds - (DateTime.UtcNow - attempt.CurrentServedAt.Value).TotalSeconds);
            // Never send the correct answer while the question is live.
            question = new PlayQuestion(attempt.CurrentIndex, attempt.CurrentIndex + 1, q.Text, q.Choices, q.TimeLimitSeconds, Math.Round(remaining, 1), q.Points);
        }

        return new PlayState(s.Id, s.Title, s.TournamentId, t?.Name ?? string.Empty, s.ClosesAt,
            quiz.Questions.Count, attempt.AnsweredCount, attempt.Score, attempt.CorrectCount, done, question);
    }

    // -------------------------------------------------------------------------
    // Standings
    // -------------------------------------------------------------------------
    public List<StandingRow> Standings(string tournamentId)
    {
        lock (_lock)
        {
            return _attempts.Values
                .Where(a => a.TournamentId == tournamentId)
                .GroupBy(a => a.UserEmail)
                .Select(g =>
                {
                    var name = _members.GetValueOrDefault((tournamentId, g.Key))?.DisplayName
                               ?? g.OrderByDescending(a => a.StartedAt).First().DisplayName;
                    return new { Email = g.Key, Name = name, Score = g.Sum(a => a.Score), Sessions = g.Count(), Correct = g.Sum(a => a.CorrectCount), Answered = g.Sum(a => a.AnsweredCount) };
                })
                .OrderByDescending(r => r.Score).ThenByDescending(r => r.Correct).ThenBy(r => r.Name)
                .Select((r, i) => new StandingRow(i + 1, r.Email, r.Name, r.Score, r.Sessions, r.Correct, r.Answered))
                .ToList();
        }
    }
}
