import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";

type Props = { className?: string; characterId: string };

type Character = { id: string; name: string; status?: string; gender?: string; money?: number; locationName?: string };
type Genealogy = {
  id: string; characterId: string;
  fatherId?: string | null; father?: { id: string; name?: string } | null;
  motherId?: string | null; mother?: { id: string; name?: string } | null;
  spouses: { id: string; details?: { id: string; name?: string } | null }[];
  children: { id: string; details?: { id: string; name?: string } | null }[];
};

type Relationship = { id: string; otherId: string; other?: { id: string; name?: string } | null; type: string; trust: number; love: number; hostility: number };

type RawEvent = { id?: string; Id?: string; type?: string; Type?: string; timestamp?: string; Timestamp?: string; payloadJson?: string; PayloadJson?: string };

export default function CharacterFocus({ className, characterId }: Props) {
  const [ch, setCh] = useState<Character | null>(null);
  const [g, setG] = useState<Genealogy | null>(null);
  const [rels, setRels] = useState<Relationship[]>([]);
  const [comms, setComms] = useState<RawEvent[]>([]);
  const [_loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      try {
        const [cRes, gRes, rRes, cmRes] = await Promise.all([
          fetch(`/api/characters/${characterId}`),
          fetch(`/api/characters/${characterId}/genealogy`),
          fetch(`/api/characters/${characterId}/relationships`),
          fetch(`/api/characters/${characterId}/communications?count=50`),
        ]);
        if (!cancelled) {
          if (cRes.ok) setCh(await cRes.json()); else setCh(null);
          setG(gRes.ok ? await gRes.json() : null);
          setRels(rRes.ok ? await rRes.json() : []);
          setComms(cmRes.ok ? await cmRes.json() : []);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [characterId]);

  const relTop = useMemo(() => {
    return [...rels]
      .map(r => ({...r, score: Math.abs(r.trust) + Math.abs(r.love) + Math.abs(r.hostility)}))
      .sort((a,b)=> b.score - a.score)
      .slice(0, 10);
  }, [rels]);

  const genealogyNode = (label: string, id?: string | null, details?: { id: string; name?: string } | null) => (
    <div className="flex flex-col items-center gap-1">
      <div className="rounded-full border border-slate-300 bg-white px-3 py-1 text-xs text-slate-700 shadow-sm">{label}</div>
      {id ? (
        <button
          type="button"
          onClick={() => window.dispatchEvent(new CustomEvent("imperium:focus-character", { detail: { id } }))}
          className="rounded border border-slate-200 bg-white px-2 py-1 text-xs text-slate-700 hover:bg-slate-50"
        >
          {(details?.name ?? id).toString()}
        </button>
      ) : (
        <div className="text-xs text-slate-400">—</div>
      )}
    </div>
  );

  return (
    <div className={cn("h-full overflow-auto rounded-xl border border-slate-200 bg-white/70 p-4", className)}>
      {!ch ? (
        <div className="text-sm text-slate-500">Загрузка...</div>
      ) : (
        <>
          <div className="mb-4 flex items-center justify-between">
            <div>
              <div className="text-xs uppercase tracking-wide text-slate-500">Фокус персонажа</div>
              <div className="text-2xl font-semibold text-slate-900">{ch.name}</div>
              <div className="text-sm text-slate-600">Статус: {ch.status ?? "—"} · Пол: {ch.gender ?? "—"} · Кошелёк: {ch.money ?? 0} ¤ · Место: {ch.locationName ?? "—"}</div>
            </div>
          </div>

          {/* Genealogy */}
          <div className="mb-6 rounded-lg border border-slate-200 bg-white/70 p-3">
            <div className="mb-2 text-sm font-semibold text-slate-700">Родственные связи (2–3 уровня)</div>
            {!g ? (
              <div className="text-sm text-slate-500">Нет данных</div>
            ) : (
              <div className="grid gap-3">
                <div className="flex justify-center gap-8">
                  {genealogyNode("Отец", g.fatherId ?? undefined, (g.father as any) ?? undefined)}
                  {genealogyNode("Мать", g.motherId ?? undefined, (g.mother as any) ?? undefined)}
                </div>
                <div className="flex justify-center">
                  <div className="rounded border border-slate-300 bg-slate-50 px-3 py-1 text-sm">{ch.name}</div>
                </div>
                <div className="flex flex-wrap items-start justify-center gap-4">
                  <div>
                    <div className="text-xs uppercase tracking-wide text-slate-400">Супруг(а)</div>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {g.spouses.length ? g.spouses.map((s) => (
                        genealogyNode("Супруг", s.id, (s.details as any) ?? undefined)
                      )) : <div className="text-xs text-slate-400">—</div>}
                    </div>
                  </div>
                  <div>
                    <div className="text-xs uppercase tracking-wide text-slate-400">Дети</div>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {g.children.length ? g.children.map((c) => (
                        genealogyNode("Ребёнок", c.id, (c.details as any) ?? undefined)
                      )) : <div className="text-xs text-slate-400">—</div>}
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Communications */}
          <div className="mb-6 rounded-lg border border-slate-200 bg-white/70 p-3">
            <div className="mb-2 text-sm font-semibold text-slate-700">Коммуникации (последние)</div>
            {comms.length === 0 ? (
              <div className="text-sm text-slate-500">Нет коммуникаций</div>
            ) : (
              <ul className="space-y-1 text-xs">
                {comms.slice(0, 20).map((e, i) => (
                  <li key={e.id ?? e.Id ?? i} className="flex items-center justify-between rounded border border-slate-200 bg-white/80 px-2 py-1">
                    <span className="text-slate-700">{String(e.type ?? e.Type)} · {(e.timestamp ?? e.Timestamp) ? new Date(e.timestamp ?? (e as any).Timestamp).toLocaleString() : "—"}</span>
                    <button className="text-slate-500 underline-offset-2 hover:underline" onClick={() => alert(e.payloadJson ?? (e as any).PayloadJson ?? "")}>payload</button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* Relations mini-graph (top by score) */}
          <div className="rounded-lg border border-slate-200 bg-white/70 p-3">
            <div className="mb-2 text-sm font-semibold text-slate-700">Связи (топ)</div>
            {relTop.length === 0 ? (
              <div className="text-sm text-slate-500">Нет записей</div>
            ) : (
              <ul className="grid gap-1 md:grid-cols-2">
                {relTop.map((r) => (
                  <li key={r.id} className="flex items-center justify-between rounded border border-slate-200 bg-white/80 px-2 py-1 text-sm">
                    <span className="text-slate-700">{(r.other?.name as string) ?? r.otherId.slice(0,8)} <span className="text-xs text-slate-500">{r.type}</span></span>
                    <span className="text-xs text-slate-500">t:{r.trust} · l:{r.love} · h:{r.hostility}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </div>
  );
}

