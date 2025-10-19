
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

Удачи! 👑

## Docs и вклад
- CONTRIBUTING: https://github.com/timeshift92/emperium/blob/main/CONTRIBUTING.md
- Архитектура: https://github.com/timeshift92/emperium/blob/main/docs/architecture.md
