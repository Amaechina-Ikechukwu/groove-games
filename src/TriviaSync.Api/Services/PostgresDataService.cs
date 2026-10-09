using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TriviaSync.Api.Data;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public class PostgresDataService : ITriviaDataService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgresDataService> _logger;
    private readonly bool _usePostgres;

    // High performance local cache
    private static readonly ConcurrentDictionary<string, Quiz> _localQuizzes = new();
    private static readonly ConcurrentDictionary<string, PersistentPlayer> _localPlayers = new();
    private static readonly ConcurrentDictionary<string, GameSession> _localSessions = new();
    private static readonly ConcurrentBag<object> _localAuditLogs = new();

    public PostgresDataService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<PostgresDataService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var connString = config.GetConnectionString("DefaultConnection") 
                         ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(connString))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                db.Database.EnsureCreated();
                _usePostgres = true;
                _logger.LogInformation("PostgreSQL connected successfully. Tables ensured.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not connect to PostgreSQL. Operating in resilient in-memory storage mode.");
                _usePostgres = false;
            }
        }
        else
        {
            _logger.LogInformation("No PostgreSQL connection string configured. Operating in high-performance memory storage mode.");
            _usePostgres = false;
        }

        SeedInitialData();
    }

    private void SeedInitialData()
    {
        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();

                if (!db.Quizzes.Any())
                {
                    var q1 = GetSampleQuiz1();
                    var q2 = GetSampleQuiz2();
                    db.Quizzes.Add(QuizEntity.FromModel(q1));
                    db.Quizzes.Add(QuizEntity.FromModel(q2));

                    var p1 = GetSamplePlayer1();
                    var p2 = GetSamplePlayer2();
                    var p3 = GetSamplePlayer3();
                    db.Players.AddRange(p1, p2, p3);

                    db.SaveChanges();
                }

                // Load to cache
                foreach (var entity in db.Quizzes)
                {
                    _localQuizzes[entity.Id] = entity.ToModel();
                }
                foreach (var player in db.Players)
                {
                    _localPlayers[player.PlayerId] = player;
                }
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error seeding PostgreSQL data, falling back to local memory seeding.");
            }
        }

        if (_localQuizzes.IsEmpty)
        {
            var q1 = GetSampleQuiz1();
            var q2 = GetSampleQuiz2();
            _localQuizzes[q1.Id] = q1;
            _localQuizzes[q2.Id] = q2;

            var p1 = GetSamplePlayer1();
            var p2 = GetSamplePlayer2();
            var p3 = GetSamplePlayer3();
            _localPlayers[p1.PlayerId] = p1;
            _localPlayers[p2.PlayerId] = p2;
            _localPlayers[p3.PlayerId] = p3;
        }
    }

    private static Quiz GetSampleQuiz1() => new Quiz
    {
        Id = "quiz_csharp_microservices",
        Title = "C# & .NET Microservices Masterclass",
        Description = "Rapid fire questions on .NET 8, SignalR, and PostgreSQL.",
        CreatedBy = "admin@triviasync.com",
        CreatedAt = DateTime.UtcNow,
        Questions = new List<Question>
        {
            new() { QuestionId = "q1", Text = "Which collection is thread-safe in .NET without explicit locking?", Choices = new() { "ConcurrentDictionary", "List<T>", "Dictionary<TKey, TValue>", "HashSet<T>" }, CorrectIndex = 0, TimeLimitSeconds = 20, Points = 1000 },
            new() { QuestionId = "q2", Text = "SignalR uses WebSockets as its primary transport when supported by client and server.", Choices = new() { "True", "False" }, CorrectIndex = 0, TimeLimitSeconds = 15, Points = 1000 },
            new() { QuestionId = "q3", Text = "What does API stand for?", Choices = new() { "Applied Protocol Integration", "Application Programming Interface", "Advanced Programmer Interaction", "Automated Processing Interface" }, CorrectIndex = 1, TimeLimitSeconds = 20, Points = 1000 },
            new() { QuestionId = "q4", Text = "In ASP.NET Core, which lifecycle registers a service created once per client request?", Choices = new() { "Transient", "Singleton", "Scoped", "Static" }, CorrectIndex = 2, TimeLimitSeconds = 20, Points = 1000 },
            new() { QuestionId = "q5", Text = "Which protocol does gRPC primarily use for high-performance transport?", Choices = new() { "HTTP/1.1", "HTTP/2", "FTP", "Telnet" }, CorrectIndex = 1, TimeLimitSeconds = 20, Points = 1000 }
        }
    };

    private static Quiz GetSampleQuiz2() => new Quiz
    {
        Id = "quiz_general_tech",
        Title = "Cloud, Containers & PostgreSQL",
        Description = "Showdown on modern cloud, containers, and database technologies.",
        CreatedBy = "host@triviasync.com",
        CreatedAt = DateTime.UtcNow,
        Questions = new List<Question>
        {
            new() { QuestionId = "q2_1", Text = "Which container runtime is standard for Kubernetes OCI conformance?", Choices = new() { "containerd", "VirtualBox", "Hyper-V", "VMware Fusion" }, CorrectIndex = 0, TimeLimitSeconds = 20, Points = 1000 },
            new() { QuestionId = "q2_2", Text = "What is the default port for PostgreSQL database?", Choices = new() { "3306", "5432", "8080", "27017" }, CorrectIndex = 1, TimeLimitSeconds = 15, Points = 1000 },
            new() { QuestionId = "q2_3", Text = "PostgreSQL natively supports JSON and JSONB indexing.", Choices = new() { "True", "False" }, CorrectIndex = 0, TimeLimitSeconds = 15, Points = 1000 }
        }
    };

    private static PersistentPlayer GetSamplePlayer1() => new PersistentPlayer
    {
        PlayerId = NormalizePlayerId("Johnathan Doe", "global"),
        FullName = "Johnathan Doe",
        OrganizationId = "global",
        HostId = "host@triviasync.com",
        Identifier = "john.doe@company.com",
        TotalPointsAllTime = 24850,
        QuizzesPlayed = 12,
        QuestionsAnswered = 60,
        CorrectAnswersCount = 52,
        HighestStreak = 8,
        LastActive = DateTime.UtcNow.AddHours(-1)
    };

    private static PersistentPlayer GetSamplePlayer2() => new PersistentPlayer
    {
        PlayerId = NormalizePlayerId("Sarah Connor", "global"),
        FullName = "Sarah Connor",
        OrganizationId = "global",
        HostId = "host@triviasync.com",
        Identifier = "sarah.c@tech.io",
        TotalPointsAllTime = 19420,
        QuizzesPlayed = 9,
        QuestionsAnswered = 45,
        CorrectAnswersCount = 41,
        HighestStreak = 6,
        LastActive = DateTime.UtcNow.AddHours(-3)
    };

    private static PersistentPlayer GetSamplePlayer3() => new PersistentPlayer
    {
        PlayerId = NormalizePlayerId("Alex Rivera", "global"),
        FullName = "Alex Rivera",
        OrganizationId = "global",
        HostId = "host@triviasync.com",
        Identifier = "alex@cyber.org",
        TotalPointsAllTime = 16200,
        QuizzesPlayed = 8,
        QuestionsAnswered = 40,
        CorrectAnswersCount = 34,
        HighestStreak = 5,
        LastActive = DateTime.UtcNow.AddDays(-1)
    };

    public static string NormalizePlayerId(string fullName, string organizationId)
    {
        var cleanName = Regex.Replace(fullName.Trim().ToLowerInvariant(), @"[^a-z0-9]", "_");
        var cleanOrg = Regex.Replace(organizationId.Trim().ToLowerInvariant(), @"[^a-z0-9]", "_");
        if (string.IsNullOrWhiteSpace(cleanOrg)) cleanOrg = "global";
        return $"norm_{cleanName}_{cleanOrg}";
    }

    public async Task<List<Quiz>> GetAllQuizzesAsync()
    {
        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var entities = await db.Quizzes.OrderByDescending(q => q.CreatedAt).ToListAsync();
                return entities.Select(e => e.ToModel()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading quizzes from Postgres, returning cached data.");
            }
        }

        return _localQuizzes.Values.OrderByDescending(q => q.CreatedAt).ToList();
    }

    public async Task<Quiz?> GetQuizByIdAsync(string quizId)
    {
        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var entity = await db.Quizzes.FindAsync(quizId);
                if (entity != null)
                {
                    return entity.ToModel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading quiz {QuizId} from Postgres.", quizId);
            }
        }

        _localQuizzes.TryGetValue(quizId, out var local);
        return local;
    }

    public async Task<Quiz> SaveQuizAsync(Quiz quiz)
    {
        if (string.IsNullOrWhiteSpace(quiz.Id))
        {
            quiz.Id = $"quiz_{Guid.NewGuid().ToString("N")[..8]}";
        }

        _localQuizzes[quiz.Id] = quiz;

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var existing = await db.Quizzes.FindAsync(quiz.Id);
                var entity = QuizEntity.FromModel(quiz);

                if (existing != null)
                {
                    existing.Title = entity.Title;
                    existing.Description = entity.Description;
                    existing.QuestionsJson = entity.QuestionsJson;
                }
                else
                {
                    db.Quizzes.Add(entity);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving quiz to PostgreSQL.");
            }
        }

        return quiz;
    }

    public async Task<bool> DeleteQuizAsync(string quizId)
    {
        var removed = _localQuizzes.TryRemove(quizId, out _);

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var existing = await db.Quizzes.FindAsync(quizId);
                if (existing != null)
                {
                    db.Quizzes.Remove(existing);
                    await db.SaveChangesAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting quiz from PostgreSQL.");
            }
        }

        return removed;
    }

    public async Task SaveGameSessionAsync(GameSession session)
    {
        _localSessions[session.Pin] = session;

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var existing = await db.GameSessions.FindAsync(session.Pin);

                if (existing != null)
                {
                    existing.State = session.State.ToString();
                    existing.CurrentQuestionIndex = session.CurrentQuestionIndex;
                    existing.ConnectedPlayerCount = session.ConnectedPlayerCount;
                    existing.TotalPlayerCount = session.Players.Count;
                }
                else
                {
                    db.GameSessions.Add(new GameSessionEntity
                    {
                        Pin = session.Pin,
                        QuizId = session.QuizId,
                        QuizTitle = session.Quiz.Title,
                        HostId = session.HostId,
                        HostEmail = session.HostEmail,
                        TournamentId = session.TournamentId,
                        TournamentName = session.TournamentName,
                        SessionType = session.SessionType,
                        SessionNumber = session.SessionNumber,
                        TotalSessions = session.TotalSessions,
                        State = session.State.ToString(),
                        CurrentQuestionIndex = session.CurrentQuestionIndex,
                        CreatedAt = session.CreatedAt,
                        StartedAt = session.StartedAt,
                        ConnectedPlayerCount = session.ConnectedPlayerCount,
                        TotalPlayerCount = session.Players.Count
                    });
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving session to PostgreSQL.");
            }
        }
    }

    public async Task<GameSession?> GetGameSessionAsync(string pin)
    {
        _localSessions.TryGetValue(pin, out var session);
        return await Task.FromResult(session);
    }

    public async Task<PersistentPlayer> GetOrCreatePlayerAsync(
        string fullName, string organizationId = "global", string identifier = "", string hostId = "")
    {
        var playerId = NormalizePlayerId(fullName, organizationId);

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var player = await db.Players.FindAsync(playerId);

                if (player != null)
                {
                    if (!string.IsNullOrWhiteSpace(hostId) && string.IsNullOrWhiteSpace(player.HostId))
                    {
                        player.HostId = hostId;
                        await db.SaveChangesAsync();
                    }
                    _localPlayers[playerId] = player;
                    return player;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error querying player from PostgreSQL.");
            }
        }

        return _localPlayers.GetOrAdd(playerId, id => new PersistentPlayer
        {
            PlayerId = id,
            FullName = fullName.Trim(),
            OrganizationId = string.IsNullOrWhiteSpace(organizationId) ? "global" : organizationId.Trim(),
            HostId = hostId.Trim(),
            Identifier = identifier.Trim(),
            TotalPointsAllTime = 0,
            QuizzesPlayed = 0,
            QuestionsAnswered = 0,
            CorrectAnswersCount = 0,
            HighestStreak = 0,
            LastActive = DateTime.UtcNow
        });
    }

    public async Task UpdatePlayerStatsAsync(
        string fullName, string organizationId, int pointsEarned, int correctAnswers, int streak, string identifier = "", string hostId = "", int questionsAnswered = 1)
    {
        var player = await GetOrCreatePlayerAsync(fullName, organizationId, identifier, hostId);

        lock (player)
        {
            player.TotalPointsAllTime += pointsEarned;
            player.QuizzesPlayed += 1;
            // Count every question the player was asked, so accuracy can never exceed 100%.
            player.QuestionsAnswered += Math.Max(questionsAnswered, correctAnswers);
            player.CorrectAnswersCount += correctAnswers;
            if (streak > player.HighestStreak)
            {
                player.HighestStreak = streak;
            }
            player.LastActive = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                player.Identifier = identifier;
            }
            if (!string.IsNullOrWhiteSpace(hostId))
            {
                player.HostId = hostId;
            }
        }

        _localPlayers[player.PlayerId] = player;

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var existing = await db.Players.FindAsync(player.PlayerId);

                if (existing != null)
                {
                    existing.TotalPointsAllTime = player.TotalPointsAllTime;
                    existing.QuizzesPlayed = player.QuizzesPlayed;
                    existing.QuestionsAnswered = player.QuestionsAnswered;
                    existing.CorrectAnswersCount = player.CorrectAnswersCount;
                    existing.HighestStreak = player.HighestStreak;
                    existing.LastActive = player.LastActive;
                    existing.HostId = player.HostId;
                    if (!string.IsNullOrWhiteSpace(identifier)) existing.Identifier = identifier;
                }
                else
                {
                    db.Players.Add(player);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating player stats in PostgreSQL.");
            }
        }
    }

    public async Task<List<PersistentPlayer>> GetPersistentLeaderboardAsync(
        string? organizationId = null, string? hostId = null, string? search = null, int limit = 100)
    {
        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                IQueryable<PersistentPlayer> q = db.Players;

                if (!string.IsNullOrWhiteSpace(hostId) && !hostId.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    q = q.Where(p => p.HostId == hostId || p.OrganizationId == hostId);
                }
                else if (!string.IsNullOrWhiteSpace(organizationId) && !organizationId.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    q = q.Where(p => p.OrganizationId == organizationId);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim().ToLower();
                    q = q.Where(p => p.FullName.ToLower().Contains(s) || p.Identifier.ToLower().Contains(s));
                }

                var dbPlayers = await q.OrderByDescending(p => p.TotalPointsAllTime)
                                       .Take(limit)
                                       .ToListAsync();

                return dbPlayers.OrderByDescending(p => p.TotalPointsAllTime)
                                .ThenByDescending(p => p.AccuracyPercentage)
                                .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading leaderboard from PostgreSQL, using memory cache.");
            }
        }

        IEnumerable<PersistentPlayer> query = _localPlayers.Values;

        if (!string.IsNullOrWhiteSpace(hostId) && !hostId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => string.Equals(p.HostId, hostId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(p.OrganizationId, hostId, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(organizationId) && !organizationId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => string.Equals(p.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.FullName.ToLowerInvariant().Contains(s) || p.Identifier.ToLowerInvariant().Contains(s));
        }

        var results = query.OrderByDescending(p => p.TotalPointsAllTime)
                          .ThenByDescending(p => p.AccuracyPercentage)
                          .Take(limit)
                          .ToList();

        return await Task.FromResult(results);
    }

    public async Task<bool> ResetLeaderboardAsync(string? organizationId = null, string? hostId = null)
    {
        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();

                if (!string.IsNullOrWhiteSpace(hostId) && !hostId.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var players = await db.Players.Where(p => p.HostId == hostId).ToListAsync();
                    db.Players.RemoveRange(players);
                }
                else if (string.IsNullOrWhiteSpace(organizationId) || organizationId.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    db.Players.RemoveRange(db.Players);
                }
                else
                {
                    var players = await db.Players.Where(p => p.OrganizationId == organizationId).ToListAsync();
                    db.Players.RemoveRange(players);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resetting leaderboard in PostgreSQL.");
            }
        }

        if (!string.IsNullOrWhiteSpace(hostId) && !hostId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var toRemove = _localPlayers.Values.Where(p => string.Equals(p.HostId, hostId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var p in toRemove)
            {
                _localPlayers.TryRemove(p.PlayerId, out _);
            }
        }
        else if (string.IsNullOrWhiteSpace(organizationId) || organizationId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            _localPlayers.Clear();
        }
        else
        {
            var toRemove = _localPlayers.Values.Where(p => string.Equals(p.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var p in toRemove)
            {
                _localPlayers.TryRemove(p.PlayerId, out _);
            }
        }

        return await Task.FromResult(true);
    }

    public async Task RecordRoundAuditAsync(string pin, RoundStats roundStats, List<AnswerSubmission> submissions)
    {
        var audit = new
        {
            Pin = pin,
            RoundIndex = roundStats.QuestionIndex,
            QuestionText = roundStats.QuestionText,
            CorrectIndex = roundStats.CorrectIndex,
            Stats = roundStats,
            Submissions = submissions,
            RecordedAt = DateTime.UtcNow
        };

        _localAuditLogs.Add(audit);

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                db.RoundAudits.Add(new RoundAuditEntity
                {
                    Pin = pin,
                    RoundIndex = roundStats.QuestionIndex,
                    QuestionText = roundStats.QuestionText,
                    CorrectIndex = roundStats.CorrectIndex,
                    StatsJson = JsonSerializer.Serialize(roundStats),
                    SubmissionsJson = JsonSerializer.Serialize(submissions),
                    RecordedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving round audit to PostgreSQL.");
            }
        }
    }
}
