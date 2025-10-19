
import { useWorldStore } from "../store/useWorldStore"
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
export default function EventFeed() {
  const items = useWorldStore(s => s.events)
  return (
    <Card className="flex-1 overflow-auto">
      <CardHeader><CardTitle>📜 Хроника мира</CardTitle></CardHeader>
      <CardContent>
        <ul className="text-sm space-y-2">
          {items.map(e => (
            <li key={e.id} className="border-b border-amber-200 pb-1">
              <span className="opacity-60 mr-2">{new Date(e.payloadJson.at ?? Date.now()).toLocaleTimeString()}</span>
              {e.payloadJson.text ?? e.type}
            </li>
          ))}
          {items.length === 0 && <p className="text-zinc-500 text-sm italic">Событий пока нет…</p>}
        </ul>
      </CardContent>
    </Card>
  )
}
