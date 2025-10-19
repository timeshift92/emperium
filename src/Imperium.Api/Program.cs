using Microsoft.EntityFrameworkCore;
using Imperium.Llm;

var builder = WebApplication.CreateBuilder(args);

// Удобный дефолтный URL для локальной разработки
builder.WebHost.UseUrls("http://localhost:5000");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Настройка EF Core (SQLite)
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "imperium.db");
builder.Services.AddDbContext<Imperium.Infrastructure.ImperiumDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// Регистрация агентов и воркера
// Регистрируем IWorldAgent реализации (TimeAgent, WeatherAgent, SeasonAgent)
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.TimeAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.WeatherAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.SeasonAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.EconomyAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();
builder.Services.AddHostedService<Imperium.Api.TickWorker>();
// Metrics
builder.Services.AddSingleton<Imperium.Api.MetricsService>();
// Event stream for SSE
builder.Services.AddSingleton<Imperium.Api.EventStreamService>();

// LLM registration: prefer explicit Llm section (supports local ollama/mistral/llama3).
// Fallback rules:
// - if Llm:Provider == "ollama" -> use OllamaLlmClient (local HTTP server, default port 11434)
// - if Llm:Provider == "OpenAI" -> use OpenAiLlmClient only when API key is configured
// - otherwise, register the local Ollama client by default
// If OpenAI is requested but no key is present, fall back to MockLlmClient for safe local dev.
var apiKey = builder.Configuration["OpenAI:ApiKey"] ?? builder.Configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var llmOptions = builder.Configuration.GetSection("Llm").Get<Imperium.Llm.LlmOptions>() ?? new Imperium.Llm.LlmOptions();

if (llmOptions.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(apiKey))
{
    // OpenAI requested but no key — use mock to avoid startup exceptions in dev
    builder.Services.AddSingleton<Imperium.Llm.ILlmClient, Imperium.Llm.MockLlmClient>();
    builder.Services.AddSingleton<IStartupFilter>(sp => new Imperium.Api.StartupLog("MockLlmClient (no OpenAI key)"));
}
else
{
    // Register via AddLlm which wires Ollama or OpenAI depending on configuration
    builder.Services.AddLlm(builder.Configuration);
    builder.Services.AddSingleton<IStartupFilter>(sp => new Imperium.Api.StartupLog($"LLM provider: {llmOptions.Provider} model: {llmOptions.Model}"));
}

var app = builder.Build();

// Ensure database created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Imperium.Infrastructure.ImperiumDbContext>();
    db.Database.EnsureCreated();

    // Seed simple test characters for development so NpcAgent has targets
    try
    {
        if (!db.Characters.Any())
        {
            db.Characters.AddRange(
                new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Иван", Age = 33, Status = "idle" },
                new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Мария", Age = 28, Status = "idle" },
                new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Тит", Age = 41, Status = "idle" }
            );
            db.SaveChanges();
        }
    }
    catch
    {
        // best-effort seeding — ignore failures in production-like environments
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Простой endpoint для получения последнего погодного снимка (генерируется через LLM-mock)
app.MapGet("/api/weather/latest", async (Imperium.Llm.ILlmClient llm, CancellationToken ct) =>
{
    var prompt = "Generate compact JSON weather snapshot: {condition, temperatureC, windKph, precipitationMm}";
    var raw = await llm.SendPromptAsync(prompt, ct);
    if (Imperium.Llm.WeatherValidator.TryParse(raw, out var dto, out var error))
    {
        return Results.Json(dto);
    }
    return Results.Problem(detail: $"LLM returned invalid JSON: {error}", statusCode: 502);
}).WithName("GetLatestWeather");

// Dev endpoint: recent GameEvents (read-only, useful for smoke tests)
app.MapGet("/api/events/recent/{count:int?}", async (int? count, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var take = Math.Clamp(count ?? 20, 1, 200);
    var events = await db.GameEvents.OrderByDescending(e => e.Timestamp).Take(take).ToListAsync();
    return Results.Json(events);
}).WithName("GetRecentEvents");

// Events endpoint with simple filters: type, since (ISO), count
app.MapGet("/api/events", async (string? type, DateTimeOffset? since, int? count, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    IQueryable<Imperium.Domain.Models.GameEvent> q = db.GameEvents;
    if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.Type == type);
    if (since.HasValue) q = q.Where(e => e.Timestamp >= since.Value.UtcDateTime);
    var take = Math.Clamp(count ?? 100, 1, 500);
    var items = await q.OrderByDescending(e => e.Timestamp).Take(take).ToListAsync();
    return Results.Json(items);
}).WithName("GetEvents");

// Simple metrics endpoint
app.MapGet("/api/metrics", (Imperium.Api.MetricsService metrics) =>
{
    return Results.Json(metrics.Snapshot());
}).WithName("GetMetrics");

// Dev: seed characters on demand (POST)
app.MapPost("/api/dev/seed-characters", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    db.Characters.AddRange(
        new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Иван (dev)", Age = 33, Status = "idle" },
        new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Мария (dev)", Age = 28, Status = "idle" },
        new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Тит (dev)", Age = 41, Status = "idle" }
    );
    await db.SaveChangesAsync();
    return Results.Ok(new { seeded = 3 });
});

// Dev: trigger one immediate tick cycle (useful for testing)
app.MapPost("/api/dev/tick-now", async (IServiceProvider sp) =>
{
    using var scope = sp.CreateScope();
    var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>()
        .OrderBy(a => a.Name == "TimeAI" ? 0 : 1).ToList();
    foreach (var a in agents)
    {
        await a.TickAsync(scope.ServiceProvider, CancellationToken.None);
    }
    return Results.Ok(new { ticks = agents.Count });
});

// Server-Sent Events: GameEvent stream
app.MapGet("/api/events/stream", async (Imperium.Api.EventStreamService stream, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    var reader = stream.Events;
    await foreach (var e in reader.ReadAllAsync(ctx.RequestAborted))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
}).WithName("EventStream");

// Server-Sent Events: WeatherSnapshot stream
app.MapGet("/api/weather/stream", async (Imperium.Api.EventStreamService stream, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    var reader = stream.Weathers;
    await foreach (var s in reader.ReadAllAsync(ctx.RequestAborted))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
}).WithName("WeatherStream");

// Return last saved weather snapshot from DB
app.MapGet("/api/weather/latest/db", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var snap = await db.WeatherSnapshots.OrderByDescending(s => s.Timestamp).FirstOrDefaultAsync();
    if (snap == null) return Results.NotFound();
    return Results.Json(snap);
}).WithName("GetLatestWeatherDb");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
