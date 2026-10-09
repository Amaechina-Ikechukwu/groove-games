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

    private static Question Q(string text = "Capital of France?", params string[] choices) => new()
    {
        Text = text,
        Choices = choices.Length > 0 ? choices.ToList() : new() { "Paris", "Rome" },
        CorrectIndex = 0,
        TimeLimitSeconds = 20,
        Points = 1000
    };

    [Fact]
    public void Sessions_GetUniqueSixDigitCodesThatCanBeLookedUp()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var a = _service.CreateSession(t.Id, "A", TwoQuestionQuiz(), SessionModes.Live);
        var b = _service.CreateSession(t.Id, "B", TwoQuestionQuiz(), SessionModes.SelfPaced);

        Assert.Matches(@"^\d{6}$", a.Code);
        Assert.NotEqual(a.Code, b.Code);
        Assert.Equal(b.Id, _service.FindSessionByCode(b.Code)!.Id);
        Assert.True(_service.CodeInUse(a.Code));
        Assert.Null(_service.FindSessionByCode("000000"));
    }

    [Fact]
    public void UpdateSession_ReplacesQuestionsAndRenames()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "Old name", TwoQuestionQuiz(), SessionModes.SelfPaced);

        var updated = _service.UpdateSession(s.Id, "  New name ", new List<Question> { Q("  Largest planet? ", "Jupiter", "Mars", "Venus") });

        Assert.Equal("New name", updated.Title);
        var quiz = _service.QuizFor(updated);
        Assert.Single(quiz.Questions);
        Assert.Equal("Largest planet?", quiz.Questions[0].Text);
        Assert.Equal(3, quiz.Questions[0].Choices.Count);
    }

    [Fact]
    public void UpdateSession_RejectsIncompleteQuestions()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "Quiz", TwoQuestionQuiz(), SessionModes.SelfPaced);

        var oneAnswer = Q("Only one?", "Lonely");
        var duplicate = Q("Twins?", "Same", "same");
        var noCorrect = Q(); noCorrect.CorrectIndex = 5;
        var blankText = Q("   ");
        var tooFast = Q(); tooFast.TimeLimitSeconds = 1;

        foreach (var bad in new[] { oneAnswer, duplicate, noCorrect, blankText, tooFast })
        {
            var ex = Assert.Throws<TournamentException>(() => _service.UpdateSession(s.Id, "Quiz", new List<Question> { bad }));
            Assert.Equal(400, ex.StatusCode);
        }
        // Nothing was saved by the failed attempts.
        Assert.Equal(2, _service.QuizFor(_service.GetSession(s.Id)!).Questions.Count);
    }

    [Fact]
    public void UpdateSession_QuestionsLockOncePlayersHavePlayed_ButRenameStillWorks()
    {
        var (tournamentId, sessionId) = OpenSelfPaced();
        _service.EnsureMember(tournamentId, "pat@test.live", "Pat");
        _service.StartOrResume(sessionId, "pat@test.live", "Pat");
        Assert.True(_service.QuestionsLocked(_service.GetSession(sessionId)!));

        var ex = Assert.Throws<TournamentException>(() => _service.UpdateSession(sessionId, "Quiz", new List<Question> { Q() }));
        Assert.Equal(409, ex.StatusCode);

        var renamed = _service.UpdateSession(sessionId, "Renamed", null);
        Assert.Equal("Renamed", renamed.Title);
        Assert.Equal(2, _service.QuizFor(renamed).Questions.Count);
    }

    [Fact]
    public void BlankSession_CannotBeOpenedUntilItHasQuestions()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "My own quiz", new Quiz { Title = "My own quiz" }, SessionModes.SelfPaced);

        Assert.Throws<TournamentException>(() => _service.OpenSession(s.Id, DateTime.UtcNow.AddDays(1)));

        _service.UpdateSession(s.Id, "My own quiz", new List<Question> { Q() });
        var opened = _service.OpenSession(s.Id, DateTime.UtcNow.AddDays(1));
        Assert.Equal(SessionStatuses.Open, opened.Status);
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

    [Fact]
    public void SelfPaced_RecordsWhenOpenedAndClosed_AndKeepsTheOriginalDeadline()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var s = _service.CreateSession(t.Id, "Quiz", TwoQuestionQuiz(), SessionModes.SelfPaced);
        Assert.Null(s.OpenedAt);
        Assert.True(s.CreatedAt <= DateTime.UtcNow);

        var deadline = DateTime.UtcNow.AddDays(2);
        _service.OpenSession(s.Id, deadline);
        Assert.NotNull(s.OpenedAt);
        Assert.Null(s.ClosedAt);
        var firstOpened = s.OpenedAt;

        _service.CloseSession(s.Id);
        Assert.NotNull(s.ClosedAt);
        Assert.True(s.ClosesAt > DateTime.UtcNow.AddDays(1), "closing early must not overwrite the deadline");

        // Reopening clears the closed time but keeps when it was first opened.
        _service.OpenSession(s.Id, DateTime.UtcNow.AddDays(3));
        Assert.Null(s.ClosedAt);
        Assert.Equal(firstOpened, s.OpenedAt);
    }

    [Fact]
    public void LiveSession_RecordsStartAndFinish_AndResetsWhenEndedEarly()
    {
        var t = _service.Create("host@test.live", "Friday league", "");
        var finished = _service.CreateSession(t.Id, "Finished", TwoQuestionQuiz(), SessionModes.Live);
        var abandoned = _service.CreateSession(t.Id, "Abandoned", TwoQuestionQuiz(), SessionModes.Live);

        _service.SetLive(finished.Id, "111111");
        Assert.NotNull(finished.OpenedAt);
        _service.EndLive(finished.Id, finished: true, Array.Empty<LivePlayerResult>());
        Assert.NotNull(finished.ClosedAt);
        Assert.True(finished.ClosedAt >= finished.OpenedAt);

        _service.SetLive(abandoned.Id, "222222");
        _service.EndLive(abandoned.Id, finished: false, Array.Empty<LivePlayerResult>());
        Assert.Null(abandoned.OpenedAt);
        Assert.Null(abandoned.ClosedAt);
    }
}
