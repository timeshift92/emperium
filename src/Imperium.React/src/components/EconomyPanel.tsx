import { cn } from "@/lib/utils";
import { useLatestEvent } from "@/lib/useLatestEvent";
import { useEffect, useMemo, useState } from "react";

type EconomyPanelProps = {
  className?: string;
};

export default function EconomyPanel({ className }: EconomyPanelProps) {
  const snapshot = useLatestEvent("economy_snapshot", { refreshMs: 45_000 });
  const transport = useLatestEvent("transport_job", { refreshMs: 45_000 });

  const [orders, setOrders] = useState<any[]>([]);
  const [trades, setTrades] = useState<any[]>([]);
  const [loadingBook, setLoadingBook] = useState(false);
  const [errorBook, setErrorBook] = useState<string | null>(null);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [selectedLocation, setSelectedLocation] = useState<string | "all">("all");
  const [selectedItem, setSelectedItem] = useState<string>("grain");
  const [agg, setAgg] = useState<{ key: string; value: number }[]>([]);
  const [loadingAgg, setLoadingAgg] = useState(false);
  const [items, setItems] = useState<string[]>([]);
  const [shocks, setShocks] = useState<{ item: string; factor: number; expiresAt?: string }[]>([]);

  // Load locations and items once
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch("/api/locations");
        if (res.ok) {
          const data = (await res.json()) as { id: string; name: string }[];
          if (!cancelled) setLocations(data);
        }
        const resItems = await fetch("/api/economy/items");
        if (resItems.ok) {
          const items = (await resItems.json()) as string[];
          if (!cancelled && Array.isArray(items) && items.length > 0) {
            setItems(items);
            if (!items.includes(selectedItem)) setSelectedItem(items[0]);
          }
        }
      } catch {}
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Poll active shocks
  useEffect(() => {
    let cancelled = false;
    let timer: number | null = null;
    const load = async () => {
      if (cancelled) return;
      try {
        const r = await fetch("/api/economy/shocks");
        if (r.ok) {
          const data = (await r.json()) as { item: string; factor: number; expiresAt?: string }[];
          if (!cancelled) setShocks(Array.isArray(data) ? data : []);
        }
      } catch {}
      finally {
        if (!cancelled) timer = window.setTimeout(load, 20000);
      }
    };
    load();
    return () => { cancelled = true; if (timer) window.clearTimeout(timer); };
  }, []);

  // Load order book and trades
  useEffect(() => {
    let cancelled = false;
    let timer: number | null = null;
    const load = async () => {
      if (cancelled) return;
      setLoadingBook(true);
      try {
        const locParam = selectedLocation !== "all" ? `&locationId=${selectedLocation}` : "";
        const itemParam = `item=${encodeURIComponent(selectedItem)}`;
        const [rOrders, rTrades] = await Promise.all([
          fetch(`/api/economy/orders?${itemParam}${locParam}`),
          fetch(`/api/economy/trades?${itemParam}${locParam}`),
        ]);
        if (!cancelled) {
          const o = (await rOrders.json()).slice(0, 100);
          const t = (await rTrades.json()).slice(0, 50);
          setOrders(Array.isArray(o) ? o : []);
          setTrades(Array.isArray(t) ? t : []);
          setErrorBook(null);
        }
      } catch (err) {
        if (!cancelled) setErrorBook(err instanceof Error ? err.message : "Ошибка загрузки стакана");
      } finally {
        if (!cancelled) setLoadingBook(false);
        if (!cancelled) timer = window.setTimeout(load, 15000);
      }
    };
    load();
    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [selectedLocation, selectedItem]);

  // Aggregate per location for selected item
  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoadingAgg(true);
      try {
        const res = await fetch(`/api/economy/inventory/aggregate?by=location&item=${encodeURIComponent(selectedItem)}`);
        if (!res.ok) throw new Error("agg failed");
        const data = (await res.json()) as { locationId: string | null; total: number }[];
        if (!cancelled) {
          const mapNames = new Map(locations.map(l => [l.id, l.name] as const));
          setAgg((data ?? []).map(d => ({ key: d.locationId ? (mapNames.get(d.locationId) ?? d.locationId) : "global", value: d.total })).sort((a,b)=> (b.value - a.value)));
        }
      } catch {
        if (!cancelled) setAgg([]);
      } finally {
        if (!cancelled) setLoadingAgg(false);
      }
    })();
    return () => { cancelled = true; };
  }, [selectedItem, locations]);

  const bids = useMemo(() => orders.filter(o => o.side === "buy" || o.Side === "buy").sort((a,b)=> (b.price ?? b.Price) - (a.price ?? a.Price)).slice(0,10), [orders]);
  const asks = useMemo(() => orders.filter(o => o.side === "sell" || o.Side === "sell").sort((a,b)=> (a.price ?? a.Price) - (b.price ?? b.Price)).slice(0,10), [orders]);

  const payload =
    snapshot.payload && typeof snapshot.payload === "object"
      ? (snapshot.payload as Record<string, unknown>)
      : undefined;

  const transportPayload =
    transport.payload && typeof transport.payload === "object"
      ? (transport.payload as Record<string, unknown>)
      : undefined;

  const economyDetails = payload as {
    snapshotId?: string;
    avgPrecip?: number;
    descriptionRu?: string;
  };

  const transportDetails = transportPayload as {
    from?: string;
    to?: string;
    profit?: number | string;
    descriptionRu?: string;
  };

  return (
    <div
      className={cn(
        "rounded-xl border border-slate-200 bg-white/70 p-4 shadow-sm",
        "text-slate-800",
        className,
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-lg font-semibold text-slate-900">
            Экономический снимок
          </h3>
          <p className="text-xs text-slate-500">
            Последнее событие economy_snapshot и связанные транспортные задачи.
          </p>
        </div>
        <div className="text-xs uppercase tracking-wide text-slate-400">
          {snapshot.timestamp
            ? new Date(snapshot.timestamp).toLocaleString()
            : "—"}
        </div>
      </div>

      {snapshot.loading && (
        <div className="mt-4 rounded border border-dashed border-slate-200 bg-white/60 px-3 py-2 text-sm text-slate-500">
          Загрузка экономических данных...
        </div>
      )}

      {snapshot.error && (
        <div className="mt-4 rounded border border-red-200 bg-red-50/80 px-3 py-2 text-sm text-red-600">
          Ошибка: {snapshot.error}
        </div>
      )}

      {!snapshot.loading && !payload && !snapshot.error && (
        <div className="mt-4 rounded border border-dashed border-slate-200 bg-white/60 px-3 py-2 text-sm text-slate-500">
          Снимков экономики пока нет.
        </div>
      )}

      {payload && (
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
            <div className="text-xs uppercase tracking-wide text-slate-400">
              Средние осадки
            </div>
            <div className="mt-1 text-xl font-semibold text-slate-900">
              {typeof economyDetails?.avgPrecip === "number"
                ? `${economyDetails.avgPrecip.toFixed(2)} мм`
                : "—"}
            </div>
            <div className="mt-1 text-xs text-slate-500">
              Влияют на стоимость зерна
            </div>
          </div>
          <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
            <div className="text-xs uppercase tracking-wide text-slate-400">
              Snapshot Id
            </div>
            <div className="mt-1 text-sm font-medium text-slate-900 break-all">
              {typeof economyDetails?.snapshotId === "string"
                ? economyDetails.snapshotId
                : "—"}
            </div>
            <div className="mt-1 text-xs text-slate-500">
              Используйте для запроса подробностей из dev-инструментов
            </div>
          </div>
        </div>
      )}

      {typeof economyDetails?.descriptionRu === "string" && (
        <div className="mt-3 rounded-lg border border-slate-200 bg-white/60 px-4 py-3 text-sm text-slate-700">
          {economyDetails.descriptionRu}
        </div>
      )}

      {/* Controls */}
      <div className="mt-6 flex flex-wrap items-center gap-3 text-sm">
        <div>
          <label className="mr-2 text-slate-600">Товар:</label>
          <select
            className="rounded border border-slate-300 bg-white px-2 py-1"
            value={selectedItem}
            onChange={(e) => setSelectedItem(e.target.value)}
          >
            {(items.length > 0 ? items : ["grain","wine","oil"]).map(it => (
              <option key={it} value={it}>{it}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="mr-2 text-slate-600">Локация:</label>
          <select
            className="rounded border border-slate-300 bg-white px-2 py-1"
            value={selectedLocation}
            onChange={(e) => setSelectedLocation(e.target.value)}
          >
            <option value="all">Все</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>{l.name}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Order book */}
      <div className="mt-6 grid gap-4 md:grid-cols-2">
        <div className="rounded-lg border border-slate-200 bg-white/70 p-3">
          <div className="mb-2 text-sm font-semibold text-slate-700">Стакан заявок (grain)</div>
          {errorBook && (
            <div className="mb-2 rounded border border-red-200 bg-red-50/80 px-2 py-1 text-xs text-red-600">{errorBook}</div>
          )}
          {loadingBook && <div className="text-xs text-slate-500">Загрузка...</div>}
          {!loadingBook && (
            <div className="grid grid-cols-2 gap-3 text-xs">
              <div>
                <div className="mb-1 text-slate-500">Покупка (bid)</div>
                <ul className="space-y-1">
                  {bids.map((b, i) => (
                    <li key={b.id ?? b.Id ?? i} className="flex justify-between rounded border border-emerald-100 bg-emerald-50/60 px-2 py-1">
                      <span className="font-medium">{(b.price ?? b.Price)?.toFixed ? (b.price ?? b.Price).toFixed(2) : String(b.price ?? b.Price)}</span>
                      <span className="text-slate-600">{b.remaining ?? b.Remaining ?? b.quantity ?? b.Quantity}</span>
                    </li>
                  ))}
                  {bids.length === 0 && <li className="text-slate-500">—</li>}
                </ul>
              </div>
              <div>
                <div className="mb-1 text-slate-500">Продажа (ask)</div>
                <ul className="space-y-1">
                  {asks.map((a, i) => (
                    <li key={a.id ?? a.Id ?? i} className="flex justify-between rounded border border-rose-100 bg-rose-50/60 px-2 py-1">
                      <span className="font-medium">{(a.price ?? a.Price)?.toFixed ? (a.price ?? a.Price).toFixed(2) : String(a.price ?? a.Price)}</span>
                      <span className="text-slate-600">{a.remaining ?? a.Remaining ?? a.quantity ?? a.Quantity}</span>
                    </li>
                  ))}
                  {asks.length === 0 && <li className="text-slate-500">—</li>}
                </ul>
              </div>
            </div>
          )}
        </div>

        {/* Recent trades */}
        <div className="rounded-lg border border-slate-200 bg-white/70 p-3">
          <div className="mb-2 text-sm font-semibold text-slate-700">Сделки (последние)</div>
          <ul className="space-y-1 text-xs">
            {trades.slice(0, 12).map((t, i) => (
              <li key={t.id ?? t.Id ?? i} className="flex items-center justify-between rounded border border-slate-200 bg-white/80 px-2 py-1">
                <span className="text-slate-700">{(t.price ?? t.Price)?.toFixed ? (t.price ?? t.Price).toFixed(2) : String(t.price ?? t.Price)} · qty {(t.quantity ?? t.Quantity)}</span>
                <span className="text-slate-400">{new Date(t.timestamp ?? t.Timestamp ?? Date.now()).toLocaleTimeString()}</span>
              </li>
            ))}
            {trades.length === 0 && <li className="text-slate-500">Нет сделок</li>}
          </ul>
        </div>
      </div>

      {/* Active shocks */}
      <div className="mt-6 rounded-lg border border-amber-200 bg-amber-50/70 p-3">
        <div className="mb-2 text-sm font-semibold text-amber-800">Активные ценовые шоки</div>
        <ul className="space-y-1 text-xs text-amber-900">
          {shocks.length === 0 && <li>Нет</li>}
          {shocks.map((s, i) => (
            <li key={i} className="flex justify-between gap-2 border-b border-amber-100 py-1 last:border-b-0">
              <span>{s.item}</span>
              <span className="font-semibold">×{s.factor}</span>
              <span className="text-amber-700">{s.expiresAt ? new Date(s.expiresAt).toLocaleTimeString() : "∞"}</span>
            </li>
          ))}
        </ul>
      </div>

      {/* Aggregate inventory per location */}
      <div className="mt-6 rounded-lg border border-slate-200 bg-white/70 p-3">
        <div className="mb-2 text-sm font-semibold text-slate-700">Запасы по локациям — {selectedItem}</div>
        {loadingAgg ? (
          <div className="text-xs text-slate-500">Загрузка...</div>
        ) : (
          <ul className="space-y-1 text-sm">
            {agg.map((x) => (
              <li key={x.key} className="flex justify-between border-b border-slate-100 py-1">
                <span className="text-slate-600">{x.key}</span>
                <span className="font-medium text-slate-900">{x.value.toFixed ? x.value.toFixed(2) : String(x.value)}</span>
              </li>
            ))}
            {agg.length === 0 && <li className="text-slate-500">Нет данных</li>}
          </ul>
        )}
      </div>

      <div className="mt-6 rounded-lg border border-indigo-100 bg-indigo-50/70 px-4 py-3 text-sm text-indigo-800">
        <div className="flex items-center justify-between gap-3">
          <div className="font-semibold">Транспортные задачи</div>
          <div className="text-xs uppercase tracking-wide text-indigo-500">
            {transport.timestamp
              ? new Date(transport.timestamp).toLocaleString()
              : "—"}
          </div>
        </div>
        {transport.loading && (
          <div className="mt-2 text-xs text-indigo-600">
            Загрузка последних задач...
          </div>
        )}
        {!transport.loading && transportPayload ? (
          <ul className="mt-2 space-y-1 text-xs">
            <li>
              Из{" "}
              <span className="font-medium">
                {String(transportDetails?.from ?? "—")}
              </span>{" "}
              в{" "}
              <span className="font-medium">
                {String(transportDetails?.to ?? "—")}
              </span>
            </li>
            {transportDetails?.profit != null && (
              <li>
                Прибыль:{" "}
                <span className="font-semibold">
                  {String(transportDetails.profit)}
                </span>
              </li>
            )}
            {typeof transportDetails?.descriptionRu === "string" && (
              <li>{transportDetails.descriptionRu}</li>
            )}
          </ul>
        ) : (
          !transport.loading && (
            <div className="mt-2 text-xs text-indigo-600">
              Нет активных задач.
            </div>
          )
        )}
      </div>
    </div>
  );
}
