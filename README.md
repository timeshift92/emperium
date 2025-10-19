
# Imperium Aspire Skeleton (MVP)

**Состав:**
- `Imperium.Api` — ASP.NET 8 Minimal API (Swagger, TickWorker)
- `Imperium.Domain` — модели и сервисы (Economy/Decrees/NPC)
- `Imperium.Infrastructure` — EF Core + SQLite
- `Imperium.Llm` — простой OpenAI клиент (Chat Completions)
- `Imperium.AppHost`, `Imperium.ServiceDefaults` — скелет для оркестрации (Aspire-стиль)

**Запуск (локально):**
1) Установи .NET 8 SDK
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
```json
{ "title": "Зерновой налог", "content": "Ввести налог 10% на зерно..." }
```

**Примечания:**
- В SQLite база создаётся автоматически в `./data/imperium.db`.
- LLM-клиент использует модель `gpt-4o-mini` (можно сменить в `appsettings.json`).

Удачи! 👑
