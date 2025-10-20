import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type InheritanceRecord = {
  id: string;
  deceasedId: string;
  heirsJson: string;
  rulesJson: string;
  createdAt: string;
  resolutionJson?: string | null;
};

type Character = { id: string; name: string };

function parseGuidArray(json: string | null | undefined): string[] {
  if (!json) return [];
  try {
    const arr = JSON.parse(json);
    if (Array.isArray(arr)) return arr.map(String);
  } catch {}
  return [];
}

export default function InheritancePanel({ className, onSelectCharacter }: { className?: string; onSelectCharacter?: (id: string) => void }) {
  const [records, setRecords] = useState<InheritanceRecord[]>([]);
  const [characters, setCharacters] = useState<Character[]>([]);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  useEffect(() => {
    let cancel = false;
    const load = async () => {
      setLoading(true);
      try {
        const [r1, r2] = await Promise.all([
          fetch("/api/inheritance-records"),
          fetch("/api/characters"),
        ]);
        const recs = (await r1.json()) as InheritanceRecord[];
        const chars = (await r2.json()) as Character[];
        if (!cancel) {
          setRecords(recs);
          setCharacters(chars);
        }
      } catch {
        if (!cancel) setRecords([]);
      } finally {
        if (!cancel) setLoading(false);
      }
    };
    load();
    return () => {
      cancel = true;
    };
  }, []);

  const nameOf = (id: string) => characters.find((c) => c.id === id)?.name ?? id.slice(0, 8);

  const items = useMemo(() => records, [records]);

  const resolveOne = async (id: string) => {
    setBusyId(id);
    try {
      const res = await fetch(`/api/dev/resolve-inheritance/${id}`, { method: "POST" });
      if (!res.ok) throw new Error("resolve failed");
      // reload list after resolve
      const recs = (await (await fetch("/api/inheritance-records")).json()) as InheritanceRecord[];
      setRecords(recs);
    } catch {
      // ignore
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className={cn("space-y-4", className)}>
      <div className="text-lg font-semibold text-slate-900">Наследование</div>
      {loading ? (
        <div className="text-sm text-slate-500">Загрузка...</div>
      ) : items.length === 0 ? (
        <div className="text-sm text-slate-500">Записей нет.</div>
      ) : (
        <ul className="space-y-3">
          {items.map((r) => {
            const heirs = parseGuidArray(r.heirsJson);
            const resolved = !!r.resolutionJson && r.resolutionJson.length > 2;
            return (
              <li key={r.id} className="rounded-md border border-slate-200 bg-white p-3 shadow-sm">
                <div className="flex items-center justify-between">
                  <div className="text-sm font-medium text-slate-900">
                    {new Date(r.createdAt).toLocaleString()} — умер {" "}
                    <button
                      type="button"
                      onClick={() => onSelectCharacter?.(r.deceasedId)}
                      className="underline decoration-slate-300 underline-offset-2 hover:text-slate-900 hover:decoration-slate-400"
                    >
                      {nameOf(r.deceasedId)}
                    </button>
                  </div>
                  <div className="text-xs text-slate-500">#{r.id.slice(0, 8)}</div>
                </div>
                <div className="mt-2 text-sm text-slate-700">
                  Наследники:{" "}
                  {heirs.length > 0 ? (
                    <span className="inline-flex flex-wrap gap-2">
                      {heirs.map((hid) => (
                        <button
                          key={hid}
                          type="button"
                          onClick={() => onSelectCharacter?.(hid)}
                          className="rounded-full border border-slate-200 bg-white px-2 py-0.5 text-xs text-slate-700 shadow-sm hover:border-slate-300 hover:bg-slate-50"
                        >
                          {nameOf(hid)}
                        </button>
                      ))}
                    </span>
                  ) : (
                    "—"
                  )}
                </div>
                <div className="mt-2 flex items-center gap-3">
                  <span className={cn("text-xs", resolved ? "text-emerald-600" : "text-slate-500")}>{resolved ? "Разрешено" : "Ожидает разрешения"}</span>
                  {!resolved && import.meta.env.DEV && (
                    <Button size="sm" onClick={() => resolveOne(r.id)} disabled={busyId === r.id}>
                      {busyId === r.id ? "Обработка..." : "Разрешить (dev)"}
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
