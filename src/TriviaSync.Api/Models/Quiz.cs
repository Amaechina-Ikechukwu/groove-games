namespace TriviaSync.Api.Models;

public class Quiz
{
    public string Id { get; set; } = $"quiz_{Guid.NewGuid().ToString("N")[..8]}";
    public string Title { get; set; } = "Untitled Quiz";
    public string Description { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "admin";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Question> Questions { get; set; } = new();
}
