import { useEffect, useState } from "react";

export default function EconomyPanel() {
  const [snap, setSnap] = useState<any>(null);

  useEffect(() => {
    fetch('/api/events?type=economy_snapshot&count=1')
      .then(r => r.json())
      .then(a => setSnap(a?.[0] ?? null))
      .catch(() => setSnap(null));
  }, []);

    if (!snap) return <div className="p-3">Экономика: нет снимков</div>;

  let payload = null;
  try { payload = JSON.parse(snap.payloadJson); } catch { payload = snap.payloadJson; }

    return (
      <div className="p-3 border rounded">
        <div className="text-xs text-muted-foreground">{new Date(snap.timestamp).toLocaleString()}</div>
        <div className="text-sm font-semibold">Экономический снимок</div>
        <pre className="text-xs mt-2">{JSON.stringify(payload, null, 2)}</pre>
      </div>
    );
}
