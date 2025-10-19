import { useEffect, useState } from "react";

export default function WeatherCard() {
  const [snap, setSnap] = useState<any>(null);

  useEffect(() => {
    fetch('/api/weather/latest')
      .then(r => r.json())
      .then(setSnap)
      .catch(() => setSnap(null));
  }, []);

  if (!snap) return <div className="p-3">Погода: нет данных</div>;

  return (
    <div className="p-3 border rounded">
      <div className="text-xs text-muted-foreground">{new Date(snap.timestamp || Date.now()).toLocaleString()}</div>
      <div className="text-lg font-semibold">{snap.condition} — {snap.temperatureC}°C</div>
      <div className="text-sm">Осадки: {snap.precipitationMm} мм, Ветер: {snap.windKph} kph</div>
    </div>
  );
}
