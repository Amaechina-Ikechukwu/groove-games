namespace TriviaSync.Api.Models;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Player"; // "Player" or "Host"
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Host"; // "SuperAdmin", "Admin", "Host", "Player"
}

public class RoleAssignRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Host";
}

public class UserDto
{
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Host";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
