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
        string? preferredPin = null);

    GameSession? GetSession(string pin);
    List<GameSession> GetAllActiveSessions();
    List<GameSession> GetSessionsByHost(string hostId);
    List<GameSession> GetSessionsByTournament(string tournamentId);
    bool CloseSession(string pin);

    void RegisterHostConnection(string pin, string connectionId);
    (bool Success, string Message, Player? Player) JoinOrReconnectPlayer(string pin, string connectionId, string fullName, string identifier, string organizationId = "global");
    (string? Pin, Player? Player) HandlePlayerDisconnect(string connectionId);

    Task<bool> StartQuizAsync(string pin);
    (bool Success, string Message, AnswerSubmission? Submission) SubmitAnswer(string pin, string connectionId, int questionIndex, int choiceIndex);
    Task<bool> AdvanceQuestionAsync(string pin);
    bool EndQuestionEarly(string pin);
    (bool Success, string? ConnectionId) KickPlayer(string pin, string playerId);

    int CalculatePoints(int basePoints, double elapsedSeconds, double timeLimitSeconds);
}
