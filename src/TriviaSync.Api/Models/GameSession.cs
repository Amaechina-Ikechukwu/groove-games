using System.Collections.Concurrent;

namespace TriviaSync.Api.Models;

public enum GameState
{
    Lobby,
    QuestionCountdown,
    QuestionActive,
    AnswerReveal,
    RoundLeaderboard,
    GameEnded
}

public class AuditEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? PlayerId { get; set; }
    public int? QuestionIndex { get; set; }
    public int? ChoiceIndex { get; set; }
    public int? PointsEarned { get; set; }
    public double? ElapsedMilliseconds { get; set; }
}

public class GameSession
{
    public string Pin { get; set; } = string.Empty; // Access Code
    public string QuizId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public string HostEmail { get; set; } = string.Empty;
    public string HostConnectionId { get; set; } = string.Empty;

    // Multi-session Tournament metadata
    public string TournamentId { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
    public string SessionType { get; set; } = "Single"; // "Single" or "MultiSession"
    public int SessionNumber { get; set; } = 1;
    public int TotalSessions { get; set; } = 1;

    public GameState State { get; set; } = GameState.Lobby;
    public int CurrentQuestionIndex { get; set; } = -1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? QuestionStartedAtUtc { get; set; }
    public int RemainingSeconds { get; set; } = 0;
    public bool AutoAdvance { get; set; } = false;

    public Quiz Quiz { get; set; } = new();
    public ConcurrentDictionary<string, Player> Players { get; set; } = new();
    
    // Key: PlayerId
    public ConcurrentDictionary<string, AnswerSubmission> CurrentRoundAnswers { get; set; } = new();
    
    // Historical round stats for all questions in this session
    public List<RoundStats> RoundHistory { get; set; } = new();

    // Audit logs for post-game inspection
    public List<AuditEntry> AuditLogs { get; set; } = new();

    public int ConnectedPlayerCount => Players.Values.Count(p => p.IsConnected);
}
