
# Imperium Aspire Skeleton (MVP)

**Состав:**
- `Imperium.Api` — ASP.NET 9 Minimal API (Swagger, TickWorker)
- `Imperium.Domain` — модели и сервисы (Economy/Decrees/NPC)
- `Imperium.Infrastructure` — EF Core + SQLite
- `Imperium.Llm` — простой OpenAI клиент (Chat Completions)
- `Imperium.AppHost`, `Imperium.ServiceDefaults` — скелет для оркестрации (Aspire-стиль)

**Запуск (локально):**
1) Установи .NET 9 SDK
2) Экспортируй ключ: `export OPENAI_API_KEY=sk-...`
3) `dotnet build`
4) `dotnet run --project src/Imperium.Api`  
   Swagger: http://localhost:5186/swagger (порт зависит от dev-среды)

**Docker:**
```
OPENAI_API_KEY=sk-... docker compose up --build
# затем: http://localhost:8080/swagger
```

**Эндпоинты:**
- `GET /api/economy/latest`
- `GET /api/events`
- `GET /api/decrees`
- `POST /api/decrees` body:
- `GET /api/weather/latest`
 - `GET /api/economy/items` — список имён товаров
 - `POST /api/economy/items` — добавить массив имён товаров
 - `GET /api/economy/item-defs` — получить определения (метаданные) всех товаров
 - `GET /api/economy/item-defs/{name}` — получить определение товара по имени
 - `POST /api/economy/item-defs` — создать/обновить определение товара (JSON в теле запроса)
- `GET /metrics` — Prometheus-совместимые метрики (OpenTelemetry)
- `SignalR /hubs/events` — поток `GameEvent`/`WeatherSnapshot` в реальном времени
 - Dev endpoints: `POST /api/dev/tick-now?advanceTime=true|false` — прогнать все агенты (опционально Advance WorldTime), `POST /api/dev/tick-time` — прогон только TimeAI
    
Weather POST will be added to allow manual override in future updates.
```json
{ "title": "Зерновой налог", "content": "Ввести налог 10% на зерно..." }
```

**Примечания:**
- В SQLite база создаётся автоматически в `./data/imperium.db`.
- LLM-клиент использует модель `gpt-4o-mini` (можно сменить в `appsettings.json`).

Новый агент: Weather/World AI генерирует погодные снимки каждую эпоху (тик) и записывает `WeatherSnapshot` в БД.
Дополнительно реализованы простые стабы агентов:
- CouncilAI — даёт совет по указам и записывает `council_advice` в `Events`.
- ConflictAI — оценивает риск конфликтов и пишет `conflict_warning`.
- CultureAI — генерирует культурные события `culture`.

Примеры промптов:
 - Weather: "Generate compact JSON: {condition, temperatureC, windKph, precipitationMm} for an ancient Mediterranean city."
 - Council: "Advise on current tax policy given treasury X and tax rate Y. Provide concise recommendation." 
 - Conflict: "Assess revolt risk based on avg loyalty L and treasury T. Return short assessment." 

Экономика: товары и метаданные
- Экономика теперь поддерживает динамические товары. У каждого товара есть определение с полями Name, BasePrice, Unit, ConsumptionPerTick и Tags.
- Используйте эндпоинты `/api/economy/item-defs` для просмотра и управления метаданными товаров. Агенты (Production, Consumption, Economy) используют эти определения в реальном времени.

Удачи! 👑

Dev endpoints (для разработки)
 - POST /api/dev/tick-now?advanceTime=true
    - Выполняет один full tick — вызывает зарегистрированные IWorldAgent в порядке Time→World→Season→…
    - Параметр advanceTime=true дополнительно продвигает WorldTime (TimeAI).
    - Возвращает объект с текущим `worldTime` и краткой статистикой тика.
    Пример ответа:
    ```json
    {
       "worldTime": { "tick": 1234, "hour": 10, "day": 5, "year": 1, "isDaytime": true, "month": 2, "dayOfMonth": 14 },
       "eventsPublished": 12
    }
    ```

 - POST /api/dev/tick-time
    - Прогон только TimeAI (без остальных агентов). Возвращает `worldTime`.

Использование:
 - Легко триггерить тик с помощью скриптов или UI (например, кнопка в `Imperium.React`).
 - В тестах удобно выставлять `advanceTime=true` для синхронных проверок событий, связанных с сменой дня/месяца.

## Docs и вклад
- CONTRIBUTING: https://github.com/timeshift92/emperium/blob/main/CONTRIBUTING.md
- Архитектура: https://github.com/timeshift92/emperium/blob/main/docs/architecture.md
