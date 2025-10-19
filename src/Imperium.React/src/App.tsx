import EventsList from "@/components/EventsList";
import WeatherCard from "@/components/WeatherCard";
import EconomyPanel from "@/components/EconomyPanel";
import NpcReplies from "@/components/NpcReplies";

function App() {
  return (
    <div className="min-h-screen p-8 bg-gray-50 text-black">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-3xl font-bold">Imperium — Dashboard</h1>
        <div className="flex gap-2">
          <button className="px-3 py-1 border rounded" onClick={() => fetch('/api/dev/seed-characters', { method: 'POST' })}>Seed</button>
          <button className="px-3 py-1 border rounded" onClick={() => fetch('/api/dev/tick-now', { method: 'POST' })}>Tick</button>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-4">
          <div className="h-[40vh] overflow-auto"><WeatherCard /></div>
          <div className="h-[40vh] overflow-auto"><NpcReplies /></div>
        </div>
        <div className="space-y-4">
          <div className="h-[80vh] overflow-auto"><EventsList /></div>
          <div className="mt-2"><EconomyPanel /></div>
        </div>
      </div>
    </div>
  );
}

export default App;