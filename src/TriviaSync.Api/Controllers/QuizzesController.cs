using Microsoft.AspNetCore.Mvc;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Controllers;

public class ParseQuizRequest
{
    public string RawText { get; set; } = string.Empty;
    public string? Title { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class QuizzesController : ControllerBase
{
    private readonly IQuizParserEngine _parser;
    private readonly ITriviaDataService _dataService;
    private readonly ILogger<QuizzesController> _logger;

    public QuizzesController(IQuizParserEngine parser, ITriviaDataService dataService, ILogger<QuizzesController> logger)
    {
        _parser = parser;
        _dataService = dataService;
        _logger = logger;
    }

    [HttpPost("parse")]
    public ActionResult<QuizParseResult> ParseQuiz([FromBody] ParseQuizRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
        {
            return BadRequest(new { message = "RawText is required." });
        }

        var result = _parser.Parse(request.RawText, request.Title);
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<List<Quiz>>> GetAllQuizzes()
    {
        var quizzes = await _dataService.GetAllQuizzesAsync();
        return Ok(quizzes);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Quiz>> GetQuizById(string id)
    {
        var quiz = await _dataService.GetQuizByIdAsync(id);
        if (quiz == null)
        {
            return NotFound(new { message = $"Quiz '{id}' not found." });
        }
        return Ok(quiz);
    }

    [HttpPost]
    public async Task<ActionResult<Quiz>> SaveQuiz([FromBody] Quiz quiz)
    {
        if (quiz.Questions == null || quiz.Questions.Count == 0)
        {
            return BadRequest(new { message = "Quiz must have at least one question." });
        }

        var saved = await _dataService.SaveQuizAsync(quiz);
        return CreatedAtAction(nameof(GetQuizById), new { id = saved.Id }, saved);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteQuiz(string id)
    {
        var deleted = await _dataService.DeleteQuizAsync(id);
        if (deleted)
        {
            return Ok(new { message = $"Quiz '{id}' deleted." });
        }
        return NotFound(new { message = $"Quiz '{id}' not found." });
    }
}
