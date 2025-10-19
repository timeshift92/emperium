# Архитектура Imperium

Документ на основе `.github/.copilot-instructions.md` — краткое описание слоёв и циклов симуляции.

## Слои
- `Imperium.Api` — REST API, эндпоинты, `TickWorker`.
- `Imperium.Domain` — доменные модели и бизнес-логика (Character, Family, Location, EconomySnapshot, WeatherSnapshot, GameEvent).
- `Imperium.Infrastructure` — EF Core, SQLite, миграции и сидеры.
- `Imperium.Llm` — клиент для взаимодействия с LLM, управление промптами и валидация JSON-ответов.
- `Imperium.AppHost` / `ServiceDefaults` — оркестрация, конфигурация служб (Aspire).

## Цикл тикa (каждые 30s)
1. WorldAI — обновление погоды и климатических эффектов.
2. EconomyAI — пересчет цен, налогов, запасов.
3. NpcAI — поведение NPC и их решения.
4. ConflictAI / CultureAI — генерация событий.
5. EventDispatcher — запись `GameEvent` и отправка в UI.

## Контракты LLM
- Все ответы LLM — структурированные JSON объекты. Пример формата для погодных снимков:

```json
{
  "condition": "sunny | rain | storm | drought | fog",
  "temperatureC": 28,
  "windKph": 12,
  "precipitationMm": 0
}
```

## Основные сущности
- `Character` — имя, возраст, статус, навыки.
- `Family` — имущество, наследники.
- `Location` — города и деревни.
- `EconomySnapshot` — цены и налоги.
- `WeatherSnapshot` — состояние погоды.
- `GameEvent` — запись событий.

## Рекомендации разработчикам
- Писать чистую, модульную логику в `Imperium.Domain`.
- Валидировать JSON-ответы LLM перед применением.
- Логировать все `GameEvent` и хранить в БД для реплея и отладки.

---
Файл сгенерирован автоматически. При изменениях синхронизируйте с `.github/.copilot-instructions.md`.

## OpenAI API key (локальная и CI-настройка)

Imperium читает OpenAI-ключ из конфигурации в следующем порядке:

1) `OpenAI:ApiKey` (поддерживает `dotnet user-secrets` и `appsettings.json`)
2) `OPENAI_API_KEY` в конфигурации (например, `launchSettings.json`)
3) системная переменная окружения `OPENAI_API_KEY`

Рекомендованные способы задать ключ локально (PowerShell):

- Временно для текущей сессии:

```powershell
$env:OPENAI_API_KEY = 'sk-...'
```

- Постоянно для пользователя (PowerShell, записывает в пользовательские переменные окружения):

```powershell
[Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-...', 'User')
```

- Безопасно через `dotnet user-secrets` (рекомендуется для разработки, не коммитится в репозиторий):

```powershell
cd src/Imperium.Api
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

- Для отладчика VS/launchSettings (не храните секреты в VCS):

Добавьте в `Properties/launchSettings.json` секцию окружения под соответствующим профилем:

```json
"environmentVariables": {
  "ASPNETCORE_ENVIRONMENT": "Development",
  "OPENAI_API_KEY": "sk-..."
}
```

CI/CD (GitHub Actions) — пример секрета и шага:

```yaml
env:
  OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}

# или в шаге:
- name: Run API tests
  env:
    OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
  run: dotnet test ./tests/...
```

Безопасность:
- Никогда не коммитьте реальные ключи в репозиторий.
- Для локальной разработки предпочтительнее `dotnet user-secrets` или временная переменная окружения.

