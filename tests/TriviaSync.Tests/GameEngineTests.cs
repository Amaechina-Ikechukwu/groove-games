using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class GameEngineTests
{
    private readonly Mock<IHubContext<QuizHub, IQuizClient>> _mockHubContext;
    private readonly Mock<IHubClients<IQuizClient>> _mockClients;
    private readonly Mock<IQuizClient> _mockClient;
    private readonly Mock<ITriviaDataService> _mockDataService;
    private readonly GameEngineService _engine;

    public GameEngineTests()
    {
        _mockHubContext = new Mock<IHubContext<QuizHub, IQuizClient>>();
        _mockClients = new Mock<IHubClients<IQuizClient>>();
        _mockClient = new Mock<IQuizClient>();
        _mockDataService = new Mock<ITriviaDataService>();

        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClient.Object);
        _mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_mockClient.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);

        _engine = new GameEngineService(_mockHubContext.Object, _mockDataService.Object, NullLogger<GameEngineService>.Instance);
    }

    [Fact]
    public void CalculatePoints_CorrectCalculationBasedOnElapsedTime()
    {
        int basePoints = 1000;
        double timeLimit = 20.0;

        // Instant response (0s elapsed) -> 100% of points
        int pointsInstant = _engine.CalculatePoints(basePoints, 0.0, timeLimit);
        Assert.Equal(1000, pointsInstant);

        // Halfway response (10s elapsed) -> 1000 * (1 - 0.5 * 0.5) = 750 points
        int pointsHalfway = _engine.CalculatePoints(basePoints, 10.0, timeLimit);
        Assert.Equal(750, pointsHalfway);

        // Deadline response (20s elapsed) -> 1000 * (1 - 1.0 * 0.5) = 500 points
        int pointsDeadline = _engine.CalculatePoints(basePoints, 20.0, timeLimit);
        Assert.Equal(500, pointsDeadline);

        // Past deadline (25s elapsed) -> clamped at 50%
        int pointsPastDeadline = _engine.CalculatePoints(basePoints, 25.0, timeLimit);
        Assert.Equal(500, pointsPastDeadline);
    }

    [Fact]
    public void CreateSession_GeneratesUnique6CharPin()
    {
        var quiz = new Quiz
        {
            Id = "test_q",
            Title = "Test Quiz",
            Questions = new List<Question>
            {
                new() { Text = "Q1", Choices = new() { "A", "B" }, CorrectIndex = 0 }
            }
        };

        var session = _engine.CreateSession(quiz, "host_1");

        Assert.NotNull(session);
        Assert.Equal(6, session.Pin.Length);
        Assert.Equal(GameState.Lobby, session.State);
    }

    [Fact]
    public void JoinOrReconnectPlayer_RegistersNewPlayerAndHandlesReconnect()
    {
        var quiz = new Quiz
        {
            Id = "test_q",
            Title = "Test Quiz",
            Questions = new List<Question>
            {
                new() { Text = "Q1", Choices = new() { "A", "B" }, CorrectIndex = 0 }
            }
        };

        var session = _engine.CreateSession(quiz, "host_1");

        // 1. Initial Join
        var (success1, msg1, player1) = _engine.JoinOrReconnectPlayer(session.Pin, "conn_1", "Jane Doe", "jane@corp.com");
        Assert.True(success1);
        Assert.NotNull(player1);
        Assert.Equal("Jane Doe", player1.FullName);
        Assert.Equal(1, session.ConnectedPlayerCount);

        // 2. Disconnect
        var (discPin, discPlayer) = _engine.HandlePlayerDisconnect("conn_1");
        Assert.Equal(session.Pin, discPin);
        Assert.NotNull(discPlayer);
        Assert.False(discPlayer.IsConnected);
        Assert.Equal(0, session.ConnectedPlayerCount);

        // 3. Reconnect with new connection ID but same FullName
        var (success2, msg2, player2) = _engine.JoinOrReconnectPlayer(session.Pin, "conn_2", "Jane Doe", "jane@corp.com");
        Assert.True(success2);
        Assert.NotNull(player2);
        Assert.Equal(player1.PlayerId, player2.PlayerId);
        Assert.True(player2.IsConnected);
        Assert.Equal(1, session.ConnectedPlayerCount);
    }

    [Fact]
    public async Task FinishGame_QuickGameFeedsTheSharedLeaderboard_ButTournamentGameDoesNot()
    {
        var quiz = new Quiz
        {
            Title = "Q",
            Questions = new List<Question> { new() { Text = "?", Choices = new() { "a", "b" }, CorrectIndex = 0 } }
        };

        var quick = _engine.CreateSession(quiz, "host@test.live");
        _engine.JoinOrReconnectPlayer(quick.Pin, "conn1", "Quick Quinn", "");
        await _engine.FinishGameAsync(quick);

        var tournament = _engine.CreateSession(quiz, "host@test.live", tournamentSessionId: "ses_1");
        _engine.JoinOrReconnectPlayer(tournament.Pin, "conn2", "Tourney Tess", "tess@test.live");
        await _engine.FinishGameAsync(tournament);

        _mockDataService.Verify(d => d.UpdatePlayerStatsAsync("Quick Quinn", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockDataService.Verify(d => d.UpdatePlayerStatsAsync("Tourney Tess", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
