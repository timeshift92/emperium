
import { useState } from "react"
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
import { Button } from "./ui/button"
import { Textarea } from "./ui/textarea"
import { sendDecree } from "../services/api"

export default function CouncilPanel() {
  const [decree, setDecree] = useState("Снизить налог на зерно в Сиракузах на 10%")
  const [reply, setReply] = useState("")
  const [busy, setBusy] = useState(false)
  const handleSend = async () => {
    if (!decree.trim()) return
    setBusy(true); setReply("📜 Совет обсуждает ваш указ...")
    const res = await sendDecree(decree)
    setBusy(false); setReply(res.message)
  }
  return (
    <Card className="shadow-md">
      <CardHeader><CardTitle>🏛 Совет Империи</CardTitle></CardHeader>
      <CardContent className="flex flex-col gap-2">
        <Textarea value={decree} onChange={e=>setDecree(e.target.value)} />
        <Button onClick={handleSend} disabled={busy}>{busy ? "Отправка..." : "Отправить указ"}</Button>
        {reply && <p className="text-sm italic mt-1">{reply}</p>}
      </CardContent>
    </Card>
  )
}
