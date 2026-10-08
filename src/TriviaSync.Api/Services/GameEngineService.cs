using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public class GameEngineService : IGameEngineService
{
    private readonly IHubContext<QuizHub, IQuizClient> _hubContext;
    private readonly ITriviaDataService _dataService;
    private readonly ILogger<GameEngineService> _logger;

    // Active sessions: Pin -> GameSession
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    // Map: ConnectionId -> (Pin, PlayerId)
    private readonly ConcurrentDictionary<string, (string Pin, string PlayerId)> _connectionMap = new();

    // Timer cancellation tokens for active questions: Pin -> CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timerCts = new();

    private static readonly Random _rng = new();

    public GameEngineService(
        IHubContext<QuizHub, IQuizClient> hubContext,
        ITriviaDataService dataService,
        ILogger<GameEngineService> logger)
    {
        _hubContext = hubContext;
        _dataService = dataService;
        _logger = logger;
    }

    public GameSession CreateSession(
        Quiz quiz, 
        string hostId, 
        bool autoAdvance = false, 
        string tournamentId = "", 
        string tournamentName = "", 
        int sessionNumber = 1, 
        int totalSessions = 1, 
        string sessionType = "Single")
    {
        var pin = GenerateUniquePin();
        var session = new GameSession
        {
            Pin = pin,
            QuizId = quiz.Id,
            HostId = string.IsNullOrWhiteSpace(hostId) ? "host" : hostId,
            HostEmail = hostId,
            TournamentId = string.IsNullOrWhiteSpace(tournamentId) ? $"tourn_{Guid.NewGuid().ToString("N")[..8]}" : tournamentId,
            TournamentName = string.IsNullOrWhiteSpace(tournamentName) ? (totalSessions > 1 ? $"Tournament Session {sessionNumber}" : quiz.Title) : tournamentName,
            SessionNumber = sessionNumber,
            TotalSessions = totalSessions,
            SessionType = sessionType,
            Quiz = quiz,
            AutoAdvance = autoAdvance,
            State = GameState.Lobby
        };

        _sessions[pin] = session;
        _logger.LogInformation("Created game session PIN: {Pin} for Quiz: {QuizTitle} (Host: {HostId}, Tournament: {TournamentId})", 
            pin, quiz.Title, session.HostId, session.TournamentId);
        return session;
    }

    private string GenerateUniquePin()
    {
        for (int i = 0; i < 1000; i++)
        {
            // 6-digit numeric PIN for easy player entry
            var pin = _rng.Next(100000, 999999).ToString();
            if (!_sessions.ContainsKey(pin))
            {
                return pin;
            }
        }
        return Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    public GameSession? GetSession(string pin)
    {
        _sessions.TryGetValue(pin, out var session);
        return session;
    }

    public List<GameSession> GetAllActiveSessions()
    {
        return _sessions.Values.ToList();
    }

    public List<GameSession> GetSessionsByHost(string hostId)
    {
        return _sessions.Values
            .Where(s => string.Equals(s.HostId, hostId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<GameSession> GetSessionsByTournament(string tournamentId)
    {
        return _sessions.Values
            .Where(s => string.Equals(s.TournamentId, tournamentId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.SessionNumber)
            .ToList();
    }

    public bool CloseSession(string pin)
    {
        if (_timerCts.TryRemove(pin, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        var removed = _sessions.TryRemove(pin, out var session);
        if (removed && session != null)
        {
            foreach (var player in session.Players.Values)
            {
                _connectionMap.TryRemove(player.ConnectionId, out _);
            }
        }
        return removed;
    }

    public void RegisterHostConnection(string pin, string connectionId)
    {
        if (_sessions.TryGetValue(pin, out var session))
        {
            session.HostConnectionId = connectionId;
        }
    }

    public (bool Success, string Message, Player? Player) JoinOrReconnectPlayer(
        string pin, string connectionId, string fullName, string identifier, string organizationId = "global")
    {
        if (!_sessions.TryGetValue(pin, out var session))
        {
            return (false, "Game session not found. Check the PIN and try again.", null);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Full name is required.", null);
        }

        var playerId = PostgresDataService.NormalizePlayerId(fullName, organizationId);

        // Check if player is reconnecting
        if (session.Players.TryGetValue(playerId, out var existingPlayer))
        {
            // Remove old connection mapping
            if (!string.IsNullOrWhiteSpace(existingPlayer.ConnectionId))
            {
                _connectionMap.TryRemove(existingPlayer.ConnectionId, out _);
            }

            existingPlayer.ConnectionId = connectionId;
            existingPlayer.IsConnected = true;
            existingPlayer.LastActive = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                existingPlayer.Identifier = identifier;
            }

            _connectionMap[connectionId] = (pin, playerId);
            _logger.LogInformation("Player reconnected: {FullName} ({PlayerId}) in room {Pin}", fullName, playerId, pin);
            return (true, "Reconnected successfully.", existingPlayer);
        }

        // New player joining under this Host & Tournament
        var effectiveOrg = (string.IsNullOrWhiteSpace(organizationId) || organizationId == "global") ? session.HostId : organizationId.Trim();
        var newPlayer = new Player
        {
            PlayerId = playerId,
            ConnectionId = connectionId,
            FullName = fullName.Trim(),
            Identifier = identifier.Trim(),
            OrganizationId = effectiveOrg,
            HostId = session.HostId,
            TournamentId = session.TournamentId,
            Score = 0,
            Rank = session.Players.Count + 1,
            IsConnected = true,
            JoinedAt = DateTime.UtcNow,
            LastActive = DateTime.UtcNow
        };

        if (session.Players.TryAdd(playerId, newPlayer))
        {
            _connectionMap[connectionId] = (pin, playerId);
            _logger.LogInformation("Player joined: {FullName} in room {Pin} under Host {HostId}", fullName, pin, session.HostId);
            return (true, "Joined room successfully.", newPlayer);
        }

        return (false, "Unable to register player. Please try again.", null);
    }

    public (string? Pin, Player? Player) HandlePlayerDisconnect(string connectionId)
    {
        if (_connectionMap.TryRemove(connectionId, out var info))
        {
            if (_sessions.TryGetValue(info.Pin, out var session))
            {
                if (session.Players.TryGetValue(info.PlayerId, out var player))
                {
                    player.IsConnected = false;
                    player.LastActive = DateTime.UtcNow;
                    return (info.Pin, player);
                }
            }
        }
        return (null, null);
    }

    public async Task<bool> StartQuizAsync(string pin)
    {
        if (!_sessions.TryGetValue(pin, out var session))
        {
            return false;
        }

        if (session.Quiz.Questions.Count == 0)
        {
            return false;
        }

        session.StartedAt = DateTime.UtcNow;
        session.CurrentQuestionIndex = -1;
        return await AdvanceQuestionAsync(pin);
    }

    public async Task<bool> AdvanceQuestionAsync(string pin)
    {
        if (!_sessions.TryGetValue(pin, out var session))
        {
            return false;
        }

        // Cancel any pending timer
        if (_timerCts.TryRemove(pin, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        int nextIndex = session.CurrentQuestionIndex + 1;

        if (nextIndex >= session.Quiz.Questions.Count)
        {
            // Quiz completed! Transition to GameEnded
            await FinishGameAsync(session);
            return true;
        }

        session.CurrentQuestionIndex = nextIndex;
        session.CurrentRoundAnswers.Clear();
        var question = session.Quiz.Questions[nextIndex];

        // 1. Brief countdown stage (3 seconds) to allow participants to get ready
        session.State = GameState.QuestionCountdown;
        await _hubContext.Clients.Group(pin).QuestionCountdown(new
        {
            countdownSeconds = 3,
            questionIndex = nextIndex + 1,
            totalQuestions = session.Quiz.Questions.Count
        });

        await Task.Delay(3000);

        // Check if session was closed or changed during delay
        if (!_sessions.ContainsKey(pin) || session.CurrentQuestionIndex != nextIndex)
        {
            return false;
        }

        // 2. Question Active stage
        session.State = GameState.QuestionActive;
        session.QuestionStartedAtUtc = DateTime.UtcNow;
        session.RemainingSeconds = question.TimeLimitSeconds;

        // Security check: NEVER transmit correctIndex to clients during QuestionActive!
        await _hubContext.Clients.Group(pin).QuestionStarted(new
        {
            index = nextIndex,
            questionNumber = nextIndex + 1,
            totalQuestions = session.Quiz.Questions.Count,
            text = question.Text,
            choices = question.Choices,
            timeLimit = question.TimeLimitSeconds,
            points = question.Points
        });

        // Launch server countdown loop
        var cts = new CancellationTokenSource();
        _timerCts[pin] = cts;
        _ = RunQuestionTimerAsync(session, question, cts.Token);

        return true;
    }

    private async Task RunQuestionTimerAsync(GameSession session, Question question, CancellationToken token)
    {
        var pin = session.Pin;
        int remaining = question.TimeLimitSeconds;

        try
        {
            while (remaining > 0 && !token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                remaining--;
                session.RemainingSeconds = remaining;

                await _hubContext.Clients.Group(pin).TimerTick(new
                {
                    remainingSeconds = remaining
                });

                // Check if all connected players submitted their answers
                int connectedCount = session.ConnectedPlayerCount;
                if (connectedCount > 0 && session.CurrentRoundAnswers.Count >= connectedCount)
                {
                    _logger.LogInformation("All {Count} players answered for PIN {Pin}, concluding question early.", connectedCount, pin);
                    break;
                }
            }

            if (!token.IsCancellationRequested)
            {
                await CompleteRoundAsync(session, question);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when round advanced manually or early
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in question timer for PIN: {Pin}", pin);
        }
    }

    public (bool Success, string Message, AnswerSubmission? Submission) SubmitAnswer(
        string pin, string connectionId, int questionIndex, int choiceIndex)
    {
        if (!_sessions.TryGetValue(pin, out var session))
        {
            return (false, "Game session not found.", null);
        }

        if (session.State != GameState.QuestionActive)
        {
            return (false, "Not accepting answers at this time.", null);
        }

        if (session.CurrentQuestionIndex != questionIndex)
        {
            return (false, "Question has expired.", null);
        }

        if (!_connectionMap.TryGetValue(connectionId, out var info) || info.Pin != pin)
        {
            return (false, "Player identity not recognized.", null);
        }

        if (!session.Players.TryGetValue(info.PlayerId, out var player))
        {
            return (false, "Player record not found.", null);
        }

        // Check if already answered this question
        if (session.CurrentRoundAnswers.ContainsKey(player.PlayerId))
        {
            return (false, "You have already submitted an answer for this question.", null);
        }

        var question = session.Quiz.Questions[questionIndex];
        if (choiceIndex < 0 || choiceIndex >= question.Choices.Count)
        {
            return (false, "Invalid choice selected.", null);
        }

        var now = DateTime.UtcNow;
        var start = session.QuestionStartedAtUtc ?? now;
        var elapsedSeconds = Math.Max(0.0, (now - start).TotalSeconds);
        var elapsedMs = Math.Max(0.0, (now - start).TotalMilliseconds);

        bool isCorrect = (choiceIndex == question.CorrectIndex);
        int pointsEarned = 0;

        if (isCorrect)
        {
            pointsEarned = CalculatePoints(question.Points, elapsedSeconds, question.TimeLimitSeconds);
            player.CurrentStreak++;
            if (player.CurrentStreak > player.HighestStreak)
            {
                player.HighestStreak = player.CurrentStreak;
            }
        }
        else
        {
            player.CurrentStreak = 0;
        }

        player.ScoreBeforeLastAnswer = player.Score;
        player.Score += pointsEarned;
        player.LastAnswerQuestionIndex = questionIndex;
        player.LastAnswerChoiceIndex = choiceIndex;
        player.LastAnswerPoints = pointsEarned;
        player.LastAnswerIsCorrect = isCorrect;
        player.LastActive = now;

        var submission = new AnswerSubmission
        {
            PlayerId = player.PlayerId,
            FullName = player.FullName,
            QuestionIndex = questionIndex,
            ChoiceIndex = choiceIndex,
            SubmittedAtUtc = now,
            ElapsedMilliseconds = elapsedMs,
            IsCorrect = isCorrect,
            PointsEarned = pointsEarned
        };

        session.CurrentRoundAnswers[player.PlayerId] = submission;

        // Log audit entry
        session.AuditLogs.Add(new AuditEntry
        {
            Timestamp = now,
            EventType = "AnswerSubmitted",
            PlayerId = player.PlayerId,
            QuestionIndex = questionIndex,
            ChoiceIndex = choiceIndex,
            PointsEarned = pointsEarned,
            ElapsedMilliseconds = elapsedMs,
            Details = $"Player '{player.FullName}' chose index {choiceIndex}. Correct: {isCorrect}. Points: {pointsEarned}"
        });

        // Notify host of response count progress
        _ = _hubContext.Clients.Group(pin).AnswerReceived(new
        {
            totalAnswers = session.CurrentRoundAnswers.Count,
            totalPlayers = session.ConnectedPlayerCount
        });

        return (true, "Answer submitted.", submission);
    }

    public int CalculatePoints(int basePoints, double elapsedSeconds, double timeLimitSeconds)
    {
        // Formula: Points = BasePoints * (1 - (Elapsed / TimeLimit) * 0.5)
        if (timeLimitSeconds <= 0) timeLimitSeconds = 20;
        double ratio = Math.Clamp(elapsedSeconds / timeLimitSeconds, 0.0, 1.0);
        double multiplier = 1.0 - (ratio * 0.5);
        int points = (int)Math.Round(basePoints * multiplier);
        return Math.Max(0, points);
    }

    public async Task CompleteRoundAsync(GameSession session, Question question)
    {
        session.State = GameState.AnswerReveal;

        // Tally answers
        int choiceCount = question.Choices.Count;
        int[] counts = new int[choiceCount];
        double totalTime = 0.0;

        foreach (var sub in session.CurrentRoundAnswers.Values)
        {
            if (sub.ChoiceIndex >= 0 && sub.ChoiceIndex < choiceCount)
            {
                counts[sub.ChoiceIndex]++;
            }
            totalTime += (sub.ElapsedMilliseconds / 1000.0);
        }

        var roundStats = new RoundStats
        {
            QuestionIndex = session.CurrentQuestionIndex,
            QuestionText = question.Text,
            CorrectIndex = question.CorrectIndex,
            ChoiceCounts = counts,
            TotalAnswersSubmitted = session.CurrentRoundAnswers.Count,
            TotalEligiblePlayers = session.ConnectedPlayerCount,
            AverageResponseTimeSeconds = session.CurrentRoundAnswers.Count > 0 ? Math.Round(totalTime / session.CurrentRoundAnswers.Count, 2) : 0
        };

        session.RoundHistory.Add(roundStats);

        // Update player rankings
        UpdateRankings(session);

        // 1. Broadcast Round Completed with the correct answer and distribution stats
        await _hubContext.Clients.Group(session.Pin).RoundCompleted(new
        {
            questionIndex = session.CurrentQuestionIndex,
            correctIndex = question.CorrectIndex,
            correctAnswerText = question.Choices[question.CorrectIndex],
            stats = roundStats.ChoiceCounts,
            totalAnswers = roundStats.TotalAnswersSubmitted,
            averageTime = roundStats.AverageResponseTimeSeconds
        });

        // 2. Send personalized result to each player
        foreach (var player in session.Players.Values)
        {
            session.CurrentRoundAnswers.TryGetValue(player.PlayerId, out var sub);
            bool isCorrect = sub?.IsCorrect ?? false;
            int pointsEarned = sub?.PointsEarned ?? 0;

            if (!string.IsNullOrWhiteSpace(player.ConnectionId))
            {
                await _hubContext.Clients.Client(player.ConnectionId).PlayerRoundResult(new
                {
                    isCorrect = isCorrect,
                    pointsEarned = pointsEarned,
                    totalScore = player.Score,
                    rank = player.Rank,
                    streak = player.CurrentStreak,
                    correctIndex = question.CorrectIndex
                });
            }
        }

        // Save round audit asynchronously
        _ = _dataService.RecordRoundAuditAsync(session.Pin, roundStats, session.CurrentRoundAnswers.Values.ToList());

        // Wait a few seconds on answer reveal screen, then broadcast round leaderboard
        await Task.Delay(4000);

        if (_sessions.ContainsKey(session.Pin) && session.State == GameState.AnswerReveal)
        {
            session.State = GameState.RoundLeaderboard;
            var topPlayers = session.Players.Values
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.JoinedAt)
                .Take(10)
                .Select(p => new
                {
                    playerId = p.PlayerId,
                    fullName = p.FullName,
                    score = p.Score,
                    rank = p.Rank,
                    streak = p.CurrentStreak,
                    pointsGained = p.LastAnswerQuestionIndex == session.CurrentQuestionIndex ? p.LastAnswerPoints : 0
                })
                .ToList();

            await _hubContext.Clients.Group(session.Pin).LeaderboardUpdate(new
            {
                isRoundLeaderboard = true,
                questionNumber = session.CurrentQuestionIndex + 1,
                totalQuestions = session.Quiz.Questions.Count,
                topPlayers = topPlayers
            });

            // If AutoAdvance is true, automatically advance after 5 seconds
            if (session.AutoAdvance)
            {
                await Task.Delay(5000);
                if (_sessions.ContainsKey(session.Pin) && session.State == GameState.RoundLeaderboard)
                {
                    await AdvanceQuestionAsync(session.Pin);
                }
            }
        }
    }

    private static void UpdateRankings(GameSession session)
    {
        var sorted = session.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.JoinedAt)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
        }
    }

    public async Task FinishGameAsync(GameSession session)
    {
        session.State = GameState.GameEnded;
        UpdateRankings(session);

        var topPlayers = session.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.JoinedAt)
            .ToList();

        var podium = topPlayers.Take(3).Select((p, idx) => new
        {
            rank = idx + 1,
            fullName = p.FullName,
            score = p.Score,
            streak = p.HighestStreak
        }).ToList();

        var allPlayers = topPlayers.Select(p => new
        {
            rank = p.Rank,
            fullName = p.FullName,
            score = p.Score,
            streak = p.HighestStreak
        }).ToList();

        await _hubContext.Clients.Group(session.Pin).GameEnded(new
        {
            podium = podium,
            allPlayers = allPlayers,
            totalPlayers = session.Players.Count
        });

        // Persist session and update persistent player records in database
        await _dataService.SaveGameSessionAsync(session);

        foreach (var player in session.Players.Values)
        {
            int correctCount = session.RoundHistory
                .Count(r => session.AuditLogs.Any(a => a.PlayerId == player.PlayerId && a.QuestionIndex == r.QuestionIndex && a.PointsEarned > 0));

            _ = _dataService.UpdatePlayerStatsAsync(
                player.FullName,
                player.OrganizationId,
                player.Score,
                correctCount,
                player.HighestStreak,
                player.Identifier,
                session.HostId);
        }

        _logger.LogInformation("Game concluded for PIN: {Pin}. Total players: {Count}", session.Pin, session.Players.Count);
    }

    public (bool Success, string? ConnectionId) KickPlayer(string pin, string playerId)
    {
        if (_sessions.TryGetValue(pin, out var session))
        {
            if (session.Players.TryRemove(playerId, out var player))
            {
                _connectionMap.TryRemove(player.ConnectionId, out _);
                return (true, player.ConnectionId);
            }
        }
        return (false, null);
    }
}
