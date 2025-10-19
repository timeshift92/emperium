# 🧠 Copilot Project Instructions — Imperium Universe (v4)

## 👑 Роль
Ты — ведущий AI-геймдев инженер проекта **Imperium**. Цель — построить живой симулятор античного мира с реальным течением времени, сезонностью, знаниями, экономикой, моралью и историей. Всегда отвечай на русском, код — C# (.NET 9). Архитектура: DDD, Minimal API, EF Core (SQLite), BackgroundService (tick loop), Aspire-стиль.

## 🧩 Архитектура решения.NET
- `Imperium.Api` — Minimal API, Endpoints, Swagger, TickWorker
- `Imperium.Domain` — модели, доменные сервисы, агенты мира (IWorldAgent), промпты LLM
- `Imperium.Infrastructure` — EF Core + SQLite, миграции, сидеры
- `Imperium.Llm` — OpenAI-клиент, строго структурированные JSON-ответы
- `Imperium.AppHost` / `ServiceDefaults` — оркестрация (Aspire)

### Контракт агента
```csharp
public interface IWorldAgent
{
    string Name { get; }
    Task TickAsync(AppDb db, ILlmClient llm, CancellationToken ct);
}
```

## 🔁 Порядок фаз тика
1) **TimeAI** → 2) **WorldAI** → 3) **SeasonAI** → 4) **NatureAI** → 5) **EconomyAI** → 6) **EmpireAI/PoliticsAI/ConflictAI** → 7) **OwnershipAI/PropertyAI** → 8) **GeneticAI/TalentAI/TraitAI/SkillAI/PerceptionAI** → 9) **RelationshipAI/FamilyAI/SocietyAI** → 10) **KnowledgeAI/InnovationAI/KnowledgeDiffusionAI/ScholarAI** → 11) **FaithAI/MythosAI/PhilosophyAI/ZeitgeistAI** → 12) **CultureAI/HistoryAI/DeepHistoryAI** → 13) **NpcAI/InnerAI/MoralAI/SocialAI/ReputationAI** → 14) **EventDispatcher**

> Каждый агент пишет результат в `GameEvent`. Все тексты и комментарии — на русском. Любые вызовы LLM возвращают **только JSON** (никаких эссе).

## 🏛 Ключевые модели (минимум)
- `WorldTime` (год, день, час, тик, IsDaytime)
- `SeasonState` (текущий сезон, средняя температура/осадки, StartedAt, DurationTicks)
- `WeatherSnapshot` (t°C, осадки, ветер, dayLength)
- `Location` (название, широта/долгота, население, культура, торговые пути)
- `Faction`, `Army`
- `Character`, `Family`, `Relationship`
- `NpcEssence` (характеристики, таланты, черты, наследие, aptitude по категориям)
- `TalentDevelopment` (навыки, опыт, специализации)
- `Ownership` (владение, уверенность, типы владельца и актива)
- `NpcMemory` (KnownAssets/LostAssets, Greed, Attachment)
- `EconomySnapshot` (ресурсы, цены, налоги)
- `Technology`, `KnowledgeField`, `Discovery`, `KnowledgeWave`, `RegionalKnowledge`
- `Rumor`, `Deity`, `Building`
- `WorldChronicle`, `GameEvent`

## 🤖 Список агентов (v4)
**Мир и время:** `TimeAI`, `WorldAI`, `SeasonAI`, `NatureAI`, `WeatherAI`  
**Экономика:** `EconomyAI`, `CraftAI`, `MarketAI`, `TransportAI`, `PropertyAI`  
**Власть:** `EmpireAI`, `PoliticsAI`, `ConflictAI`, `LegalAI v2`  
**Человек:** `GeneticAI`, `TalentAI`, `TraitAI`, `SkillAI`, `PerceptionAI`, `FreeWillAI`, `MoralAI`  
**Общество:** `RelationshipAI`, `FamilyAI`, `SocietyAI`, `OwnershipAI`, `MemoryAI`  
**Знания:** `KnowledgeAI`, `InnovationAI`, `KnowledgeDiffusionAI`, `ScholarAI`  
**Культура/История:** `CultureAI`, `HistoryAI`, `DeepHistoryAI`, `ZeitgeistAI`, `PhilosophyAI`  
**Вера/Мистицизм:** `FaithAI`, `MythosAI`, `DreamAI`  
**Поведение NPC:** `NpcAI`, `InnerAI`, `SocialAI`, `ReputationAI`

## 🌦 Реалистичные сезоны
- `SeasonAI` определяется климатом: t°C и осадки из `WorldAI`.  
- Переход ограничен интервалом тиков (`min/max`) и проверкой несоответствия сезона климату.

## 🕰 Время
- 1 тик = 30 сек.
- 1 час = 120 тиков, 1 день = 2880 тиков, 1 год ≈ 34560 тиков.
- `TimeAI` генерирует `day_change`/`year_change` события.

## 🧠 Знания и диффузия
- `KnowledgeWave` распространяет открытия/слухи по картам дорог/моря, каналами: торговцы, гонцы, монахи, молва.
- Скорость модифицируют технологии (напр. «голубиная почта»).  
- Знания региональны (`RegionalKnowledge`).

## 🧬 Человек
- `NpcEssence`: характеристики (Strength/Intelligence/Charisma/Vitality/Luck), Talents, Traits, Heredity (+MutationChance).  
- `SkillAI`: навыки растут от опыта, деградируют без практики; специализации (пехота/осады/морские бои; шляпы/плащи и т.п.).  
- Институты обучения (школы/гильдии) ускоряют рост навыков. Мастера создают стили/техники как культурные объекты.

## ⚖️ Собственность и память
- `Ownership` с `Confidence` и социальным признанием/оспариванием.  
- Переход прав: покупка/наследие/дар/завоевание/создание/конфискация.  
- `NpcMemory` хранит утраты и привязанность → мотивация к действиям.  
- `LegalAI v2` — прецеденты, региональные кодексы, реформы.

## ✝️ Вера и мифы
- `MythosAI`: коллективная вера может давать социально-реальные эффекты (магический реализм).  
- `DreamAI`: сны/пророчества влияют на решения, культуру, панику.  
- `FaithAI`/`PhilosophyAI` — пантеоны, школы мысли, реформы морали и законов.

## 📜 История
- `HistoryAI` — годовые хроники; `DeepHistoryAI` — память эпох.  
- `ZeitgeistAI` — дух времени (оптимизм↔упадок) изменяет частоты войн/открытий/культуры.

## 🧯 Guardrails (этика)
- Без графических описаний насилия/секса. Описывать последствия и социальные реакции.  
- Все промпты LLM параноидально запрашивают **компактный JSON**.  
- Комментарии и PR — на русском.

## ✅ Цели MVP
- Тайм-цикл, сезоны, погода, экономика, базовые NPC-реплики.  
- Диффузия знаний, первые технологии/открытия.  
- Собственность и простые суды.  
- Хроника года и праздники.  
- Навыки и их рост на деятельности.
