# ⚙️ 05. Архитектура AI-симуляции

## 🧱 Технологический стек
- .NET 9 (Minimal API), C# 13  
- EF Core + SQLite  
- Aspire-стиль: Api / Domain / Infrastructure / Llm / AppHost

## 🔁 Tick-Loop (порядок фаз)
1) TimeAI → 2) WorldAI → 3) SeasonAI → 4) NatureAI → 5) EconomyAI →  
6) Empire/Politics/Conflict → 7) Ownership/Property →  
8) Genetic/Talent/Trait/Skill/Perception → 9) Relationship/Family/Society →  
10) Knowledge/Innovation/Diffusion/Scholar → 11) Faith/Mythos/Philosophy/Zeitgeist →  
12) Culture/History/DeepHistory → 13) Npc/Inner/Moral/Social/Reputation → 14) EventDispatcher

## 🧩 Контракт агента
```csharp
public interface IWorldAgent
{
    string Name { get; }
    Task TickAsync(AppDb db, ILlmClient llm, CancellationToken ct);
}
```

## 📦 Ключевые модели
WorldTime, SeasonState, WeatherSnapshot, Location, Faction, Army, Character, Family, Relationship,  
NpcEssence, TalentDevelopment, Ownership, NpcMemory, EconomySnapshot, Technology, KnowledgeWave, RegionalKnowledge,  
Rumor, Deity, Building, WorldChronicle, GameEvent.

## 🤖 LLM-промпты (только JSON)
- WeatherSnapshot → `{ condition, temperatureC, windKph, precipitationMm, dayLength }`  
- NpcReply → `{ reply, moodDelta?, loyaltyDelta? }`  
- KnowledgeDiscovery → `{ name, field, effect, impact }`  
- MythosEffect → `{ omen, crowdEffect, duration }`  
- Dream → `{ symbol, meaning, action }`  
- HistorySummary → `{ summary, highlights[] }`  
- LegalDecision → `{ verdict, penalty, precedentNote }`


## ⚙️ Распределение моделей Imperium

Ниже показано, как разные LLM-модели взаимодействуют внутри симуляции Imperium.

![Схема распределения моделей Imperium](./A_flowchart_diagram_in_SVG_format_illustrates_the_.png)

> Каждый слой использует собственную модель Ollama или OpenAI:
> - WorldAI / SeasonAI — `mistral`
> - EconomyAI / ConflictAI — `llama3:8b`
> - CouncilAI / CultureAI — `gemma2:9b`
> - NpcAI — `phi3:medium`