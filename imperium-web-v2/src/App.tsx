
import WorldMapPanel from './components/WorldMapPanel'
import CouncilPanel from './components/CouncilPanel'
import EventFeed from './components/EventFeed'
import TimePanel from './components/TimePanel'
import CharacterPanel from './components/CharacterPanel'
import EconomyPanel from './components/EconomyPanel'
import NpcPanel from './components/NpcPanel'
import { startMockStream } from './services/mockStream'

startMockStream()

export default function App() {
  return (
    <div className="min-h-screen text-zinc-800 p-6 grid grid-cols-12 gap-4">
      <div className="col-span-4"><WorldMapPanel /></div>
      <div className="col-span-4 flex flex-col gap-4">
        <CouncilPanel />
        <NpcPanel />
        <EventFeed />
      </div>
      <div className="col-span-4 flex flex-col gap-4">
        <CharacterPanel />
        <TimePanel />
        <EconomyPanel />
      </div>
    </div>
  )
}
