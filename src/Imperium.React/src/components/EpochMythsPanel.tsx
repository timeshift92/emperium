import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

export default function EpochMythsPanel({ className }: { className?: string }) {
  const [entries, setEntries] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const r = await fetch('/api/chronicles');
        if (!r.ok) return;
        const data = await r.json();
        if (!cancelled) setEntries(data ?? []);
      } catch {
        // ignore
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <div className={cn("p-4 rounded-xl border bg-white/80 shadow-sm", className)}>
      <h3 className="text-lg font-semibold">Мифы эпох</h3>
      {loading ? (
        <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
      ) : entries.length === 0 ? (
        <div className="mt-2 text-sm text-slate-500">Нет записей</div>
      ) : (
        <ul className="mt-3 space-y-3 text-sm">
          {entries.map((e) => (
            <li key={e.id} className="border p-2 rounded">
              <div className="text-xs text-slate-500">Год {e.year}</div>
              <div className="text-sm text-slate-700 mt-1">{typeof e.summary === 'string' ? e.summary : JSON.stringify(e.summary)}</div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
