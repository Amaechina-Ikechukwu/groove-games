using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TriviaSync.Api.Data;
using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public static class Roles
{
    public const string Player = "Player";
    public const string Host = "Host";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    public static readonly string[] Assignable = { Player, Host, Admin };
    public static readonly string[] SelfRegistrable = { Player, Host };

    public static bool IsAdmin(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, SuperAdmin, StringComparison.OrdinalIgnoreCase);

    public static bool CanHost(string? role) =>
        IsAdmin(role) || string.Equals(role, Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the canonical casing of a known role, or null.</summary>
    public static string? Canonical(string? role) =>
        Assignable.Concat(new[] { SuperAdmin }).FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public enum RoleChangeResult
{
    Ok,
    NotFound,
    InvalidRole,
    WouldRemoveLastAdmin
}

public interface IAuthService
{
    AuthResponse Login(string email, string password);
    AuthResponse Register(RegisterRequest req);
    AuthResponse BecomeHost(string email);
    UserEntity? FindUser(string email);
    List<UserDto> GetAllUsers();
    RoleChangeResult AssignRole(string email, string role);
}

public class AuthService : IAuthService
{
    public const int MinPasswordLength = 8;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    private readonly IUserStore _users;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly object _roleLock = new();
    private readonly ILiveNotifier? _live;

    public AuthService(IConfiguration config, IUserStore users, JwtSigningKey signingKey, ILiveNotifier? live = null)
    {
        _live = live;
        _users = users;
        _signingKey = signingKey.Key;
        _issuer = config["Jwt:Issuer"] ?? "Groove";
        _audience = config["Jwt:Audience"] ?? "GrooveClients";
    }

    public AuthResponse Register(RegisterRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var displayName = (req.FullName ?? string.Empty).Trim();
        var password = req.Password ?? string.Empty;

        if (!IsValidEmail(email))
            throw new ArgumentException("Enter a valid email address.");
        if (displayName.Length is < 2 or > 60)
            throw new ArgumentException("Name must be between 2 and 60 characters.");
        if (password.Length < MinPasswordLength)
            throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.");

        var role = Roles.Canonical(string.IsNullOrWhiteSpace(req.Role) ? Roles.Player : req.Role);
        if (role == null || !Roles.SelfRegistrable.Contains(role))
            throw new ArgumentException("You can only sign up as a player or a host.");

        var user = new UserEntity
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        if (!_users.TryAdd(user))
            throw new InvalidOperationException("An account with this email already exists. Sign in instead.");

        _live?.Publish(LiveTopics.Admin, "people");
        return BuildResponse(user);
    }

    public AuthResponse Login(string email, string password)
    {
        var user = _users.Find(email ?? string.Empty);
        // Same message whether the email or the password is wrong, so accounts can't be enumerated.
        if (user == null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("Incorrect email or password.");

        return BuildResponse(user);
    }

    public AuthResponse BecomeHost(string email)
    {
        lock (_roleLock)
        {
            var user = _users.Find(email) ?? throw new UnauthorizedAccessException("Account not found.");
            if (Roles.CanHost(user.Role))
                return BuildResponse(user);

            var updated = Copy(user);
            updated.Role = Roles.Host;
            _users.Update(updated);
            _live?.Publish(LiveTopics.Admin, "people");
            return BuildResponse(updated);
        }
    }

    public UserEntity? FindUser(string email) => _users.Find(email);

    public List<UserDto> GetAllUsers()
    {
        return _users.All().Select(u => new UserDto
        {
            Uid = UidFor(u.Email),
            Email = u.Email,
            DisplayName = u.DisplayName,
            Role = u.Role,
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    public RoleChangeResult AssignRole(string email, string role)
    {
        var canonical = Roles.Canonical(role);
        if (canonical == null || !Roles.Assignable.Contains(canonical))
            return RoleChangeResult.InvalidRole;

        lock (_roleLock)
        {
            var user = _users.Find(email ?? string.Empty);
            if (user == null)
                return RoleChangeResult.NotFound;

            if (Roles.IsAdmin(user.Role) && !Roles.IsAdmin(canonical) &&
                _users.All().Count(u => Roles.IsAdmin(u.Role)) <= 1)
                return RoleChangeResult.WouldRemoveLastAdmin;

            var updated = Copy(user);
            updated.Role = canonical;
            _users.Update(updated);
            _live?.Publish(LiveTopics.Admin, "people");
            return RoleChangeResult.Ok;
        }
    }

    /// <summary>Creates an account if it doesn't exist yet. Used for configured/seeded accounts only.</summary>
    public bool EnsureAccount(string email, string password, string displayName, string role)
    {
        if (_users.Find(email) != null)
            return false;

        return _users.TryAdd(new UserEntity
        {
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            CreatedAt = DateTime.UtcNow
        });
    }

    private AuthResponse BuildResponse(UserEntity user) => new()
    {
        Token = GenerateToken(user),
        Uid = UidFor(user.Email),
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role
    };

    private string GenerateToken(UserEntity user)
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Email),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.Add(TokenLifetime),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static string UidFor(string email) => $"usr_{email.Replace("@", "_").Replace(".", "_")}";

    private static UserEntity Copy(UserEntity u) => new()
    {
        Email = u.Email,
        DisplayName = u.DisplayName,
        PasswordHash = u.PasswordHash,
        Role = u.Role,
        CreatedAt = u.CreatedAt
    };

    private static bool IsValidEmail(string email)
    {
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed))
            return false;
        return parsed.Address == email && parsed.Host.Contains('.');
    }
}

/// <summary>
/// The JWT signing key. Uses Jwt:Key when configured; otherwise generates a random key per
/// process so a well-known default can never be used to forge tokens.
/// </summary>
public sealed class JwtSigningKey
{
    public SymmetricSecurityKey Key { get; }
    public bool IsEphemeral { get; }

    public JwtSigningKey(string? configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey) && Encoding.UTF8.GetByteCount(configuredKey) >= 32)
        {
            Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredKey));
        }
        else
        {
            Key = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            IsEphemeral = true;
        }
    }
}
