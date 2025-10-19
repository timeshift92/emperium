ТЗ: Начальный мир (Сиракузы + деревни), сидер, локации и интеграция с AI
🎯 Цель

Добавить в проект Imperium базовую модель локаций (город/деревни у моря), начальные данные для Сиракуз и 2–3 деревень, а также интегрировать локации в экономику/события и AI-агентов. Обеспечить REST-эндпоинты для получения и просмотра локаций.

📦 Точки интеграции проекта

Imperium.Domain — модели, сервисы, сидер

Imperium.Infrastructure — AppDb, миграции/EnsureCreated

Imperium.Api — REST-эндпоинты /api/locations

TickWorker + агенты (WorldAI, EconomyAI, NpcAI) — учитывать локации

1) Модель данных: Location
1.1. Entity: Location

Добавь в Imperium.Domain новый класс:

namespace Imperium.Domain;

public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = "";     // "Syracusae", "Acre", "Gela", "Enna"
    public string Type { get; set; } = "city"; // "city" | "village" | "port" (для MVP: "city"/"village"; указываем флаг прибрежности)
    public bool IsCoastal { get; set; }        // прибрежная ли локация (Сиракузы, Гелла — true)
    public int Population { get; set; }        // ориентирная численность
    public double Wealth { get; set; }         // относительное богатство 0..2 (1.0 — базовое)
    public double Happiness { get; set; }      // настроение  -1..1
    public double Loyalty { get; set; }        // лояльность   -1..1
}

1.2. DbSet и конфигурация

В Imperium.Infrastructure/AppDb.cs:

Добавь public DbSet<Location> Locations => Set<Location>();

Укажи разумные типы/ограничения (строки как TEXT, числовые как REAL/INTEGER).

Важно: мы пока используем EnsureCreated() — миграции опциональны. Если захочешь, добавь миграцию InitialLocations.

1.3. (Опционально) связи

В будущем у Npc можно добавить LocationId (FK). Для MVP — необязательно, но предусмотри в коде упоминание, что NPC могут быть привязаны к локации.

В GameEvent.PayloadJson при генерации событий указывать location (имя/Id) — хотя бы для части событий.

2) Сидер начального мира: InitialWorldSeeder
2.1. Класс сидера

Создай Imperium.Domain/InitialWorldSeeder.cs:

using Microsoft.EntityFrameworkCore;

namespace Imperium.Domain;

public static class InitialWorldSeeder
{
    public static async Task SeedAsync(AppDb db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!await db.Locations.AnyAsync(ct))
        {
            db.Locations.AddRange(
                new Location { Name = "Syracusae", Type = "city", IsCoastal = true,  Population = 20000, Wealth = 1.0, Happiness = 0.1, Loyalty = 0.2 },
                new Location { Name = "Acre",      Type = "village", IsCoastal = false, Population = 3000, Wealth = 0.6, Happiness = 0.0, Loyalty = 0.1 },
                new Location { Name = "Gela",      Type = "village", IsCoastal = true,  Population = 4000, Wealth = 0.8, Happiness = 0.05, Loyalty = 0.1 },
                new Location { Name = "Enna",      Type = "village", IsCoastal = false, Population = 2500, Wealth = 0.5, Happiness = -0.05, Loyalty = 0.0 }
            );
        }

        // При желании — инициализируй несколько NPC, привязав их мысленно к локациям (пока без FK):
        if (!await db.Npcs.AnyAsync(ct))
        {
            db.Npcs.AddRange(
                new Npc { Name = "Гай",   Role = "peasant", Loyalty = 0.1, Influence = 0.1 },
                new Npc { Name = "Квинт", Role = "advisor", Loyalty = 0.3, Influence = 0.5 },
                new Npc { Name = "Луций", Role = "general", Loyalty = 0.2, Influence = 0.6 }
            );
        }

        await db.SaveChangesAsync(ct);
    }
}

2.2. Вызов сидера при старте

В Imperium.Api/Program.cs, внутри блока инициализации БД:

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await InitialWorldSeeder.SeedAsync(db);
}


Примечание: если проект стартует синхронно — сделай SeedAsync(...).GetAwaiter().GetResult();

3) REST-эндпоинты для локаций

В Imperium.Api/Program.cs добавь эндпоинты:

app.MapGet("/api/locations", (AppDb db) =>
    Results.Ok(db.Locations.OrderBy(l => l.Id).ToList()));

app.MapGet("/api/locations/{id:int}", (AppDb db, int id) =>
{
    var loc = db.Locations.FirstOrDefault(l => l.Id == id);
    return loc is null ? Results.NotFound() : Results.Ok(loc);
});


(Опционально) фильтры/пагинация позже.

4) Интеграция локаций в AI и тик-цикл
4.1. WorldAI (погода/урожай/стихии)

При генерации world_event укажи произвольную локацию (случайно выбирай Location) и добавь её в payload_json.

Пример payload:

{ "weather": "засуха", "location": "Acre", "description": "Жара высушила поля Акре." }

4.2. EconomyAI (экономика по локациям)

На каждом тике учитывай суммарное богатство/население локаций для расчёта общей казны/цены зерна.

Простая формула (MVP):

Совокупное богатство = sum(Wealth * Population)

Влияние прибрежных локаций (IsCoastal) слегка снижает волатильность цен (лучше торговля).

Можно записывать в EconomySnapshot «агрегированное» состояние, как и раньше.

4.3. NpcAI (реакции NPC)

Подавай в контекст LLM краткую сводку по локациям (например, 1–2 ключевые метрики) — без увеличения токенов: просто название города и факт («в Акре засуха»).

Часть реплик NPC может ссылаться на локации: “Рынок в Сиракузах оживлён” и т.п.

5) События с привязкой к локациям

Для GameEvent.PayloadJson добавляй поле location там, где событие локальное:

world_event (погода) → всегда локальное

culture_event → может быть общегородским (Syracusae) или «в деревне»

conflict → можно рандомно выбирать локацию конфликта

6) Контрольные значения (для проверки)

После запуска сидера и пары тиков:

В /api/locations должны быть 4 записи:

Syracusae (city, coastal, 20000, wealth=1.0)

Acre (village, inland, 3000, wealth=0.6)

Gela (village, coastal, 4000, wealth=0.8)

Enna (village, inland, 2500, wealth=0.5)

В /api/events периодически появляются world_event/culture_event/conflict с location в payload.

EconomySnapshot продолжает пополняться; поведение цен чуть «мягче», если много прибрежных локаций.

7) Критерии готовности (Acceptance Criteria)

Модель и БД

 Есть Location entity и DbSet<Location> в AppDb

 EnsureCreated() создаёт таблицу locations

 Сидирование добавляет 4 локации (см. состав выше)

API

 GET /api/locations возвращает массив локаций

 GET /api/locations/{id} возвращает конкретную локацию или 404

AI и тик-цикл

 WorldAI генерирует world_event с привязкой к случайной локации

 EconomyAI учитывает агрегаты по локациям в расчётах

 NpcAI включает краткое упоминание локальных условий в контекст (без раздувания токенов)

Стабильность

 Проект стартует без ошибок, Swagger доступен

 При 3–5 тиках появляются корректные события и обновления экономики

 В логах видно “AI Tick: WorldAI/EconomyAI/NpcAI” и отсутствие исключений

8) Пример минимальных правок коду (куда именно)

Imperium.Domain/Location.cs — новая модель

Imperium.Infrastructure/AppDb.cs — DbSet<Location>

Imperium.Domain/InitialWorldSeeder.cs — сидер

Imperium.Api/Program.cs — вызов сидера + новые эндпоинты /api/locations*

Imperium.Domain/Agents.cs — правки WorldAI, EconomyAI, NpcAI для работы с локациями (location в payload, агрегации)

9) Подсказки для промптов LLM (по-русски)

Для world_event:

“Опиши коротко (до 20 слов) погодное событие в локации {name}.”

Для NpcAI:

“Ты — {role}. Отреагируй коротко (до 25 слов) с учётом текущей цены зерна и факта: {locationFact}.”

10) Дальнейшие шаги (после выполнения этого ТЗ)

Добавить FK Npc.LocationId и фильтрацию реакций по локациям

Ввести фактор “сезон” (12 тиков = год), WorldAI меняет сезон, EconomyAI учитывает урожайность по сезонам

Добавить GET /api/locations/{id}/events (пагинация по событиям локации)

Выполни всё строго по ТЗ. Пиши комментарии в коде на русском. Не меняй существующую архитектуру (API ↔ Domain ↔ Infrastructure ↔ Llm).