import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "./ui/card";
import { Button } from "./ui/button";
import {
  cancelMarketOrder,
  createMarketOrder,
  fetchEconomyItems,
  fetchHousehold,
  fetchHouseholds,
  fetchInventory,
  fetchLocations,
  fetchMarketOrders,
  HouseholdDetails,
  HouseholdSummary,
  InventoryRow,
  LocationSummary,
  MarketOrderSummary
} from "../services/api";

type OrderFormState = {
  side: "buy" | "sell";
  item: string;
  price: string;
  quantity: string;
  locationId: string;
  ttlMinutes: string;
};

const INITIAL_FORM: OrderFormState = {
  side: "buy",
  item: "grain",
  price: "10",
  quantity: "5",
  locationId: "",
  ttlMinutes: "15"
};

function toNumber(value: string, fallback = 0): number {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? parsed : fallback;
}

function formatAmount(value: number, fraction = 2) {
  return value.toLocaleString("ru-RU", { minimumFractionDigits: fraction, maximumFractionDigits: fraction });
}

export default function HouseholdPanel() {
  const [households, setHouseholds] = useState<HouseholdSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [details, setDetails] = useState<HouseholdDetails | null>(null);
  const [inventory, setInventory] = useState<InventoryRow[]>([]);
  const [orders, setOrders] = useState<MarketOrderSummary[]>([]);
  const [locations, setLocations] = useState<LocationSummary[]>([]);
  const [items, setItems] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<OrderFormState>(INITIAL_FORM);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadHouseholds = useCallback(async (signal?: AbortSignal) => {
    const list = await fetchHouseholds(signal);
    setHouseholds(list);
    if (list.length > 0 && !selectedId) {
      setSelectedId(list[0].id);
    }
  }, [selectedId]);

  const loadItemsAndLocations = useCallback(async (signal?: AbortSignal) => {
    const [locs, goods] = await Promise.all([fetchLocations(signal), fetchEconomyItems(signal)]);
    setLocations(locs);
    setItems(goods);
    if (!form.locationId && locs.length > 0) {
      setForm(prev => ({ ...prev, locationId: locs[0].id }));
    }
    if (!goods.includes(form.item) && goods.length > 0) {
      setForm(prev => ({ ...prev, item: goods[0] }));
    }
  }, [form.item, form.locationId]);

  const loadHouseholdData = useCallback(
    async (householdId: string, signal?: AbortSignal) => {
      const [info, inv, ords] = await Promise.all([
        fetchHousehold(householdId, signal),
        fetchInventory({ ownerId: householdId, ownerType: "household" }, signal),
        fetchMarketOrders({ ownerId: householdId, ownerType: "household" }, signal)
      ]);
      setDetails(info);
      setInventory(inv);
      setOrders(ords);
      if (!form.locationId && info.locationId) {
        setForm(prev => ({ ...prev, locationId: info.locationId! }));
      }
    },
    [form.locationId]
  );

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    loadHouseholds(controller.signal)
      .catch(err => setError(err instanceof Error ? err.message : "Не удалось загрузить семьи"))
      .finally(() => setLoading(false));
    loadItemsAndLocations(controller.signal).catch(err => setError(err instanceof Error ? err.message : "Не удалось загрузить справочники"));
    return () => controller.abort();
  }, [loadHouseholds, loadItemsAndLocations]);

  useEffect(() => {
    if (!selectedId) return;
    const controller = new AbortController();
    setLoading(true);
    loadHouseholdData(selectedId, controller.signal)
      .catch(err => setError(err instanceof Error ? err.message : "Не удалось загрузить данные семьи"))
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, [selectedId, loadHouseholdData]);

  const handleSubmit = async (evt: FormEvent<HTMLFormElement>) => {
    evt.preventDefault();
    if (!selectedId) return;
    setLoading(true);
    setError(null);
    setActionMessage(null);
    try {
      const expiresAt =
        form.ttlMinutes.trim().length > 0
          ? new Date(Date.now() + toNumber(form.ttlMinutes, 15) * 60 * 1000).toISOString()
          : undefined;
      await createMarketOrder(
        {
          ownerId: selectedId,
          ownerType: "household",
          item: form.item,
          side: form.side,
          price: toNumber(form.price),
          quantity: toNumber(form.quantity),
          locationId: form.locationId,
          expiresAt
        },
        undefined
      );
      setActionMessage("Ордер размещён");
      await loadHouseholdData(selectedId);
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
      if (selectedId) await loadHouseholdData(selectedId);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось отменить ордер");
    } finally {
      setLoading(false);
    }
  };

  const householdOptions = useMemo(
    () => households.map(h => ({ value: h.id, label: h.name })),
    [households]
  );

  return (
    <Card className="shadow-md">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>🏠 Домохозяйства</CardTitle>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wide text-zinc-500">
            <label className="flex items-center gap-1">
              <span>Семья</span>
              <select
                value={selectedId ?? ""}
                onChange={event => setSelectedId(event.target.value || null)}
                className="rounded-md border border-zinc-200 bg-white px-2 py-1 text-sm focus:border-emerald-500 focus:outline-none"
              >
                {householdOptions.map(opt => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </label>
            <Button variant="outline" size="sm" onClick={() => selectedId && loadHouseholdData(selectedId)} disabled={loading}>
              Обновить
            </Button>
            {loading && <span className="text-emerald-600">загрузка…</span>}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        {error && <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-600">{error}</div>}
        {actionMessage && <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-700">{actionMessage}</div>}

        {details ? (
          <>
            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="flex flex-wrap items-center gap-4 text-xs">
                <div>
                  <span className="uppercase text-zinc-500">Название</span>
                  <div className="text-base font-semibold text-zinc-800">{details.name}</div>
                </div>
                <div>
                  <span className="uppercase text-zinc-500">Богатство</span>
                  <div className="text-base font-semibold text-emerald-700">{formatAmount(details.wealth)}</div>
                </div>
                <div>
                  <span className="uppercase text-zinc-500">Число членов</span>
                  <div className="text-base font-semibold text-zinc-800">{details.members.length}</div>
                </div>
              </div>
              {details.membersDetail.length > 0 && (
                <div className="mt-3 text-xs">
                  <div className="font-semibold text-zinc-600 mb-1">Члены семьи</div>
                  <ul className="grid gap-1 lg:grid-cols-2">
                    {details.membersDetail.map(member => (
                      <li key={member.id} className="rounded bg-white/60 px-2 py-1">
                        <span className="font-medium text-zinc-800">{member.name}</span>
                        <span className="text-zinc-500"> — {member.status ?? "неизвестно"} ({member.locationName ?? "?"})</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>

            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Инвентарь</div>
              {inventory.length === 0 ? (
                <div className="text-xs text-zinc-500">Склад пуст</div>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-zinc-500">
                      <th className="pb-1 text-left font-semibold">Товар</th>
                      <th className="pb-1 text-left font-semibold">Количество</th>
                      <th className="pb-1 text-left font-semibold">Локация</th>
                    </tr>
                  </thead>
                  <tbody>
                    {inventory.map(row => (
                      <tr key={row.id} className="odd:bg-white even:bg-white/50">
                        <td className="py-1 pr-2">{row.item}</td>
                        <td className="py-1 pr-2">{formatAmount(row.quantity)}</td>
                        <td className="py-1">{row.locationId ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Создать ордер</div>
              <form className="grid gap-2 text-xs md:grid-cols-2" onSubmit={handleSubmit}>
                <label className="flex flex-col gap-1">
                  <span>Тип</span>
                  <select value={form.side} onChange={event => setForm(prev => ({ ...prev, side: event.target.value as "buy" | "sell" }))} className="rounded border border-zinc-200 px-2 py-1">
                    <option value="buy">Покупка</option>
                    <option value="sell">Продажа</option>
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span>Товар</span>
                  <select value={form.item} onChange={event => setForm(prev => ({ ...prev, item: event.target.value }))} className="rounded border border-zinc-200 px-2 py-1">
                    {items.map(opt => (
                      <option key={opt} value={opt}>
                        {opt}
                      </option>
                    ))}
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
                    required
                    min="0"
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
                    required
                    min="0.01"
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
                    min="1"
                    step="1"
                    value={form.ttlMinutes}
                    onChange={event => setForm(prev => ({ ...prev, ttlMinutes: event.target.value }))}
                    className="rounded border border-zinc-200 px-2 py-1"
                  />
                </label>
                <div className="md:col-span-2 flex justify-end">
                  <Button type="submit" disabled={loading}>
                    Разместить
                  </Button>
                </div>
              </form>
            </div>

            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Ордеры</div>
              {orders.length === 0 ? (
                <div className="text-xs text-zinc-500">Активных ордеров нет</div>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-zinc-500">
                      <th className="pb-1 text-left font-semibold">Тип</th>
                      <th className="pb-1 text-left font-semibold">Товар</th>
                      <th className="pb-1 text-left font-semibold">Цена</th>
                      <th className="pb-1 text-left font-semibold">Остаток</th>
                      <th className="pb-1 text-left font-semibold">Статус</th>
                      <th className="pb-1 text-left font-semibold"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map(order => (
                      <tr key={order.id} className="odd:bg-white even:bg-white/40">
                        <td className="py-1 pr-2">{order.side}</td>
                        <td className="py-1 pr-2">{order.item}</td>
                        <td className="py-1 pr-2">{formatAmount(order.price)}</td>
                        <td className="py-1 pr-2">
                          {formatAmount(order.remaining)} / {formatAmount(order.quantity)}
                        </td>
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
          </>
        ) : (
          <div className="text-xs text-zinc-500">Выберите домохозяйство для просмотра.</div>
        )}
      </CardContent>
    </Card>
  );
}
