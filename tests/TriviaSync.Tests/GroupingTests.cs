using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class GroupingTests
{
    private readonly GameEngineService _engine;

    public GroupingTests()
    {
        var client = new Mock<IQuizClient>();
        var clients = new Mock<IHubClients<IQuizClient>>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(client.Object);
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<QuizHub, IQuizClient>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        _engine = new GameEngineService(hub.Object, new Mock<ITriviaDataService>().Object, NullLogger<GameEngineService>.Instance);
    }

    private GameSession Session(int players, string? mode, int value)
    {
        var quiz = new Quiz
        {
            Title = "Q",
            Questions = new List<Question> { new() { Text = "?", Choices = new() { "a", "b" }, CorrectIndex = 0 } }
        };
        var s = _engine.CreateSession(quiz, "host", groupingMode: mode, groupingValue: value);
        for (int i = 0; i < players; i++)
            _engine.JoinOrReconnectPlayer(s.Pin, $"c{i}", $"P{i}", $"p{i}@x.com");
        return s;
    }

    [Theory]
    [InlineData(9, "ByCount", 2, 2, 5)]
    [InlineData(10, "BySize", 4, 3, 4)]
    [InlineData(2, "ByCount", 5, 2, 1)]
    public void AssignGroups_BalancesPlayers(int players, string mode, int value, int expectedGroups, int maxSize)
    {
        var s = Session(players, mode, value);
        GameEngineService.AssignGroups(s);

        Assert.Equal(expectedGroups, s.Groups.Count);
        var sizes = s.Players.Values.GroupBy(p => p.GroupId).Select(g => g.Count()).ToList();
        Assert.Equal(players, sizes.Sum());
        Assert.Equal(maxSize, sizes.Max());
        Assert.True(sizes.Max() - sizes.Min() <= 1);
    }

    [Fact]
    public void InvalidGrouping_IsIgnored()
    {
        Assert.False(Session(2, "ByCount", 1).GroupingEnabled);
        Assert.False(Session(2, null, 3).GroupingEnabled);
    }

    [Fact]
    public async Task Finish_AddsGroupTotalToEachMember_AndNamesWinner()
    {
        var s = Session(4, "ByCount", 2);
        await _engine.StartQuizAsync(s.Pin);
        var members = s.Groups.ToDictionary(g => g.Id, g => s.Players.Values.Where(p => p.GroupId == g.Id).ToList());
        var winnerGroup = s.Groups[0];
        var loserGroup = s.Groups[1];
        members[winnerGroup.Id][0].Score = 300; members[winnerGroup.Id][1].Score = 200;
        members[loserGroup.Id][0].Score = 100; members[loserGroup.Id][1].Score = 50;

        await _engine.FinishGameAsync(s);

        Assert.All(members[winnerGroup.Id], p => Assert.Equal(500, p.GroupBonus));
        Assert.Equal(800, members[winnerGroup.Id][0].Score);
        Assert.Equal(700, members[winnerGroup.Id][1].Score);
        Assert.Equal(250, members[loserGroup.Id][0].Score);
        Assert.Equal(GameEngineService.GroupStandings(s)[0].Id, winnerGroup.Id);
    }

    [Fact]
    public async Task LateJoiner_GoesToSmallestGroup()
    {
        var s = Session(3, "ByCount", 2); // groups of 2 and 1
        await _engine.StartQuizAsync(s.Pin);
        _engine.JoinOrReconnectPlayer(s.Pin, "late", "Late", "late@x.com");

        var sizes = s.Players.Values.GroupBy(p => p.GroupId).Select(g => g.Count()).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 2, 2 }, sizes);
    }
}
