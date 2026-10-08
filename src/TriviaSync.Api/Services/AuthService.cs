using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public interface IAuthService
{
    AuthResponse Login(string email, string password, string? portal = null, string? requestedRole = null);
    AuthResponse Register(RegisterRequest req);
    List<UserDto> GetAllUsers();
    bool AssignRole(string email, string role);
}

public class AuthService : IAuthService
{
    private readonly IConfiguration _config;
    private static readonly Dictionary<string, (string PasswordHash, string Role, string DisplayName)> _users = new(StringComparer.OrdinalIgnoreCase)
    {
        { "admin@groove.live", ("admin123", "Admin", "Super Admin") },
        { "host@groove.live", ("host123", "Host", "Quiz Facilitator") },
        { "demo@groove.live", ("demo123", "Host", "Demo Host") },
        { "player@groove.live", ("player123", "Player", "Alex Rivera") },
        { "contender@groove.live", ("contender123", "Player", "Sarah Connor") },
        { "admin@triviasync.com", ("admin123", "Admin", "Super Admin") },
        { "host@triviasync.com", ("host123", "Host", "Quiz Facilitator") }
    };

    public AuthService(IConfiguration config)
    {
        _config = config;
    }

    public AuthResponse Register(RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        {
            throw new ArgumentException("Email and password are required.");
        }

        var email = req.Email.Trim().ToLowerInvariant();
        var role = req.Role?.Trim();
        if (string.IsNullOrWhiteSpace(role) || role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            role = "Player"; // Admin roles cannot be self-registered
        }

        var displayName = string.IsNullOrWhiteSpace(req.FullName) ? email.Split('@')[0] : req.FullName.Trim();

        if (_users.TryGetValue(email, out var existing))
        {
            // If the user already existed with Player role and is now registering as Host with matching password,
            // upgrade their role to Host!
            if (role.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                existing.Role.Equals("Player", StringComparison.OrdinalIgnoreCase) &&
                existing.PasswordHash == req.Password)
            {
                _users[email] = (req.Password, role, displayName);
                var upToken = GenerateToken(email, role, displayName);
                return new AuthResponse
                {
                    Token = upToken,
                    Uid = $"usr_{email.Replace("@", "_").Replace(".", "_")}",
                    Email = email,
                    DisplayName = displayName,
                    Role = role
                };
            }

            throw new InvalidOperationException("An account with this email already exists.");
        }

        _users[email] = (req.Password, role, displayName);

        var token = GenerateToken(email, role, displayName);
        return new AuthResponse
        {
            Token = token,
            Uid = $"usr_{email.Replace("@", "_").Replace(".", "_")}",
            Email = email,
            DisplayName = displayName,
            Role = role
        };
    }

    public AuthResponse Login(string email, string password, string? portal = null, string? requestedRole = null)
    {
        email = email.Trim().ToLowerInvariant();

        // Determine effective requested role if specified or inferred from portal
        var effectiveRole = !string.IsNullOrWhiteSpace(requestedRole) ? requestedRole.Trim() :
                            string.Equals(portal, "Host", StringComparison.OrdinalIgnoreCase) ? "Host" :
                            string.Equals(portal, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" :
                            string.Equals(portal, "Player", StringComparison.OrdinalIgnoreCase) ? "Player" : null;

        if (_users.TryGetValue(email, out var user))
        {
            if (user.PasswordHash == password)
            {
                var role = user.Role;
                // If logging in via Host portal or requesting Host role, upgrade Player to Host
                if (string.Equals(effectiveRole, "Host", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(role, "Player", StringComparison.OrdinalIgnoreCase))
                {
                    role = "Host";
                    _users[email] = (user.PasswordHash, role, user.DisplayName);
                }

                var token = GenerateToken(email, role, user.DisplayName);
                return new AuthResponse
                {
                    Token = token,
                    Uid = $"usr_{email.Replace("@", "_").Replace(".", "_")}",
                    Email = email,
                    DisplayName = user.DisplayName,
                    Role = role
                };
            }
            throw new UnauthorizedAccessException("Invalid password.");
        }

        // Fast sign-in for new contenders / facilitators who haven't signed in before
        if (!string.IsNullOrWhiteSpace(email) && password.Length >= 4)
        {
            string role;
            if (!string.IsNullOrWhiteSpace(effectiveRole))
            {
                role = effectiveRole;
            }
            else if (email.Contains("admin", StringComparison.OrdinalIgnoreCase))
            {
                role = "Admin";
            }
            else if (email.Contains("host", StringComparison.OrdinalIgnoreCase))
            {
                role = "Host";
            }
            else
            {
                role = "Player";
            }

            var displayName = email.Split('@')[0];
            _users[email] = (password, role, displayName);
            var token = GenerateToken(email, role, displayName);
            return new AuthResponse
            {
                Token = token,
                Uid = $"usr_{email.Replace("@", "_").Replace(".", "_")}",
                Email = email,
                DisplayName = displayName,
                Role = role
            };
        }

        throw new UnauthorizedAccessException("Invalid credentials.");
    }

    public List<UserDto> GetAllUsers()
    {
        return _users.Select(kvp => new UserDto
        {
            Uid = $"usr_{kvp.Key.Replace("@", "_").Replace(".", "_")}",
            Email = kvp.Key,
            DisplayName = kvp.Value.DisplayName,
            Role = kvp.Value.Role,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        }).ToList();
    }

    public bool AssignRole(string email, string role)
    {
        if (_users.TryGetValue(email, out var existing))
        {
            _users[email] = (existing.PasswordHash, role, existing.DisplayName);
            return true;
        }
        return false;
    }

    private string GenerateToken(string email, string role, string displayName)
    {
        var secret = _config["Jwt:Key"] ?? "GrooveSuperSecretSigningKeyForDevelopmentAndTesting2026!";
        var issuer = _config["Jwt:Issuer"] ?? "Groove";
        var audience = _config["Jwt:Audience"] ?? "GrooveClients";

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secret);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, email),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),
                new Claim("user_id", email)
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
