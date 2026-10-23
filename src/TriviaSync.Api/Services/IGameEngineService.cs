using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public interface IGameEngineService
{
    GameSession CreateSession(
        Quiz quiz, 
        string hostId, 
        bool autoAdvance = false, 
        string tournamentId = "", 
        string tournamentName = "", 
        int sessionNumber = 1, 
        int totalSessions = 1, 
        string sessionType = "Single",
        string tournamentSessionId = "",
        string? preferredPin = null,
        string? groupingMode = null,
        int groupingValue = 0);

    GameSession? GetSession(string pin);
    List<GameSession> GetAllActiveSessions();
    List<GameSession> GetSessionsByHost(string hostId);
    List<GameSession> GetSessionsByTournament(string tournamentId);
    bool CloseSession(string pin);

    void RegisterHostConnection(string pin, string connectionId);
    (bool Success, string Message, Player? Player) JoinOrReconnectPlayer(string pin, string connectionId, string fullName, string identifier, string organizationId = "global");
    (string? Pin, Player? Player) HandlePlayerDisconnect(string connectionId);

    /// <summary>Turn grouping on/off or change it. Only allowed while the game is still in the lobby.</summary>
    (bool Success, string Message) SetGrouping(string pin, string? mode, int value);

    Task<bool> StartQuizAsync(string pin);
    (bool Success, string Message, AnswerSubmission? Submission) SubmitAnswer(string pin, string connectionId, int questionIndex, int choiceIndex);
    Task<bool> AdvanceQuestionAsync(string pin);
    bool EndQuestionEarly(string pin);
    (bool Success, string? ConnectionId) KickPlayer(string pin, string playerId);

    int CalculatePoints(int basePoints, double elapsedSeconds, double timeLimitSeconds);
}
