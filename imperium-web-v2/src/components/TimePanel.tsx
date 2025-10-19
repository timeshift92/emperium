
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
export default function TimePanel() {
  return (
    <Card className="shadow-md">
      <CardHeader><CardTitle>🕰 Время и Погода</CardTitle></CardHeader>
      <CardContent className="text-sm">
        <p>Лето, 287 год до н.э. — День 34, полдень</p>
        <p>Погода: ясно, южный ветер, +26°C</p>
        <div className="mt-2 flex gap-2">
          <button className="px-3 py-1 rounded bg-amber-700 text-white text-xs">×1</button>
          <button className="px-3 py-1 rounded border border-amber-700 text-amber-800 text-xs">×2</button>
          <button className="px-3 py-1 rounded border border-amber-700 text-amber-800 text-xs">×5</button>
          <button className="px-3 py-1 rounded border border-amber-700 text-amber-800 text-xs">Пауза</button>
        </div>
      </CardContent>
    </Card>
  )
}
