using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class LiveUpdateTests
{
    private sealed class Recorder : ILiveNotifier
    {
        public List<(string Group, string Kind)> Sent { get; } = new();
        public void Publish(string group, string kind) => Sent.Add((group, kind));
        public bool Has(string group, string kind) => Sent.Contains((group, kind));
    }

    private static ClaimsPrincipal Person(string email, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, email),
        new Claim(ClaimTypes.Role, role)
    }, "test"));

    private static Quiz TwoQuestions() => new()
    {
        Title = "Quiz",
        Questions = new List<Question>
        {
            new() { Text = "1?", Choices = new() { "a", "b" }, CorrectIndex = 0 },
            new() { Text = "2?", Choices = new() { "a", "b" }, CorrectIndex = 0 }
        }
    };

    // -------------------------------------------------------------------------
    // Who may listen to what
    // -------------------------------------------------------------------------
    [Fact]
    public void Resolve_OnlyHandsOutGroupsThePersonMaySee()
    {
        var service = new TournamentService();
        var t = service.Create("host@test.live", "League", "");
        service.EnsureMember(t.Id, "pat@test.live", "Pat");

        var anonymous = (ClaimsPrincipal?)null;
        var member = Person("pat@test.live", "Player");
        var stranger = Person("eve@test.live", "Player");
        var owner = Person("host@test.live", "Host");
        var otherHost = Person("other@test.live", "Host");
        var admin = Person("admin@test.live", "Admin");

        // Public things
        Assert.Equal(LiveTopics.Public, LiveTopics.Resolve("public", anonymous, service));
        Assert.Equal(LiveTopics.TournamentPublic(t.Id), LiveTopics.Resolve($"tournament:{t.Id}:pub", anonymous, service));

        // Own account and admin feed
        Assert.Null(LiveTopics.Resolve("user", anonymous, service));
        Assert.Equal(LiveTopics.User("pat@test.live"), LiveTopics.Resolve("user", member, service));
        Assert.Null(LiveTopics.Resolve("admin", member, service));
        Assert.Null(LiveTopics.Resolve("admin", owner, service));
        Assert.Equal(LiveTopics.Admin, LiveTopics.Resolve("admin", admin, service));

        // Members-only tournament feed
        var feed = $"tournament:{t.Id}";
        Assert.Null(LiveTopics.Resolve(feed, anonymous, service));
        Assert.Null(LiveTopics.Resolve(feed, stranger, service));
        Assert.Null(LiveTopics.Resolve(feed, otherHost, service));
        Assert.Equal(LiveTopics.Tournament(t.Id), LiveTopics.Resolve(feed, member, service));
        Assert.Equal(LiveTopics.Tournament(t.Id), LiveTopics.Resolve(feed, owner, service));
        Assert.Equal(LiveTopics.Tournament(t.Id), LiveTopics.Resolve(feed, admin, service));

        // Manager-only feed
        var managers = $"tournament:{t.Id}:mgr";
        Assert.Null(LiveTopics.Resolve(managers, member, service));
        Assert.Null(LiveTopics.Resolve(managers, otherHost, service));
        Assert.Equal(LiveTopics.TournamentManagers(t.Id), LiveTopics.Resolve(managers, owner, service));
        Assert.Equal(LiveTopics.TournamentManagers(t.Id), LiveTopics.Resolve(managers, admin, service));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("tournament:")]
    [InlineData("tournament:missing")]
    [InlineData("tournament:a:b:c")]
    [InlineData("live:admin")]
    public void Resolve_RejectsUnknownOrMalformedTopics(string topic)
    {
        Assert.Null(LiveTopics.Resolve(topic, Person("admin@test.live", "Admin"), new TournamentService()));
    }

    [Fact]
    public void Resolve_RemovedMemberLosesTheMembersFeed()
    {
        var service = new TournamentService();
        var t = service.Create("host@test.live", "League", "");
        service.EnsureMember(t.Id, "pat@test.live", "Pat");
        service.RemoveMember(t.Id, "pat@test.live");

        Assert.Null(LiveTopics.Resolve($"tournament:{t.Id}", Person("pat@test.live", "Player"), service));
    }

    // -------------------------------------------------------------------------
    // What each change announces
    // -------------------------------------------------------------------------
    [Fact]
    public void JoiningATournament_TellsTheHostAndTheMember()
    {
        var live = new Recorder();
        var service = new TournamentService(live);
        var t = service.Create("host@test.live", "League", "");
        live.Sent.Clear();

        service.Join(t.JoinCode, "pat@test.live", "Pat");

        Assert.True(live.Has(LiveTopics.Tournament(t.Id), "members"));
        Assert.True(live.Has(LiveTopics.User("host@test.live"), "hosting"));
        Assert.True(live.Has(LiveTopics.User("pat@test.live"), "joined"));
        Assert.True(live.Has(LiveTopics.Admin, "tournaments"));
    }

    [Fact]
    public void ChangingSessions_TellsMembersHostAndAdmins()
    {
        var live = new Recorder();
        var service = new TournamentService(live);
        var t = service.Create("host@test.live", "League", "");
        service.EnsureMember(t.Id, "pat@test.live", "Pat");
        live.Sent.Clear();

        var s = service.CreateSession(t.Id, "Quiz", TwoQuestions(), SessionModes.SelfPaced);
        service.OpenSession(s.Id, DateTime.UtcNow.AddDays(1));
        service.CloseSession(s.Id);

        Assert.True(live.Has(LiveTopics.Tournament(t.Id), "sessions"));
        Assert.True(live.Has(LiveTopics.User("pat@test.live"), "joined"));
        Assert.True(live.Has(LiveTopics.User("host@test.live"), "hosting"));
        Assert.True(live.Has(LiveTopics.Admin, "tournaments"));
        Assert.Equal(3, live.Sent.Count(x => x == (LiveTopics.Tournament(t.Id), "sessions")));
    }

    [Fact]
    public void AnsweringAQuestion_UpdatesStandingsHostResultsAndPlayerHistory()
    {
        var live = new Recorder();
        var service = new TournamentService(live);
        var t = service.Create("host@test.live", "League", "");
        service.EnsureMember(t.Id, "pat@test.live", "Pat");
        var s = service.CreateSession(t.Id, "Quiz", TwoQuestions(), SessionModes.SelfPaced);
        service.OpenSession(s.Id, DateTime.UtcNow.AddDays(1));
        service.StartOrResume(s.Id, "pat@test.live", "Pat");
        live.Sent.Clear();

        service.Answer(s.Id, "pat@test.live", 0, 0);

        Assert.True(live.Has(LiveTopics.TournamentPublic(t.Id), "standings"));
        Assert.True(live.Has(LiveTopics.TournamentManagers(t.Id), "attempts"));
        Assert.True(live.Has(LiveTopics.User("pat@test.live"), "history"));
    }

    [Fact]
    public void FinishingALiveSession_UpdatesStandingsForEveryPlayer()
    {
        var live = new Recorder();
        var service = new TournamentService(live);
        var t = service.Create("host@test.live", "League", "");
        var s = service.CreateSession(t.Id, "Live", TwoQuestions(), SessionModes.Live);
        service.SetLive(s.Id, "123456");
        live.Sent.Clear();

        service.EndLive(s.Id, finished: true, new[]
        {
            new LivePlayerResult("pat@test.live", "Pat", 900, 1, 2),
            new LivePlayerResult("sam@test.live", "Sam", 700, 1, 2)
        });

        Assert.True(live.Has(LiveTopics.TournamentPublic(t.Id), "standings"));
        Assert.True(live.Has(LiveTopics.User("pat@test.live"), "history"));
        Assert.True(live.Has(LiveTopics.User("sam@test.live"), "history"));
        Assert.True(live.Has(LiveTopics.Tournament(t.Id), "sessions"));
    }

    [Fact]
    public void DeletingATournament_AnnouncesItToViewersAndMembers()
    {
        var live = new Recorder();
        var service = new TournamentService(live);
        var t = service.Create("host@test.live", "League", "");
        service.EnsureMember(t.Id, "pat@test.live", "Pat");
        live.Sent.Clear();

        service.Delete(t.Id);

        Assert.True(live.Has(LiveTopics.Tournament(t.Id), "deleted"));
        Assert.True(live.Has(LiveTopics.TournamentPublic(t.Id), "deleted"));
        Assert.True(live.Has(LiveTopics.User("pat@test.live"), "joined"));
        Assert.True(live.Has(LiveTopics.Public, "tournaments"));
    }

    // -------------------------------------------------------------------------
    // Bursts become one message
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Notifier_CollapsesABurstIntoOneMessagePerGroupAndKind()
    {
        var client = new Mock<IQuizClient>();
        client.Setup(c => c.Changed(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients<IQuizClient>>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<QuizHub, IQuizClient>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var notifier = new LiveNotifier(hub.Object, NullLogger<LiveNotifier>.Instance);
        for (var i = 0; i < 25; i++) notifier.Publish("live:t:abc:pub", "standings");
        notifier.Publish("live:t:abc:pub", "details");
        notifier.Publish("live:t:xyz:pub", "standings");

        await Task.Delay(900);

        client.Verify(c => c.Changed("live:t:abc:pub", "standings"), Times.Once);
        client.Verify(c => c.Changed("live:t:abc:pub", "details"), Times.Once);
        client.Verify(c => c.Changed("live:t:xyz:pub", "standings"), Times.Once);

        // A later change after the burst is delivered again.
        notifier.Publish("live:t:abc:pub", "standings");
        await Task.Delay(900);
        client.Verify(c => c.Changed("live:t:abc:pub", "standings"), Times.Exactly(2));
    }
}
