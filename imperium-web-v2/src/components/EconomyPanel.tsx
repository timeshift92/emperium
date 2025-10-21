import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Button } from "./ui/button";
import { cancelMarketOrder, createMarketOrder, fetchEconomyItems, fetchLocations, fetchMarketOrders, fetchTrades, MarketOrderSummary, TradeSummary } from "../services/api";
import { useWorldStore } from "../store/useWorldStore";

function formatNumber(value: number, fraction = 2) {
  return value.toLocaleString("ru-RU", { minimumFractionDigits: fraction, maximumFractionDigits: fraction });
}

function formatTimestamp(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

type ItemListSelectorProps = {
  items: string[];
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

function ItemListSelector({ items, value, onChange, disabled }: ItemListSelectorProps) {
  const [query, setQuery] = useState("");

  useEffect(() => {
    if (items.length > 0 && !items.includes(value)) {
      onChange(items[0]);
    }
  }, [items, onChange, value]);

  useEffect(() => {
    setQuery("");
  }, [items]);

  const filtered = useMemo(() => {
    if (!query.trim()) return items;
    const q = query.trim().toLowerCase();
    return items.filter(name => name.toLowerCase().includes(q));
  }, [items, query]);

  return (
    <div className="flex flex-col gap-2">
      <input
        type="text"
        value={query}
        onChange={event => setQuery(event.target.value)}
        placeholder="Фильтр товаров…"
        className="rounded border border-zinc-200 px-2 py-1 text-sm focus:border-emerald-500 focus:outline-none disabled:bg-zinc-100"
        disabled={disabled || items.length === 0}
      />
      <div className="max-h-72 overflow-y-auto rounded border border-zinc-200 bg-white shadow-inner">
        {items.length === 0 ? (
          <div className="px-3 py-2 text-xs text-zinc-500">Список товаров пуст</div>
        ) : filtered.length === 0 ? (
          <div className="px-3 py-2 text-xs text-zinc-500">Нет совпадений</div>
        ) : (
          <ul className="divide-y divide-zinc-100">
            {filtered.map(name => {
              const active = name === value;
              return (
                <li key={name}>
                  <button
                    type="button"
                    onClick={() => onChange(name)}
                    disabled={disabled}
                    className={`flex w-full items-center justify-between px-3 py-2 text-left text-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 ${
                      active ? "bg-emerald-100/90 font-medium text-emerald-700" : "text-zinc-700 hover:bg-emerald-50"
                    } ${disabled ? "cursor-not-allowed opacity-70" : ""}`}
                  >
                    <span className="truncate">{name}</span>
                    {active && <span className="text-[10px] uppercase text-emerald-600">выбрано</span>}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
      <div className="text-xs text-zinc-500">Всего товаров: {items.length}</div>
    </div>
  );
}

export default function EconomyPanel() {
  const [item, setItem] = useState("grain");
  const [orders, setOrders] = useState<MarketOrderSummary[]>([]);
  const [trades, setTrades] = useState<TradeSummary[]>([]);
  const [items, setItems] = useState<string[]>([]);
  const [locations, setLocations] = useState<{ id: string; name: string }[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [form, setForm] = useState({
    ownerId: "",
    ownerType: "character",
    side: "buy",
    price: "10",
    quantity: "5",
    locationId: "",
    ttlMinutes: "15"
  });
  const economyVersion = useWorldStore(s => s.economyVersion);
  const itemRef = useRef(item);

  useEffect(() => {
    const controller = new AbortController();
    (async () => {
      try {
        const [itemsPayload, locationsPayload] = await Promise.all([
          fetchEconomyItems(controller.signal),
          fetchLocations(controller.signal)
        ]);
        if (controller.signal.aborted) return;
        setItems(itemsPayload);
        setLocations(locationsPayload.map(loc => ({ id: loc.id, name: loc.name })));
        setItem(prev => (itemsPayload.length > 0 && !itemsPayload.includes(prev) ? itemsPayload[0] : prev));
        setForm(prev => {
          if (prev.locationId) return prev;
          if (locationsPayload.length === 0) return prev;
          return { ...prev, locationId: locationsPayload[0].id };
        });
      }
      catch (err) {
        if (!controller.signal.aborted) {
          setError(err instanceof Error ? err.message : "Не удалось загрузить справочники экономики");
        }
      }
    })();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    itemRef.current = item;
  }, [item]);

  const loadData = useCallback(
    async (targetItem: string, signal?: AbortSignal, silent = false) => {
      if (!silent) {
        setLoading(true);
        setActionMessage(null);
      }
      setError(null);
      try {
        const [ordersPayload, tradesPayload] = await Promise.all([
          fetchMarketOrders({ item: targetItem }, signal),
          fetchTrades({ item: targetItem }, signal)
        ]);
        if (signal?.aborted) return;
        setOrders(ordersPayload);
        setTrades(tradesPayload);
        setForm(prev => {
          if (prev.locationId) return prev;
          const first = ordersPayload.find(o => o.locationId);
          if (first?.locationId) {
            return { ...prev, locationId: first.locationId ?? "" };
          }
          return prev;
        });
      } catch (err) {
        if (signal?.aborted) return;
        setError(err instanceof Error ? err.message : "Неизвестная ошибка при загрузке экономики");
      } finally {
        if (!signal?.aborted && !silent) {
          setLoading(false);
        }
      }
    },
    []
  );

  useEffect(() => {
    const controller = new AbortController();
    void loadData(item, controller.signal);
    return () => controller.abort();
  }, [item, loadData]);

  useEffect(() => {
    if (economyVersion === 0) return;
    const controller = new AbortController();
    void loadData(itemRef.current, controller.signal, true);
    return () => controller.abort();
  }, [economyVersion, loadData]);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setLoading(true);
    setError(null);
    setActionMessage(null);
    try {
      if (!form.ownerId.trim()) throw new Error("Укажите владельца (GUID)");
      if (!form.locationId.trim()) throw new Error("Укажите локацию");
      const ttl = Number(form.ttlMinutes) || 0;
      const expiresAt = ttl > 0 ? new Date(Date.now() + ttl * 60000).toISOString() : undefined;
      await createMarketOrder(
        {
          ownerId: form.ownerId.trim(),
          ownerType: form.ownerType as "character" | "household",
          item,
          side: form.side as "buy" | "sell",
          price: Number(form.price),
          quantity: Number(form.quantity),
          locationId: form.locationId,
          expiresAt
        },
        undefined
      );
      setActionMessage("Ордер создан");
      await loadData(item, undefined, true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось создать ордер");
    } finally {
      setLoading(false);
    }
  };

  const handleCancel = async (orderId: string) => {
    setLoading(true);
    setError(null);
    setActionMessage(null);
    try {
      await cancelMarketOrder(orderId);
      setActionMessage("Ордер отменён");
      await loadData(item, undefined, true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось отменить ордер");
    } finally {
      setLoading(false);
    }
  };

  const computeTtl = (expiresAt?: string | null) => {
    if (!expiresAt) return "—";
    const exp = new Date(expiresAt).getTime();
    if (Number.isNaN(exp)) return "—";
    const diff = exp - Date.now();
    if (diff <= 0) return "истёк";
    const mins = Math.floor(diff / 60000);
    const secs = Math.floor((diff % 60000) / 1000);
    return `${mins}м ${secs}с`;
  };

  const estimateFee = (order: MarketOrderSummary) => {
    const base = Math.max(0, order.price * order.remaining);
    return formatNumber(base * 0.01);
  };

  const bids = useMemo(() => {
    return orders
      .filter(o => o.side.toLowerCase() === "buy")
      .sort((a, b) => b.price - a.price)
      .slice(0, 10);
  }, [orders]);

  const asks = useMemo(() => {
    return orders
      .filter(o => o.side.toLowerCase() === "sell")
      .sort((a, b) => a.price - b.price)
      .slice(0, 10);
  }, [orders]);

  const recentTrades = useMemo(() => {
    return trades
      .slice()
      .sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime())
      .slice(0, 10);
  }, [trades]);

  const summary = useMemo(() => {
    const bestBid = bids[0];
    const bestAsk = asks[0];
    const lastTrade = recentTrades[0];
    const spread =
      bestBid && bestAsk ? Math.max(bestAsk.price - bestBid.price, 0) : undefined;
    const mid =
      bestBid && bestAsk ? (bestBid.price + bestAsk.price) / 2 : bestBid?.price ?? bestAsk?.price;
    const totalVolume = recentTrades.reduce((sum, trade) => sum + trade.quantity, 0);
    return {
      bestBid,
      bestAsk,
      spread,
      mid,
      lastTrade,
      totalVolume
    };
  }, [asks, bids, recentTrades]);

  return (
    <Card className="shadow-md">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle>💰 Экономика</CardTitle>
          {loading && <span className="text-xs uppercase tracking-wide text-emerald-600">обновление…</span>}
        </div>
      </CardHeader>
      <CardContent className="text-sm">
        <div className="grid gap-4 lg:grid-cols-[minmax(220px,260px)_1fr]">
          <aside className="space-y-3 rounded-md border border-zinc-200 bg-white/70 p-3">
            <div className="text-xs uppercase tracking-wide text-zinc-500">Список товаров</div>
            <ItemListSelector items={items} value={item} onChange={setItem} disabled={loading} />
            <div className="text-xs text-zinc-500">
              Выбрано: <span className="font-semibold text-zinc-700">{item || "—"}</span>
            </div>
          </aside>
          <section className="space-y-4">
            {error && <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-600">{error}</div>}
            {actionMessage && <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-700">{actionMessage}</div>}
            {!error && (
              <>
                <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
              <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
                <div className="text-xs uppercase text-zinc-500">Лучший бид</div>
                <div className="text-base font-semibold text-emerald-700">
                  {summary.bestBid ? formatNumber(summary.bestBid.price) : "—"}
                </div>
                <div className="text-xs text-zinc-500">
                  {summary.bestBid ? `объём ${formatNumber(summary.bestBid.remaining)}` : "нет заявок"}
                </div>
              </div>
              <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
                <div className="text-xs uppercase text-zinc-500">Лучший аск</div>
                <div className="text-base font-semibold text-rose-700">
                  {summary.bestAsk ? formatNumber(summary.bestAsk.price) : "—"}
                </div>
                <div className="text-xs text-zinc-500">
                  {summary.bestAsk ? `объём ${formatNumber(summary.bestAsk.remaining)}` : "нет заявок"}
                </div>
              </div>
              <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
                <div className="text-xs uppercase text-zinc-500">Спред</div>
                <div className="text-base font-semibold text-zinc-800">
                  {summary.spread != null ? formatNumber(summary.spread) : "—"}
                </div>
                <div className="text-xs text-zinc-500">
                  Mid {summary.mid != null ? formatNumber(summary.mid) : "—"}
                </div>
              </div>
              <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
                <div className="text-xs uppercase text-zinc-500">Последняя сделка</div>
                {summary.lastTrade ? (
                  <>
                    <div className="text-base font-semibold text-zinc-800">
                      {formatNumber(summary.lastTrade.price)} / {formatNumber(summary.lastTrade.quantity)}
                    </div>
                    <div className="text-xs text-zinc-500">{formatTimestamp(summary.lastTrade.timestamp)}</div>
                  </>
                ) : (
                  <div className="text-base font-semibold text-zinc-500">—</div>
                )}
                <div className="mt-1 text-xs text-zinc-500">
                  Объём за период: {formatNumber(summary.totalVolume)}
                </div>
              </div>
            </div>

            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Создать ордер</div>
              <form className="grid gap-2 text-xs md:grid-cols-3" onSubmit={handleSubmit}>
                <label className="flex flex-col gap-1">
                  <span>Владелец GUID</span>
                  <input
                    type="text"
                    value={form.ownerId}
                    onChange={event => setForm(prev => ({ ...prev, ownerId: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                    placeholder="GUID персонажа или семьи"
                    required
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span>Тип владельца</span>
                  <select
                    value={form.ownerType}
                    onChange={event => setForm(prev => ({ ...prev, ownerType: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                  >
                    <option value="character">Персонаж</option>
                    <option value="household">Семья</option>
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span>Товар</span>
                  <input
                    type="text"
                    value={item}
                    readOnly
                    className="rounded border border-zinc-200 bg-zinc-100 px-2 py-1 text-zinc-700"
                  />
                  <span className="text-[10px] text-zinc-500">Выбор товара — в списке слева</span>
                </label>
                <label className="flex flex-col gap-1">
                  <span>Тип ордера</span>
                  <select
                    value={form.side}
                    onChange={event => setForm(prev => ({ ...prev, side: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                  >
                    <option value="buy">Покупка</option>
                    <option value="sell">Продажа</option>
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span>Цена</span>
                  <input
                    type="number"
                    step="0.01"
                    value={form.price}
                    onChange={event => setForm(prev => ({ ...prev, price: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                    min="0"
                    required
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span>Количество</span>
                  <input
                    type="number"
                    step="0.01"
                    value={form.quantity}
                    onChange={event => setForm(prev => ({ ...prev, quantity: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                    min="0.01"
                    required
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span>Локация</span>
                  <select value={form.locationId} onChange={event => setForm(prev => ({ ...prev, locationId: event.target.value }))} className="rounded border border-zinc-200 px-2 py-1">
                    {locations.map(loc => (
                      <option key={loc.id} value={loc.id}>
                        {loc.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span>TTL (минут)</span>
                  <input
                    type="number"
                    value={form.ttlMinutes}
                    onChange={event => setForm(prev => ({ ...prev, ttlMinutes: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                    min="1"
                  />
                </label>
                <div className="md:col-span-3 flex items-center justify-end gap-3">
                  <span className="text-zinc-500">Комиссия ~1% от цены × объём</span>
                  <Button type="submit" disabled={loading}>
                    Разместить ордер
                  </Button>
                </div>
              </form>
            </div>

            <div className="grid gap-3 lg:grid-cols-2">
              <div className="rounded-md border border-emerald-100 bg-emerald-50/70 p-3">
                <div className="mb-2 text-xs font-semibold uppercase text-emerald-700">Покупка</div>
                {bids.length === 0 ? (
                  <div className="text-xs text-zinc-500">Нет заявок на покупку</div>
                ) : (
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="text-zinc-500">
                        <th className="pb-1 text-left font-semibold">Цена</th>
                        <th className="pb-1 text-left font-semibold">Объём</th>
                        <th className="pb-1 text-left font-semibold">TTL</th>
                        <th className="pb-1 text-left font-semibold">Комиссия</th>
                        <th className="pb-1 text-left font-semibold">Статус</th>
                        <th className="pb-1 text-left font-semibold"></th>
                      </tr>
                    </thead>
                    <tbody>
                      {bids.map(order => (
                        <tr key={order.id} className="odd:bg-white/60 even:bg-white/30">
                          <td className="py-1 pr-2">{formatNumber(order.price)}</td>
                          <td className="py-1 pr-2">
                            {formatNumber(order.remaining)} / {formatNumber(order.quantity)}
                          </td>
                          <td className="py-1 pr-2">{computeTtl(order.expiresAt)}</td>
                          <td className="py-1 pr-2">{estimateFee(order)}</td>
                          <td className="py-1 pr-2">{order.status}</td>
                          <td className="py-1 text-right">
                            {order.status === "open" ? (
                              <Button variant="ghost" size="sm" onClick={() => handleCancel(order.id)} disabled={loading}>
                                Отменить
                              </Button>
                            ) : (
                              <span className="text-zinc-400">—</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
              <div className="rounded-md border border-rose-100 bg-rose-50/70 p-3">
                <div className="mb-2 text-xs font-semibold uppercase text-rose-700">Продажа</div>
                {asks.length === 0 ? (
                  <div className="text-xs text-zinc-500">Нет заявок на продажу</div>
                ) : (
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="text-zinc-500">
                        <th className="pb-1 text-left font-semibold">Цена</th>
                        <th className="pb-1 text-left font-semibold">Объём</th>
                        <th className="pb-1 text-left font-semibold">TTL</th>
                        <th className="pb-1 text-left font-semibold">Комиссия</th>
                        <th className="pb-1 text-left font-semibold">Статус</th>
                        <th className="pb-1 text-left font-semibold"></th>
                      </tr>
                    </thead>
                    <tbody>
                      {asks.map(order => (
                        <tr key={order.id} className="odd:bg-white/60 even:bg-white/30">
                          <td className="py-1 pr-2">{formatNumber(order.price)}</td>
                          <td className="py-1 pr-2">
                            {formatNumber(order.remaining)} / {formatNumber(order.quantity)}
                          </td>
                          <td className="py-1 pr-2">{computeTtl(order.expiresAt)}</td>
                          <td className="py-1 pr-2">{estimateFee(order)}</td>
                          <td className="py-1 pr-2">{order.status}</td>
                          <td className="py-1 text-right">
                            {order.status === "open" ? (
                              <Button variant="ghost" size="sm" onClick={() => handleCancel(order.id)} disabled={loading}>
                                Отменить
                              </Button>
                            ) : (
                              <span className="text-zinc-400">—</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </div>

            <div className="rounded-md border border-zinc-200 bg-white/60 p-3">
              <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Последние сделки</div>
              {recentTrades.length === 0 ? (
                <div className="text-xs text-zinc-500">Пока сделок нет</div>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-zinc-500">
                      <th className="pb-1 text-left font-semibold">Время</th>
                      <th className="pb-1 text-left font-semibold">Цена</th>
                      <th className="pb-1 text-left font-semibold">Объём</th>
                    </tr>
                  </thead>
                  <tbody>
                    {recentTrades.map(trade => (
                      <tr key={trade.id} className="odd:bg-white even:bg-white/40">
                        <td className="py-1 pr-2">{formatTimestamp(trade.timestamp)}</td>
                        <td className="py-1 pr-2">{formatNumber(trade.price)}</td>
                        <td className="py-1">{formatNumber(trade.quantity)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </>
        )}
        </section>
      </div>
      </CardContent>
    </Card>
  );
}
