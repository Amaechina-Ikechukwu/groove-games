using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
            { "Jwt:Issuer", "Groove" },
            { "Jwt:Audience", "GrooveClients" }
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        _authService = new AuthService(_config, new UserStore(), new JwtSigningKey("TestOnlySigningKey-AtLeast32Bytes-Long!!"));
        _authService.EnsureAccount("admin@groove.live", "admin-password", "Admin", Roles.Admin);
    }

    private static RegisterRequest NewRegistration(string role = "Player", string password = "strongPassword123") => new()
    {
        Email = $"user_{Guid.NewGuid():N}@test.live",
        Password = password,
        FullName = "Contender Neo",
        Role = role
    };

    [Fact]
    public void AdminLogin_ReturnsSignedJwtWithAdminRoleClaim()
    {
        var auth = _authService.Login("admin@groove.live", "admin-password");

        Assert.Equal("admin@groove.live", auth.Email);
        Assert.Equal("Admin", auth.Role);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(auth.Token);
        Assert.Equal("Groove", token.Issuer);
        var roleClaim = token.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("Admin", roleClaim.Value);
    }

    [Fact]
    public void Register_CreatesPlayerAccountThatCanSignIn()
    {
        var reg = NewRegistration();
        var response = _authService.Register(reg);

        Assert.Equal(reg.Email, response.Email);
        Assert.Equal("Contender Neo", response.DisplayName);
        Assert.Equal("Player", response.Role);

        var login = _authService.Login(reg.Email, reg.Password);
        Assert.Equal("Player", login.Role);
    }

    [Fact]
    public void Register_StoresHashedPasswordNotPlaintext()
    {
        var reg = NewRegistration();
        _authService.Register(reg);

        var stored = _authService.FindUser(reg.Email)!;
        Assert.DoesNotContain(reg.Password, stored.PasswordHash);
        Assert.StartsWith("pbkdf2$", stored.PasswordHash);
    }

    [Fact]
    public void Register_DuplicateEmail_Throws()
    {
        var reg = NewRegistration();
        reg.Email = "ADMIN@groove.live";
        Assert.Throws<InvalidOperationException>(() => _authService.Register(reg));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    [InlineData("Owner")]
    public void Register_CannotSelfAssignPrivilegedOrUnknownRole(string role)
    {
        Assert.Throws<ArgumentException>(() => _authService.Register(NewRegistration(role)));
    }

    [Fact]
    public void Register_ShortPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _authService.Register(NewRegistration(password: "short")));
    }

    [Fact]
    public void Register_InvalidEmail_Throws()
    {
        var reg = NewRegistration();
        reg.Email = "not-an-email";
        Assert.Throws<ArgumentException>(() => _authService.Register(reg));
    }

    [Fact]
    public void Register_HostRole_CreatesHostAccount()
    {
        var reg = _authService.Register(NewRegistration("Host"));
        Assert.Equal("Host", reg.Role);
    }

    [Fact]
    public void Login_UnknownEmail_IsRejectedAndDoesNotCreateAccount()
    {
        var email = $"admin_{Guid.NewGuid():N}@gmail.com";

        Assert.Throws<UnauthorizedAccessException>(() => _authService.Login(email, "whatever123"));
        Assert.Null(_authService.FindUser(email));
    }

    [Fact]
    public void Login_WrongPassword_IsRejected()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _authService.Login("admin@groove.live", "admin123"));
    }

    [Fact]
    public void BecomeHost_UpgradesPlayerExplicitly()
    {
        var reg = _authService.Register(NewRegistration());
        Assert.Equal("Player", _authService.Login(reg.Email, "strongPassword123").Role);

        var upgraded = _authService.BecomeHost(reg.Email);

        Assert.Equal("Host", upgraded.Role);
        Assert.Equal("Host", _authService.FindUser(reg.Email)!.Role);
    }

    [Fact]
    public void BecomeHost_DoesNotDowngradeAdmin()
    {
        Assert.Equal("Admin", _authService.BecomeHost("admin@groove.live").Role);
    }

    [Fact]
    public void AssignRole_CannotRemoveLastAdmin()
    {
        Assert.Equal(RoleChangeResult.WouldRemoveLastAdmin, _authService.AssignRole("admin@groove.live", "Host"));
        Assert.Equal("Admin", _authService.FindUser("admin@groove.live")!.Role);
    }

    [Fact]
    public void AssignRole_RejectsUnknownRole()
    {
        var reg = _authService.Register(NewRegistration());
        Assert.Equal(RoleChangeResult.InvalidRole, _authService.AssignRole(reg.Email, "SuperAdmin"));
    }

    [Fact]
    public void AssignRole_PromotesExistingUser()
    {
        var reg = _authService.Register(NewRegistration());
        Assert.Equal(RoleChangeResult.Ok, _authService.AssignRole(reg.Email, "host"));
        Assert.Equal("Host", _authService.FindUser(reg.Email)!.Role);
    }

    [Fact]
    public void AuthController_Login_UnknownUser_Returns401()
    {
        var controller = new AuthController(_authService);

        var actionResult = controller.Login(new LoginRequest
        {
            Email = $"controller_host_{Guid.NewGuid():N}@gmail.com",
            Password = "password123"
        });

        Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
    }

    [Fact]
    public void SessionAccess_OnlyOwningHostOrAdminCanControl()
    {
        var session = new GameSession { Pin = "123456", HostId = "owner@test.live" };

        static ClaimsPrincipal As(string email, string role) => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, email),
            new Claim(ClaimTypes.Role, role)
        }, "test"));

        Assert.True(As("owner@test.live", "Host").CanControl(session));
        Assert.False(As("other@test.live", "Host").CanControl(session));
        Assert.False(As("owner@test.live", "Player").CanControl(session));
        Assert.True(As("someone@test.live", "Admin").CanControl(session));
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).CanControl(session));
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
