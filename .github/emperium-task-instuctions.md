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
 - [x] Household (глава, члены, имущество, обязательства) — базовая модель/эндпоинты/панель: `Household`, `Families`, `HouseholdsPanel`
 дава- [x] InheritanceRecord (правила наследования, спор) — базовая модель, DbSet и dev-эндпоинты реализованы (лист/создать/resolve-dev)
 
 > Примечание: сейчас `ResolveInheritanceDev` выполняет лишь безопасную операцию — равномерное распределение `resolution` и запись `GameEvent`. Следующие шаги: реализовать перенос богатства/Ownership (транзакционно), добавить юнит‑/интеграционные тесты для эндпоинтов и зафиксировать traceId в `GameEvent.PayloadJson` для устойчивой корреляции.
 - [x] Genealogy (родство, семейные ветви) — `GenealogyRecord` (DbSet), API `/api/characters/{id}/genealogy` и UI `NpcProfiles` поддерживают вывод
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
 - [x] Юнит/интеграционные тесты: добавлены тесты на устойчивость агентов при падении LLM (in-memory SQLite shared)

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
  - `RoleLlmRouter`: добавлена генерация per-request traceId и лог-скоуп; traceId виден в логах EF Core / HTTP / агентов.
  - `NpcAgent`: строгие JSON-ответы, уменьшено число reask, таймауты и парсинг `characterId/essence/skills/history`.
  - `NpcAgent`: исправлены ошибки компиляции, LLM-вызовы обёрнуты per-agent таймаутом; длинные EF-вызовы больше не получают per-agent CancellationToken.
  - Автоматическая миграция БД при старте (`db.Database.Migrate()`), расширение `Character`.
  - Эндпоинты для персонажей, событий, дев-операций; SSE `/api/events/stream`.
  - `Relationship` + `RelationshipAI`: формирование связей, события `marriage`, `betrayal`, `child_birth`.
  - `EventDispatcherService`: унифицированный канал для записи/стрима `GameEvent` (фикс двойной регистрации).
  - LLM fallback: при сбоях Ollama/OpenAI `RoleLlmRouter` корректно логирует ошибку и использует `MockLlmClient` (тесты и runtime подтверждают).
  - Тесты: добавлен интеграционный тест, использующий in-memory SQLite (shared connection) для имитации EF Core поведения; dotnet test проходит (2/2 в текущей сессии).

- **Frontend**
  - `NpcProfiles.tsx`: список, профиль, live‑timeline (SSE + авто-реконнект).
  - Обновлённый дашборд: боковая панель статуса мира, вкладки, EventsList с фильтрами и поиском, автообновление погоды/экономики.

- **Dev / Tooling**
  - PowerShell-скрипты `smoke-test.ps1`, `check-apis.ps1`.
  - Документация по запуску API в foreground/background, helper шаги.

## Примечания / риски
- LLM иногда возвращает латиницу — `NpcAgent` смягчён, но нужно дальнейшее улучшение промптов.
- Высокоуровневые модели (KnowledgeWave, DeepHistory, TalentAI и пр.) ещё не реализованы — ожидают следующего этапа.
 - В logging включены scopes (IncludeScopes=true) в `Program.cs`, поэтому traceId из `RoleLlmRouter` может быть виден рядом с EF Core и HTTP логами; это упрощает корреляцию.
 - При медленной/неудовлетворительной работе локального Ollama возможны частые fallback-ы — это ожидаемое поведение при выбранном таймауте; при необходимости можно увеличить таймаут или добавить retry-политику.

## Что дальше рекомендую
1. Прогнать `POST /api/dev/seed-characters`, `POST /api/dev/tick-now`, затем `GET /api/events?type=npc_reply&count=50` — убедиться, что RelationshipAI и NpcAI генерируют события.
2. Расширить unit/integration тесты (Prompts.cs, `RoleLlmRouter`, `NpcAgent`) и добавить репорты покрытия.
3. Добавить запись `meta.traceId` в `GameEvent.PayloadJson` (очень маленькое изменение) чтобы облегчить поиск событий, связанных с конкретным LLM-вызовом; могу сделать патч и запустить тест/сервер чтобы показать результат.
4. Расширить сид (Sicilia Seed) и добавить первые панели экономики/метрик на фронтенде.

---

## 14) Экономика: торговля, запасы, сделки — итеративный план

Цель: перейти от «снимка цен» к живому рынку с запасами, заявками и сделками. Реализуем по этапам, чтобы быстро увидеть движение.

- [ ] E1. Fast‑track рынок на событиях (без миграций)
  - GameEvent: `order_placed`, `trade_executed`, `inventory_changed` (payload с ownerId, locationId, item, qty, price)
  - EconomyAgent: простой матчинг buy/sell по локациям и item; учёт остатков в памяти процесса + эмиссия событий
  - Эндпоинты просмотра: `/api/economy/orders`, `/api/economy/trades` (чтение из GameEvents)
  - UI: секция в EconomyPanel — стакан заявок и последние сделки
- [x] E2. Полноценные модели и миграции
  - Модели: `Inventory`, `MarketOrder` (buy/sell, price, qty, remaining, status), `Trade`
  - Миграции + репозитории, транзакционный матчинг
  - Перенос логики из E1 (события остаются как аудит)
- [x] E3. Производство и потребление
  - `ProductionAgent`: начисление ресурсов по локациям/домохозяйствам (урожайность зависит от сезона/погоды)
  - `ConsumptionAgent`: базовое потребление домохозяйств; дефицит → статусы/события, рост спроса
- [x] E4. Логистика
  - `LogisticsAgent`: перевозки между локациями (стоимость/время, базовые риски), перемещение Inventory
  - Поддержка `transport_job` → выполнение → `transport_completed`
- [x] E5. Цена как производная рынка
  - «Снимок цен» = средневзвешенная цена последних сделок + mid(best bid/ask) по локациям
- [x] E6. Метрики/наблюдаемость
  - Счётчики `economy.orders`, `economy.trades`, оборот, latency матчинга; экспорт в `/api/metrics`

### Выполнено дополнительно в E2–E6
- [x] Резервирование средств/товара и возвраты при истечении/отмене (DELETE /api/economy/orders/{id})
- [x] Казна локаций и пошлины (1%) на сделки
- [x] Household‑учёт при размещении ордеров (OwnerType=household)
- [x] UI: инвентарь персонажа, агрегаты запасов по локациям (EconomyPanel)
- [x] SQLite fix: перенос сортировки decimal на клиент в `/api/economy/inventory`

## 16) Character Focus (новый раздел UI)
- [x] API: `/api/characters/{id}/relationships`, `/api/characters/{id}/communications`
- [x] UI: вкладка «Фокус» — генеалогия (2–3 уровня), коммуникации, мини‑список связей (топ by |trust|+|love|+|hostility|)

## 17) Следующие шаги
- [ ] Household UI: инвентарь семьи, создание/отмена ордеров от household
- [ ] Character Focus+: мини‑граф связей и фильтр коммуникаций по собеседнику
- [ ] Заказ/отмена ордеров с фронта (POST/DELETE), показ TTL/пошлины
- [ ] Treasury/Logistics: очередь и резерв бюджета, конфиг матрицы расстояний
- [ ] Тесты: резервы и возвраты, treasury/fees, logistics costs; Focus endpoints

## 15) Наследование: завершение цикла
- [x] Базовая `InheritanceRecord` и dev‑resolve
- [ ] Транзакционный перенос богатства и владений (сервис `InheritanceService`):
  - Перенос `Ownership` и (опционально) распределение `Household.Wealth` между наследниками
  - Аудит‑события: `inheritance_transfer`, `inheritance_wealth_transfer`
- [ ] Интеграция с `OwnershipAgent`: автосоздание записей при `AcquisitionType == "inheritance"`
- [ ] Тесты: интеграционные кейсы на несколько наследников и распределение активов (round‑robin/equal_split)

## 16) Пол персонажа: поведенческое влияние
- [x] Добавлен `Character.Gender` (API + сидер + UI)
- [ ] Миграция БД (выполнить командами EF)
- [ ] Уточнение промптов NpcAI: стиль речи/тоновое смещение
- [ ] RelationshipAI: мягкие модификаторы доверия/любви (без жёстких ограничений)
- [ ] Фильтр по полу в списке персонажей (UI)

## 17) Улучшения UI/UX
- [x] Вкладка «Наследование», события и переходы к профилям
- [x] Лента событий: быстрые фильтры наследования, фильтр по персонажу
- [x] Сайдбар: метрики NPC реакций/конфликтов/наследования/решений
- [ ] EconomyPanel: стакан заявок и последние сделки (E1)
- [ ] Панель запаса/инвентаря по локациям/владельцам

## 18) Метрики и устойчивость
- [ ] Больше счётчиков: `npc.replies`, `orders.active`, `orders.filled`, `trades.24h`
- [ ] Пер‑агентные таймауты/ретраи как политики (конфигурируемые)
- [ ] Трассировка traceId → запись в `GameEvent.PayloadJson.meta.traceId`

---

## Предлагаемый порядок работ (итерации)
1) E1 Fast‑track рынок на событиях (минимум миграций, быстрый визуальный результат)
2) Транзакционный `InheritanceService` (перенос богатства/владений) + тесты
3) EconomyPanel — стакан + сделки; эндпоинты просмотра
4) E2 полноценные модели рынка + миграции
5) E3 производство/потребление; затем E4 логистика

Если ок — стартую с E1 (fast‑track рынок на GameEvent) и покажу первые сделки и стакан заявок в UI.
