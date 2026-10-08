using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TriviaSync.Api.Services;
using Xunit;

namespace TriviaSync.Tests;

public class LeaderboardTests
{
    private readonly PostgresDataService _dataService;

    public LeaderboardTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"ConnectionStrings:DefaultConnection", ""} // forces high performance local fallback
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        _dataService = new PostgresDataService(mockScopeFactory.Object, config, NullLogger<PostgresDataService>.Instance);
    }

    [Fact]
    public void NormalizePlayerId_ConsistentDeduplicationKey()
    {
        var id1 = PostgresDataService.NormalizePlayerId("John Doe", "global");
        var id2 = PostgresDataService.NormalizePlayerId("  john doe  ", "GLOBAL");
        var id3 = PostgresDataService.NormalizePlayerId("Jane Doe", "global");

        Assert.Equal("norm_john_doe_global", id1);
        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public async Task UpdatePlayerStats_AggregatesCumulativePointsAndCalculatesAccuracy()
    {
        string name = "Test Champion";
        string org = "org_alpha";

        // Day 1: 900 points, 1 correct
        await _dataService.UpdatePlayerStatsAsync(name, org, 900, 1, 3, "champ@alpha.com");

        var p1 = await _dataService.GetOrCreatePlayerAsync(name, org);
        Assert.Equal(900, p1.TotalPointsAllTime);
        Assert.Equal(1, p1.QuizzesPlayed);
        Assert.Equal(1, p1.CorrectAnswersCount);
        Assert.Equal(3, p1.HighestStreak);
        Assert.Equal(100.0, p1.AccuracyPercentage);

        // Day 2 / Round 2: 1200 points, 2 correct
        await _dataService.UpdatePlayerStatsAsync(name, org, 1200, 2, 5, "champ@alpha.com");

        var p2 = await _dataService.GetOrCreatePlayerAsync(name, org);
        Assert.Equal(2100, p2.TotalPointsAllTime);
        Assert.Equal(2, p2.QuizzesPlayed);
        Assert.Equal(3, p2.CorrectAnswersCount);
        Assert.Equal(5, p2.HighestStreak);
    }

    [Fact]
    public async Task GetPersistentLeaderboard_FiltersAndSortsCorrectly()
    {
        var leaderboard = await _dataService.GetPersistentLeaderboardAsync();
        Assert.NotEmpty(leaderboard);

        // Should be ordered by total points descending
        for (int i = 0; i < leaderboard.Count - 1; i++)
        {
            Assert.True(leaderboard[i].TotalPointsAllTime >= leaderboard[i + 1].TotalPointsAllTime);
        }
    }
}
