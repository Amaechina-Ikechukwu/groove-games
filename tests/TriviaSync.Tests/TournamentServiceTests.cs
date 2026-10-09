using TriviaSync.Api.Models;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class TournamentServiceTests
{
    private readonly TournamentService _service = new();

    private static Quiz TwoQuestionQuiz() => new()
    {
        Title = "Quick quiz",
        Questions = new List<Question>
        {
            new() { Text = "1 + 1?", Choices = new() { "1", "2" }, CorrectIndex = 1, TimeLimitSeconds = 20, Points = 1000 },
            new() { Text = "Sky colour?", Choices = new() { "Blue", "Green" }, CorrectIndex = 0, TimeLimitSeconds = 20, Points = 1000 }
        }
    };

    private (string TournamentId, string SessionId) OpenSelfPaced()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "", TwoQuestionQuiz(), SessionModes.SelfPaced);
        _service.OpenSession(s.Id, DateTime.UtcNow.AddDays(1));
        return (t.Id, s.Id);
    }

    [Fact]
    public void Join_WithCode_AddsMembership()
    {
        var t = _service.Create("host@test.live", "Friday league", "");

        _service.Join(t.JoinCode.ToLowerInvariant(), "Pat@Test.live", "Pat");

        Assert.True(_service.IsMember(t.Id, "pat@test.live"));
        Assert.Single(_service.JoinedBy("pat@test.live"));
    }

    [Fact]
    public void Join_HostCannotJoinOwnTournament()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var ex = Assert.Throws<TournamentException>(() => _service.Join(t.JoinCode, "host@test.live", "Host"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void SelfPaced_NonMemberCannotPlay()
    {
        var (_, sessionId) = OpenSelfPaced();
        var ex = Assert.Throws<TournamentException>(() => _service.StartOrResume(sessionId, "stranger@test.live", "Stranger"));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public void SelfPaced_FullAttempt_ScoresAndNeverLeaksAnswerUpFront()
    {
        var (tournamentId, sessionId) = OpenSelfPaced();
        _service.EnsureMember(tournamentId, "pat@test.live", "Pat");

        var state = _service.StartOrResume(sessionId, "pat@test.live", "Pat");
        Assert.NotNull(state.Question);
        Assert.Equal(0, state.Question!.Index);

        var (first, afterFirst) = _service.Answer(sessionId, "pat@test.live", 0, 1);
        Assert.True(first!.IsCorrect);
        Assert.True(first.PointsEarned > 500);
        Assert.Null(afterFirst.Question); // next question isn't timed until requested

        // Replaying the same answer is ignored rather than scored twice.
        var (dupe, _) = _service.Answer(sessionId, "pat@test.live", 0, 1);
        Assert.Null(dupe);

        var next = _service.StartOrResume(sessionId, "pat@test.live", "Pat");
        Assert.Equal(1, next.Question!.Index);
        var (second, done) = _service.Answer(sessionId, "pat@test.live", 1, 1);
        Assert.False(second!.IsCorrect);
        Assert.Equal(0, second.CorrectIndex);
        Assert.True(done.Done);
        Assert.Equal(1, done.CorrectCount);

        var standings = _service.Standings(tournamentId);
        Assert.Single(standings);
        Assert.Equal(first.PointsEarned, standings[0].TotalScore);
        Assert.Single(_service.AttemptsBy("pat@test.live"));
    }

    [Fact]
    public void SelfPaced_ClosedSessionRejectsNewAttempts()
    {
        var (tournamentId, sessionId) = OpenSelfPaced();
        _service.EnsureMember(tournamentId, "pat@test.live", "Pat");
        _service.CloseSession(sessionId);

        var ex = Assert.Throws<TournamentException>(() => _service.StartOrResume(sessionId, "pat@test.live", "Pat"));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void OpenSession_RejectsPastDeadline()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "", TwoQuestionQuiz(), SessionModes.SelfPaced);
        Assert.Throws<TournamentException>(() => _service.OpenSession(s.Id, DateTime.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void EndLive_FinishedGameRecordsResultsAndClosesSession()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "Round 1", TwoQuestionQuiz(), SessionModes.Live);
        _service.SetLive(s.Id, "123456");

        _service.EndLive(s.Id, finished: true, new[] { new LivePlayerResult("pat@test.live", "Pat", 1800, 2, 2) });

        Assert.Equal(SessionStatuses.Closed, _service.GetSession(s.Id)!.Status);
        Assert.Equal(1800, _service.Standings(t.Id)[0].TotalScore);
    }

    [Fact]
    public void EndLive_EarlyEndDiscardsAndAllowsRerun()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "Round 1", TwoQuestionQuiz(), SessionModes.Live);
        _service.SetLive(s.Id, "123456");

        _service.EndLive(s.Id, finished: false, Array.Empty<LivePlayerResult>());

        Assert.Equal(SessionStatuses.Draft, _service.GetSession(s.Id)!.Status);
        Assert.Empty(_service.Standings(t.Id));
    }
}
