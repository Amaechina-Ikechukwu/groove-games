using System.Text.Json;
using System.Text.RegularExpressions;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public class QuizParserEngine : IQuizParserEngine
{
    private static readonly Regex QuestionHeaderRegex = new(
        @"^(?:#{1,6}\s*)?(?:Q(?:uestion)?\s*(?:(\d+)[:\.\-\)]?|[:\.\-\)])|(\d+)[\.\)\-])\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChoicePrefixRegex = new(
        @"^(?:[-*+]\s+)?(?:\[([ xX])\]\s*)?(?:(?:([A-Fa-f]|\d+)[\.\)\:\-]\s*)|(?:\[([ xX])\]\s*))?(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex AnswerKeywordRegex = new(
        @"^(?:(?:(?:Correct|Right)\s+)?(?:Answer|Ans|Solution|Key)|Correct|Right)\s*(?:[:\-=]\s*|\s+)(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimeRegex = new(
        @"(?:Time(?: Limit)?|Timer|Duration)[:\s]+(\d+)\s*(?:s|sec|seconds)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PointsRegex = new(
        @"(?:Points?|Pts|Score)[:\s]+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChoiceLetterRegex = new(
        @"^(?:[-+\u2022]\s*)?\(?[A-Fa-f][\.\)]\s+\S",
        RegexOptions.Compiled);

    private static readonly Regex CheckMarkRegex = new(
        @"\s*[\u2713\u2714\u2705\u2611]\uFE0F?\s*",
        RegexOptions.Compiled);

    /// <summary>
    /// Cleans one pasted line: removes markdown emphasis and quote markers, and turns the usual
    /// "this is the right one" signals (check marks, a fully bold choice) into a trailing asterisk.
    /// </summary>
    private static string NormalizeLine(string raw)
    {
        var t = raw.Trim().Trim('\uFEFF', '\u200B');
        if (t.Length == 0) return t;

        while (t.StartsWith(">")) t = t[1..].TrimStart();
        if (t.StartsWith("\u2022")) t = "- " + t[1..].TrimStart();

        var wasBold = t.Length > 4 && t.StartsWith("**") && t.EndsWith("**");
        t = t.Replace("**", "").Replace("__", "").Replace("`", "").Trim();

        if (CheckMarkRegex.IsMatch(t))
        {
            t = CheckMarkRegex.Replace(t, " ").Trim();
            if (!t.EndsWith("*")) t += " *";
        }
        else if (wasBold && ChoiceLetterRegex.IsMatch(t) && !t.EndsWith("*"))
        {
            t += " *";
        }

        return t;
    }

    public QuizParseResult Parse(string rawText, string? title = null)
    {
        var result = new QuizParseResult();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            result.Success = false;
            result.Errors.Add("Provided text is empty.");
            return result;
        }

        var trimmed = rawText.Trim();

        // 1. Check if raw text is JSON
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            if (TryParseJson(trimmed, title, result))
            {
                return result;
            }
            // If JSON fails, proceed to line-by-line parsing
        }

        // 2. Parse text/markdown format
        return ParseTextFormat(trimmed, title);
    }

    private static bool TryParseJson(string json, string? title, QuizParseResult result)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (json.StartsWith("{"))
            {
                var quiz = JsonSerializer.Deserialize<Quiz>(json, options);
                if (quiz != null && quiz.Questions.Count > 0)
                {
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        quiz.Title = title;
                    }
                    ValidateQuizQuestions(quiz, result);
                    result.Quiz = quiz;
                    result.Success = result.Errors.Count == 0;
                    return true;
                }
            }
            else if (json.StartsWith("["))
            {
                var questions = JsonSerializer.Deserialize<List<Question>>(json, options);
                if (questions != null && questions.Count > 0)
                {
                    var quiz = new Quiz
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? "Imported Quiz" : title,
                        Questions = questions
                    };
                    ValidateQuizQuestions(quiz, result);
                    result.Quiz = quiz;
                    result.Success = result.Errors.Count == 0;
                    return true;
                }
            }
        }
        catch
        {
            // Not valid JSON, continue with text parser
        }
        return false;
    }

    private static QuizParseResult ParseTextFormat(string text, string? title)
    {
        var result = new QuizParseResult();
        var quiz = new Quiz
        {
            Title = string.IsNullOrWhiteSpace(title) ? ExtractTitle(text) ?? "New Quiz" : title
        };

        // Split text into lines
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Group into question blocks
        var blocks = SplitIntoQuestionBlocks(lines);

        if (blocks.Count == 0)
        {
            result.Success = false;
            result.Errors.Add("No questions could be identified. Use format like 'Q1: Question text' or '1. Question text'.");
            return result;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var parseQ = ParseQuestionBlock(block, i + 1);
            if (parseQ.question != null)
            {
                quiz.Questions.Add(parseQ.question);
            }
            if (parseQ.errors.Count > 0)
            {
                result.Errors.AddRange(parseQ.errors);
            }
            if (parseQ.warnings.Count > 0)
            {
                result.Warnings.AddRange(parseQ.warnings);
            }
        }

        result.Quiz = quiz;
        result.Success = result.Errors.Count == 0 && quiz.Questions.Count > 0;
        if (quiz.Questions.Count == 0 && result.Errors.Count == 0)
        {
            result.Errors.Add("No valid questions parsed.");
            result.Success = false;
        }

        return result;
    }

    private static string? ExtractTitle(string text)
    {
        var firstLine = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstLine != null && (firstLine.StartsWith("# ") || firstLine.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)))
        {
            return firstLine.Replace("# ", "").Replace("Title:", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        return null;
    }

    private static List<List<string>> SplitByBlankLines(string[] lines)
    {
        // For text with no "Q1:" / "1." numbering: each paragraph is one question,
        // with the question on its first line and the answers beneath it.
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in lines)
        {
            var line = NormalizeLine(raw);
            if (line.Length == 0)
            {
                if (current.Count > 0) { blocks.Add(current); current = new List<string>(); }
                continue;
            }
            if (blocks.Count == 0 && current.Count == 0 &&
                (line.StartsWith("# ") || line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(current);
        return blocks;
    }

    private static List<List<string>> SplitIntoQuestionBlocks(string[] lines)
    {
        if (!lines.Any(l => IsQuestionStart(NormalizeLine(l))))
        {
            return SplitByBlankLines(lines);
        }

        var blocks = new List<List<string>>();
        List<string>? currentBlock = null;

        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Check if line indicates start of a question
            if (IsQuestionStart(line))
            {
                if (currentBlock != null && currentBlock.Count > 0)
                {
                    blocks.Add(currentBlock);
                }
                currentBlock = new List<string> { line };
            }
            else
            {
                if (currentBlock == null)
                {
                    // Check if it's title
                    if (line.StartsWith("# ") || line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    currentBlock = new List<string>();
                }
                currentBlock.Add(line);
            }
        }

        if (currentBlock != null && currentBlock.Count > 0)
        {
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    private static bool IsQuestionStart(string line)
    {
        var match = QuestionHeaderRegex.Match(line);
        return match.Success;
    }

    private static (Question? question, List<string> errors, List<string> warnings) ParseQuestionBlock(List<string> blockLines, int questionNumber)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (blockLines.Count == 0)
        {
            return (null, errors, warnings);
        }

        string questionText = string.Empty;
        var choices = new List<string>();
        int correctIndex = -1;
        int timeLimitSeconds = 20;
        int points = 1000;
        string? explicitAnswerStr = null;

        int lineIdx = 0;

        // Parse Question text (could span first 1 or 2 lines before choices start)
        var firstMatch = QuestionHeaderRegex.Match(blockLines[0]);
        if (firstMatch.Success && !string.IsNullOrWhiteSpace(firstMatch.Groups[3].Value))
        {
            questionText = firstMatch.Groups[3].Value.Trim();
            lineIdx = 1;
        }
        else if (firstMatch.Success)
        {
            lineIdx = 1;
            if (lineIdx < blockLines.Count)
            {
                questionText = blockLines[lineIdx].Trim();
                lineIdx++;
            }
        }
        else
        {
            questionText = blockLines[0].Trim();
            lineIdx = 1;
        }

        // Parse choices and metadata
        for (; lineIdx < blockLines.Count; lineIdx++)
        {
            var line = blockLines[lineIdx].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Check metadata: Time
            var timeMatch = TimeRegex.Match(line);
            if (timeMatch.Success && int.TryParse(timeMatch.Groups[1].Value, out var parsedTime))
            {
                timeLimitSeconds = parsedTime > 0 ? parsedTime : 20;
                continue;
            }

            // Check metadata: Points
            var pointsMatch = PointsRegex.Match(line);
            if (pointsMatch.Success && int.TryParse(pointsMatch.Groups[1].Value, out var parsedPoints))
            {
                points = parsedPoints > 0 ? parsedPoints : 1000;
                continue;
            }

            // Check explicit Answer keyword: e.g. "Answer: A" or "Ans: True"
            var ansMatch = AnswerKeywordRegex.Match(line);
            if (ansMatch.Success)
            {
                explicitAnswerStr = ansMatch.Groups[1].Value.Trim();
                continue;
            }

            // Check for choice format
            bool isChoice = false;
            bool isCorrectChoice = false;
            string choiceText = line;

            // Check checked box [x] or [X]
            if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || line.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
            {
                isChoice = true;
                isCorrectChoice = true;
                choiceText = Regex.Replace(line, @"^[-*+]?\s*\[[xX]\]\s*", "");
            }
            else if (line.StartsWith("[ ]") || line.StartsWith("- [ ]"))
            {
                isChoice = true;
                isCorrectChoice = false;
                choiceText = Regex.Replace(line, @"^[-*+]?\s*\[\s*\]\s*", "");
            }
            else
            {
                // Check A) B) C) or 1) 2) or standard bullet
                var prefixMatch = ChoicePrefixRegex.Match(line);
                if (prefixMatch.Success && (!string.IsNullOrEmpty(prefixMatch.Groups[2].Value) || !string.IsNullOrEmpty(prefixMatch.Groups[1].Value) || !string.IsNullOrEmpty(prefixMatch.Groups[3].Value)))
                {
                    isChoice = true;
                    var box1 = prefixMatch.Groups[1].Value;
                    var box2 = prefixMatch.Groups[3].Value;
                    if (box1.Equals("x", StringComparison.OrdinalIgnoreCase) || box2.Equals("x", StringComparison.OrdinalIgnoreCase))
                    {
                        isCorrectChoice = true;
                    }
                    choiceText = prefixMatch.Groups[4].Value.Trim();
                }
                else if (choices.Count > 0 && (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• ")))
                {
                    isChoice = true;
                    choiceText = line[2..].Trim();
                }
            }

            if (isChoice)
            {
                // Check if choice ends with asterisk * or (Correct)
                if (choiceText.EndsWith("*"))
                {
                    isCorrectChoice = true;
                    choiceText = choiceText.TrimEnd('*').Trim();
                }
                else if (choiceText.EndsWith("(correct)", StringComparison.OrdinalIgnoreCase) || choiceText.EndsWith("(x)", StringComparison.OrdinalIgnoreCase))
                {
                    isCorrectChoice = true;
                    choiceText = Regex.Replace(choiceText, @"\((?:correct|x)\)$", "", RegexOptions.IgnoreCase).Trim();
                }

                if (isCorrectChoice)
                {
                    if (correctIndex != -1)
                    {
                        warnings.Add($"Q{questionNumber}: Multiple correct answers flagged; selecting first occurrence.");
                    }
                    else
                    {
                        correctIndex = choices.Count;
                    }
                }

                choices.Add(choiceText);
            }
            else
            {
                // If we haven't found choices yet, could be multi-line question text
                if (choices.Count == 0)
                {
                    questionText += " " + line;
                }
            }
        }

        // If explicit Answer keyword was specified (e.g. Answer: A or Answer: 2 or Answer: True)
        // A true/false question written without listed options: "Q: The sky is blue. Answer: True"
        if (choices.Count == 0 && !string.IsNullOrWhiteSpace(explicitAnswerStr) &&
            (explicitAnswerStr.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
             explicitAnswerStr.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            choices.AddRange(new[] { "True", "False" });
        }

        if (correctIndex == -1 && !string.IsNullOrWhiteSpace(explicitAnswerStr))
        {
            explicitAnswerStr = explicitAnswerStr.Trim().Trim('*', '.', ' ');
            var matchLetter = Regex.Match(explicitAnswerStr, @"^\(?([A-Fa-f])\)?(?:[\.\)\:\-]\s*.*)?$");
            if (matchLetter.Success)
            {
                int charIndex = char.ToUpper(matchLetter.Groups[1].Value[0]) - 'A';
                if (charIndex >= 0 && charIndex < choices.Count)
                {
                    correctIndex = charIndex;
                }
            }
            else if (int.TryParse(explicitAnswerStr, out var digitIndex) && digitIndex >= 1 && digitIndex <= choices.Count)
            {
                correctIndex = digitIndex - 1;
            }
            else
            {
                // Try text matching with choice
                for (int c = 0; c < choices.Count; c++)
                {
                    if (string.Equals(choices[c].Trim(), explicitAnswerStr, StringComparison.OrdinalIgnoreCase))
                    {
                        correctIndex = c;
                        break;
                    }
                }
            }
        }

        // Validation
        if (string.IsNullOrWhiteSpace(questionText))
        {
            errors.Add($"Q{questionNumber}: Missing question text.");
        }

        if (choices.Count < 2)
        {
            errors.Add($"Q{questionNumber}: Must have at least 2 choices (found {choices.Count}).");
        }
        else if (choices.Count > 6)
        {
            errors.Add($"Q{questionNumber}: Supports maximum 6 choices (found {choices.Count}).");
        }

        if (choices.Count >= 2 && correctIndex == -1)
        {
            errors.Add($"Q{questionNumber}: No correct answer indicator found (use '*', '[x]', or 'Answer: A').");
        }

        if (errors.Count > 0)
        {
            return (null, errors, warnings);
        }

        var q = new Question
        {
            QuestionId = $"q_{questionNumber}_{Guid.NewGuid().ToString("N")[..6]}",
            Text = questionText,
            Choices = choices,
            CorrectIndex = correctIndex,
            TimeLimitSeconds = timeLimitSeconds,
            Points = points
        };

        return (q, errors, warnings);
    }

    private static void ValidateQuizQuestions(Quiz quiz, QuizParseResult result)
    {
        for (int i = 0; i < quiz.Questions.Count; i++)
        {
            var q = quiz.Questions[i];
            if (string.IsNullOrWhiteSpace(q.Text))
            {
                result.Errors.Add($"Question {i + 1}: Text cannot be empty.");
            }
            if (q.Choices == null || q.Choices.Count < 2)
            {
                result.Errors.Add($"Question {i + 1}: Must contain at least 2 choices.");
            }
            else if (q.Choices.Count > 6)
            {
                result.Errors.Add($"Question {i + 1}: Max 6 choices supported.");
            }
            else if (q.CorrectIndex < 0 || q.CorrectIndex >= q.Choices.Count)
            {
                result.Errors.Add($"Question {i + 1}: CorrectIndex ({q.CorrectIndex}) is out of range.");
            }
            if (q.TimeLimitSeconds <= 0)
            {
                q.TimeLimitSeconds = 20;
            }
            if (q.Points <= 0)
            {
                q.Points = 1000;
            }
        }
    }
}
