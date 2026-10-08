using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public class QuizParseResult
{
    public bool Success { get; set; }
    public Quiz Quiz { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public interface IQuizParserEngine
{
    QuizParseResult Parse(string rawText, string? title = null);
}
