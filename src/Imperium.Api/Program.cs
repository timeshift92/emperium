
using Imperium.ServiceDefaults;
using Imperium.Domain;
using Microsoft.EntityFrameworkCore;
using Imperium.Llm;

var builder = WebApplication.CreateBuilder(args).AddServiceDefaults();

// Config
var cs = builder.Configuration.GetConnectionString("Default") ?? "Data Source=./data/imperium.db";
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

// DB
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(cs));

// LLM
var apiKey = builder.Configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    Console.WriteLine("⚠️ Warning: OPENAI_API_KEY not set, LLM calls will fail.");
builder.Services.AddHttpClient();
builder.Services.AddTransient<ILlmClient>(sp => {
    var http = sp.GetRequiredService<HttpClient>();
    var model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";
    return new OpenAiLlmClient(http, apiKey ?? "", model);
});

// Domain services
builder.Services.AddSingleton<IEconomyService, EconomyService>();
builder.Services.AddSingleton<IDecreeService, DecreeService>();
builder.Services.AddSingleton<INpcService, NpcService>();

// Tick worker
builder.Services.AddHostedService<TickWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    if (!db.Npcs.Any())
    {
        db.Npcs.AddRange(
            new Npc { Name = "Гай", Role = "peasant", Loyalty = 0.1, Influence = 0.1 },
            new Npc { Name = "Квинт", Role = "advisor", Loyalty = 0.3, Influence = 0.5 },
            new Npc { Name = "Луций", Role = "general", Loyalty = 0.2, Influence = 0.6 }
        );
        db.SaveChanges();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

// Endpoints
app.MapGet("/api/economy/latest", (AppDb db) =>
{
    var e = db.Economy.OrderByDescending(x => x.Id).FirstOrDefault();
    return Results.Ok(e);
});

app.MapGet("/api/events", (AppDb db, int take = 50) =>
    Results.Ok(db.Events.OrderByDescending(e => e.Id).Take(take).ToList()));

app.MapGet("/api/decrees", (AppDb db) =>
    Results.Ok(db.Decrees.OrderByDescending(d => d.Id).ToList()));

app.MapPost("/api/decrees", async (AppDb db, IDecreeService svc, DecreeDto dto, CancellationToken ct) =>
{
    var d = await svc.CreateAsync(db, dto.Title, dto.Content, ct);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/decrees/{d.Id}", d);
});

app.Run();

record DecreeDto(string Title, string Content);

// Tick worker
public class TickWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TickWorker> _log;
    private readonly TimeSpan _period;
    public TickWorker(IServiceProvider sp, IConfiguration cfg, ILogger<TickWorker> log)
    {
        _sp = sp; _log = log;
        _period = TimeSpan.FromSeconds(cfg.GetValue<int>("Tick:PeriodSeconds", 30));
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("TickWorker started with period {sec}s", _period.TotalSeconds);
        while (!ct.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var econ = scope.ServiceProvider.GetRequiredService<IEconomyService>();
                var dec = scope.ServiceProvider.GetRequiredService<IDecreeService>();
                var npc = scope.ServiceProvider.GetRequiredService<INpcService>();

                econ.UpdateEconomy(db);
                await dec.ApplyActiveAsync(db, ct);
                foreach (var n in npc.SelectActive(db, 3))
                    await npc.ProcessNpcAsync(db, n, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Tick error");
            }
            var delay = _period - (DateTime.UtcNow - started);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        }
    }
}
