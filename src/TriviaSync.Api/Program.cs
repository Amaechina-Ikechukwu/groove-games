using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TriviaSync.Api.Data;
using TriviaSync.Api.Hubs;
using TriviaSync.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers & JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Groove API", Version = "v1" });
});

// CORS: allow any origin, method, header, and credentials for SignalR WebSockets
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Authentication & JWT
var signingKey = new JwtSigningKey(builder.Configuration["Jwt:Key"]);
var issuer = builder.Configuration["Jwt:Issuer"] ?? "Groove";
var audience = builder.Configuration["Jwt:Audience"] ?? "GrooveClients";
builder.Services.AddSingleton(signingKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey.Key,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    options.Events = new JwtBearerEvents
    {
        // Support token in SignalR query string
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        // The role inside a token is only a snapshot. Re-read it from the user store on every
        // request so promotions and demotions apply immediately and removed accounts lose access.
        OnTokenValidated = context =>
        {
            var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var email = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = email == null ? null : auth.FindUser(email);
            if (user == null || context.Principal?.Identity is not ClaimsIdentity identity)
            {
                context.Fail("Account no longer exists.");
                return Task.CompletedTask;
            }

            foreach (var claim in identity.FindAll(c => c.Type == identity.RoleClaimType || c.Type == "role").ToList())
            {
                identity.RemoveClaim(claim);
            }
            identity.AddClaim(new Claim(identity.RoleClaimType, user.Role));
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin, Roles.SuperAdmin));
    options.AddPolicy("HostOnly", policy => policy.RequireRole(Roles.Host, Roles.Admin, Roles.SuperAdmin));
});

// Throttle credential endpoints per client IP to slow down password guessing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"message\":\"Too many attempts. Wait a minute and try again.\"}", token);
    };
});

// PostgreSQL & EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
                       ?? "Host=localhost;Port=5432;Database=groove;Username=postgres;Password=postgres";

builder.Services.AddDbContext<TriviaDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Application Services
builder.Services.AddSingleton<IQuizParserEngine, QuizParserEngine>();
builder.Services.AddSingleton<ITriviaDataService, PostgresDataService>();
builder.Services.AddSingleton<IExportService, ExportService>();
builder.Services.AddSingleton<ILiveNotifier, LiveNotifier>();
builder.Services.AddSingleton<IUserStore, UserStore>();
builder.Services.AddSingleton<ITournamentService, TournamentService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());
builder.Services.AddSingleton<IGameEngineService, GameEngineService>();

var app = builder.Build();

SeedAccounts(app);

// Configure the HTTP request pipeline
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Groove API v1");
    });
}

// Health check endpoint for Docker & Dokploy
app.MapGet("/health", () => Results.Ok(new { status = "healthy", db = "postgresql", timestamp = DateTime.UtcNow }));

// Search engines: robots.txt and a sitemap of the public pages
app.MapGet("/robots.txt", (HttpContext ctx) => Results.Text(
    $"User-agent: *\nDisallow: /api/\nDisallow: /hubs/\nDisallow: /swagger/\n\nSitemap: {SiteUrl(ctx)}/sitemap.xml\n",
    "text/plain"));

app.MapGet("/sitemap.xml", (HttpContext ctx) =>
{
    var site = SiteUrl(ctx);
    var urls = string.Concat(new[] { "/", "/player.html", "/leaderboard.html" }
        .Select(p => $"  <url><loc>{site}{p}</loc></url>\n"));
    return Results.Text(
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n{urls}</urlset>\n",
        "application/xml");
});

// Serve static frontend files (player.html, host.html, admin.html, index.html)
app.UseDefaultFiles();

// HTML pages carry {{SITE_URL}} for canonical and social-share links, filled in per request
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value;
    if (HttpMethods.IsGet(ctx.Request.Method) && path != null && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
    {
        var file = app.Environment.WebRootFileProvider.GetFileInfo(path);
        if (file.Exists && !file.IsDirectory)
        {
            await WriteHtmlPage(ctx, file);
            return;
        }
    }
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuizHub>("/hubs/game");

app.MapFallback(ctx => WriteHtmlPage(ctx, app.Environment.WebRootFileProvider.GetFileInfo("index.html")));

app.Run();

// Public address of the site. Set Seo__SiteUrl in production (e.g. https://groove.example.com);
// otherwise it's taken from the request, honouring the proxy's X-Forwarded-Proto.
string SiteUrl(HttpContext ctx)
{
    var configured = app.Configuration["Seo:SiteUrl"];
    if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/');
    var scheme = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? ctx.Request.Scheme;
    return $"{scheme}://{ctx.Request.Host}";
}

async Task WriteHtmlPage(HttpContext ctx, Microsoft.Extensions.FileProviders.IFileInfo file)
{
    using var reader = new StreamReader(file.CreateReadStream());
    var html = (await reader.ReadToEndAsync()).Replace("{{SITE_URL}}", SiteUrl(ctx));
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.WriteAsync(html);
}

static void SeedAccounts(WebApplication app)
{
    var auth = app.Services.GetRequiredService<AuthService>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    if (app.Services.GetRequiredService<JwtSigningKey>().IsEphemeral)
    {
        logger.LogWarning("Jwt:Key is not set (or shorter than 32 bytes). Using a random signing key, so everyone is signed out when the app restarts. Set Jwt__Key to a long random secret.");
    }

    // Bootstrap admin from configuration (Admin__Email / Admin__Password).
    var adminEmail = app.Configuration["Admin:Email"];
    var adminPassword = app.Configuration["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword) &&
        auth.EnsureAccount(adminEmail, adminPassword, "Administrator", Roles.Admin))
    {
        logger.LogInformation("Created admin account {Email} from configuration.", adminEmail);
    }

    // Demo accounts exist only in Development so their well-known passwords never reach production.
    if (app.Environment.IsDevelopment())
    {
        auth.EnsureAccount("admin@groove.live", "admin123", "Demo Admin", Roles.Admin);
        auth.EnsureAccount("host@groove.live", "host1234", "Demo Host", Roles.Host);
        auth.EnsureAccount("player@groove.live", "player123", "Alex Rivera", Roles.Player);
    }

    if (!auth.GetAllUsers().Any(u => Roles.IsAdmin(u.Role)))
    {
        logger.LogWarning("No admin account exists. Set Admin__Email and Admin__Password and restart to create one.");
    }
}
