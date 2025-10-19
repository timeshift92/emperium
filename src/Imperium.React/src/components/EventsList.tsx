import { useEffect, useMemo, useState } from "react";
import { useSSE } from "@/lib/useSSE";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type EventsListProps = {
  className?: string;
};

type RawEvent = {
  Id?: string;
  id?: string;
  Timestamp?: string;
  timestamp?: string;
  Type?: string;
  type?: string;
  Location?: string;
  location?: string;
  PayloadJson?: string;
  payloadJson?: string;
  Payload?: unknown;
  payload?: unknown;
};

type NormalizedEvent = {
  id: string;
  timestamp: string | null;
  type: string;
  location: string;
  payload: unknown;
  rawPayload: unknown;
};

function normalizeEvent(ev: RawEvent, fallbackId: string): NormalizedEvent {
  const id = ev.Id ?? ev.id ?? fallbackId;
  const timestamp = ev.Timestamp ?? ev.timestamp ?? null;
  const type = ev.Type ?? ev.type ?? "unknown";
  const location = ev.Location ?? ev.location ?? "—";
  const raw =
    ev.PayloadJson ?? ev.payloadJson ?? ev.Payload ?? ev.payload ?? null;
  let payload: unknown = raw;
  if (typeof raw === "string") {
    try {
      payload = JSON.parse(raw);
    } catch {
      payload = raw;
    }
  }

  return {
    id,
    timestamp,
    type,
    location,
    payload,
    rawPayload: raw,
  };
}

function getEventKey(ev: RawEvent, fallback: string) {
  return ev.Id ?? ev.id ?? ev.Timestamp ?? ev.timestamp ?? fallback;
}

function getTimestampValue(ev: RawEvent) {
  const ts = ev.Timestamp ?? ev.timestamp ?? null;
  return ts ? Date.parse(ts) : Number.MIN_SAFE_INTEGER;
}

export default function EventsList({ className }: EventsListProps) {
  const streamEvents = useSSE<RawEvent>("/api/events/stream");
  const [activeType, setActiveType] = useState<string>("all");
  const [search, setSearch] = useState<string>("");
  const [initialEvents, setInitialEvents] = useState<RawEvent[]>([]);
  const [initialLoading, setInitialLoading] = useState<boolean>(false);
  const [initialError, setInitialError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const loadInitial = async () => {
      if (cancelled) return;
      setInitialLoading(true);
      try {
        const res = await fetch("/api/events?count=100");
        if (!res.ok) {
          throw new Error(`Статус ${res.status}`);
        }
        const data = (await res.json()) as RawEvent[];
        if (!cancelled) {
          setInitialEvents(data);
          setInitialError(null);
        }
      } catch (err) {
        if (!cancelled) {
          setInitialError(
            err instanceof Error
              ? err.message
              : "Не удалось получить историю событий",
          );
        }
      } finally {
        if (!cancelled) {
          setInitialLoading(false);
        }
      }
    };

    loadInitial();

    return () => {
      cancelled = true;
    };
  }, []);

  const combinedEvents = useMemo(() => {
    const map = new Map<string, RawEvent>();
    const insert = (source: RawEvent[], isStream: boolean) => {
      source.forEach((ev, idx) => {
        const key = getEventKey(ev, `${isStream ? "stream" : "initial"}-${idx}`);
        if (!map.has(key)) map.set(key, ev);
      });
    };
    insert(streamEvents, true);
    insert(initialEvents, false);
    return Array.from(map.values()).sort(
      (a, b) => getTimestampValue(b) - getTimestampValue(a),
    );
  }, [streamEvents, initialEvents]);

  const normalized = useMemo(
    () => combinedEvents.map((ev, idx) => normalizeEvent(ev, `event-${idx}`)),
    [combinedEvents],
  );

  const types = useMemo(() => {
    const collection = new Set<string>();
    normalized.forEach((ev) => collection.add(ev.type));
    return Array.from(collection).sort((a, b) => a.localeCompare(b));
  }, [normalized]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return normalized.filter((ev) => {
      if (activeType !== "all" && ev.type !== activeType) return false;
      if (!term) return true;
      const haystack = `${ev.type} ${ev.location} ${JSON.stringify(ev.payload)}`;
      return haystack.toLowerCase().includes(term);
    });
  }, [normalized, activeType, search]);

  return (
    <div
      className={cn(
        "flex h-full min-h-0 flex-col rounded-xl border border-slate-200 bg-white/70 p-4 shadow-sm",
        className,
      )}
    >
      <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-slate-900">
            Лента событий
          </h2>
          <p className="text-sm text-slate-500">
            Поток GameEvent с фильтрами по типу и поиском.
          </p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          <div className="flex flex-wrap gap-2">
            <Button
              variant={activeType === "all" ? "default" : "outline"}
              onClick={() => setActiveType("all")}
            >
              Все ({normalized.length})
            </Button>
            {types.map((type) => (
              <Button
                key={type}
                variant={activeType === type ? "default" : "outline"}
                onClick={() =>
                  setActiveType((prev) => (prev === type ? "all" : type))
                }
              >
                {type}
              </Button>
            ))}
          </div>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по payload"
            className="h-9 rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-700 placeholder:text-slate-400 focus:border-slate-400 focus:outline-none focus:ring-2 focus:ring-slate-200"
          />
        </div>
      </div>

      <div className="flex-1 min-h-0 overflow-y-auto pr-1">
        {initialError && (
          <div className="mb-3 rounded border border-red-200 bg-red-50/80 px-3 py-2 text-xs text-red-600">
            {`История недоступна: ${initialError}`}
          </div>
        )}
        <ul className="space-y-3">
          {filtered.map((ev) => (
            <li
              key={ev.id}
              className="rounded-lg border border-slate-200 bg-white/80 p-3 text-sm shadow-sm"
            >
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <div className="font-semibold text-slate-900">{ev.type}</div>
                <div className="text-xs uppercase tracking-wide text-slate-400">
                  {ev.timestamp
                    ? new Date(ev.timestamp).toLocaleString()
                    : "—"}
                </div>
              </div>
              <div className="mt-1 text-xs text-slate-500">
                Локация: {ev.location || "—"}
              </div>
              <details className="mt-3 rounded border border-slate-200 bg-slate-50/70 px-3 py-2 text-xs text-slate-700">
                <summary className="cursor-pointer text-slate-600">
                  Показать payload
                </summary>
                <pre className="mt-2 max-h-56 overflow-auto whitespace-pre-wrap text-[11px] leading-tight">
                  {JSON.stringify(ev.payload, null, 2)}
                </pre>
              </details>
            </li>
          ))}
          {filtered.length === 0 && (
            <li className="rounded-lg border border-dashed border-slate-200 bg-white/60 p-6 text-center text-sm text-slate-500">
              {initialLoading
                ? "Загрузка списка событий..."
                : "Нет событий по заданным фильтрам."}
            </li>
          )}
        </ul>
      </div>
    </div>
  );
}
