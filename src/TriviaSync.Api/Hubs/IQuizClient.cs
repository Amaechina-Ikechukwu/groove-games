namespace TriviaSync.Api.Hubs;

public interface IQuizClient
{
    Task PlayerJoined(object payload);
    Task PlayerLeft(object payload);
    Task PlayerKicked(object payload);
    Task RoomState(object payload);
    Task QuestionCountdown(object payload);
    Task QuestionStarted(object payload);
    Task TimerTick(object payload);
    Task AnswerReceived(object payload);
    Task RoundCompleted(object payload);
    Task PlayerRoundResult(object payload);
    Task LeaderboardUpdate(object payload);
    Task GameEnded(object payload);
    Task SessionClosed(object payload);
    Task SignInRequired(object payload);
    /// <summary>A kind of data changed for a live-update group. Clients re-fetch what they show.</summary>
    Task Changed(string group, string kind);
    Task ErrorNotification(string message);
}
