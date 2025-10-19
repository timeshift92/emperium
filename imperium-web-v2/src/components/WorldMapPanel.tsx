
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card"
export default function WorldMapPanel() {
  return (
    <Card className="w-full h-[90vh] bg-[url('https://images.unsplash.com/photo-1527153907022-465ee4752fdc?q=80&w=1200&auto=format&fit=crop')] bg-cover bg-center">
      <CardHeader><CardTitle>🗺 Мир Imperium</CardTitle></CardHeader>
      <CardContent>
        <p className="text-sm italic opacity-70">Сиракузы и окрестности. Кликните на объекты (в будущем).</p>
        <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
          <div className="bg-white/70 p-2 rounded">Город: Сиракузы</div>
          <div className="bg-white/70 p-2 rounded">Деревни: 3</div>
          <div className="bg-white/70 p-2 rounded">Флот: 2 триремы</div>
          <div className="bg-white/70 p-2 rounded">Храмы: 1</div>
        </div>
      </CardContent>
    </Card>
  )
}
