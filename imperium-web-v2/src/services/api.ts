
const API_BASE = import.meta.env.VITE_API_URL ?? "http://localhost:5000";

export type DecreeResult = { ok: boolean; message: string };

export async function sendDecree(text: string): Promise<DecreeResult> {
  await new Promise(r => setTimeout(r, 1000));
  const messages = [
    "⚖️ Совет: Указ принят. Налог на зерно снижен на 10%.",
    "⚖️ Совет: Вопрос отправлен на доработку. Ждём расчёты EconomyAI.",
    "⚖️ Совет: Указ принят частично. Введена отсрочка на месяц."
  ];
  return { ok: true, message: messages[Math.floor(Math.random() * messages.length)] };
}

export type CharacterSummary = {
  id: string;
  name: string;
  age: number;
  status: string | null;
  gender?: string | null;
  locationName?: string | null;
};

export async function fetchCharacters(gender?: string, signal?: AbortSignal): Promise<CharacterSummary[]> {
  const url = new URL("/api/characters", API_BASE);
  if (gender && gender !== "any") {
    url.searchParams.set("gender", gender);
  }

  const response = await fetch(url.toString(), { signal });
  if (!response.ok) {
    throw new Error(`Не удалось получить список персонажей: ${response.status}`);
  }

  const data = await response.json();
  if (!Array.isArray(data)) {
    throw new Error("Ответ API не соответствует ожидаемому формату");
  }
  return data as CharacterSummary[];
}

export type MarketOrderSummary = {
  id: string;
  ownerId: string;
  ownerType: string;
  locationId: string | null;
  item: string;
  side: string;
  price: number;
  quantity: number;
  remaining: number;
  status: string;
  reservedFunds?: number;
  reservedQty?: number;
  createdAt: string;
  updatedAt?: string;
  expiresAt?: string | null;
};

export async function fetchMarketOrders(
  options?: { item?: string; ownerId?: string; ownerType?: string },
  signal?: AbortSignal
): Promise<MarketOrderSummary[]> {
  const url = new URL("/api/economy/orders", API_BASE);
  if (options?.item) url.searchParams.set("item", options.item);
  if (options?.ownerId) url.searchParams.set("ownerId", options.ownerId);
  if (options?.ownerType) url.searchParams.set("ownerType", options.ownerType);

  const response = await fetch(url.toString(), { signal });
  if (!response.ok) {
    throw new Error(`Не удалось получить ордера: ${response.status}`);
  }
  const data = await response.json();
  if (!Array.isArray(data)) {
    throw new Error("Ответ /api/economy/orders имеет неожиданный формат");
  }
  return data as MarketOrderSummary[];
}

export type TradeSummary = {
  id: string;
  timestamp: string;
  locationId: string | null;
  item: string;
  price: number;
  quantity: number;
  buyOrderId: string;
  sellOrderId: string;
  buyerId: string;
  sellerId: string;
};

export async function fetchTrades(options?: { item?: string; locationId?: string }, signal?: AbortSignal): Promise<TradeSummary[]> {
  const url = new URL("/api/economy/trades", API_BASE);
  if (options?.item) url.searchParams.set("item", options.item);
  if (options?.locationId) url.searchParams.set("locationId", options.locationId);

  const response = await fetch(url.toString(), { signal });
  if (!response.ok) {
    throw new Error(`Не удалось получить сделки: ${response.status}`);
  }
  const data = await response.json();
  if (!Array.isArray(data)) {
    throw new Error("Ответ /api/economy/trades имеет неожиданный формат");
  }
  return data as TradeSummary[];
}

export type HouseholdSummary = {
  id: string;
  name: string;
  locationId: string | null;
  headId: string | null;
  members: string[];
  wealth: number;
};

export type HouseholdDetails = HouseholdSummary & {
  membersDetail: { id: string; name: string; status: string | null; locationName: string | null }[];
};

export async function fetchHouseholds(signal?: AbortSignal): Promise<HouseholdSummary[]> {
  const response = await fetch(new URL("/api/households", API_BASE), { signal });
  if (!response.ok) throw new Error(`Не удалось получить семьи: ${response.status}`);
  const data = await response.json();
  if (!Array.isArray(data)) throw new Error("Ответ /api/households имеет неожиданный формат");
  return data as HouseholdSummary[];
}

export async function fetchHousehold(id: string, signal?: AbortSignal): Promise<HouseholdDetails> {
  const response = await fetch(new URL(`/api/households/${id}`, API_BASE), { signal });
  if (response.status === 404) throw new Error("Семья не найдена");
  if (!response.ok) throw new Error(`Не удалось загрузить семью: ${response.status}`);
  const data = await response.json();
  const membersDetail = Array.isArray(data.members)
    ? (data.members as { id: string; name: string; status: string | null; locationName: string | null }[])
    : [];
  return {
    id: data.id,
    name: data.name,
    locationId: data.locationId,
    headId: data.headId,
    members: Array.isArray(data.memberIds) ? data.memberIds : [],
    wealth: data.wealth ?? 0,
    membersDetail
  };
}

export type InventoryRow = {
  id: string;
  ownerId: string;
  ownerType: string;
  item: string;
  quantity: number;
  locationId: string | null;
};

export async function fetchInventory(options: { ownerId?: string; ownerType?: string }, signal?: AbortSignal): Promise<InventoryRow[]> {
  const url = new URL("/api/economy/inventory", API_BASE);
  if (options.ownerId) url.searchParams.set("ownerId", options.ownerId);
  if (options.ownerType) url.searchParams.set("ownerType", options.ownerType);
  const response = await fetch(url.toString(), { signal });
  if (!response.ok) throw new Error(`Не удалось получить инвентарь: ${response.status}`);
  const data = await response.json();
  if (!Array.isArray(data)) throw new Error("Ответ /api/economy/inventory имеет неожиданный формат");
  return data as InventoryRow[];
}

export type LocationSummary = { id: string; name: string; latitude: number | null; longitude: number | null };

export async function fetchLocations(signal?: AbortSignal): Promise<LocationSummary[]> {
  const response = await fetch(new URL("/api/locations", API_BASE), { signal });
  if (!response.ok) throw new Error(`Не удалось получить локации: ${response.status}`);
  const data = await response.json();
  if (!Array.isArray(data)) throw new Error("Ответ /api/locations имеет неожиданный формат");
  return data as LocationSummary[];
}

export async function fetchEconomyItems(signal?: AbortSignal): Promise<string[]> {
  const response = await fetch(new URL("/api/economy/items", API_BASE), { signal });
  if (!response.ok) throw new Error(`Не удалось загрузить список товаров: ${response.status}`);
  const data = await response.json();
  if (!Array.isArray(data)) throw new Error("Ответ /api/economy/items имеет неожиданный формат");
  return data as string[];
}

export type CreateOrderPayload = {
  ownerId: string;
  ownerType: "character" | "household";
  locationId: string;
  item: string;
  side: "buy" | "sell";
  price: number;
  quantity: number;
  expiresAt?: string;
};

export async function createMarketOrder(payload: CreateOrderPayload, signal?: AbortSignal): Promise<MarketOrderSummary> {
  const response = await fetch(new URL("/api/economy/orders", API_BASE), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
    signal
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Ошибка создания ордера (${response.status}): ${text}`);
  }
  return (await response.json()) as MarketOrderSummary;
}

export async function cancelMarketOrder(id: string, signal?: AbortSignal): Promise<void> {
  const response = await fetch(new URL(`/api/economy/orders/${id}`, API_BASE), { method: "DELETE", signal });
  if (response.status === 404) throw new Error("Ордер уже отсутствует");
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Ошибка отмены ордера (${response.status}): ${text}`);
  }
}
