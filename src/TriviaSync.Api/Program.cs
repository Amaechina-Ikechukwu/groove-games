using System.Text;
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
    options.EnableDetailedErrors = true;
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
var secret = builder.Configuration["Jwt:Key"] ?? "GrooveSuperSecretSigningKeyForDevelopmentAndTesting2026!";
var issuer = builder.Configuration["Jwt:Issuer"] ?? "Groove";
var audience = builder.Configuration["Jwt:Audience"] ?? "GrooveClients";

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    };

    // Support token in SignalR query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin", "SuperAdmin"));
    options.AddPolicy("HostOnly", policy => policy.RequireRole("Host", "Admin", "SuperAdmin"));
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
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IGameEngineService, GameEngineService>();

var app = builder.Build();

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

// Serve static frontend files (player.html, host.html, admin.html, index.html)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuizHub>("/hubs/game");

app.MapFallbackToFile("index.html");

app.Run();
