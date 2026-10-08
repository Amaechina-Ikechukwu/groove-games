using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Data;

[Table("quizzes")]
public class QuizEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("questions_json", TypeName = "text")]
    public string QuestionsJson { get; set; } = "[]";

    public Quiz ToModel()
    {
        var questions = JsonSerializer.Deserialize<List<Question>>(QuestionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return new Quiz
        {
            Id = Id,
            Title = Title,
            Description = Description,
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            Questions = questions
        };
    }

    public static QuizEntity FromModel(Quiz quiz)
    {
        return new QuizEntity
        {
            Id = quiz.Id,
            Title = quiz.Title,
            Description = quiz.Description,
            CreatedBy = quiz.CreatedBy,
            CreatedAt = quiz.CreatedAt,
            QuestionsJson = JsonSerializer.Serialize(quiz.Questions)
        };
    }
}

[Table("game_sessions")]
public class GameSessionEntity
{
    [Key]
    [Column("pin")]
    public string Pin { get; set; } = string.Empty;

    [Column("quiz_id")]
    public string QuizId { get; set; } = string.Empty;

    [Column("quiz_title")]
    public string QuizTitle { get; set; } = string.Empty;

    [Column("host_id")]
    public string HostId { get; set; } = string.Empty;

    [Column("host_email")]
    public string HostEmail { get; set; } = string.Empty;

    [Column("tournament_id")]
    public string TournamentId { get; set; } = string.Empty;

    [Column("tournament_name")]
    public string TournamentName { get; set; } = string.Empty;

    [Column("session_type")]
    public string SessionType { get; set; } = "Single";

    [Column("session_number")]
    public int SessionNumber { get; set; } = 1;

    [Column("total_sessions")]
    public int TotalSessions { get; set; } = 1;

    [Column("state")]
    public string State { get; set; } = "Lobby";

    [Column("current_question_index")]
    public int CurrentQuestionIndex { get; set; } = -1;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("connected_player_count")]
    public int ConnectedPlayerCount { get; set; } = 0;

    [Column("total_player_count")]
    public int TotalPlayerCount { get; set; } = 0;
}

[Table("round_audits")]
public class RoundAuditEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Column("pin")]
    public string Pin { get; set; } = string.Empty;

    [Column("round_index")]
    public int RoundIndex { get; set; }

    [Column("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [Column("correct_index")]
    public int CorrectIndex { get; set; }

    [Column("stats_json", TypeName = "text")]
    public string StatsJson { get; set; } = "{}";

    [Column("submissions_json", TypeName = "text")]
    public string SubmissionsJson { get; set; } = "[]";

    [Column("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public class TriviaDbContext : DbContext
{
    public TriviaDbContext(DbContextOptions<TriviaDbContext> options) : base(options)
    {
    }

    public DbSet<QuizEntity> Quizzes => Set<QuizEntity>();
    public DbSet<GameSessionEntity> GameSessions => Set<GameSessionEntity>();
    public DbSet<PersistentPlayer> Players => Set<PersistentPlayer>();
    public DbSet<RoundAuditEntity> RoundAudits => Set<RoundAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PersistentPlayer>()
            .HasIndex(p => p.HostId);

        modelBuilder.Entity<PersistentPlayer>()
            .HasIndex(p => p.TotalPointsAllTime);

        modelBuilder.Entity<GameSessionEntity>()
            .HasIndex(s => s.TournamentId);

        modelBuilder.Entity<GameSessionEntity>()
            .HasIndex(s => s.HostId);
    }
}
