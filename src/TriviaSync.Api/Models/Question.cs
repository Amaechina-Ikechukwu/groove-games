namespace TriviaSync.Api.Models;

public class Question
{
    public string QuestionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public List<string> Choices { get; set; } = new();
    public int CorrectIndex { get; set; } = 0;
    public int TimeLimitSeconds { get; set; } = 20;
    public int Points { get; set; } = 1000;
}
