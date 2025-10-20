import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";

type Character = {
  id: string;
  name: string;
  age?: number;
  status?: string;
  gender?: string;
  money?: number;
  locationName?: string;
  essence?: string | Record<string, unknown>;
  history?: string;
  skills?: string | Record<string, unknown>;
};

type RawEvent = {
  id?: string;
  Id?: string;
  timestamp?: string;
  Timestamp?: string;
  payloadJson?: string;
  PayloadJson?: string;
  payload?: unknown;
  Payload?: unknown;
};

type ParsedReply = {
  id: string;
  timestamp: string | null;
  reply: string;
  moodDelta: number | null;
  meta?: Record<string, unknown>;
  raw: unknown;
};

type NpcProfilesProps = {
  className?: string;
  refreshVersion?: number;
  focusCharacterId?: string | null;
  onFocusConsumed?: () => void;
};

type RelatedSummary = {
  id: string;
  name?: string;
  status?: string;
  gender?: string;
  locationName?: string;
};

type GenealogyRelation = {
  id: string;
  details?: RelatedSummary | null;
};

type GenealogyResponse = {
  id: string;
  characterId: string;
  fatherId?: string | null;
  father?: RelatedSummary | null;
  motherId?: string | null;
  mother?: RelatedSummary | null;
  spouses: GenealogyRelation[];
  children: GenealogyRelation[];
};

function parseJson<T>(value: unknown): T | null {
  if (typeof value === "string") {
    try {
      return JSON.parse(value) as T;
    } catch {
      return null;
    }
  }
  if (value && typeof value === "object") {
    return value as T;
  }
  return null;
}

function normalizeNpcEvent(ev: RawEvent): ParsedReply {
  const fallbackId = `${ev.timestamp ?? ev.Timestamp ?? Date.now()}-${Math.random()
    .toString(16)
    .slice(2, 8)}`;
  const id = ev.id ?? ev.Id ?? fallbackId;
  const timestamp = ev.timestamp ?? ev.Timestamp ?? null;
  const raw =
    ev.payloadJson ?? ev.PayloadJson ?? ev.payload ?? ev.Payload ?? null;
  let payload: any = raw;
  if (typeof raw === "string") {
    try {
      payload = JSON.parse(raw);
    } catch {
      payload = raw;
    }
  }

  const reply =
    typeof payload === "object" && payload !== null && "reply" in payload
      ? String(payload.reply)
      : typeof payload === "string"
        ? payload
        : "";
  const mood =
    typeof payload === "object" && payload !== null && "moodDelta" in payload
      ? Number(payload.moodDelta)
      : null;

  const meta =
    typeof payload === "object" && payload !== null && "meta" in payload
      ? (payload.meta as Record<string, unknown>)
      : undefined;

  return { id, timestamp, reply, moodDelta: Number.isFinite(mood) ? mood : null, meta, raw: payload };
}

export default function NpcProfiles({
  className,
  refreshVersion = 0,
  focusCharacterId = null,
  onFocusConsumed,
}: NpcProfilesProps) {
  const [chars, setChars] = useState<Character[]>([]);
  const [filter, setFilter] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Character | null>(null);
  const [events, setEvents] = useState<RawEvent[]>([]);
  const [loadingChars, setLoadingChars] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [resetting, setResetting] = useState(false);
  const [genealogy, setGenealogy] = useState<GenealogyResponse | null>(null);
  const [loadingGenealogy, setLoadingGenealogy] = useState(false);
  const [genealogyError, setGenealogyError] = useState<string | null>(null);
  const [inheritance, setInheritance] = useState<any[]>([]);
  const [loadingInheritance, setLoadingInheritance] = useState(false);
  const [inventory, setInventory] = useState<{ item: string; quantity: number }[]>([]);
  const [loadingInventory, setLoadingInventory] = useState(false);
  const [relations, setRelations] = useState<any[]>([]);
  const [loadingRelations, setLoadingRelations] = useState(false);

  const handleSelectRelative = (id?: string | null) => {
    if (!id) return;
    setSelectedId(id);
  };

  const relationChip = (id: string, details?: RelatedSummary | null, key?: string) => {
    const label = details?.name ?? id.slice(0, 8);
    const meta = details?.status ?? details?.locationName ?? null;
    return (
      <button
        key={key ?? id}
        type="button"
        onClick={() => handleSelectRelative(id)}
        className="flex items-center gap-2 rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50"
      >
        <span className="font-semibold">{label}</span>
        {meta ? <span className="text-[10px] text-slate-500">{meta}</span> : null}
      </button>
    );
  };

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoadingChars(true);
      try {
        const res = await fetch("/api/characters");
        if (!res.ok) throw new Error("characters request failed");
        const data = (await res.json()) as Character[];
        if (!cancelled) {
          setChars(data);
        }
      } catch {
        if (!cancelled) {
          setChars([]);
        }
      } finally {
        if (!cancelled) {
          setLoadingChars(false);
        }
      }
    };
    load();
    return () => {
      cancelled = true;
    };
  }, [refreshVersion]);

  useEffect(() => {
    if (!focusCharacterId) return;
    setSelectedId(focusCharacterId);
    onFocusConsumed?.();
  }, [focusCharacterId, onFocusConsumed]);

  const filtered = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return chars;
    return chars.filter((c) => {
      const haystack = `${c.name} ${c.locationName ?? ""}`.toLowerCase();
      return haystack.includes(term);
    });
  }, [chars, filter]);

  useEffect(() => {
    if (filtered.length === 0) {
      setSelectedId(null);
      return;
    }
    if (selectedId && filtered.some((c) => c.id === selectedId)) {
      return;
    }
    setSelectedId(filtered[0].id);
  }, [filtered, selectedId]);

  useEffect(() => {
    let es: EventSource | null = null;
    let closed = false;
    let reconnect = 1000;

    const connect = () => {
      if (closed) return;
      es = new EventSource("/api/events/stream");
      es.onmessage = (ev) => {
        try {
          const parsed = JSON.parse(ev.data) as RawEvent & { Type?: string };
          if (
            parsed?.Type === "npc_reply" &&
            selectedId &&
            typeof parsed.PayloadJson === "string" &&
            parsed.PayloadJson.includes(selectedId)
          ) {
            setEvents((prev) => {
              const exists = prev.some(
                (old) => (old.id ?? old.Id) === (parsed.id ?? parsed.Id),
              );
              if (exists) return prev;
              return [parsed, ...prev].slice(0, 100);
            });
          }
        } catch {
          // ignore malformed messages
        }
      };
      es.onerror = () => {
        try {
          es?.close();
        } catch {
          // ignore
        }
        if (closed) return;
        setTimeout(() => {
          reconnect = Math.min(30_000, reconnect * 1.8);
          connect();
        }, reconnect);
      };
    };

    connect();
    return () => {
      closed = true;
      try {
        es?.close();
      } catch {
        // ignore
      }
    };
  }, [selectedId]);

  useEffect(() => {
    if (!selectedId) {
      setSelected(null);
      setEvents([]);
      setGenealogy(null);
      setGenealogyError(null);
      setInheritance([]);
      return;
    }
    const load = async () => {
      setLoadingEvents(true);
      setLoadingGenealogy(true);
      setLoadingInheritance(true);
      try {
        const [profileRes, eventsRes, genealogyRes, inheritanceRes] = await Promise.all([
          fetch(`/api/characters/${selectedId}`),
          fetch(`/api/characters/${selectedId}/events?count=50`),
          fetch(`/api/characters/${selectedId}/genealogy`),
          fetch(`/api/inheritance-records/by-character/${selectedId}`),
        ]);

        if (profileRes.ok) {
          const profile = (await profileRes.json()) as Character;
          setSelected(profile);
        } else {
          setSelected(null);
        }

        if (eventsRes.ok) {
          const eventData = (await eventsRes.json()) as RawEvent[];
          setEvents(eventData);
        } else {
          setEvents([]);
        }

        if (genealogyRes.status === 404) {
          setGenealogy(null);
          setGenealogyError(null);
        } else if (genealogyRes.ok) {
          const genealogyPayload = (await genealogyRes.json()) as GenealogyResponse;
          setGenealogy(genealogyPayload);
          setGenealogyError(null);
        } else {
          setGenealogy(null);
          setGenealogyError(`Genealogy error ${genealogyRes.status}`);
        }

        if (inheritanceRes.ok) {
          const inh = (await inheritanceRes.json()) as any[];
          setInheritance(inh);
        } else {
          setInheritance([]);
        }

        // load inventory for character
        setLoadingInventory(true);
        try {
          const invRes = await fetch(`/api/economy/inventory?ownerId=${selectedId}&ownerType=character`);
          if (invRes.ok) {
            const inv = (await invRes.json()) as { item?: string; Item?: string; quantity?: number; Quantity?: number }[];
            const list = inv.map((i) => ({ item: (i.item ?? (i as any).Item) as string, quantity: (i.quantity ?? (i as any).Quantity) as number }));
            setInventory(list);
          } else {
            setInventory([]);
          }
        } catch {
          setInventory([]);
        } finally {
          setLoadingInventory(false);
        }

        // load relationships
        setLoadingRelations(true);
        try {
          const relRes = await fetch(`/api/characters/${selectedId}/relationships`);
          if (relRes.ok) {
            const arr = (await relRes.json()) as any[];
            setRelations(arr);
          } else {
            setRelations([]);
          }
        } catch {
          setRelations([]);
        } finally {
          setLoadingRelations(false);
        }
      } catch {
        setSelected(null);
        setEvents([]);
        setGenealogy(null);
        setGenealogyError("Genealogy request failed");
        setInheritance([]);
      } finally {
        setLoadingEvents(false);
        setLoadingGenealogy(false);
        setLoadingInheritance(false);
      }
    };
    load();
  }, [selectedId, refreshVersion]);

  const replies = useMemo(
    () => events.map((ev) => normalizeNpcEvent(ev)),
    [events],
  );

  const essence = useMemo(
    () => parseJson<Record<string, unknown>>(selected?.essence ?? null),
    [selected?.essence],
  );

  const skills = useMemo(() => {
    if (!selected?.skills) return null;
    const parsed = parseJson<unknown>(selected.skills);
    return parsed ?? selected.skills;
  }, [selected?.skills]);

  const handleReset = async () => {
    setResetting(true);
    try {
      await fetch("/api/dev/reset-characters", { method: "POST" });
      setLoadingChars(true);
      const res = await fetch("/api/characters");
      const data = (await res.json()) as Character[];
      setChars(data);
      setSelectedId(null);
      setSelected(null);
      setEvents([]);
      setGenealogy(null);
      setGenealogyError(null);
    } catch {
      // ignore
    } finally {
      setLoadingChars(false);
      setResetting(false);
    }
  };

  return (
    <div
      className={cn(
        "flex h-full min-h-0 overflow-hidden rounded-xl border border-slate-200 bg-white/70 shadow-sm",
        className,
      )}
    >
      <div className="flex h-full w-72 flex-col border-r border-slate-200 bg-white/80">
        <div className="border-b border-slate-200 px-4 py-3">
          <div className="text-sm font-semibold text-slate-900">
            Персонажи
          </div>
          <div className="text-xs text-slate-500">
            {loadingChars ? "Загрузка..." : `${filtered.length} из ${chars.length}`}
          </div>
        </div>
        <div className="px-4 py-3">
          <input
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Поиск по имени или месту"
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 placeholder:text-slate-400 focus:border-slate-400 focus:outline-none focus:ring-2 focus:ring-slate-200"
          />
        </div>
        <div className="flex-1 overflow-y-auto px-2 pb-4">
          {loadingChars ? (
            <div className="px-2 text-sm text-slate-500">Загрузка...</div>
          ) : filtered.length === 0 ? (
            <div className="px-2 text-sm text-slate-500">
              Персонажи не найдены.
            </div>
          ) : (
            <ul className="space-y-1">
              {filtered.map((c) => {
                const isSelected = selectedId === c.id;
                return (
                  <li key={c.id}>
                    <button
                      type="button"
                      onClick={() => setSelectedId(c.id)}
                      className={cn(
                        "w-full rounded-lg border px-3 py-2 text-left text-sm transition",
                        isSelected
                          ? "border-slate-500 bg-slate-100/80 text-slate-900"
                          : "border-transparent bg-white hover:border-slate-200 hover:bg-slate-50",
                      )}
                    >
                      <div className="font-medium">{c.name}</div>
                      <div className="text-xs text-slate-500">
                        {c.locationName ?? "—"} · {c.status ?? "—"}
                      </div>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>

      <div className="flex flex-1 flex-col">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <div className="text-xs uppercase tracking-wide text-slate-500">
              Профиль NPC
            </div>
            <div className="text-xl font-semibold text-slate-900">
              {selected?.name ?? "—"}
            </div>
            {selected && (
              <div className="text-sm text-slate-500">
                Возраст: {selected.age ?? "—"} · Статус: {selected.status ?? "—"} · Пол: {selected.gender ?? "—"} · Кошелёк: {selected.money != null ? `${selected.money.toFixed ? selected.money.toFixed(2) : selected.money} ¤` : "—"} · Место: {selected.locationName ?? "—"}
              </div>
            )}
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={handleReset}
              disabled={resetting}
            >
              {resetting ? "Сброс..." : "Сбросить dev-данные"}
            </Button>
            {selected?.id && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  const ev = new CustomEvent("imperium:show-character-events", { detail: { id: selected.id } });
                  window.dispatchEvent(ev);
                }}
              >
                События этого персонажа
              </Button>
            )}
            {selected?.id && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  const ev = new CustomEvent("imperium:focus-character", { detail: { id: selected.id } });
                  window.dispatchEvent(ev);
                }}
              >
                Фокус персонажа
              </Button>
            )}
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-6">
          {selected ? (
            <>
              <section>
                <h4 className="text-sm font-semibold text-slate-700">
                  История
                </h4>
                <p className="mt-2 rounded-lg border border-slate-200 bg-white/70 px-4 py-3 text-sm leading-relaxed text-slate-700">
                  {selected.history ?? "Описание отсутствует."}
                </p>
              </section>
              <section>
                <h4 className="text-sm font-semibold text-slate-700">Наследство</h4>
                {loadingInheritance ? (
                  <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
                ) : inheritance.length === 0 ? (
                  <div className="mt-2 text-sm text-slate-500">Записей нет.</div>
                ) : (
                  <ul className="mt-2 space-y-2">
                    {inheritance.map((r: any) => {
                      let heirs: string[] = [];
                      try {
                        heirs = JSON.parse(r.heirsJson ?? "[]");
                      } catch {
                        heirs = [];
                      }
                      const resolved = !!r.resolutionJson && r.resolutionJson.length > 2;
                      const isDeceased = r.deceasedId?.toLowerCase?.() === selectedId?.toLowerCase?.();
                      return (
                        <li key={r.id} className="rounded border border-slate-200 bg-white/70 px-3 py-2 text-sm">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <div className="font-medium text-slate-900">
                              {new Date(r.createdAt).toLocaleString()}
                            </div>
                            <div className={cn("text-xs", resolved ? "text-emerald-600" : "text-slate-500")}>{resolved ? "разрешено" : "ожидает"}</div>
                          </div>
                          <div className="mt-1 text-xs text-slate-600">
                            {isDeceased ? "Умерший" : "Участник"}: {selected?.name}
                          </div>
                          <div className="mt-1 text-xs text-slate-600">
                            Наследники: {heirs.length ? heirs.map((h) => (h === selectedId ? (selected?.name ?? h) : h.slice(0,8))).join(", ") : "—"}
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </section>

              <section>
                <h4 className="text-sm font-semibold text-slate-700">Инвентарь</h4>
                {loadingInventory ? (
                  <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
                ) : inventory.length === 0 ? (
                  <div className="mt-2 text-sm text-slate-500">Пусто</div>
                ) : (
                  <ul className="mt-2 grid gap-2 md:grid-cols-2">
                    {inventory.map((it, idx) => (
                      <li key={`${it.item}-${idx}`} className="flex items-center justify-between rounded border border-slate-200 bg-white/70 px-3 py-2 text-sm">
                        <span className="text-slate-600">{it.item}</span>
                        <span className="font-medium text-slate-900">{it.quantity.toFixed ? it.quantity.toFixed(2) : String(it.quantity)}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section>
                <h4 className="text-sm font-semibold text-slate-700">Отношения</h4>
                {loadingRelations ? (
                  <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
                ) : relations.length === 0 ? (
                  <div className="mt-2 text-sm text-slate-500">Нет записей</div>
                ) : (
                  <ul className="mt-2 divide-y divide-slate-200 rounded border border-slate-200 bg-white/70">
                    {relations.slice(0, 20).map((r, idx) => (
                      <li key={r.id ?? idx} className="flex items-center justify-between px-3 py-2 text-sm">
                        <div className="text-slate-700">
                          {(r.other?.name as string) ?? (r.otherId as string)?.slice(0,8)}
                          <span className="ml-2 text-xs text-slate-500">{r.type}</span>
                        </div>
                        <div className="text-xs text-slate-500">
                          доверие <span className="font-medium text-slate-900">{r.trust}</span> · симпатия <span className="font-medium text-slate-900">{r.love}</span> · враждебность <span className="font-medium text-slate-900">{r.hostility}</span>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              <section>
                <h4 className="text-sm font-semibold text-slate-700">
                  Genealogy
                </h4>
                {loadingGenealogy ? (
                  <div className="mt-2 text-sm text-slate-500">
                    Loading genealogy...
                  </div>
                ) : genealogyError ? (
                  <div className="mt-2 text-sm text-red-500">{genealogyError}</div>
                ) : genealogy ? (
                  <div className="mt-3 space-y-3 text-sm text-slate-700">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs uppercase tracking-wide text-slate-400">
                        Father
                      </span>
                      {genealogy.fatherId
                        ? relationChip(genealogy.fatherId, genealogy.father, "father")
                        : <span className="text-slate-500">no data</span>}
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-xs uppercase tracking-wide text-slate-400">
                        Mother
                      </span>
                      {genealogy.motherId
                        ? relationChip(genealogy.motherId, genealogy.mother, "mother")
                        : <span className="text-slate-500">no data</span>}
                    </div>
                    <div>
                      <span className="text-xs uppercase tracking-wide text-slate-400">
                        Spouses
                      </span>
                      {genealogy.spouses.length ? (
                        <div className="mt-2 flex flex-wrap gap-2">
                          {genealogy.spouses.map((spouse, idx) =>
                            relationChip(spouse.id, spouse.details, `spouse-${idx}`),
                          )}
                        </div>
                      ) : (
                        <div className="mt-2 text-sm text-slate-500">no records</div>
                      )}
                    </div>
                    <div>
                      <span className="text-xs uppercase tracking-wide text-slate-400">
                        Children
                      </span>
                      {genealogy.children.length ? (
                        <div className="mt-2 flex flex-wrap gap-2">
                          {genealogy.children.map((child, idx) =>
                            relationChip(child.id, child.details, `child-${idx}`),
                          )}
                        </div>
                      ) : (
                        <div className="mt-2 text-sm text-slate-500">no records</div>
                      )}
                    </div>
                  </div>
                ) : (
                  <div className="mt-2 text-sm text-slate-500">
                    No genealogy data.
                  </div>
                )}
              </section>
              <section>
                <h4 className="text-sm font-semibold text-slate-700">
                  Характеристики
                </h4>
                {!essence ? (
                  <div className="mt-2 text-sm text-slate-500">Нет данных.</div>
                ) : (
                  <div className="mt-3 grid gap-3 md:grid-cols-2">
                    {Object.entries(essence).map(([key, value]) => (
                      <div
                        key={key}
                        className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3 text-sm"
                      >
                        <div className="text-xs uppercase tracking-wide text-slate-400">
                          {key}
                        </div>
                        {typeof value === "number" ? (
                          <div className="mt-2">
                            <div className="h-2 rounded-full bg-slate-200">
                              <div
                                className="h-2 rounded-full bg-emerald-500"
                                style={{
                                  width: `${Math.min(
                                    100,
                                    Math.round((value / 10) * 100),
                                  )}%`,
                                }}
                              />
                            </div>
                            <div className="mt-1 text-xs text-slate-500">
                              {value}/10
                            </div>
                          </div>
                        ) : Array.isArray(value) ? (
                          <div className="mt-2 flex flex-wrap gap-1">
                            {value.map((item, idx) => (
                              <span
                                key={`${key}-${idx}`}
                                className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-700"
                              >
                                {String(item)}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <div className="mt-2 text-sm text-slate-700">
                            {String(value)}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </section>

              <section>
                <h4 className="text-sm font-semibold text-slate-700">
                  Навыки и таланты
                </h4>
                {!skills ? (
                  <div className="mt-2 text-sm text-slate-500">Нет данных.</div>
                ) : typeof skills === "string" ? (
                  <pre className="mt-2 max-h-60 overflow-auto rounded-lg border border-slate-200 bg-slate-50/70 p-3 text-xs text-slate-700">
                    {skills}
                  </pre>
                ) : Array.isArray(skills) ? (
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    {skills.map((item, idx) => (
                      <span
                        key={`skill-${idx}`}
                        className="rounded-full bg-blue-100 px-2 py-0.5 text-xs text-blue-700"
                      >
                        {String(item)}
                      </span>
                    ))}
                  </div>
                ) : typeof skills === "object" ? (
                  <div className="mt-3 space-y-1 text-sm text-slate-700">
                    {Object.entries(skills).map(([key, value]) => (
                      <div key={key} className="flex items-center justify-between rounded border border-slate-200 bg-white/60 px-3 py-2">
                        <span className="text-slate-500">{key}</span>
                        <span className="font-medium text-slate-900">
                          {String(value)}
                        </span>
                      </div>
                    ))}
                  </div>
                ) : null}
              </section>

              <section>
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-semibold text-slate-700">
                    Хронология ответов
                  </h4>
                  {loadingEvents && (
                    <span className="text-xs text-slate-500">
                      Загрузка событий...
                    </span>
                  )}
                </div>
                {replies.length === 0 ? (
                  <div className="mt-2 text-sm text-slate-500">
                    Нет реплик для отображения.
                  </div>
                ) : (
                  <ul className="mt-3 space-y-3">
                    {replies.map((ev) => (
                      <li
                        key={ev.id}
                        className="rounded-lg border border-slate-200 bg-white/80 px-4 py-3 text-sm"
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div className="text-xs text-slate-500">
                            {ev.timestamp
                              ? new Date(ev.timestamp).toLocaleString()
                              : "—"}
                          </div>
                          {ev.moodDelta !== null && (
                            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-700">
                              настроение {ev.moodDelta > 0 ? "+" : ""}
                              {ev.moodDelta}
                            </span>
                          )}
                        </div>
                        <div className="mt-2 text-base font-medium text-slate-900">
                          {ev.reply || "—"}
                        </div>
                        {ev.meta && (
                          <div className="mt-2 text-xs text-slate-500">
                            reasks: {String(ev.meta.reasksPerformed ?? 0)} ·
                            sanitizations: {String(ev.meta.sanitizations ?? 0)}
                          </div>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </>
          ) : (
            <div className="rounded-lg border border-dashed border-slate-200 bg-white/60 p-6 text-sm text-slate-500">
              Выберите персонажа слева, чтобы увидеть профиль и ответы.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}



