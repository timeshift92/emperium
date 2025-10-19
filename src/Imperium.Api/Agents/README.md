Содержит реализации IWorldAgent (TimeAgent, WeatherAgent и в будущем другие агенты).

Правила:
- Имя класса агента реализует IWorldAgent и имеет свойство Name, используемое TickWorker для порядка исполнения.
- Все агенты должны принимать IServiceProvider в TickAsync чтобы получать scoped зависимости (DbContext и т.д.).

Файлы:
- TimeAgent.cs
- WeatherAgent.cs
