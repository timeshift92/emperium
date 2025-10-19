import { useEffect, useState } from "react";

export default function NpcReplies() {
  const [items, setItems] = useState<any[]>([]);

  useEffect(() => {
    fetch('/api/events?type=npc_reply&count=20')
      .then(r => r.json())
      .then(setItems)
      .catch(() => setItems([]));
  }, []);

  return (
    <div className="p-3">
      <h3 className="text-lg mb-2">NPC ответы</h3>
      <ul className="space-y-2">
        {items.map((it:any) => {
          let payload = null;
          try { payload = JSON.parse(it.payloadJson); } catch { payload = it.payloadJson; }
          return (
            <li key={it.id} className="p-2 border rounded">
              <div className="text-sm text-muted-foreground">{new Date(it.timestamp).toLocaleString()}</div>
              <div className="font-medium">{payload?.name}</div>
              <div className="text-sm mt-1">{payload?.reply}</div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
