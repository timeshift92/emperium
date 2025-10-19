# 🧱 Задачи для Copilot — Imperium Universe (v4)

## 0) Основы проекта
- [x] Создать решения: Api/Domain/Infrastructure/Llm/AppHost/ServiceDefaults (Aspire)
- [ ] Подключить EF Core SQLite, миграции, сидеры (`InitialWorldSeeder`)
- [x] Реализовать `TickWorker` с порядком фаз (см. инструкции)

## 1) Модели и контекст
- [ ] WorldTime, SeasonState, WeatherSnapshot
- [ ] Location (с TradeRoutesJson), Faction, Army
- [ ] Character, Family, Relationship
- [ ] NpcEssence (характеристики/таланты/черты/наследие)
- [ ] TalentDevelopment (skills/experience/specializations/aptitude)
- [ ] Ownership, NpcMemory
- [ ] EconomySnapshot, Building
- [ ] Technology, KnowledgeField, Discovery, KnowledgeWave, RegionalKnowledge
- [ ] Rumor, Deity
- [ ] WorldChronicle, GameEvent

## 2) Агенты (минимальный набор для запуска симуляции)
- [x] TimeAI — ход времени, события суток/года
- [x] WorldAI — погода/климат/длина дня
- [x] SeasonAI — определение сезона по климату (min/max ticks, mismatch-правила)
- [ ] NatureAI — урожай, фауна, болезни
- [x] EconomyAI — ресурсы/цены/налоги
- [x] NpcAI — короткие реплики (<=35 слов), контекст дня/погоды/настроения
- [x] EventDispatcher — запись в GameEvent

## 3) Человек и навыки
- [ ] GeneticAI — наследие, мутации на рождении
- [ ] TalentAI — активация врождённых склонностей
- [ ] TraitAI — аномалии/болезни/условия
- [ ] SkillAI — рост/деградация навыков, специализации, институты обучения
- [ ] PerceptionAI — реакция общества на черты/таланты
- [ ] FreeWillAI/MoralAI — принятие решений на основе морали и контекста

## 4) Знания и технологии
- [ ] KnowledgeAI — прогресс по областям знаний
- [ ] InnovationAI — генерация технологий/открытий
- [ ] KnowledgeDiffusionAI — волны знаний через торговлю/гонцов/монахов/молву
- [ ] ScholarAI — учителя/ученики, школы ремёсел и мысли

## 5) Вера/Культура/История
- [ ] FaithAI — пантеоны/ритуалы/праздники
- [ ] MythosAI — социально-реальные эффекты сильной веры
- [ ] DreamAI — сны, пророчества
- [ ] CultureAI — праздники/искусство/легенды
- [ ] HistoryAI — годовые хроники
- [ ] DeepHistoryAI — память эпох
- [ ] PhilosophyAI — школы мысли, влияние на право и мораль
- [ ] ZeitgeistAI — дух времени (оптимизм↔упадок)

## 6) Власть/Право/Собственность
- [ ] EmpireAI — геополитика, войны, вассалы, налоги
- [ ] PoliticsAI — фракции, выборы, заговоры
- [ ] ConflictAI — мятежи, битвы, кризисы
- [ ] LegalAI v2 — региональные кодексы, прецеденты, реформы
- [ ] OwnershipAI/PropertyAI — владение активами, конфискации, рынок земли

## 7) Экономика и логистика
- [ ] CraftAI — производство, износ предметов
- [ ] MarketAI — спрос/предложение, ценовые колебания
- [ ] TransportAI — доставка/караваны/флот, влияние войн и погоды

## 8) Промпты (Prompts.cs) — строго JSON
- [ ] Prompts.WeatherSnapshot → { condition, temperatureC, windKph, precipitationMm, dayLength }
- [x] Prompts.NpcReply → { reply, moodDelta?, loyaltyDelta? }
- [ ] Prompts.KnowledgeDiscovery → { name, field, effect, impact }
- [ ] Prompts.MythosEffect → { omen, crowdEffect, duration }
- [ ] Prompts.Dream → { symbol, meaning, action }
- [ ] Prompts.HistorySummary → { summary, highlights[] }
- [ ] Prompts.LegalDecision → { verdict, penalty, precedentNote }

## 9) MVP-валидатор
- [ ] Запуск симуляции на 1000 тиков без ошибок
- [ ] В БД есть: смены дня/года, погода, сезон, базовая экономика
- [ ] События: discovery, festival, rumor, battle, legal_verdict, season_change, day_change, year_change
- [ ] Навыки NPC меняются в зависимости от деятельности
- [ ] В хронике есть годовой summary

## 10) Нефункциональные требования
- [ ] Комментарии и PR — только на русском
- [ ] LLM-ответы — только компактный JSON
- [ ] Домены изолированы: логика в `Domain`, API только маршрутизирует
- [ ] Все агенты записывают `GameEvent` и не блокируют тик
