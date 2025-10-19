
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
export default function EconomyPanel() {
  return (
    <Card className="shadow-md">
      <CardHeader><CardTitle>💰 Экономика</CardTitle></CardHeader>
      <CardContent className="text-sm grid grid-cols-2 gap-2">
        <div className="bg-white/70 p-2 rounded">Казна: 1200 денариев</div>
        <div className="bg-white/70 p-2 rounded">Налог на зерно: 15%</div>
        <div className="bg-white/70 p-2 rounded">Запасы зерна: 78%</div>
        <div className="bg-white/70 p-2 rounded">Цена хлеба: 1.2</div>
      </CardContent>
    </Card>
  )
}
