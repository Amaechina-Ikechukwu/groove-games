using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaSync.Api.Models;

[Table("players")]
public class PersistentPlayer
{
    [Key]
    [Column("player_id")]
    public string PlayerId { get; set; } = string.Empty;

    [Column("full_name")]
    public string FullName { get; set; } = string.Empty;

    [Column("organization_id")]
    public string OrganizationId { get; set; } = "global";

    [Column("host_id")]
    public string HostId { get; set; } = string.Empty;

    [Column("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [Column("total_points_all_time")]
    public long TotalPointsAllTime { get; set; } = 0;

    [Column("quizzes_played")]
    public int QuizzesPlayed { get; set; } = 0;

    [Column("questions_answered")]
    public int QuestionsAnswered { get; set; } = 0;

    [Column("correct_answers_count")]
    public int CorrectAnswersCount { get; set; } = 0;

    [Column("highest_streak")]
    public int HighestStreak { get; set; } = 0;

    [Column("last_active")]
    public DateTime LastActive { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public double AccuracyPercentage =>
        QuestionsAnswered > 0 ? Math.Round((double)CorrectAnswersCount / QuestionsAnswered * 100.0, 1) : 0.0;
}
