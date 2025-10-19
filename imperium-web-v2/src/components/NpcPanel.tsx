
import { useWorldStore } from "../store/useWorldStore"
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
export default function NpcPanel() {
  const npcReplies = useWorldStore(s => s.npcReplies)
  return (
    <Card className="shadow-md">
      <CardHeader><CardTitle>💬 Голоса мира</CardTitle></CardHeader>
      <CardContent className="text-sm space-y-2">
        {npcReplies.slice(0, 6).map((r, i) => (
          <div key={i} className="border-b border-amber-200 pb-1">
            <b>{r.name}:</b> {r.reply}
          </div>
        ))}
        {npcReplies.length === 0 && <p className="text-zinc-500 text-sm italic">Пока никто не говорит…</p>}
      </CardContent>
    </Card>
  )
}
