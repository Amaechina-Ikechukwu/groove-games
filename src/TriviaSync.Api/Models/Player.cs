namespace TriviaSync.Api.Models;

public class Player
{
    public string PlayerId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = "global";
    public string HostId { get; set; } = string.Empty;
    public string TournamentId { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public int Score { get; set; } = 0;
    public int Rank { get; set; } = 1;
    public int CurrentStreak { get; set; } = 0;
    public int HighestStreak { get; set; } = 0;
    public int CorrectAnswers { get; set; } = 0;
    public int TotalAnswers { get; set; } = 0;
    public bool IsConnected { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;

    // Recent question feedback
    public int LastAnswerQuestionIndex { get; set; } = -1;
    public int LastAnswerChoiceIndex { get; set; } = -1;
    public int LastAnswerPoints { get; set; } = 0;
    public bool LastAnswerIsCorrect { get; set; } = false;
    public int ScoreBeforeLastAnswer { get; set; } = 0;
}
