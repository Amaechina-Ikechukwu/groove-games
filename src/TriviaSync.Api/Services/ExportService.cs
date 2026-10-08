using System.Text;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public class ExportService : IExportService
{
    private static readonly byte[] Utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };

    public byte[] ExportLeaderboardToCsv(IEnumerable<PersistentPlayer> players)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Player ID,Full Name,Organization,Identifier,Total Points,Quizzes Played,Questions Answered,Correct Answers,Accuracy %,Highest Streak,Last Active");

        int rank = 1;
        foreach (var p in players)
        {
            sb.AppendLine($"{rank}," +
                          $"\"{EscapeCsv(p.PlayerId)}\"," +
                          $"\"{EscapeCsv(p.FullName)}\"," +
                          $"\"{EscapeCsv(p.OrganizationId)}\"," +
                          $"\"{EscapeCsv(p.Identifier)}\"," +
                          $"{p.TotalPointsAllTime}," +
                          $"{p.QuizzesPlayed}," +
                          $"{p.QuestionsAnswered}," +
                          $"{p.CorrectAnswersCount}," +
                          $"{p.AccuracyPercentage:F1}%," +
                          $"{p.HighestStreak}," +
                          $"\"{p.LastActive:yyyy-MM-dd HH:mm:ss UTC}\"");
            rank++;
        }

        var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[Utf8Bom.Length + contentBytes.Length];
        Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
        Buffer.BlockCopy(contentBytes, 0, result, Utf8Bom.Length, contentBytes.Length);
        return result;
    }

    public byte[] ExportLeaderboardToExcelXml(IEnumerable<PersistentPlayer> players)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#4F46E5\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");
        sb.AppendLine(" <Worksheet ss:Name=\"Persistent Leaderboard\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Row ss:StyleID=\"Header\">");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Rank</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Full Name</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Organization</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Identifier</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Total Points</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Quizzes Played</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Correct Answers</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Accuracy %</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Highest Streak</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Last Active</Data></Cell>");
        sb.AppendLine("   </Row>");

        int rank = 1;
        foreach (var p in players)
        {
            sb.AppendLine("   <Row>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{rank}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(p.FullName)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(p.OrganizationId)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(p.Identifier)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.TotalPointsAllTime}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.QuizzesPlayed}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.CorrectAnswersCount}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{p.AccuracyPercentage:F1}%</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.HighestStreak}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{p.LastActive:yyyy-MM-dd HH:mm:ss}</Data></Cell>");
            sb.AppendLine("   </Row>");
            rank++;
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");
        sb.AppendLine("</Workbook>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] ExportSessionToCsv(GameSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Session PIN: {session.Pin} - Quiz: {session.Quiz.Title}");
        sb.AppendLine($"Date: {session.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine();
        sb.AppendLine("Rank,Player ID,Full Name,Score,Highest Streak,Joined At,Status");

        var ranked = session.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.JoinedAt)
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            sb.AppendLine($"{i + 1}," +
                          $"\"{EscapeCsv(p.PlayerId)}\"," +
                          $"\"{EscapeCsv(p.FullName)}\"," +
                          $"{p.Score}," +
                          $"{p.HighestStreak}," +
                          $"\"{p.JoinedAt:yyyy-MM-dd HH:mm:ss}\"," +
                          $"{(p.IsConnected ? "Connected" : "Disconnected")}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Question Breakdown ---");
        for (int q = 0; q < session.Quiz.Questions.Count; q++)
        {
            var question = session.Quiz.Questions[q];
            sb.AppendLine($"Question {q + 1}: \"{EscapeCsv(question.Text)}\"");
            sb.AppendLine($"Correct Answer: \"{EscapeCsv(question.Choices[question.CorrectIndex])}\"");
            var round = session.RoundHistory.FirstOrDefault(r => r.QuestionIndex == q);
            if (round != null)
            {
                sb.AppendLine($"Total Answers: {round.TotalAnswersSubmitted}, Avg Time: {round.AverageResponseTimeSeconds}s");
            }
            sb.AppendLine();
        }

        var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[Utf8Bom.Length + contentBytes.Length];
        Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
        Buffer.BlockCopy(contentBytes, 0, result, Utf8Bom.Length, contentBytes.Length);
        return result;
    }

    public byte[] ExportSessionToExcelXml(GameSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine(" <Styles>");
        sb.AppendLine("  <Style ss:ID=\"Header\">");
        sb.AppendLine("   <Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
        sb.AppendLine("   <Interior ss:Color=\"#4F46E5\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine("  </Style>");
        sb.AppendLine(" </Styles>");
        sb.AppendLine(" <Worksheet ss:Name=\"Scoreboard\">");
        sb.AppendLine("  <Table>");
        sb.AppendLine("   <Row ss:StyleID=\"Header\">");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Rank</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Full Name</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Score</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Highest Streak</Data></Cell>");
        sb.AppendLine("    <Cell><Data ss:Type=\"String\">Joined At</Data></Cell>");
        sb.AppendLine("   </Row>");

        var ranked = session.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.JoinedAt)
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            var p = ranked[i];
            sb.AppendLine("   <Row>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{i + 1}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(p.FullName)}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.Score}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{p.HighestStreak}</Data></Cell>");
            sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{p.JoinedAt:yyyy-MM-dd HH:mm:ss}</Data></Cell>");
            sb.AppendLine("   </Row>");
        }

        sb.AppendLine("  </Table>");
        sb.AppendLine(" </Worksheet>");
        sb.AppendLine("</Workbook>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static string EscapeXml(string value)
    {
        return value.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
    }
}
