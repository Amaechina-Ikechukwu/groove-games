using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TriviaSync.Api.Data;

namespace TriviaSync.Api.Services;

public interface IUserStore
{
    UserEntity? Find(string email);
    IReadOnlyList<UserEntity> All();
    bool TryAdd(UserEntity user);
    void Update(UserEntity user);
}

/// <summary>
/// Users are few and read on every authenticated request, so they are cached in memory
/// and written through to PostgreSQL. Falls back to memory-only when the database is
/// unreachable, matching PostgresDataService.
/// </summary>
public class UserStore : IUserStore
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<UserStore>? _logger;
    private readonly bool _usePostgres;
    private readonly ConcurrentDictionary<string, UserEntity> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Memory-only store (tests, or no database configured).</summary>
    public UserStore()
    {
    }

    public UserStore(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<UserStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var connString = config.GetConnectionString("DefaultConnection")
                         ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connString))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            // EnsureCreated is a no-op on databases created before the users table existed,
            // so create it explicitly as well.
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS users (
                    email text PRIMARY KEY,
                    display_name text NOT NULL,
                    password_hash text NOT NULL,
                    role text NOT NULL,
                    created_at timestamp with time zone NOT NULL
                )");

            foreach (var user in db.Users.AsNoTracking())
            {
                _cache[user.Email] = user;
            }
            _usePostgres = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "User store could not reach PostgreSQL. Accounts will not survive a restart.");
        }
    }

    public UserEntity? Find(string email)
    {
        return _cache.TryGetValue(Normalize(email), out var user) ? user : null;
    }

    public IReadOnlyList<UserEntity> All() => _cache.Values.OrderBy(u => u.CreatedAt).ToList();

    public bool TryAdd(UserEntity user)
    {
        user.Email = Normalize(user.Email);
        if (!_cache.TryAdd(user.Email, user))
        {
            return false;
        }

        if (_usePostgres)
        {
            try
            {
                using var scope = _scopeFactory!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                db.Users.Add(Clone(user));
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                _cache.TryRemove(user.Email, out _);
                _logger?.LogError(ex, "Failed to persist new user {Email}", user.Email);
                throw;
            }
        }
        return true;
    }

    public void Update(UserEntity user)
    {
        user.Email = Normalize(user.Email);
        if (_usePostgres)
        {
            using var scope = _scopeFactory!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            db.Users.Update(Clone(user));
            db.SaveChanges();
        }
        _cache[user.Email] = user;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static UserEntity Clone(UserEntity u) => new()
    {
        Email = u.Email,
        DisplayName = u.DisplayName,
        PasswordHash = u.PasswordHash,
        Role = u.Role,
        CreatedAt = u.CreatedAt
    };
}
