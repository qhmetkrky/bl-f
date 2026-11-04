using System.Threading.RateLimiting;
using Bluff.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BluffDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Bluff")));

builder.Services
    .AddAuthentication(PlayerTokenHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PlayerTokenHandler>(PlayerTokenHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddSignalR(o => o.AddFilter<GameRuleFilter>());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ScoreRecorder>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddHostedService<GameLoopService>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("signup", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

// Native apps ignore CORS; this only matters for browser builds.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod();
    if (builder.Environment.IsDevelopment())
        policy.AllowAnyOrigin();
    else
        policy.WithOrigins(allowedOrigins);
}));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<BluffDb>().Database.EnsureCreatedAsync();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPlayerEndpoints();
app.MapHub<GameHub>("/hubs/game");

await app.RunAsync();

public partial class Program;
