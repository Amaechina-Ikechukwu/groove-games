namespace TriviaSync.Api.Services;

public static class Scoring
{
    /// <summary>Points = BasePoints * (1 - (Elapsed / TimeLimit) * 0.5). Used by live and self-paced play.</summary>
    public static int CalculatePoints(int basePoints, double elapsedSeconds, double timeLimitSeconds)
    {
        if (timeLimitSeconds <= 0) timeLimitSeconds = 20;
        double ratio = Math.Clamp(elapsedSeconds / timeLimitSeconds, 0.0, 1.0);
        double multiplier = 1.0 - (ratio * 0.5);
        int points = (int)Math.Round(basePoints * multiplier);
        return Math.Max(0, points);
    }
}
