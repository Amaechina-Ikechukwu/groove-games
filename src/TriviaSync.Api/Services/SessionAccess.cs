using System.Security.Claims;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public static class SessionAccess
{
    public static string? UserEmail(this ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static bool IsAdmin(this ClaimsPrincipal? user) =>
        user != null && (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin));

    public static bool CanHost(this ClaimsPrincipal? user) =>
        user.IsAdmin() || (user?.IsInRole(Roles.Host) ?? false);

    /// <summary>Admins can run any session; hosts only the sessions they created.</summary>
    public static bool CanControl(this ClaimsPrincipal? user, GameSession session)
    {
        if (user.IsAdmin())
            return true;

        var email = user.UserEmail();
        return email != null && user.CanHost() &&
               string.Equals(session.HostId, email, StringComparison.OrdinalIgnoreCase);
    }
}
