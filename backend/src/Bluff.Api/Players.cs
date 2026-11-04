using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bluff.Api;

public sealed class PlayerRecord
{
    public required string Id { get; init; }
    public required string TokenHash { get; init; }
    public required string Name { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
}

public sealed class BluffDb(DbContextOptions<BluffDb> options) : DbContext(options)
{
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var player = model.Entity<PlayerRecord>();
        player.HasKey(p => p.Id);
        player.HasIndex(p => p.TokenHash).IsUnique();
        player.Property(p => p.Name).HasMaxLength(PlayerName.MaxLength);
    }
}

public static class PlayerName
{
    public const int MaxLength = 16;

    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength ? null : trimmed;
    }
}

/// <summary>
/// Guest accounts: the app stores a random token, the server stores only its hash.
/// REST calls send it as a bearer token; WebSockets cannot set headers, so the hub
/// also accepts SignalR's standard access_token query parameter.
/// </summary>
public sealed class PlayerTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BluffDb db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PlayerToken";

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (token is null)
            return AuthenticateResult.NoResult();

        var hash = Hash(token);
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.TokenHash == hash);
        if (player is null)
            return AuthenticateResult.Fail("Geçersiz oyuncu anahtarı.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, player.Id)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private string? ReadToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        var queryToken = Request.Query["access_token"].ToString();
        return Request.Path.StartsWithSegments("/hubs") && queryToken.Length > 0 ? queryToken : null;
    }
}

public sealed record PlayerNameRequest(string? Name);
public sealed record NewPlayerResponse(string Id, string Name, string Token);
public sealed record PlayerResponse(string Id, string Name, int Wins, int Losses);

public static class PlayerEndpoints
{
    public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/players", async (PlayerNameRequest request, BluffDb db) =>
        {
            if (PlayerName.Normalize(request.Name) is not { } name)
                return InvalidName();

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var player = new PlayerRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                TokenHash = PlayerTokenHandler.Hash(token),
                Name = name,
            };
            db.Players.Add(player);
            await db.SaveChangesAsync();
            return Results.Ok(new NewPlayerResponse(player.Id, player.Name, token));
        }).RequireRateLimiting("signup");

        app.MapPut("/api/players/me", async (PlayerNameRequest request, ClaimsPrincipal user, BluffDb db) =>
        {
            if (PlayerName.Normalize(request.Name) is not { } name)
                return InvalidName();

            var player = await db.Players.FindAsync(user.FindFirstValue(ClaimTypes.NameIdentifier));
            if (player is null)
                return Results.Unauthorized();

            player.Name = name;
            await db.SaveChangesAsync();
            return Results.Ok(new PlayerResponse(player.Id, player.Name, player.Wins, player.Losses));
        }).RequireAuthorization();

        app.MapGet("/api/leaderboard", async (BluffDb db) =>
            await db.Players.AsNoTracking()
                .Where(p => p.Wins + p.Losses > 0)
                .OrderByDescending(p => p.Wins).ThenBy(p => p.Losses).ThenBy(p => p.Name)
                .Take(50)
                .Select(p => new PlayerResponse(p.Id, p.Name, p.Wins, p.Losses))
                .ToListAsync());
    }

    private static IResult InvalidName() =>
        Results.BadRequest(new { error = $"İsim 1-{PlayerName.MaxLength} karakter olmalı." });
}

public sealed class ScoreRecorder(IServiceScopeFactory scopes, ILogger<ScoreRecorder> logger)
{
    public async Task RecordAsync(GameOutcome outcome)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BluffDb>();
            await db.Players.Where(p => outcome.WinnerIds.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Wins, p => p.Wins + 1));
            await db.Players.Where(p => outcome.LoserIds.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Losses, p => p.Losses + 1));
        }
        catch (Exception ex)
        {
            // A failed score write must not take the live game down with it.
            logger.LogError(ex, "Oyun sonucu kaydedilemedi.");
        }
    }
}
