using System.Text.Json;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using System;
using Microsoft.EntityFrameworkCore;
using Imperium.Llm;

namespace Imperium.Api;

/// <summary>
/// Инициализация Первого Мира Imperium I Эпохи.
/// Запускается при пустой базе данных и создаёт базовый мир, ресурсы, племена и хронику.
/// </summary>
public static class WorldGenesisService
{
    public static async Task InitializeAsync(ImperiumDbContext db, IServiceProvider services, CancellationToken ct = default)
    {
        if (await db.WorldTimes.AnyAsync(ct))
            return; // мир уже создан

        Console.WriteLine("[WorldGenesis] Создание Первого Мира...");

        // 1️⃣ Базовое время
        var worldTime = new WorldTime { Tick = 0, Day = 1, Year = 1, Hour = 6, IsDaytime = true };
        db.WorldTimes.Add(worldTime);

        // 2️⃣ Климат и сезоны
        db.SeasonStates.Add(new SeasonState
        {
            CurrentSeason = "spring",
            AverageTemperatureC = 18,
            AveragePrecipitationMm = 1.2,
            DurationTicks = 2880
        });

        // 3️⃣ География (локации)
        var locations = new[]
        {
            new Location { Id = Guid.NewGuid(), Name = "Сиракузы", Latitude = 37.06, Longitude = 15.29, Culture = "hellenic", Population = 540 },
            new Location { Id = Guid.NewGuid(), Name = "Мессана", Latitude = 38.19, Longitude = 15.55, Culture = "hellenic", Population = 320 },
            new Location { Id = Guid.NewGuid(), Name = "Граста", Latitude = 37.8, Longitude = 16.2, Culture = "tribal", Population = 210 },
            new Location { Id = Guid.NewGuid(), Name = "Каганья", Latitude = 38.3, Longitude = 16.7, Culture = "tribal", Population = 190 },
            new Location { Id = Guid.NewGuid(), Name = "Камтана", Latitude = 36.9, Longitude = 17.0, Culture = "tribal", Population = 150 },
            new Location { Id = Guid.NewGuid(), Name = "Кетамикос", Latitude = 37.3, Longitude = 15.0, Culture = "tribal", Population = 170 },
            new Location { Id = Guid.NewGuid(), Name = "Внила", Latitude = 36.8, Longitude = 14.6, Culture = "tribal", Population = 120 },
            new Location { Id = Guid.NewGuid(), Name = "Троста", Latitude = 37.1, Longitude = 16.5, Culture = "tribal", Population = 160 },
            new Location { Id = Guid.NewGuid(), Name = "Visca", Latitude = 37.5, Longitude = 15.8, Culture = "tribal", Population = 200 },
        };
        db.Locations.AddRange(locations);

        // 4️⃣ Экономика и ресурсы
        var resources = new Dictionary<string, object>
        {
            ["wood"] = 5000,
            ["fish"] = 3000,
            ["grain"] = 2500,
            ["iron"] = 400,
            ["stone"] = 1000,
            ["salt"] = 200
        };
        var fauna = new Dictionary<string, object>
        {
            ["deer"] = 120,
            ["wolf"] = 25,
            ["boar"] = 70,
            ["bear"] = 8
        };
        db.EconomySnapshots.Add(new EconomySnapshot
        {
            Timestamp = DateTime.UtcNow,
            ResourcesJson = JsonSerializer.Serialize(resources),
            PricesJson = JsonSerializer.Serialize(new { wood = 1.0, fish = 1.3, grain = 0.9, iron = 3.5, stone = 1.5 }),
            TaxesJson = JsonSerializer.Serialize(new { market = 0.05 }),
            Treasury = 1000m
        });
        db.WeatherSnapshots.Add(new WeatherSnapshot
        {
            Timestamp = DateTime.UtcNow,
            TemperatureC = 20,
            WindKph = 3,
            PrecipitationMm = 0.5,
            DayLengthHours = 13
        });

        // 5️⃣ Первые фракции
        var factions = new[]
        {
            new Faction { Name = "Дом Сиракуз", Type = "city_state" },
            new Faction { Name = "Племя Каганьи", Type = "tribe" },
            new Faction { Name = "Племя Грасты", Type = "tribe" },
            new Faction { Name = "Племя Камтаны", Type = "tribe" },
        };
        db.Factions.AddRange(factions);

        // 6️⃣ Первые персонажи (вожди и жрецы)
        var characters = new[]
        {
            new Character { Id = Guid.NewGuid(), Name = "Архонт Филомах", Gender = "male", LocationId = locations[0].Id },
            new Character { Id = Guid.NewGuid(), Name = "Вождь Карет из Каганьи", Gender = "male", LocationId = locations[3].Id },
            new Character { Id = Guid.NewGuid(), Name = "Жрица Илария из Грасты", Gender = "female", LocationId = locations[2].Id },
            new Character { Id = Guid.NewGuid(), Name = "Старейшина Дракон из Камтаны", Gender = "male", LocationId = locations[4].Id },
        };
        db.Characters.AddRange(characters);

        // 7️⃣ Сущности персонажей
        db.NpcEssences.AddRange(new[]
        {
            new NpcEssence { CharacterId = characters[0].Id, Strength = 6, Intelligence = 8, Charisma = 9, Vitality = 7, Luck = 5, MutationChance = 0.02 },
            new NpcEssence { CharacterId = characters[1].Id, Strength = 8, Intelligence = 6, Charisma = 7, Vitality = 8, Luck = 4, MutationChance = 0.03 },
            new NpcEssence { CharacterId = characters[2].Id, Strength = 4, Intelligence = 9, Charisma = 8, Vitality = 6, Luck = 6, MutationChance = 0.01 },
            new NpcEssence { CharacterId = characters[3].Id, Strength = 7, Intelligence = 7, Charisma = 6, Vitality = 8, Luck = 5, MutationChance = 0.02 },
        });

        // 8️⃣ Первые здания
        db.Buildings.AddRange(new[]
        {
            new Building { LocationId = locations[0].Id, Kind = "acropolis" },
            new Building { LocationId = locations[3].Id, Kind = "tribal_hall" },
            new Building { LocationId = locations[2].Id, Kind = "temple" },
        });

        // 9️⃣ Первые знания
        db.KnowledgeFields.AddRange(new[]
        {
            new KnowledgeField { Name = "Астрономия" },
            new KnowledgeField { Name = "Кузнечное дело" },
            new KnowledgeField { Name = "Навигация" },
        });

        // 🔟 Хроника начала мира
        // Try to generate mythic text via LLM if available
        string chronicleSummary;
        try
        {
            var llm = services.GetService<ILlmClient>();
            if (llm != null)
            {
                var prompt = "Generate a short mythic chronicle of the founding of the first era in Russian, max 120 chars. Return JSON: {summary}";
                var raw = await llm.SendPromptAsync(prompt, ct);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        var json = JsonDocument.Parse(raw);
                        if (json.RootElement.TryGetProperty("summary", out var el)) chronicleSummary = el.GetString() ?? "Начало I Эпохи.";
                        else chronicleSummary = raw.Trim();
                    }
                    catch
                    {
                        chronicleSummary = raw.Trim();
                    }
                }
                else chronicleSummary = "Начало I Эпохи: первые поселения выросли на берегах морей.";
            }
            else chronicleSummary = "Начало I Эпохи: первые поселения выросли на берегах морей.";
        }
        catch
        {
            chronicleSummary = "Начало I Эпохи: первые поселения выросли на берегах морей.";
        }

        db.WorldChronicles.Add(new WorldChronicle
        {
            Year = 1,
            Summary = chronicleSummary
        });

        // 11️⃣ Первые слухи
        db.Rumors.AddRange(new[]
        {
            new Rumor { Content = "Говорят, море хранит память о древних богах, утонувших при сотворении мира..." },
            new Rumor { Content = "В Грасте найден источник, вода которого светится ночью." }
        });

        // 💾 Сохранение
        await db.SaveChangesAsync(ct);
        Console.WriteLine("[WorldGenesis] Мир Imperium I Эпохи успешно создан!");
    }
}
