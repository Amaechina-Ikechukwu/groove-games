using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class QuizParserTests
{
    private readonly QuizParserEngine _parser = new();

    [Fact]
    public void Parse_PrdExample_ParsesSuccessfully()
    {
        string rawText = @"
Q1: What does API stand for?
A) Application Programming Interface *
B) Applied Protocol Integration
C) Advanced Programmer Interaction
D) Automated Processing Interface
Time: 15s
Points: 1000

Q2: SignalR uses WebSockets as its primary transport when supported.
[x] True
[ ] False
";

        var result = _parser.Parse(rawText, "PRD Test Quiz");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Quiz.Questions.Count);

        // Q1
        var q1 = result.Quiz.Questions[0];
        Assert.Equal("What does API stand for?", q1.Text);
        Assert.Equal(4, q1.Choices.Count);
        Assert.Equal(0, q1.CorrectIndex);
        Assert.Equal("Application Programming Interface", q1.Choices[0]);
        Assert.Equal(15, q1.TimeLimitSeconds);
        Assert.Equal(1000, q1.Points);

        // Q2
        var q2 = result.Quiz.Questions[1];
        Assert.Equal("SignalR uses WebSockets as its primary transport when supported.", q2.Text);
        Assert.Equal(2, q2.Choices.Count);
        Assert.Equal(0, q2.CorrectIndex);
        Assert.Equal("True", q2.Choices[0]);
        Assert.Equal("False", q2.Choices[1]);
    }

    [Fact]
    public void Parse_AnswerKeywordFormat_IdentifiesCorrectIndex()
    {
        string rawText = @"
1. Which collection is thread-safe in .NET?
A) Dictionary
B) List
C) ConcurrentDictionary
D) Queue
Answer: C
Time: 25
Points: 800
";

        var result = _parser.Parse(rawText);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(result.Quiz.Questions);

        var q = result.Quiz.Questions[0];
        Assert.Equal(2, q.CorrectIndex); // C is index 2
        Assert.Equal("ConcurrentDictionary", q.Choices[2]);
        Assert.Equal(25, q.TimeLimitSeconds);
        Assert.Equal(800, q.Points);
    }

    [Fact]
    public void Parse_JsonFormat_ParsesDirectly()
    {
        string json = @"
{
  ""title"": ""JSON Quiz"",
  ""questions"": [
    {
      ""questionId"": ""jq1"",
      ""text"": ""What is 2 + 2?"",
      ""choices"": [""3"", ""4"", ""5""],
      ""correctIndex"": 1,
      ""timeLimitSeconds"": 10,
      ""points"": 500
    }
  ]
}";

        var result = _parser.Parse(json);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("JSON Quiz", result.Quiz.Title);
        Assert.Single(result.Quiz.Questions);
        Assert.Equal(1, result.Quiz.Questions[0].CorrectIndex);
    }

    [Fact]
    public void Parse_InvalidMissingCorrectAnswer_FailsWithErrors()
    {
        string rawText = @"
Q1: Incomplete question without marked answer?
A) Option One
B) Option Two
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("No correct answer indicator"));
    }

    [Fact]
    public void Parse_SingleChoice_FailsValidation()
    {
        string rawText = @"
Q1: Question with one choice
A) Sole choice *
";

        var result = _parser.Parse(rawText);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("at least 2 choices"));
    }
}
