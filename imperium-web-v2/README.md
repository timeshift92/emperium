
# Imperium Web v2 (React + Vite + Tailwind + Zustand)

Живой интерфейс Imperium с мок-потоком событий (вместо SignalR), панелью NPC-ответов и античным стилем.

## 🚀 Запуск
```bash
npm install
npm run dev
```
Открой `http://localhost:5173`

## 🔌 Позже: подключение к .NET 9 (SignalR)
- замените `src/services/mockStream.ts` на SignalR-клиент
- вызывайте `connection.on("GameEvent", ...)` и прокидывайте события в `useWorldStore`
