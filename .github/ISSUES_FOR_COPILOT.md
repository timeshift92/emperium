# 🧱 Задачи для Copilot — Imperium Universe (v4)

## 0) Основы проекта
- [x] Aspire-решение: Api/Domain/Infrastructure/Llm/AppHost/ServiceDefaults
- [x] EF Core SQLite, миграции, сидеры (`db.Database.Migrate()` на старте)
- [x] TickWorker: упорядоченные фазы (Time → Weather/Season → Economy → NPC → Dispatch)
- [ ] Метрики/наблюдаемость: OpenTelemetry/Prometheus, счётчики тиков, LLM-вызовов, длительность фаз

## 1) Модели и контекст
- [x] WorldTime, SeasonState, WeatherSnapshot
- [x] Location, Faction (скелет), Army (скелет)
- [x] Character (+`EssenceJson`, `SkillsJson`, `History`, `LocationName`)
- [x] Relationship (`SourceId`, `TargetId`, `Type`, `Trust`, `Love`, `Hostility`)
- [x] Ownership (базовая), NpcMemory (скелет)
- [x] EconomySnapshot, Building (скелет)
- [ ] Household (глава, члены, имущество, обязательства)
- [ ] InheritanceRecord (правила наследования, спор)
- [ ] Genealogy (родство, семейные ветви)
- [ ] TalentDevelopment (aptitude, специализации, опыт по под‑скиллам)
- [ ] Technology, KnowledgeField, Discovery, KnowledgeWave, RegionalKnowledge
- [ ] Rumor (источник/достоверность/радиус/TTL)
- [ ] Deity, Cult, Ritual, Festival
- [ ] HealthState, Livestock/Fishery/Hunting ресурсы
- [ ] CommRoute, Courier/PigeonPost (маршруты и время доставки)
- [x] WorldChronicle, GameEvent

## 2) Агенты (минимально работоспособный мир)
- [x] TimeAI — ход времени/суток
- [x] WeatherAI/WorldAI — погода, длина дня, аномалии
- [x] SeasonAI — сезон по климату/гео
- [x] EconomyAI — цены, налоги, запасы
- [x] RelationshipAI — marriage/betrayal/child_birth
- [x] NpcAI — короткие реплики (<=35 слов, эпохальный стиль)
- [x] EventDispatcher — запись/стрим `GameEvent`
- [ ] NatureAI — урожай, фауна, эпидемии
- [ ] PropertyAI — переходы имущества, аренда, конфискации
- [ ] InheritanceAI — смерти, завещания, обновление Household/Genealogy
- [ ] KnowledgeAI — прогресс областей знаний, локальные модификаторы
- [ ] InnovationAI — генерация технологий/открытий
- [ ] KnowledgeDiffusionAI — торговые пути/гонцы/голуби/монахи
- [ ] ScholarAI — школы, мастер-подмастерье, влияние на под-навыки
- [ ] CrimeAI/LegalAI v2 — преступления, суд, наказания, прецеденты
- [ ] LogisticsAI — караваны/флот, штормы, пираты
- [ ] MarketAI — спрос/предложение, волатильность
- [ ] CraftAI — производство, брак, износ
- [ ] TransportAI — стоимость/время доставки, риски
- [ ] FreeWillAI/MoralAI — решения на основе морали/репутации
- [ ] PerceptionAI — общественная реакция
- [ ] FaithAI/MythosAI/DreamAI/CultureAI — ритуалы, знамения, сны, праздники
- [ ] HistoryAI/DeepHistoryAI/ZeitgeistAI — хроники, память эпох, дух времени
- [ ] ConflictAI/EmpireAI/PoliticsAI — войны, выборы, заговоры

## 3) Человек, навыки, специализации
- [ ] GeneticAI — наследуемые склонности, мутации при рождении
- [ ] TalentAI — активация врождённых/скрытых талантов
- [ ] TraitAI — состояния, аномалии, болезни
- [ ] SkillAI — рост/деградация, под-дисциплины (пример: воин→тактик/осада/флот)
- [ ] Training/Institutions — обучение у мастеров, стоимость и длительность
- [ ] NpcMemory v2 — долговременная память, обещания/долги, локальные слухи

## 4) Знания и технологии
- [ ] Модели: KnowledgeField, Technology, Discovery, RegionalKnowledge, KnowledgeWave
- [ ] Агенты: KnowledgeAI, InnovationAI, KnowledgeDiffusionAI, ScholarAI
- [ ] Коммуникации: торговцы/гонцы/голубиная почта/монахи, задержки, изоляция регионов

## 5) Вера, культура и история
- [ ] Модели: Deity, Cult, Ritual, Festival
- [ ] FaithAI/MythosAI/CultureAI — ритуалы, знамения, праздники
- [ ] DreamAI — сны как подсказки и сюжетные крючки
- [ ] HistoryAI — годовой summary, WorldChronicle
- [ ] DeepHistoryAI — память эпох
- [ ] ZeitgeistAI — дух времени, влияние на войны/открытия

## 6) Власть, право и собственность
- [ ] OwnershipAI/PropertyAI — рынок земли/домов/скота, аренда, конфискации
- [ ] LegalAI v2 — кодексы, суд, штрафы, ссылки, прецеденты
- [ ] EmpireAI/PoliticsAI/ConflictAI — внешняя и внутренняя динамика

## 7) Экономика и логистика
- [ ] CraftAI / MarketAI / TransportAI / LogisticsAI (продвинутая версия)
- [ ] Узлы торговли (порт/базар/склад), влияние штормов/ветров
- [ ] Рыболовство/охота/скотоводство — сезонные ресурсы

## 8) Промпты (Prompts.cs) — строго JSON
- [x] WeatherSnapshot → { condition, temperatureC, windKph, precipitationMm, dayLength }
- [x] NpcReply → { reply, moodDelta?, loyaltyDelta? }
- [ ] KnowledgeDiscovery → { name, field, effect, impact, region }
- [ ] RumorPrompt → { text, credibility, spreadRadiusKm, ttlTicks }
- [ ] LegalDecision → { verdict, penalty, precedentNote }
- [ ] HistorySummary → { summary, highlights[] }
- [ ] Dream → { symbol, meaning, action }
- [ ] MythosEffect → { omen, crowdEffect, duration }

## 9) Сид начального мира (Sicilia Seed)
- [ ] География: Сиракузы + деревни, порт, храм, мастерские, рыбацкая артель
- [ ] Фракции: совет, ремесленники, торговцы, жрецы, гарнизон
- [ ] Торговые линии: прибрежные рейсы, караваны вглубь острова
- [ ] Начальные знания/технологии: античный baseline
- [ ] Начальные школы/мастера и ученики
- [ ] Репутации и семейные линии для старта

## 10) API / Frontend (MVP Web)
- [x] REST: `/api/characters`, `/api/characters/{id}`, `/api/characters/{id}/events`
- [x] Dev endpoints: `/api/dev/seed-characters`, `/api/dev/reset-characters`, `/api/dev/tick-now`
- [x] SSE `/api/events/stream` (через EventDispatcher)
- [ ] SignalR `/hubs/events` (альтернатива SSE)
- [x] Frontend v2: вкладки “События/Персонажи/Экономика”, фильтры и поиск
- [ ] Панель экономики с живыми метриками (ценовые ряды, запасы, налоги)
- [ ] Панель указов (Council) с решениями EconomyAI/LegalAI
- [ ] Карта с маршрутами/штормами/караванами, кликабельными объектами

## 11) Наблюдаемость, тесты, надёжность
- [ ] OpenTelemetry/Prometheus: durata фаз тика, частота/ошибки LLM
- [ ] Юнит-тесты: NpcAgent, Prompts, RoleLlmRouter, KnowledgeDiffusion
- [ ] Нагрузочный режим “1000 тиков без ошибок” + отчёт
- [ ] Фейковые LLM-клиенты и детерминированные сиды для офлайна

## 12) MVP-валидатор
- [ ] 1000 тиков без ошибок
- [ ] В БД: смены дня/года, сезоны, погода, базовая экономика
- [ ] События: discovery, festival, rumor, battle, legal_verdict, season_change, day_change, year_change
- [ ] Навыки NPC меняются от деятельности/обучения
- [ ] Годовой summary (HistoryAI) в WorldChronicle
- [ ] Игрок умирает → переключение на наследника (Genealogy/Inheritance)

## 13) Нефункциональные требования
- [ ] Комментарии и PR — только на русском
- [ ] LLM — строго компактный JSON
- [ ] Чистая архитектура: Domain содержит правила мира, API — маршрутизация
- [x] Все агенты записывают `GameEvent` и не блокируют тик (через EventDispatcher)

## Сделано (кратко)
- **Backend**
  - `RoleLlmRouter`: роутинг role → model, поддержка Ollama/OpenAI (fallback + логирование).
  - `NpcAgent`: строгие JSON-ответы, уменьшено число reask, таймауты и парсинг `characterId/essence/skills/history`.
  - Автоматическая миграция БД при старте (`db.Database.Migrate()`), расширение `Character`.
  - Эндпоинты для персонажей, событий, дев-операций; SSE `/api/events/stream`.
  - `Relationship` + `RelationshipAI`: формирование связей, события `marriage`, `betrayal`, `child_birth`.
  - `EventDispatcherService`: унифицированный канал для записи/стрима `GameEvent` (фикс двойной регистрации).

- **Frontend**
  - `NpcProfiles.tsx`: список, профиль, live‑timeline (SSE + авто-реконнект).
  - Обновлённый дашборд: боковая панель статуса мира, вкладки, EventsList с фильтрами и поиском, автообновление погоды/экономики.

- **Dev / Tooling**
  - PowerShell-скрипты `smoke-test.ps1`, `check-apis.ps1`.
  - Документация по запуску API в foreground/background, helper шаги.

## Примечания / риски
- LLM иногда возвращает латиницу — `NpcAgent` смягчён, но нужно дальнейшее улучшение промптов.
- Высокоуровневые модели (KnowledgeWave, DeepHistory, TalentAI и пр.) ещё не реализованы — ожидают следующего этапа.

## Что дальше рекомендую
1. Прогнать `POST /api/dev/seed-characters`, `POST /api/dev/tick-now`, затем `GET /api/events?type=npc_reply&count=50` — убедиться, что RelationshipAI и NpcAI генерируют события.
2. Добавить unit-тесты для промптов и агентов (`MockLlmClient`), интегрировать наблюдаемость (Prometheus + OTEL).
3. Расширить сид (Sicilia Seed) и добавить первые панели экономики/метрик на фронтенде.
