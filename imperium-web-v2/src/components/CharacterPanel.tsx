
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
import { Input } from "./ui/input"
import { useState } from "react"
export default function CharacterPanel() {
  const [name, setName] = useState("Луций Марцелл")
  const [origin, setOrigin] = useState("сын крестьянина")
  return (
    <Card className="shadow-md">
      <CardHeader><CardTitle>👤 Персонаж</CardTitle></CardHeader>
      <CardContent className="space-y-2 text-sm">
        <div className="grid grid-cols-3 items-center gap-2"><span>Имя</span><Input className="col-span-2" value={name} onChange={e=>setName(e.target.value)} /></div>
        <div className="grid grid-cols-3 items-center gap-2"><span>Происхождение</span><Input className="col-span-2" value={origin} onChange={e=>setOrigin(e.target.value)} /></div>
        <div><b>Навыки:</b> земледелие, торговля, красноречие</div>
        <div><b>Настроение:</b> довольный ☀️</div>
      </CardContent>
    </Card>
  )
}
