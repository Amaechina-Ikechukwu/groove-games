namespace TriviaSync.Api.Models;

public class AnswerSubmission
{
    public string PlayerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int QuestionIndex { get; set; }
    public int ChoiceIndex { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public double ElapsedMilliseconds { get; set; }
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
}
