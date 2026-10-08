using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public interface ITriviaDataService
{
    // Quizzes
    Task<List<Quiz>> GetAllQuizzesAsync();
    Task<Quiz?> GetQuizByIdAsync(string quizId);
    Task<Quiz> SaveQuizAsync(Quiz quiz);
    Task<bool> DeleteQuizAsync(string quizId);

    // Game Sessions
    Task SaveGameSessionAsync(GameSession session);
    Task<GameSession?> GetGameSessionAsync(string pin);

    // Persistent Players & Leaderboard
    Task<PersistentPlayer> GetOrCreatePlayerAsync(string fullName, string organizationId = "global", string identifier = "", string hostId = "");
    Task UpdatePlayerStatsAsync(string fullName, string organizationId, int pointsEarned, int correctAnswers, int streak, string identifier = "", string hostId = "");
    Task<List<PersistentPlayer>> GetPersistentLeaderboardAsync(string? organizationId = null, string? hostId = null, string? search = null, int limit = 100);
    Task<bool> ResetLeaderboardAsync(string? organizationId = null, string? hostId = null);

    // Audit logs
    Task RecordRoundAuditAsync(string pin, RoundStats roundStats, List<AnswerSubmission> submissions);
}
