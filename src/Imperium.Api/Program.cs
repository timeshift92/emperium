using System.Collections.Generic;
using Imperium.Api;
using Imperium.Api.Extensions;
using Imperium.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Imperium.Llm;
using Imperium.Api.Models;
using System.Linq;
using System.Text.Json;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Configure logging: include scopes so our TraceId from LLM router appears in logs and can be correlated with EF Core logs
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

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

builder.Services.AddSignalR();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Imperium.Api"))
    .WithTracing(tracing =>
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Imperium.Llm", "Imperium.TickWorker"))
    .WithMetrics(metrics =>
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Imperium.Api.Metrics")
            .AddPrometheusExporter());

// Регистрация агентов и воркера
// Регистрируем IWorldAgent реализации (TimeAgent, WeatherAgent, SeasonAgent)
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.TimeAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.WeatherAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.SeasonAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.ProductionAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.EconomyAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.ConsumptionAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.WagesAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.LogisticsAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcBehaviorAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.RelationshipAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.ConflictAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.LegalAgent>();
builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.OwnershipAgent>();
// Hosted services (TickWorker, EventDispatcher) can be disabled via configuration or env var
// Useful for integration tests that control ticks manually to avoid SQLite in-memory race conditions.
var disableTickWorker = builder.Configuration.GetValue<bool?>("DisableTickWorker") ?? (Environment.GetEnvironmentVariable("DISABLE_TICKWORKER") == "1");
if (!disableTickWorker)
{
    builder.Services.AddHostedService<Imperium.Api.TickWorker>();
}
// Metrics
builder.Services.AddSingleton<Imperium.Api.MetricsService>();
// Event stream for SSE
builder.Services.AddSingleton<Imperium.Api.EventStreamService>();
// NPC reply queue and background worker
builder.Services.AddSingleton<Imperium.Api.Services.INpcReplyQueue, Imperium.Api.Services.NpcReplyQueueService>();
builder.Services.AddSingleton<Imperium.Api.Services.NpcReplyQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Imperium.Api.Services.NpcReplyQueueService>());
// Names generator
builder.Services.AddSingleton<Imperium.Api.Services.NamesService>();
// Economy options
builder.Services.Configure<Imperium.Api.EconomyOptions>(builder.Configuration.GetSection("Economy"));
// Economy state (dynamic items & price shocks)
var seedItems = builder.Configuration.GetSection("Economy:Items").Exists()
    ? builder.Configuration.GetSection("Economy:Items").Get<string[]>()
    : null;
builder.Services.AddSingleton(new Imperium.Api.EconomyStateService(seedItems));
builder.Services.Configure<Imperium.Api.LogisticsOptions>(builder.Configuration.GetSection("Logistics"));
builder.Services.AddSingleton<LogisticsQueueService>();
// Currency options (DecimalPlaces)
if (builder.Configuration.GetSection("Currency").Exists())
{
    builder.Services.Configure<Imperium.Api.CurrencyOptions>(builder.Configuration.GetSection("Currency"));
}
else
{
    // fallback to Economy:CurrencyDecimalPlaces or default
    var fallback = new Imperium.Api.CurrencyOptions();
    var dp = builder.Configuration.GetValue<int?>("Economy:CurrencyDecimalPlaces");
    if (dp.HasValue) fallback.DecimalPlaces = dp.Value;
    builder.Services.Configure<Imperium.Api.CurrencyOptions>(opts => {
        opts.DecimalPlaces = fallback.DecimalPlaces;
    });
}

// Inheritance options (tie-breaker policy)
if (builder.Configuration.GetSection("Inheritance").Exists())
{
    builder.Services.Configure<Imperium.Api.InheritanceOptions>(builder.Configuration.GetSection("Inheritance"));
}
else
{
    builder.Services.Configure<Imperium.Api.InheritanceOptions>(opts => { opts.TieBreaker = Imperium.Api.TieBreakerOption.DeterministicHash; opts.Salt = ""; });
}

// Event dispatcher: background persistence & publishing
builder.Services.AddSingleton<Imperium.Api.EventDispatcherService>();
builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<Imperium.Api.EventDispatcherService>());
var disableEventDispatcher = builder.Configuration.GetValue<bool?>("DisableEventDispatcher") ?? (Environment.GetEnvironmentVariable("DISABLE_EVENT_DISPATCHER") == "1");
if (!disableEventDispatcher)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<Imperium.Api.EventDispatcherService>());
}

// Inheritance service
builder.Services.AddScoped<Imperium.Api.Services.InheritanceService>();

// Random provider (seedable) for deterministic tests
builder.Services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();

// Ensure safe early exception middleware is registered as the first middleware
builder.Services.AddSingleton<IStartupFilter, Imperium.Api.EarlyExceptionStartupFilter>();

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
    builder.Services.AddSingleton<Imperium.Llm.MockLlmClient>();
    builder.Services.AddSingleton<Imperium.Llm.IFallbackLlmProvider, Imperium.Llm.MockFallbackProvider>();
    builder.Services.AddSingleton<IStartupFilter>(sp => new Imperium.Api.StartupLog("MockLlmClient (no OpenAI key)"));

    builder.Services.AddSingleton<Imperium.Llm.ILlmClient>(sp =>
        new Imperium.Api.Services.LlmMetricsDecorator(
            sp.GetRequiredService<Imperium.Llm.MockLlmClient>(),
            sp.GetRequiredService<Imperium.Api.MetricsService>(),
            sp.GetService<ILogger<Imperium.Api.Services.LlmMetricsDecorator>>()));
}
else
{
    // Register via AddLlm which wires Ollama or OpenAI depending on configuration
    builder.Services.AddLlm(builder.Configuration);
    // Options: Npc reaction tuning
    builder.Services.Configure<Imperium.Api.NpcReactionOptions>(builder.Configuration.GetSection("NpcReactions"));
    builder.Services.Configure<Imperium.Api.RelationshipModifierOptions>(builder.Configuration.GetSection("RelationshipModifiers"));
    // Ensure MockLlmClient is available as a fallback even when primary provider is configured
    builder.Services.TryAddSingleton<Imperium.Llm.MockLlmClient>();
    builder.Services.TryAddSingleton<Imperium.Llm.IFallbackLlmProvider, Imperium.Llm.MockFallbackProvider>();
    builder.Services.AddSingleton<IStartupFilter>(sp => new Imperium.Api.StartupLog($"LLM provider: {llmOptions.Provider} model: {llmOptions.Model}"));

    builder.Services.AddTransient<Imperium.Llm.ILlmClient>(sp =>
        new Imperium.Api.Services.LlmMetricsDecorator(
            sp.GetRequiredService<Imperium.Llm.RoleLlmRouter>(),
            sp.GetRequiredService<Imperium.Api.MetricsService>(),
            sp.GetService<ILogger<Imperium.Api.Services.LlmMetricsDecorator>>()));
}

var app = builder.Build();

app.MapPrometheusScrapingEndpoint();
app.MapHub<Imperium.Api.Hubs.EventsHub>("/hubs/events");

// Ensure database created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Imperium.Infrastructure.ImperiumDbContext>();
    // Используем миграции EF Core вместо runtime ALTER TABLE. Для локальной разработки
    // выполните `dotnet ef migrations add InitialCreate --project src/Imperium.Infrastructure --startup-project src/Imperium.Api`
    // и затем `dotnet ef database update --project src/Imperium.Infrastructure --startup-project src/Imperium.Api`.
    try
    {
        db.Database.Migrate();
    }
    catch (InvalidOperationException ex) when (ex.Message?.Contains("PendingModelChangesWarning") == true)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program");
        logger?.LogWarning(ex, "EF Core reports pending model changes. Falling back to EnsureCreated() for development convenience.");
        db.Database.EnsureCreated();
    }
    catch (Exception) // defensive fallback for environments where migrations can't be applied at startup
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program");
        logger?.LogWarning("EF Core migrations could not be applied at startup. Falling back to EnsureCreated() for development convenience.");
        db.Database.EnsureCreated();
    }

    // Defensive: ensure InheritanceRecords table exists even when EnsureCreated path was used previously
    try
    {
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS InheritanceRecords (
            Id TEXT NOT NULL CONSTRAINT PK_InheritanceRecords PRIMARY KEY,
            DeceasedId TEXT NOT NULL,
            HeirsJson TEXT NOT NULL,
            RulesJson TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            ResolutionJson TEXT NULL
        );");
    }
    catch { /* ignore */ }

    // Defensive: ensure Locations table has Biome and Culture columns if DB was created before these fields were added
    try
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Locations');";
            using var reader = cmd.ExecuteReader();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
                columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("Biome"))
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Locations ADD COLUMN Biome TEXT DEFAULT 'unknown';");
            }
            if (!columns.Contains("Culture"))
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Locations ADD COLUMN Culture TEXT DEFAULT 'unknown';");
            }
        }
    }
    catch { /* ignore - best effort for dev convenience */ }

    // Seed simple test characters for development so NpcAgent has targets
    try
    {
        if (!db.Characters.Any())
        {
            db.Characters.AddRange(
                new Imperium.Domain.Models.Character {
                    Id = Guid.NewGuid(), Name = "Иван", Age = 33, Status = "idle",
                    LocationName = "Агрокопт", EssenceJson = "{\"strength\":7,\"intelligence\":4,\"talents\":[\"плуг\",\"сбор\"]}",
                    History = "Родился в деревне, пас скот и помогал на полях"
                },
                new Imperium.Domain.Models.Character {
                    Id = Guid.NewGuid(), Name = "Мария", Age = 28, Status = "idle",
                    LocationName = "Прибрежный рынок", EssenceJson = "{\"charisma\":8,\"intelligence\":6,\"talents\":[\"торговля\",\"переговоры\"]}",
                    History = "Помогает семье вести лавку, умеет торговаться"
                },
                new Imperium.Domain.Models.Character {
                    Id = Guid.NewGuid(), Name = "Тит", Age = 41, Status = "idle",
                    LocationName = "Казармы", EssenceJson = "{\"strength\":8,\"endurance\":7,\"talents\":[\"постр\",\"меч\"]}",
                    History = "Бывший солдат, теперь сторожит ворота города"
                }
            );
            db.SaveChanges();
        }
    }
    catch
    {
        // best-effort seeding — ignore failures in production-like environments
    }

    // World genesis: initialize full world on fresh DB
    try
    {
    await WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
        try
    {
        await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program.NatureGenesis");
        logger?.LogError(ex, "Nature genesis failed: {Message}", ex.Message);
    }
    try
    {
        await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program.TribesGenesis");
        logger?.LogError(ex, "Tribes genesis failed: {Message}", ex.Message);
    }
    try
    {
        await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program.CivilizationGenesis");
        logger?.LogError(ex, "Civilization genesis failed: {Message}", ex.Message);
    }
    try
    {
        var llm = scope.ServiceProvider.GetService<Imperium.Llm.ILlmClient>();
        await Imperium.Infrastructure.Setup.EmpireGenesisService.InitializeAsync(db, llm);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program.EmpireGenesis");
        logger?.LogError(ex, "Empire genesis failed: {Message}", ex.Message);
    }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Program.WorldGenesis");
        logger?.LogError(ex, "World genesis failed: {Message}", ex.Message);
    }
}

// Global error handler: catch exceptions and write JSON using System.Text.Json
// This prevents some test-host PipeWriter implementations from triggering
// System.Text.Json's WriteAsJsonAsync which expects UnflushedBytes on the PipeWriter.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        try
        {
            var logger = context.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("GlobalErrorMiddleware");
            logger?.LogError(ex, "Unhandled exception while processing {Path}", context.Request.Path);
        }
        catch { }

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var payload = new { error = "internal_server_error", message = ex.Message };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            await  context.Response.WriteAsync(json);
        }
        else
        {
            // If response already started, we can't change it - rethrow to allow hosting to handle
            throw;
        }
    }
});

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

// Health endpoint
app.MapGet("/health", () => Results.Json(new { status = "ok", time = DateTime.UtcNow })).WithName("Health");

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

app.MapGet("/api/metrics/ticks", (Imperium.Api.MetricsService metrics) =>
{
    var samples = metrics.GetRecentTickDurations();
    var average = samples.Length > 0 ? samples.Average() : 0;
    var last = samples.Length > 0 ? samples[^1] : 0;
    return Results.Json(new { durationsMs = samples, averageMs = average, lastMs = last });
}).WithName("GetTickMetrics");

// Chronicles: latest myth and list
app.MapGet("/api/chronicles", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var items = await db.WorldChronicles.OrderByDescending(c => c.Year).Take(50).ToListAsync();
    return Results.Json(items);
}).WithName("GetChronicles");

app.MapGet("/api/chronicles/latest", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var item = await db.WorldChronicles.OrderByDescending(c => c.Year).FirstOrDefaultAsync();
    if (item == null) return Results.NotFound();
    return Results.Json(item);
}).WithName("GetLatestChronicle");

// Factions (read-only)
app.MapGet("/api/factions", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var items = await db.Factions.OrderBy(f => f.Name).ToListAsync();
    return Results.Json(items);
}).WithName("GetFactions");

// Rumors (read-only)
app.MapGet("/api/rumors", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var items = await db.Rumors.OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync();
    return Results.Json(items);
}).WithName("GetRumors");

// Queue metrics: processed, dropped, average processing time
app.MapGet("/api/metrics/queue", (Imperium.Api.MetricsService metrics, Imperium.Api.Services.NpcReplyQueueService queue) =>
{
    var snapshot = metrics.Snapshot();
    var processed = queue.ProcessedCount;
    var dropped = queue.DroppedCount;
    var avgMs = processed > 0 ? (double)queue.TotalProcessingMs / processed : 0.0;
    return Results.Json(new { counters = snapshot, processed, dropped, avgProcessingMs = avgMs });
}).WithName("GetQueueMetrics");

// Economy: dev queries for orders/trades/inventory (backed by EF models)
app.MapGet("/api/economy/orders", async (string? item, Guid? locationId, Guid? ownerId, string? ownerType, Guid? reachableFromLocationId, double? radiusKm, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var q = db.MarketOrders.AsQueryable();
    if (!string.IsNullOrWhiteSpace(item)) q = q.Where(o => o.Item == item);
    if (locationId.HasValue) q = q.Where(o => o.LocationId == locationId);
    if (ownerId.HasValue) q = q.Where(o => o.OwnerId == ownerId);
    if (!string.IsNullOrWhiteSpace(ownerType)) q = q.Where(o => o.OwnerType == ownerType);
    var list = await q.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync();
    // Reachability filter (client-side with coordinates)
    if (reachableFromLocationId.HasValue && radiusKm.HasValue && radiusKm.Value > 0)
    {
        var locs = await db.Locations.ToDictionaryAsync(l => l.Id, l => new { l.Latitude, l.Longitude });
        if (locs.TryGetValue(reachableFromLocationId.Value, out var origin) && origin.Latitude.HasValue && origin.Longitude.HasValue)
        {
            list = list.Where(o => o.LocationId.HasValue &&
                o.LocationId.Value != Guid.Empty &&
                locs.TryGetValue(o.LocationId.Value, out var dst) && dst.Latitude.HasValue && dst.Longitude.HasValue &&
                Imperium.Api.Services.GeoService.DistanceKm(origin.Latitude!.Value, origin.Longitude!.Value, dst.Latitude!.Value, dst.Longitude!.Value) <= radiusKm.Value
            ).ToList();
        }
        else
        {
            list = new List<Imperium.Domain.Models.MarketOrder>();
        }
    }
    var items = list.Take(200).ToList();
    return Results.Json(items);
}).WithName("GetOrders");

// Create order: reserves funds/qty and supports household/character owner
app.MapPost("/api/economy/orders", async (
    Imperium.Domain.Models.MarketOrder input,
    Imperium.Infrastructure.ImperiumDbContext db,
    Imperium.Domain.Services.IEventDispatcher dispatcher,
    Imperium.Api.MetricsService metrics,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(input.Item) || string.IsNullOrWhiteSpace(input.Side))
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "item and side are required" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }
    if (input.Quantity <= 0 || input.Price < 0)
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "quantity must be > 0 and price >= 0" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }
    if (input.LocationId == null)
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "locationId is required for orders" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }

    var now = DateTime.UtcNow;
    var ord = new Imperium.Domain.Models.MarketOrder
    {
        Id = Guid.NewGuid(),
        OwnerId = input.OwnerId,
        OwnerType = string.IsNullOrWhiteSpace(input.OwnerType) ? "character" : input.OwnerType,
        LocationId = input.LocationId,
        Item = input.Item,
        Side = input.Side,
        Price = Math.Round(input.Price, 2),
        Quantity = Math.Round(input.Quantity, 2),
        Remaining = Math.Round(input.Quantity, 2),
        Status = "open",
        CreatedAt = now,
        UpdatedAt = now,
        ExpiresAt = input.ExpiresAt
    };

    // TTL default: 15 minutes if not provided
    if (ord.ExpiresAt == null)
        ord.ExpiresAt = now.AddMinutes(15);

    if (string.Equals(ord.Side, "buy", StringComparison.OrdinalIgnoreCase))
    {
        var total = Math.Round(ord.Price * ord.Quantity, 2);
        // Reserve from household first when ownerType == household, else from character
        if (string.Equals(ord.OwnerType, "household", StringComparison.OrdinalIgnoreCase))
        {
            var hh = await db.Households.FindAsync(ord.OwnerId);
            if (hh == null)
            {
                var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "household not found" });
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Results.Content(jsonErr, "application/json");
            }
            var toReserve = Math.Min(total, Math.Max(hh.Wealth, 0));
            if (toReserve > 0)
            {
                hh.Wealth -= toReserve;
                ord.ReservedFunds = toReserve;
            }
        }
        else
        {
            var ch = await db.Characters.FindAsync(ord.OwnerId);
            if (ch == null)
            {
                var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "character not found" });
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Results.Content(jsonErr, "application/json");
            }
            var toReserve = Math.Min(total, Math.Max(ch.Money, 0));
            if (toReserve > 0)
            {
                ch.Money -= toReserve;
                ord.ReservedFunds = toReserve;
            }
        }
    }
    else if (string.Equals(ord.Side, "sell", StringComparison.OrdinalIgnoreCase))
    {
        // Reserve quantity from inventory of owner
        var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ord.OwnerId && i.OwnerType == ord.OwnerType && i.Item == ord.Item && i.LocationId == (ord.LocationId ?? i.LocationId));
        if (inv == null || inv.Quantity <= 0)
        {
            var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "insufficient inventory to sell" });
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.Content(jsonErr, "application/json");
        }
        var toReserve = Math.Min(inv.Quantity, ord.Quantity);
        inv.Quantity -= toReserve;
        ord.ReservedQty = toReserve;
        // If could not reserve full, still place order with partial reservation; matching will handle remaining vs available
    }
    else
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "side must be 'buy' or 'sell'" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }

    db.MarketOrders.Add(ord);
    await db.SaveChangesAsync();

    metrics.Increment("economy.orders.created");
    metrics.Add("economy.orders.active", 1);

    var ev = new GameEvent
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        Type = "order_placed",
        Location = ord.LocationId?.ToString() ?? "global",
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            orderId = ord.Id,
            ownerId = ord.OwnerId,
            ownerType = ord.OwnerType,
            side = ord.Side,
            item = ord.Item,
            price = ord.Price,
            qty = ord.Quantity,
            remaining = ord.Remaining
        })
    };
    try { await dispatcher.EnqueueAsync(ev); } catch { }

    // Avoid WriteAsJsonAsync pipewriter path issues in some test hosts by serializing to string
    var json = System.Text.Json.JsonSerializer.Serialize(ord);
    http.Response.Headers.Location = $"/api/economy/orders/{ord.Id}";
    http.Response.StatusCode = StatusCodes.Status201Created;
    return Results.Content(json, "application/json");
}).WithName("CreateOrder");

// Dev: place an order and emit an "order_placed" GameEvent for fast-track market testing
app.MapPost("/api/dev/place-order-event", async (HttpContext http, Imperium.Infrastructure.ImperiumDbContext db, Imperium.Domain.Services.IEventDispatcher dispatcher) =>
{
    // Read body manually to avoid framework automatic model binding which can cause WriteAsJsonAsync during error handling in some test hosts
    string body;
    using (var reader = new System.IO.StreamReader(http.Request.Body))
    {
        body = await reader.ReadToEndAsync();
    }
    Imperium.Domain.Models.MarketOrder? input = null;
    try
    {
        input = System.Text.Json.JsonSerializer.Deserialize<Imperium.Domain.Models.MarketOrder>(body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        var j = System.Text.Json.JsonSerializer.Serialize(new { error = "invalid JSON", detail = ex.Message });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(j, "application/json");
    }
    if (input == null)
    {
        var j = System.Text.Json.JsonSerializer.Serialize(new { error = "invalid payload" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(j, "application/json");
    }

    if (string.IsNullOrWhiteSpace(input.Item) || string.IsNullOrWhiteSpace(input.Side))
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "item and side are required" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }
    if (input.Quantity <= 0 || input.Price < 0)
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "quantity must be > 0 and price >= 0" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }
    if (input.LocationId == null)
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "locationId is required for orders" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }

    var now = DateTime.UtcNow;
    var ord = new Imperium.Domain.Models.MarketOrder
    {
        Id = Guid.NewGuid(),
        OwnerId = input.OwnerId,
        OwnerType = string.IsNullOrWhiteSpace(input.OwnerType) ? "character" : input.OwnerType,
        LocationId = input.LocationId,
        Item = input.Item,
        Side = input.Side,
        Price = Math.Round(input.Price, 2),
        Quantity = Math.Round(input.Quantity, 2),
        Remaining = Math.Round(input.Quantity, 2),
        Status = "open",
        CreatedAt = now,
        UpdatedAt = now,
        ExpiresAt = input.ExpiresAt
    };

    if (ord.ExpiresAt == null) ord.ExpiresAt = now.AddMinutes(15);

    // Reserve funds or quantity similar to the regular CreateOrder endpoint
    if (string.Equals(ord.Side, "buy", StringComparison.OrdinalIgnoreCase))
    {
        var total = Math.Round(ord.Price * ord.Quantity, 2);
        if (string.Equals(ord.OwnerType, "household", StringComparison.OrdinalIgnoreCase))
        {
            var hh = await db.Households.FindAsync(ord.OwnerId);
            if (hh == null)
            {
                var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "household not found" });
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Results.Content(jsonErr, "application/json");
            }
            var toReserve = Math.Min(total, Math.Max(hh.Wealth, 0));
            if (toReserve > 0)
            {
                hh.Wealth -= toReserve;
                ord.ReservedFunds = toReserve;
            }
        }
        else
        {
            var ch = await db.Characters.FindAsync(ord.OwnerId);
            if (ch == null)
            {
                var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "character not found" });
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Results.Content(jsonErr, "application/json");
            }
            var toReserve = Math.Min(total, Math.Max(ch.Money, 0));
            if (toReserve > 0)
            {
                ch.Money -= toReserve;
                ord.ReservedFunds = toReserve;
            }
        }
    }
    else if (string.Equals(ord.Side, "sell", StringComparison.OrdinalIgnoreCase))
    {
        var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ord.OwnerId && i.OwnerType == ord.OwnerType && i.Item == ord.Item && i.LocationId == (ord.LocationId ?? i.LocationId));
        if (inv == null || inv.Quantity <= 0)
        {
            var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "insufficient inventory to sell" });
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Results.Content(jsonErr, "application/json");
        }
        var toReserve = Math.Min(inv.Quantity, ord.Quantity);
        inv.Quantity -= toReserve;
        ord.ReservedQty = toReserve;
    }
    else
    {
        var jsonErr = System.Text.Json.JsonSerializer.Serialize(new { error = "side must be 'buy' or 'sell'" });
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Results.Content(jsonErr, "application/json");
    }

    try
    {
        db.MarketOrders.Add(ord);
        await db.SaveChangesAsync();

        // Emit an order_placed GameEvent so the UI / agents can react via SSE
        var ev = new Imperium.Domain.Models.GameEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "order_placed",
            Location = ord.LocationId?.ToString() ?? "global",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { orderId = ord.Id, ownerId = ord.OwnerId, side = ord.Side, item = ord.Item, price = ord.Price, qty = ord.Quantity })
        };

        // Fire-and-forget enqueue but observe exceptions synchronously where possible
        try { _ = dispatcher.EnqueueAsync(ev); } catch { /* swallow - best-effort */ }

        var json = System.Text.Json.JsonSerializer.Serialize(ord);
        http.Response.Headers.Location = $"/api/economy/orders/{ord.Id}";
        http.Response.StatusCode = StatusCodes.Status201Created;
        return Results.Content(json, "application/json");
    }
    catch (Exception ex)
    {
        // Ensure we don't let exceptions escape to DeveloperExceptionPage which may use WriteAsJsonAsync
        try
        {
            var logger = http.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevPlaceOrder");
            logger?.LogError(ex, "Dev place-order-event failed");
        }
        catch { }
        var err = System.Text.Json.JsonSerializer.Serialize(new { error = "internal_server_error", message = ex.Message });
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Results.Content(err, "application/json");
    }
}).WithName("DevPlaceOrderEvent");

app.MapGet("/api/economy/trades", async (string? item, Guid? locationId, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var q = db.Trades.AsQueryable();
    if (!string.IsNullOrWhiteSpace(item)) q = q.Where(t => t.Item == item);
    if (locationId.HasValue) q = q.Where(t => t.LocationId == locationId);
    var items = await q.OrderByDescending(t => t.Timestamp).Take(200).ToListAsync();
    return Results.Json(items);
}).WithName("GetTrades");

app.MapGet("/api/economy/inventory", async (Guid? ownerId, string? ownerType, Guid? locationId, Guid? reachableFromLocationId, double? radiusKm, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var q = db.Inventories.AsQueryable();
    if (ownerId.HasValue) q = q.Where(i => i.OwnerId == ownerId);
    if (!string.IsNullOrWhiteSpace(ownerType)) q = q.Where(i => i.OwnerType == ownerType);
    if (locationId.HasValue) q = q.Where(i => i.LocationId == locationId);
    // SQLite provider can have issues ordering by decimal on server; order on client
    var list = await q.Take(1000).ToListAsync();
    if (reachableFromLocationId.HasValue && radiusKm.HasValue && radiusKm.Value > 0)
    {
        var locs = await db.Locations.ToDictionaryAsync(l => l.Id, l => new { l.Latitude, l.Longitude });
        if (locs.TryGetValue(reachableFromLocationId.Value, out var origin) && origin.Latitude.HasValue && origin.Longitude.HasValue)
        {
            list = list.Where(i => i.LocationId.HasValue &&
                locs.TryGetValue(i.LocationId.Value, out var dst) && dst.Latitude.HasValue && dst.Longitude.HasValue &&
                Imperium.Api.Services.GeoService.DistanceKm(origin.Latitude!.Value, origin.Longitude!.Value, dst.Latitude!.Value, dst.Longitude!.Value) <= radiusKm.Value
            ).ToList();
        }
        else
        {
            list = new List<Imperium.Domain.Models.Inventory>();
        }
    }
    var items = list.OrderBy(i => i.Item).ThenByDescending(i => i.Quantity).ToList();
    return Results.Json(items);
}).WithName("GetInventory");

// Aggregates: inventory sums per location or owner
app.MapGet("/api/economy/inventory/aggregate", async (string by, string? item, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var q = db.Inventories.AsQueryable();
    if (!string.IsNullOrWhiteSpace(item)) q = q.Where(i => i.Item == item);
    if (string.Equals(by, "location", StringComparison.OrdinalIgnoreCase))
    {
        var data = await q.GroupBy(i => i.LocationId).Select(g => new { locationId = g.Key, total = g.Sum(x => x.Quantity) }).ToListAsync();
        return Results.Json(data);
    }
    else if (string.Equals(by, "owner", StringComparison.OrdinalIgnoreCase))
    {
        var data = await q.GroupBy(i => i.OwnerId).Select(g => new { ownerId = g.Key, total = g.Sum(x => x.Quantity) }).ToListAsync();
        return Results.Json(data);
    }
    return Results.BadRequest(new { error = "by must be 'location' or 'owner'" });
}).WithName("GetInventoryAggregate");

app.MapGet("/api/logistics/jobs", (LogisticsQueueService queue) =>
{
    return Results.Json(queue.Snapshot());
}).WithName("GetLogisticsJobs");

// Economy: items list from config
app.MapGet("/api/economy/items", (Imperium.Api.EconomyStateService state) =>
{
    return Results.Json(state.GetItems());
}).WithName("GetEconomyItems");

// Economy: item definitions (metadata for each item)
app.MapGet("/api/economy/item-defs", (Imperium.Api.EconomyStateService state) =>
{
    return Results.Json(state.GetDefinitions());
}).WithName("GetItemDefinitions");

app.MapGet("/api/economy/item-defs/{name}", (string name, Imperium.Api.EconomyStateService state) =>
{
    var d = state.GetDefinition(name);
    if (d == null) return Results.NotFound();
    return Results.Json(d);
}).WithName("GetItemDefinition");

app.MapPost("/api/economy/item-defs", (EconomyItemDefinition def, Imperium.Api.EconomyStateService state) =>
{
    if (string.IsNullOrWhiteSpace(def?.Name)) return Results.BadRequest(new { error = "требуется имя (Name)" });
    if (def.BasePrice < 0m) return Results.BadRequest(new { error = "BasePrice должен быть >= 0" });
    if (def.ConsumptionPerTick < 0m) return Results.BadRequest(new { error = "ConsumptionPerTick должен быть >= 0" });
    if (def.WeightPerUnit <= 0m) return Results.BadRequest(new { error = "WeightPerUnit должен быть > 0" });
    if (def.StackSize <= 0) return Results.BadRequest(new { error = "StackSize должен быть > 0" });
    if (def.PerishableDays.HasValue && def.PerishableDays.Value < 0) return Results.BadRequest(new { error = "PerishableDays должен быть >= 0" });

    state.AddOrUpdateDefinition(def);
    return Results.Ok(def);
}).WithName("AddOrUpdateItemDefinition");

// Add items dynamically
app.MapPost("/api/economy/items", (string[] items, Imperium.Api.EconomyStateService state) =>
{
    var added = state.AddItems(items ?? Array.Empty<string>());
    return Results.Ok(new { added, total = state.GetItems().Count });
}).WithName("AddEconomyItems");

// Price shocks: item or "*" for all, factor (e.g., 1.2), optional expiresAt
app.MapPost("/api/economy/shocks", (string item, decimal factor, DateTime? expiresAt, Imperium.Api.EconomyStateService state) =>
{
    if (factor <= 0) return Results.BadRequest(new { error = "factor должен быть > 0" });
    state.SetShock(item, factor, expiresAt);
    var which = string.IsNullOrWhiteSpace(item) ? "*" : item;
    return Results.Ok(new { item = which, factor, expiresAt });
}).WithName("PostEconomyShock");

// List active shocks
app.MapGet("/api/economy/shocks", (Imperium.Api.EconomyStateService state) =>
{
    return Results.Json(state.GetShocks());
}).WithName("GetEconomyShocks");

// Dev: seed many historical items
app.MapPost("/api/dev/seed-items", (Imperium.Api.EconomyStateService state) =>
{
    var items = new [] {
        "grain","wheat","barley","rye","oats","millet","rice","olive","olive_oil","wine","grapes","figs","dates","honey","salt","fish","meat","cheese","wool","linen","hemp","flax","cotton","leather","timber","clay","brick","pottery","glass","papyrus","ink","dyes","indigo","madder","purple_dye","spices","pepper","cinnamon","cardamom","saffron","ginger","incense","myrrh","amber","tin","copper","bronze","iron","steel","silver","gold","lead","marble","granite","sandstone","chalk","coal","charcoal","pitch","tar","resin","sulfur","saltpeter","horses","cattle","sheep","goats","pigs","camels","mules","boats","oars","sails","rope","nails","tools","plow","millstone","wheat_flour","bread","beer","ale","soap","candles","lamps","textiles","tunics","cloaks","sandals","helmets","shields","spears","swords","arrows","bows","armor","chariots","wagons","cart_wheels","parchment","scrolls","books","statues","mosaics","tiles","pigments","silverware","potash","glass_beads","needles","fish_sauce","garum","olive_wood","vinegar","mustard","sesame","sesame_oil","almonds","walnuts","hazelnuts","plums","apples","pears","pomegranates"
    };
    var added = state.AddItems(items);
    return Results.Ok(new { added, total = state.GetItems().Count });
}).WithName("DevSeedItems");

// Cancel order: refunds reserved funds/qty when applicable
app.MapDelete("/api/economy/orders/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db, Imperium.Api.MetricsService metrics) =>
{
    var ord = await db.MarketOrders.FindAsync(id);
    if (ord == null) return Results.NotFound();
    if (ord.Status != "open") return Results.BadRequest(new { error = "only open orders can be cancelled" });
    ord.Status = "cancelled";
    ord.UpdatedAt = DateTime.UtcNow;
    if (ord.Side == "buy" && ord.ReservedFunds > 0)
    {
        // Try refund to character, then household
        var buyer = await db.Characters.FindAsync(ord.OwnerId);
        if (buyer != null) buyer.Money += ord.ReservedFunds; else
        {
            var hh = await db.Households.FindAsync(ord.OwnerId);
            if (hh != null) hh.Wealth += ord.ReservedFunds;
        }
        ord.ReservedFunds = 0;
    }
    if (ord.Side == "sell" && ord.ReservedQty > 0)
    {
        var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ord.OwnerId && i.OwnerType == ord.OwnerType && i.Item == ord.Item && i.LocationId == (ord.LocationId ?? i.LocationId));
        if (inv == null)
        {
            inv = new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ord.OwnerId, OwnerType = ord.OwnerType, LocationId = ord.LocationId, Item = ord.Item, Quantity = 0 };
            db.Inventories.Add(inv);
        }
        inv.Quantity += ord.ReservedQty;
        ord.ReservedQty = 0;
    }
    await db.SaveChangesAsync();
    metrics.Add("economy.orders.active", -1);
    metrics.Increment("economy.orders.cancelled");
    return Results.Ok(new { cancelled = id });
}).WithName("CancelOrder");

// Dev: seed characters on demand (POST)
app.MapPost("/api/dev/seed-characters", async (int? count, Imperium.Infrastructure.ImperiumDbContext db, Imperium.Api.Services.NamesService names) =>
{
    // When count is provided -> bulk generate; otherwise keep classic 3 demo characters
    if (count.HasValue && count.Value > 3)
    {
        var total = Math.Min(Math.Max(count.Value, 1), 20000); // safety cap
        var list = new List<Imperium.Domain.Models.Character>(total);
        var poolLocations = await db.Locations.Select(l => l.Name).ToListAsync();
        if (poolLocations.Count == 0) poolLocations = new List<string> { "Roma", "Sicilia", "Athenae", "Neapolis" };
        var rnd = Random.Shared;
        // Avoid duplicates against existing DB names
        var used = new HashSet<string>(await db.Characters.Select(c => c.Name).ToListAsync(), StringComparer.OrdinalIgnoreCase);
        foreach (var (full, female) in names.GenerateNamesUnique(total, used))
        {
            var loc = poolLocations[rnd.Next(poolLocations.Count)];
            var age = 14 + rnd.Next(60); // 14..73
            var status = rnd.NextDouble() < 0.1 ? "travel" : "idle";
            list.Add(new Imperium.Domain.Models.Character
            {
                Id = Guid.NewGuid(),
                Name = full,
                Age = age,
                Status = status,
                LocationName = loc,
                EssenceJson = JsonSerializer.Serialize(new { charisma = rnd.Next(3,9), strength = rnd.Next(2,10) }),
                SkillsJson = JsonSerializer.Serialize(new { craft = rnd.Next(0,5), commerce = rnd.Next(0,5) }),
                History = female ? "Daughter of a respected family." : "Son of a respected family.",
                Gender = female ? "female" : "male"
            });
        }
        // Insert in batches to keep memory and transaction reasonable
        const int batch = 1000;
        for (int i = 0; i < list.Count; i += batch)
        {
            var slice = list.Skip(i).Take(batch).ToList();
            db.Characters.AddRange(slice);
            await db.SaveChangesAsync();
        }
        return Results.Ok(new { seeded = list.Count });
    }
    else
    {
            var characters = new[]
        {
            new Imperium.Domain.Models.Character
            {
                Id = Guid.NewGuid(),
                Name = "Aurelia Cassia",
                Money = 50m,
                Age = 29,
                Status = "idle",
                LocationName = "Roma",
                EssenceJson = JsonSerializer.Serialize(new { charisma = 6, talents = new[] { "oratory", "diplomacy" } }),
                SkillsJson = JsonSerializer.Serialize(new { commerce = 3, rhetoric = 2 }),
                History = "Aurelia Cassia brokers grain contracts between Roma and the provinces.",
                Gender = "female"
            },
            new Imperium.Domain.Models.Character
            {
                Id = Guid.NewGuid(),
                Name = "Cassius Varro",
                Gender = "male",
                Money = 60m,
                Age = 35,
                Status = "idle",
                LocationName = "Neapolis",
                EssenceJson = JsonSerializer.Serialize(new { strength = 7, talents = new[] { "legionary", "tactics" } }),
                SkillsJson = JsonSerializer.Serialize(new { leadership = 3, logistics = 2 }),
                History = "Cassius Varro is a retired centurion seeking to restore his family's estate near Neapolis."
            },
            new Imperium.Domain.Models.Character
            {
                Id = Guid.NewGuid(),
                Name = "Selene of Syracusae",
                Gender = "female",
                Money = 55m,
                Age = 31,
                Status = "idle",
                LocationName = "Syracusae",
                EssenceJson = JsonSerializer.Serialize(new { intelligence = 8, talents = new[] { "astronomy", "engineering" } }),
                SkillsJson = JsonSerializer.Serialize(new { scholarship = 4, invention = 2 }),
                History = "Selene charts the stars above Syracusae and advises the port magistrates on navigation."
            }
        };
        db.Characters.AddRange(characters);
        if (characters.Length >= 3)
        {
            var mother = characters[0];
            var father = characters[1];
            var child = characters[2];
            var markers = new List<string>();
            SeedDevFamilyData(db, mother, father, child, markers);
            SeedDevOwnershipData(db, mother, father, child, markers, app.Services.GetRequiredService<Imperium.Api.EconomyStateService>());
        }
        await db.SaveChangesAsync();
        return Results.Ok(new { seeded = characters.Length });
    }
});
app.MapPost("/api/dev/seed-world", async (Imperium.Infrastructure.ImperiumDbContext db, Imperium.Api.EconomyStateService state) =>
{
    var created = new List<string>();
    // Seed a few locations if none exist
    if (!await db.Locations.AnyAsync())
    {
        // Coordinates tuned for background map (normalized-ish space 0..1)
        db.Locations.AddRange(
            new Imperium.Domain.Models.Location { Id = Guid.NewGuid(), Name = "Roma", Population = 100000, Latitude = 0.28, Longitude = 0.30 },
            new Imperium.Domain.Models.Location { Id = Guid.NewGuid(), Name = "Sicilia", Population = 45000, Latitude = 0.55, Longitude = 0.22 },
            new Imperium.Domain.Models.Location { Id = Guid.NewGuid(), Name = "Athenae", Population = 80000, Latitude = 0.40, Longitude = 0.65 }
        );
        created.Add("locations");
    }
    else
    {
        // Ensure coordinates exist for known dev locations
        var roma = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Roma");
        var sicilia = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Sicilia");
        var athenae = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Athenae");
        if (roma != null && (!roma.Latitude.HasValue || !roma.Longitude.HasValue)) { roma.Latitude = 0.28; roma.Longitude = 0.30; created.Add("locations_coords"); }
        if (sicilia != null && (!sicilia.Latitude.HasValue || !sicilia.Longitude.HasValue)) { sicilia.Latitude = 0.55; sicilia.Longitude = 0.22; created.Add("locations_coords"); }
        if (athenae != null && (!athenae.Latitude.HasValue || !athenae.Longitude.HasValue)) { athenae.Latitude = 0.40; athenae.Longitude = 0.65; created.Add("locations_coords"); }
    }

    // Enrich genealogies, households, ownerships based on dev characters
    var chars = await db.Characters.OrderBy(c => c.Name).Take(4).ToListAsync();
    if (chars.Count >= 3)
    {
    var mother = chars[0];
    var father = chars[1];
    var child = chars[2];
    SeedDevFamilyData(db, mother, father, child, created);
    SeedDevOwnershipData(db, mother, father, child, created, app.Services.GetRequiredService<Imperium.Api.EconomyStateService>());
    }
    else if (!await db.Households.AnyAsync())
    {
        // fallback household if characters are unavailable
        db.Households.Add(new Imperium.Domain.Models.Household
        {
            Id = Guid.NewGuid(),
            Name = "Founders Household",
            MemberIdsJson = "[]",
            Wealth = 50m
        });
        created.Add("households");
    }

    await db.SaveChangesAsync();

    // Ensure a rich set of economy items is available
    if (state.GetItems().Count < 80)
    {
        var items = new [] {
            "grain","wheat","barley","rye","oats","millet","rice","olive","olive_oil","wine","grapes","figs","dates","honey","salt","fish","meat","cheese","wool","linen","hemp","flax","cotton","leather","timber","clay","brick","pottery","glass","papyrus","ink","dyes","indigo","madder","purple_dye","spices","pepper","cinnamon","cardamom","saffron","ginger","incense","myrrh","amber","tin","copper","bronze","iron","steel","silver","gold","lead","marble","granite","sandstone","chalk","coal","charcoal","pitch","tar","resin","sulfur","saltpeter","horses","cattle","sheep","goats","pigs","camels","mules","boats","oars","sails","rope","nails","tools","plow","millstone","bread","beer","ale","soap","candles","lamps","textiles","tunics","cloaks","sandals","helmets","shields","spears","swords","arrows","bows","armor"
        };
        state.AddItems(items);
        created.Add("economy_items");
    }

    return Results.Ok(new { created });
});

// InheritanceRecords: create & list (minimal)
app.MapGet("/api/inheritance-records", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var items = await db.InheritanceRecords.OrderByDescending(i => i.CreatedAt).ToListAsync();
    return Results.Json(items);
}).WithName("ListInheritanceRecords");

app.MapPost("/api/inheritance-records", async (Imperium.Domain.Models.InheritanceRecord input, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    input.Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id;
    input.CreatedAt = DateTime.UtcNow;
    db.InheritanceRecords.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/api/inheritance-records/{input.Id}", input);
}).WithName("CreateInheritanceRecord");

// Dev: resolve inheritance by applying an equal split and emitting a GameEvent (very small safe behaviour)
app.MapPost("/api/dev/resolve-inheritance/{id:guid}", async (Guid id, Imperium.Api.Services.InheritanceService inh) =>
{
    var result = await inh.ApplyInheritanceAsync(id);
    if (!result.IsSuccess)
    {
        return Results.NotFound(new { ok = false, message = result.Message, id });
    }
    return Results.Ok(new { ok = true, inheritanceId = id, result.Data });
}).WithName("ResolveInheritanceDev");

// InheritanceRecords by character: deceased or among heirs
app.MapGet("/api/inheritance-records/by-character/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var s = id.ToString();
    var items = await db.InheritanceRecords
        .Where(i => i.DeceasedId == id || (i.HeirsJson != null && i.HeirsJson.Contains(s)))
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync();
    return Results.Json(items);
}).WithName("ListInheritanceRecordsByCharacter");

// Dev: reset (clear) characters and events, then reseed default dev characters
app.MapPost("/api/dev/reset-characters", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    // Remove all game events and characters (dev helper)
    try
    {
        db.GameEvents.RemoveRange(db.GameEvents);
        db.Characters.RemoveRange(db.Characters);
        db.GenealogyRecords.RemoveRange(db.GenealogyRecords);
        db.Households.RemoveRange(db.Households);
        db.Families.RemoveRange(db.Families);
        db.Ownerships.RemoveRange(db.Ownerships);
        db.NpcMemories.RemoveRange(db.NpcMemories);
        await db.SaveChangesAsync();
    }
    catch
    {
        // ignore if removal fails for some reason
    }

    var characters = new[]
    {
        new Imperium.Domain.Models.Character
        {
            Id = Guid.NewGuid(),
            Name = "Aurelia Cassia",
            Money = 50m,
            Age = 29,
            Status = "idle",
            LocationName = "Roma",
            EssenceJson = JsonSerializer.Serialize(new { charisma = 6, talents = new[] { "oratory", "diplomacy" } }),
            SkillsJson = JsonSerializer.Serialize(new { commerce = 3, rhetoric = 2 }),
            History = "Aurelia Cassia brokers grain contracts between Roma and the provinces.",
            Gender = "female"
        },
        new Imperium.Domain.Models.Character
        {
            Id = Guid.NewGuid(),
            Name = "Cassius Varro",
            Gender = "male",
            Money = 60m,
            Age = 35,
            Status = "idle",
            LocationName = "Neapolis",
            EssenceJson = JsonSerializer.Serialize(new { strength = 7, talents = new[] { "legionary", "tactics" } }),
            SkillsJson = JsonSerializer.Serialize(new { leadership = 3, logistics = 2 }),
            History = "Cassius Varro is a retired centurion seeking to restore his family's estate near Neapolis."
        },
        new Imperium.Domain.Models.Character
        {
            Id = Guid.NewGuid(),
            Name = "Selene of Syracusae",
            Gender = "female",
            Money = 55m,
            Age = 31,
            Status = "idle",
            LocationName = "Syracusae",
            EssenceJson = JsonSerializer.Serialize(new { intelligence = 8, talents = new[] { "astronomy", "engineering" } }),
            SkillsJson = JsonSerializer.Serialize(new { scholarship = 4, invention = 2 }),
            History = "Selene charts the stars above Syracusae and advises the port magistrates on navigation."
        }
    };

    db.Characters.AddRange(characters);

    if (characters.Length >= 3)
    {
        var mother = characters[0];
        var father = characters[1];
        var child = characters[2];
        var markers = new List<string>();
        SeedDevFamilyData(db, mother, father, child, markers);
        SeedDevOwnershipData(db, mother, father, child, markers);
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { reset = true, seeded = characters.Length });
});
// Public endpoints for characters (list / single) and their npc events
app.MapGet("/api/characters", async (string? gender, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var query = db.Characters.AsQueryable().FilterByGender(gender);
    var items = await query
        .OrderBy(c => c.Name)
        .Select(c => new { c.Id, c.Name, c.Age, c.Status, c.Gender, c.Money, c.LocationId, c.LocationName, c.Latitude, c.Longitude })
        .ToListAsync();
    return Results.Json(items);
}).WithName("GetCharacters");

app.MapGet("/api/characters/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var c = await db.Characters.FindAsync(id);
    if (c == null) return Results.NotFound();
    return Results.Json(new { c.Id, c.Name, c.Age, c.Status, c.Gender, c.Money, c.LocationId, c.LocationName, c.Latitude, c.Longitude, essence = c.EssenceJson, history = c.History, skills = c.SkillsJson });
}).WithName("GetCharacter");

// Move character to a specific location (enables proximity-gated interactions)
app.MapPost("/api/characters/{id:guid}/move", async (Guid id, Guid locationId, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var ch = await db.Characters.FindAsync(id);
    if (ch == null) return Results.NotFound();
    var loc = await db.Locations.FindAsync(locationId);
    if (loc == null) return Results.BadRequest(new { error = "location not found" });
    ch.LocationId = loc.Id;
    ch.LocationName = loc.Name;
    ch.Latitude = loc.Latitude;
    ch.Longitude = loc.Longitude;
    await db.SaveChangesAsync();
    return Results.Ok(new { id = ch.Id, ch.LocationId, ch.LocationName });
}).WithName("MoveCharacter");

// Move character to arbitrary coordinates (free movement)
app.MapPost("/api/characters/{id:guid}/move-to-coord", async (Guid id, double lat, double lon, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var ch = await db.Characters.FindAsync(id);
    if (ch == null) return Results.NotFound();
    ch.Latitude = lat;
    ch.Longitude = lon;
    ch.LocationId = null;
    ch.LocationName = null;
    await db.SaveChangesAsync();
    return Results.Ok(new { id = ch.Id, ch.Latitude, ch.Longitude });
}).WithName("MoveCharacterToCoord");

app.MapGet("/api/characters/{id:guid}/events", async (Guid id, int? count, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    // Simple filter: npc events that mention this characterId in PayloadJson
    var take = Math.Clamp(count ?? 100, 1, 500);
    var q = db.GameEvents.Where(e => e.Type == "npc_reply" && e.PayloadJson.Contains(id.ToString()));
    var items = await q.OrderByDescending(e => e.Timestamp).Take(take).ToListAsync();
    return Results.Json(items);
}).WithName("GetCharacterEvents");

// Relationships of a character (as source or target)
app.MapGet("/api/characters/{id:guid}/relationships", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var rels = await db.Relationships
        .Where(r => r.SourceId == id || r.TargetId == id)
        .ToListAsync();
    var otherIds = rels
        .Select(r => r.SourceId == id ? r.TargetId : r.SourceId)
        .Distinct()
        .ToList();
    var others = await db.Characters
        .Where(c => otherIds.Contains(c.Id))
        .Select(c => new { c.Id, c.Name, c.Status, c.LocationName })
        .ToListAsync();
    var map = others.ToDictionary(o => o.Id, o => (object)o);
    var shaped = rels.Select(r => new
    {
        id = r.Id,
        self = id,
        otherId = r.SourceId == id ? r.TargetId : r.SourceId,
        other = map.TryGetValue(r.SourceId == id ? r.TargetId : r.SourceId, out var o) ? o : null,
        r.Type,
        r.Trust,
        r.Love,
        r.Hostility,
        r.LastUpdated
    });
    return Results.Json(shaped);
}).WithName("GetCharacterRelationships");

// Communications (union of npc_reply and npc_reaction mentioning the character)
app.MapGet("/api/characters/{id:guid}/communications", async (Guid id, int? count, bool? sameLocationOnly, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var take = Math.Clamp(count ?? 50, 1, 200);
    var idStr = id.ToString();
    IQueryable<Imperium.Domain.Models.GameEvent> q = db.GameEvents.Where(e => (e.Type == "npc_reply" || e.Type == "npc_reaction") && e.PayloadJson.Contains(idStr));
    if (sameLocationOnly == true)
    {
        var ch = await db.Characters.FindAsync(id);
        var locName = ch?.LocationName;
        if (!string.IsNullOrWhiteSpace(locName))
        {
            q = q.Where(e => e.Location == locName);
        }
        else
        {
            return Results.Json(Array.Empty<object>());
        }
    }
    var items = await q.OrderByDescending(e => e.Timestamp).Take(take).ToListAsync();
    return Results.Json(items);
}).WithName("GetCharacterCommunications");

// Nearby characters by coordinates (use character's own coords if available; else fallback to location coords)
app.MapGet("/api/characters/{id:guid}/nearby", async (Guid id, double radiusKm, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var me = await db.Characters.FindAsync(id);
    if (me == null) return Results.NotFound();
    double? lat = me.Latitude, lon = me.Longitude;
    if (!lat.HasValue || !lon.HasValue)
    {
        if (me.LocationId.HasValue)
        {
            var loc = await db.Locations.FindAsync(me.LocationId.Value);
            lat = loc?.Latitude; lon = loc?.Longitude;
        }
    }
    if (!lat.HasValue || !lon.HasValue) return Results.Json(Array.Empty<object>());
    var all = await db.Characters.Where(c => c.Id != id).Select(c => new { c.Id, c.Name, c.LocationId, c.LocationName, c.Latitude, c.Longitude }).ToListAsync();
    var locs = await db.Locations.ToDictionaryAsync(l => l.Id, l => new { l.Latitude, l.Longitude });
    var res = all.Where(c => {
        var clat = c.Latitude; var clon = c.Longitude;
        if (!clat.HasValue || !clon.HasValue)
        {
            if (c.LocationId.HasValue && locs.TryGetValue(c.LocationId.Value, out var ll))
            { clat = ll.Latitude; clon = ll.Longitude; }
        }
        if (!clat.HasValue || !clon.HasValue) return false;
        return Imperium.Api.Services.GeoService.DistanceKm(lat!.Value, lon!.Value, clat.Value, clon.Value) <= radiusKm;
    }).ToList();
    return Results.Json(res);
}).WithName("GetNearbyCharacters");

app.MapGet("/api/households", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var households = await db.Households.OrderBy(h => h.Name).ToListAsync();
    var shaped = households.Select(h => new
    {
        h.Id,
        h.Name,
        h.LocationId,
        h.HeadId,
        members = ParseGuidList(h.MemberIdsJson),
        h.Wealth
    });
    return Results.Json(shaped);
}).WithName("GetHouseholds");

app.MapGet("/api/households/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var household = await db.Households.FindAsync(id);
    if (household == null) return Results.NotFound();

    var memberIds = ParseGuidList(household.MemberIdsJson);
    var memberDetails = memberIds.Count == 0
        ? new List<object>()
        : (await db.Characters
            .Where(c => memberIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Status, c.LocationName })
            .ToListAsync())
            .Select(c => (object)c)
            .ToList();

    return Results.Json(new
    {
        household.Id,
        household.Name,
        household.LocationId,
        household.HeadId,
        memberIds,
        members = memberDetails,
        household.Wealth
    });
}).WithName("GetHousehold");

// Locations list (simple dev/read endpoint)
app.MapGet("/api/locations", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var items = await db.Locations.OrderBy(l => l.Name).Select(l => new { l.Id, l.Name, l.Latitude, l.Longitude, l.NeighborsJson }).ToListAsync();
    return Results.Json(items);
}).WithName("GetLocations");

// Shortest route (BFS) using NeighborsJson adjacency
app.MapGet("/api/locations/route", async (Guid from, Guid to, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var all = await db.Locations.Select(l => new { l.Id, l.NeighborsJson }).ToListAsync();
    var neighbors = all.ToDictionary(x => x.Id, x => ParseGuidList(x.NeighborsJson));
    if (!neighbors.ContainsKey(from) || !neighbors.ContainsKey(to)) return Results.NotFound();
    var prev = new Dictionary<Guid, Guid?>();
    var q = new Queue<Guid>();
    var seen = new HashSet<Guid> { from };
    prev[from] = null; q.Enqueue(from);
    while (q.Count > 0)
    {
        var cur = q.Dequeue(); if (cur == to) break;
        foreach (var nx in neighbors[cur])
        {
            if (seen.Add(nx)) { prev[nx] = cur; q.Enqueue(nx); }
        }
    }
    if (!prev.ContainsKey(to)) return Results.Json(new { path = Array.Empty<Guid>() });
    var path = new List<Guid>();
    var t = to; while (true) { path.Add(t); var p = prev[t]; if (p == null) break; t = p.Value; }
    path.Reverse();
    return Results.Json(new { path });
}).WithName("GetRoute");

// Characters present in a specific location
app.MapGet("/api/locations/{id:guid}/characters", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var exists = await db.Locations.AnyAsync(l => l.Id == id);
    if (!exists) return Results.NotFound();
    var items = await db.Characters.Where(c => c.LocationId == id)
        .Select(c => new { c.Id, c.Name, c.Status, c.LocationId, c.LocationName })
        .OrderBy(c => c.Name)
        .ToListAsync();
    return Results.Json(items);
}).WithName("GetCharactersByLocation");

// Dev: set coordinates for a location quickly
app.MapPost("/api/dev/set-location-coords", async (Guid id, double lat, double lon, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var loc = await db.Locations.FindAsync(id);
    if (loc == null) return Results.NotFound();
    loc.Latitude = lat; loc.Longitude = lon;
    await db.SaveChangesAsync();
    return Results.Ok(new { id, lat, lon });
}).WithName("SetLocationCoords");

app.MapGet("/api/characters/{id:guid}/genealogy", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var record = await db.GenealogyRecords.FirstOrDefaultAsync(g => g.CharacterId == id);
    if (record == null) return Results.NotFound();

    var spouseIds = ParseGuidList(record.SpouseIdsJson);
    var childIds = ParseGuidList(record.ChildrenIdsJson);
    var relatedIds = new HashSet<Guid>(spouseIds.Concat(childIds));
    if (record.FatherId.HasValue) relatedIds.Add(record.FatherId.Value);
    if (record.MotherId.HasValue) relatedIds.Add(record.MotherId.Value);

    var related = relatedIds.Count == 0
        ? new Dictionary<Guid, object>()
        : (await db.Characters
            .Where(c => relatedIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Status, c.LocationName })
            .ToListAsync())
            .ToDictionary(c => c.Id, c => (object)c);

    object? Resolve(Guid? key) => key.HasValue && related.TryGetValue(key.Value, out var value) ? value : null;

    return Results.Json(new
    {
        record.Id,
        record.CharacterId,
        record.FatherId,
        father = Resolve(record.FatherId),
        record.MotherId,
        mother = Resolve(record.MotherId),
        spouses = spouseIds.Select(spouseId => new
        {
            id = spouseId,
            details = Resolve(spouseId)
        }),
        children = childIds.Select(childId => new
        {
            id = childId,
            details = Resolve(childId)
        })
    });
}).WithName("GetGenealogy");

// Dev: trigger one immediate tick cycle (useful for testing)
app.MapPost("/api/dev/tick-now", async (IServiceProvider sp, bool? advanceTime) =>
{
    using var scope = sp.CreateScope();
    var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>()
        .OrderBy(a => a.Name == "TimeAI" ? 0 : 1).ToList();

    int completed = 0;
    // Optionally run TimeAI first when requested (advanceTime=true)
    var doAdvance = advanceTime ?? false;
    if (doAdvance)
    {
        var timeAgent = agents.FirstOrDefault(a => a.Name == "TimeAI");
        if (timeAgent != null)
        {
            try
            {
                using var timeScope = sp.CreateScope();
                using var agentCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                agentCts.CancelAfter(TimeSpan.FromSeconds(15));
                await timeAgent.TickAsync(timeScope.ServiceProvider, agentCts.Token);
                System.Threading.Interlocked.Increment(ref completed);
            }
            catch (OperationCanceledException)
            {
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
                logger?.LogWarning("TimeAgent tick canceled (timeout)");
            }
            catch (Exception ex)
            {
                var logger = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
                logger?.LogError(ex, "TimeAgent tick failed");
            }
        }
    }

    var otherAgents = agents.Where(a => a.Name != "TimeAI").ToList();
    var maxConcurrency = 4;
    var sem = new System.Threading.SemaphoreSlim(maxConcurrency);
    var tasks = new List<Task>();
    foreach (var a in otherAgents)
    {
        var agentType = a.GetType();
        await sem.WaitAsync();
        tasks.Add(Task.Run(async () =>
        {
            try
            {
                using var sScope = sp.CreateScope();
                using var agentCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                agentCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    // Resolve a fresh agent instance from the per-task scope to ensure scoped services (DbContext) are unique
                    var agent = sScope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().FirstOrDefault(i => i.GetType() == agentType);
                    if (agent == null)
                    {
                        var logger = sScope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
                        logger?.LogWarning("Could not resolve agent of type {AgentType} in new scope", agentType.FullName);
                    }
                    else
                    {
                        await agent.TickAsync(sScope.ServiceProvider, agentCts.Token);
                        System.Threading.Interlocked.Increment(ref completed);
                    }
                }
                catch (OperationCanceledException)
                {
                    var logger = sScope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
                    logger?.LogWarning("Dev tick for {Agent} canceled (timeout)", a.Name);
                }
                catch (Exception ex)
                {
                    var logger = sScope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
                    logger?.LogError(ex, "Dev tick for {Agent} failed", a.Name);
                }
            }
            finally
            {
                sem.Release();
            }
        }));
    }
    await Task.WhenAll(tasks);

    // Fetch current worldTime for convenience
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        var wt = await db.WorldTimes.FirstOrDefaultAsync();
        if (wt != null)
        {
            return Results.Ok(new
            {
                ticks = completed,
                totalAgents = agents.Count,
                advanced = doAdvance,
                worldTime = new { tick = wt.Tick, hour = wt.Hour, day = wt.Day, month = wt.Month, dayOfMonth = wt.DayOfMonth, year = wt.Year }
            });
        }
    }
    catch { }

    return Results.Ok(new { ticks = completed, totalAgents = agents.Count, advanced = doAdvance });
});

// Dev: tick only TimeAI (advance world time)
app.MapPost("/api/dev/tick-time", async (IServiceProvider sp) =>
{
    using var scope = sp.CreateScope();
    var timeAgent = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().FirstOrDefault(a => a.Name == "TimeAI");
    if (timeAgent == null) return Results.Problem(detail: "TimeAI agent not found", statusCode: 500);
    try
    {
        using var agentCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await timeAgent.TickAsync(scope.ServiceProvider, agentCts.Token);
    }
    catch (OperationCanceledException)
    {
        // canceled
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("DevTick");
        logger?.LogError(ex, "TimeAgent tick failed");
    }

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        var wt = await db.WorldTimes.FirstOrDefaultAsync();
        if (wt != null)
        {
            return Results.Ok(new { success = true, worldTime = new { tick = wt.Tick, hour = wt.Hour, day = wt.Day, month = wt.Month, dayOfMonth = wt.DayOfMonth, year = wt.Year } });
        }
    }
    catch { }
    return Results.Ok(new { success = true, worldTime = (object?)null });
});

// Server-Sent Events: GameEvent stream
app.MapGet("/api/events/stream", async (Imperium.Api.EventStreamService stream, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    var reader = stream.Events;
    try
    {
        await foreach (var e in reader.ReadAllAsync(ctx.RequestAborted))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(e);
            await ctx.Response.WriteAsync($"data: {json}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // client disconnected - nothing to do
        var logger = ctx.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("EventStream");
        logger?.LogInformation("SSE client disconnected (events)");
    }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("EventStream");
        logger?.LogError(ex, "Unhandled exception in events SSE stream");
    }
}).WithName("EventStream");

// Server-Sent Events: WeatherSnapshot stream
app.MapGet("/api/weather/stream", async (Imperium.Api.EventStreamService stream, HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    var reader = stream.Weathers;
    try
    {
        await foreach (var s in reader.ReadAllAsync(ctx.RequestAborted))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(s);
            await ctx.Response.WriteAsync($"data: {json}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        var logger = ctx.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("WeatherStream");
        logger?.LogInformation("SSE client disconnected (weather)");
    }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("WeatherStream");
        logger?.LogError(ex, "Unhandled exception in weather SSE stream");
    }
}).WithName("WeatherStream");

// Return last saved weather snapshot from DB
app.MapGet("/api/weather/latest/db", async (Imperium.Infrastructure.ImperiumDbContext db) =>
{
    var snap = await db.WeatherSnapshots.OrderByDescending(s => s.Timestamp).FirstOrDefaultAsync();
    if (snap == null) return Results.NotFound();
    return Results.Json(snap);
}).WithName("GetLatestWeatherDb");

// Dev ownership / npc memory endpoints
app.MapDevOwnershipEndpoints();

static void SeedDevFamilyData(Imperium.Infrastructure.ImperiumDbContext db, Imperium.Domain.Models.Character mother, Imperium.Domain.Models.Character father, Imperium.Domain.Models.Character child, List<string>? created)
{
    void Mark(string label)
    {
        if (created == null) return;
        if (!created.Contains(label)) created.Add(label);
    }

    bool genealogyAdded = false;

    Imperium.Domain.Models.GenealogyRecord UpsertGenealogy(Imperium.Domain.Models.Character target, Guid? fatherId, Guid? motherId, IEnumerable<Guid> spouseIds, IEnumerable<Guid> childrenIds)
    {
        var record = db.GenealogyRecords.FirstOrDefault(g => g.CharacterId == target.Id);
        if (record == null)
        {
            record = new Imperium.Domain.Models.GenealogyRecord
            {
                Id = Guid.NewGuid(),
                CharacterId = target.Id
            };
            db.GenealogyRecords.Add(record);
            genealogyAdded = true;
        }

        record.FatherId = fatherId;
        record.MotherId = motherId;
        record.SpouseIdsJson = JsonSerializer.Serialize(spouseIds ?? Array.Empty<Guid>());
        record.ChildrenIdsJson = JsonSerializer.Serialize(childrenIds ?? Array.Empty<Guid>());
        return record;
    }

    UpsertGenealogy(mother, null, null, new[] { father.Id }, new[] { child.Id });
    UpsertGenealogy(father, null, null, new[] { mother.Id }, new[] { child.Id });
    UpsertGenealogy(child, father.Id, mother.Id, Array.Empty<Guid>(), Array.Empty<Guid>());

    if (genealogyAdded)
    {
        Mark("genealogy");
    }

    var householdName = $"{mother.Name.Split(' ')[0]}-{father.Name.Split(' ')[0]} Household";
    var household = db.Households.FirstOrDefault(h => h.Name == householdName);
    if (household == null)
    {
        household = new Imperium.Domain.Models.Household
        {
            Id = Guid.NewGuid(),
            Name = householdName
        };
        db.Households.Add(household);
        Mark("households");
    }

    household.HeadId ??= father.Id;
    household.MemberIdsJson = JsonSerializer.Serialize(new[] { mother.Id, father.Id, child.Id });
    household.Wealth = Math.Max(household.Wealth, 320m);
}

static void SeedDevOwnershipData(Imperium.Infrastructure.ImperiumDbContext db, Imperium.Domain.Models.Character mother, Imperium.Domain.Models.Character father, Imperium.Domain.Models.Character child, List<string>? created, Imperium.Api.EconomyStateService? econState = null)
{
    void Mark(string label)
    {
        if (created == null) return;
        if (!created.Contains(label)) created.Add(label);
    }

    bool ownershipAdded = false;
    bool memoriesAdded = false;

    Imperium.Domain.Models.Ownership EnsureOwnership(Guid ownerId, string assetType, string acquisitionType, double confidence, DateTime acquiredAt)
    {
        var ownership = db.Ownerships.FirstOrDefault(o => o.OwnerId == ownerId && o.AssetType == assetType);
        if (ownership == null)
        {
            ownership = new Imperium.Domain.Models.Ownership
            {
                Id = Guid.NewGuid(),
                AssetId = Guid.NewGuid(),
                OwnerId = ownerId,
                OwnerType = "Character",
                AssetType = assetType,
                AcquisitionType = acquisitionType,
                Confidence = confidence,
                IsRecognized = true,
                AcquiredAt = acquiredAt
            };
            db.Ownerships.Add(ownership);
            ownershipAdded = true;
        }
        else
        {
            ownership.OwnerId = ownerId;
            ownership.OwnerType = "Character";
            ownership.AssetType = assetType;
            ownership.AcquisitionType = acquisitionType;
            ownership.Confidence = Math.Max(ownership.Confidence, confidence);
            ownership.IsRecognized = true;
            ownership.AcquiredAt = acquiredAt;
        }
        return ownership;
    }

    var latifundium = EnsureOwnership(mother.Id, "Latifundium", "purchase", 0.92, DateTime.UtcNow.AddYears(-2));
    var forge = EnsureOwnership(father.Id, "Forge", "inheritance", 0.85, DateTime.UtcNow.AddYears(-1));
    var observatory = EnsureOwnership(child.Id, "Observatory", "creation", 0.88, DateTime.UtcNow.AddMonths(-10));

    Imperium.Domain.Models.NpcMemory UpsertMemory(Guid characterId, IEnumerable<Guid> known, IEnumerable<Guid> lost, double greed, double attachment)
    {
        var memory = db.NpcMemories.FirstOrDefault(m => m.CharacterId == characterId);
        if (memory == null)
        {
            memory = new Imperium.Domain.Models.NpcMemory
            {
                Id = Guid.NewGuid(),
                CharacterId = characterId
            };
            db.NpcMemories.Add(memory);
            memoriesAdded = true;
        }

        memory.KnownAssets = (known ?? Array.Empty<Guid>()).ToList();
        memory.LostAssets = (lost ?? Array.Empty<Guid>()).ToList();
        memory.Greed = greed;
        memory.Attachment = attachment;
        memory.LastUpdated = DateTime.UtcNow;
        return memory;
    }

    UpsertMemory(mother.Id, new[] { latifundium.AssetId }, Array.Empty<Guid>(), 0.45, 0.7);
    UpsertMemory(father.Id, new[] { forge.AssetId }, new[] { latifundium.AssetId }, 0.6, 0.55);
    UpsertMemory(child.Id, new[] { observatory.AssetId }, Array.Empty<Guid>(), 0.3, 0.68);

    // Ensure dev characters/household have some baseline inventories for staple goods
    void EnsureInventory(Guid ownerId, string item, decimal qty)
    {
        var inv = db.Inventories.FirstOrDefault(i => i.OwnerId == ownerId && i.Item == item);
        if (inv == null)
        {
            db.Inventories.Add(new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ownerId, OwnerType = "character", Item = item, Quantity = qty });
        }
        else
        {
            inv.Quantity = Math.Max(inv.Quantity, qty);
        }
    }

    // Determine staple items dynamically from EconomyStateService
    econState ??= new Imperium.Api.EconomyStateService();
    var staples = new[] { "grain", "wine", "oil" }.Where(s => econState.GetItems().Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();
    if (staples.Length == 0)
    {
        // fallback default staples
        staples = new[] { "grain", "wine", "oil" };
    }
    // seed staple quantities with modest distribution
    if (staples.Contains("grain")) { EnsureInventory(mother.Id, "grain", 120m); EnsureInventory(father.Id, "grain", 80m); EnsureInventory(child.Id, "grain", 60m); }
    if (staples.Contains("wine")) EnsureInventory(mother.Id, "wine", 80m);
    if (staples.Contains("oil")) EnsureInventory(father.Id, "oil", 80m);

    if (ownershipAdded) Mark("ownerships");
    if (memoriesAdded) Mark("npcmemories");
}

static List<Guid> ParseGuidList(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return new List<Guid>();
    }

    try
    {
        var parsed = JsonSerializer.Deserialize<List<Guid>>(json);
        return parsed?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
    }
    catch
    {
        return new List<Guid>();
    }
}

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
