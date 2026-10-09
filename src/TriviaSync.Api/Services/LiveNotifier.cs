using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Hubs;

namespace TriviaSync.Api.Services;

/// <summary>
/// SignalR group names for live updates. A message only says "this kind of thing changed"; clients
/// then re-fetch through the normal, permission-checked API, so no data travels in the signal.
/// </summary>
public static class LiveTopics
{
    /// <summary>Everyone: the tournament list and the quick-game leaderboard.</summary>
    public const string Public = "live:public";

    public const string Admin = "live:admin";

    /// <summary>Members and managers of a tournament: its sessions and player list.</summary>
    public static string Tournament(string id) => $"live:t:{id}";

    /// <summary>Anyone: a tournament's public standings.</summary>
    public static string TournamentPublic(string id) => $"live:t:{id}:pub";

    /// <summary>The tournament's host and admins: attempt counts and results.</summary>
    public static string TournamentManagers(string id) => $"live:t:{id}:mgr";

    /// <summary>One signed-in user: their own tournaments, history and (for hosts) running games.</summary>
    public static string User(string email) => $"live:u:{email.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Turns a topic a client asks for into a SignalR group, or null if this person may not listen to it.
    /// Topics: public, user, admin, tournament:{id} (members and managers), tournament:{id}:pub (anyone),
    /// tournament:{id}:mgr (the host and admins).
    /// </summary>
    public static string? Resolve(string? topic, ClaimsPrincipal? user, ITournamentService tournaments)
    {
        var t = (topic ?? string.Empty).Trim();
        var email = user.UserEmail();

        switch (t)
        {
            case "public": return Public;
            case "user": return email == null ? null : User(email);
            case "admin": return user.IsAdmin() ? Admin : null;
        }

        if (!t.StartsWith("tournament:"))
        {
            return null;
        }

        var parts = t.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }
        var tournament = tournaments.Get(parts[1]);
        if (tournament == null)
        {
            return null;
        }

        var canManage = email != null &&
            (user.IsAdmin() ||
             (user.CanHost() && string.Equals(tournament.HostEmail, email, StringComparison.OrdinalIgnoreCase)));

        return (parts.Length == 3 ? parts[2] : "") switch
        {
            "pub" => TournamentPublic(tournament.Id),
            "mgr" => canManage ? TournamentManagers(tournament.Id) : null,
            "" => canManage || (email != null && tournaments.IsMember(tournament.Id, email))
                ? Tournament(tournament.Id)
                : null,
            _ => null
        };
    }
}

public interface ILiveNotifier
{
    /// <summary>Tells everyone in <paramref name="group"/> that <paramref name="kind"/> changed.</summary>
    void Publish(string group, string kind);
}

public class LiveNotifier : ILiveNotifier
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(400);

    private readonly IHubContext<QuizHub, IQuizClient> _hub;
    private readonly ILogger<LiveNotifier> _logger;
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public LiveNotifier(IHubContext<QuizHub, IQuizClient> hub, ILogger<LiveNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Changes in quick succession (twenty players answering at once) are collapsed into one message per
    /// group and kind. The message goes out after a short pause, so the client's re-fetch sees all of them.
    /// </summary>
    public void Publish(string group, string kind)
    {
        var key = $"{group}|{kind}";
        if (!_pending.TryAdd(key, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(Window);
            _pending.TryRemove(key, out _);
            try
            {
                await _hub.Clients.Group(group).Changed(group, kind);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Live update to {Group} failed", group);
            }
        });
    }
}
