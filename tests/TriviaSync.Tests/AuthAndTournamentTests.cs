using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TriviaSync.Api.Controllers;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class AuthAndTournamentTests
{
    private readonly AuthService _authService;
    private readonly IConfiguration _config;

    public AuthAndTournamentTests()
    {
        var settings = new Dictionary<string, string?>
        {
            { "Jwt:Key", "GrooveSuperSecretSigningKeyForDevelopmentAndTesting2026!" },
            { "Jwt:Issuer", "Groove" },
            { "Jwt:Audience", "GrooveClients" }
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        _authService = new AuthService(_config);
    }

    [Fact]
    public void AdminLogin_ReturnsSignedJwtWithAdminRoleClaim()
    {
        var auth = _authService.Login("admin@groove.live", "admin123");

        Assert.NotNull(auth);
        Assert.Equal("admin@groove.live", auth.Email);
        Assert.Equal("Admin", auth.Role);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        // Validate JWT token contents
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(auth.Token);

        Assert.Equal("Groove", token.Issuer);
        var roleClaim = token.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("Admin", roleClaim.Value);
    }

    [Fact]
    public void Register_CreatesPlayerAccountAndGeneratesToken()
    {
        var email = $"player_{Guid.NewGuid():N}@test.live";
        var reg = new RegisterRequest
        {
            Email = email,
            Password = "strongPassword123",
            FullName = "Contender Neo",
            Role = "Player"
        };

        var response = _authService.Register(reg);

        Assert.NotNull(response);
        Assert.Equal(email, response.Email);
        Assert.Equal("Contender Neo", response.DisplayName);
        Assert.Equal("Player", response.Role);
        Assert.False(string.IsNullOrWhiteSpace(response.Token));

        // Verify can log in immediately after registration
        var login = _authService.Login(email, "strongPassword123");
        Assert.Equal(email, login.Email);
        Assert.Equal("Player", login.Role);
    }

    [Fact]
    public void Register_DuplicateEmail_ThrowsException()
    {
        var email = "admin@groove.live";
        var reg = new RegisterRequest
        {
            Email = email,
            Password = "password",
            FullName = "Imposter Admin"
        };

        Assert.Throws<InvalidOperationException>(() => _authService.Register(reg));
    }

    [Fact]
    public void TournamentLeaderboard_AggregatesPlayersAcrossMultipleSessions()
    {
        var mockDataService = new Mock<ITriviaDataService>();
        var mockExportService = new Mock<IExportService>();
        var mockGameEngine = new Mock<IGameEngineService>();

        string tournamentId = "tourn_summer_clash";

        var session1 = new GameSession
        {
            Pin = "111222",
            TournamentId = tournamentId,
            TournamentName = "Summer Esports Clash",
            SessionNumber = 1,
            TotalSessions = 2,
            Quiz = new Quiz { Title = "Round 1 Trivia" }
        };
        session1.Players["p1"] = new Player
        {
            PlayerId = "p1",
            FullName = "Player One",
            Score = 1500,
            CorrectAnswers = 3,
            TotalAnswers = 3,
            HighestStreak = 3
        };
        session1.Players["p2"] = new Player
        {
            PlayerId = "p2",
            FullName = "Player Two",
            Score = 800,
            CorrectAnswers = 2,
            TotalAnswers = 3,
            HighestStreak = 2
        };

        var session2 = new GameSession
        {
            Pin = "333444",
            TournamentId = tournamentId,
            TournamentName = "Summer Esports Clash",
            SessionNumber = 2,
            TotalSessions = 2,
            Quiz = new Quiz { Title = "Round 2 Trivia" }
        };
        session2.Players["p1"] = new Player
        {
            PlayerId = "p1",
            FullName = "Player One",
            Score = 2000,
            CorrectAnswers = 4,
            TotalAnswers = 4,
            HighestStreak = 4
        };
        session2.Players["p2"] = new Player
        {
            PlayerId = "p2",
            FullName = "Player Two",
            Score = 1200,
            CorrectAnswers = 3,
            TotalAnswers = 4,
            HighestStreak = 3
        };

        mockGameEngine.Setup(m => m.GetSessionsByTournament(tournamentId))
            .Returns(new List<GameSession> { session1, session2 });

        var controller = new LeaderboardController(
            mockDataService.Object,
            mockExportService.Object,
            mockGameEngine.Object,
            NullLogger<LeaderboardController>.Instance
        );

        var result = controller.GetTournamentLeaderboard(tournamentId) as OkObjectResult;
        Assert.NotNull(result);

        // Value contains tournamentId, totalSessions, leaderboard
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("tourn_summer_clash", json);
        Assert.Contains("Player One", json);
        Assert.Contains("3500", json); // 1500 + 2000 total score
    }
}
