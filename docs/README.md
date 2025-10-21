# 📖 Imperium — Книга живого мира (Design Bible v1)

> **Кредо:** *«Мир Imperium создан не для победы, а для понимания.»*

Этот репозиторий содержит главы книги мира *Imperium*: философию проекта, логику живой симуляции, архитектуру AI и техническое ТЗ.

## 📂 Структура
- `Cover.png` — обложка (A4)
- `Imperium_Book_Index.md` — оглавление
- `Imperium_Licence_and_CreatorNote.md` — лицензия и подпись
- `Chapters/*` — главы книги в расширенном Markdown
 - `Inheritance_Distribution.md` — описание правил распределения наследства (wealth и assets)
 - `08_Economy_Items_and_Definitions.md` — справочник динамических товаров и API для управления ними
 - `05_AI_Architecture.md` — архитектура агентов и новый time model (месяцы/дни)
 - `CivilizationGenesisService` — раннее создание городов и экономика (добавлен в 05)

Новое: `TimeAI` публикует `month` и `dayOfMonth` в `time_tick`; есть dev endpoints для прогонки тика `tick-now` и `tick-time` (TimeAI-only).

## 🛠 Сборка PDF
Рекомендуется использовать `pandoc`:
```bash
pandoc -s Imperium_Book_Index.md -o Imperium_Design_Bible_v1.pdf --from markdown --pdf-engine=xelatex
```
Для красивого стиля можно использовать шаблон `eisvogel` (опционально).

## 🧠 Для Copilot
- Вся документация на русском.
- Следуй логике мира и архитектуре из глав 2 и 5.
- При генерации кода и промптов LLM — **только структурированный JSON**.
- Учитывай систему пола: `Character.Gender`, подсказки в `NpcAI`, конфиг `RelationshipModifiers.GenderBias`, фильтр `/api/characters?gender=...`.
- Оперативные метрики:
  - Prometheus-скрейп по `/metrics` с гистограммами тиков (`imperium_tick_duration_ms`), агентов (`imperium_agent_duration_ms`) и LLM (`imperium_llm_duration_ms`), а также счётчиками `imperium_*`.
  - REST-срез `/api/metrics/ticks` возвращает последние длительности тиков и используется фронтом для спарклайна.
- Реальные события стримятся через SignalR `/hubs/events`; при недоступности хаба UI автоматически переключается на мок-стрим (SSE эндпоинты остаются для совместимости).

Changelog: добавлены `CivilizationGenesisService` и интеграционные тесты на идемпотентность и валидацию экономических маршрутов.
