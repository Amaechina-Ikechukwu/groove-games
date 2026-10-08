namespace TriviaSync.Api.Models;

public class RoundStats
{
    public int QuestionIndex { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public int CorrectIndex { get; set; }
    public int[] ChoiceCounts { get; set; } = Array.Empty<int>();
    public int TotalAnswersSubmitted { get; set; }
    public int TotalEligiblePlayers { get; set; }
    public double AverageResponseTimeSeconds { get; set; }
}
