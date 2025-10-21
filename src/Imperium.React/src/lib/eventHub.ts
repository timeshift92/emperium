import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

type RawEvent = Record<string, unknown>;
type GameEventListener = (event: RawEvent) => void;
type EconomyListener = () => void;
type WeatherListener = (snapshot: unknown) => void;

const ECONOMY_EVENT_TYPES = new Set(
  [
    "order_placed",
    "order_cancelled",
    "trade_executed",
    "economy_snapshot",
    "inventory_change",
    "logistics_job_enqueued",
    "logistics_job_completed",
  ].map((t) => t.toLowerCase()),
);

const baseUrl = (import.meta.env.VITE_API_URL ?? "").replace(/\/$/, "");
const hubUrl = `${baseUrl}/hubs/events`;
const eventsStreamUrl = `${baseUrl}/api/events/stream`;
const weatherStreamUrl = `${baseUrl}/api/weather/stream`;

const eventListeners = new Set<GameEventListener>();
const economyListeners = new Set<EconomyListener>();
const weatherListeners = new Set<WeatherListener>();

const recentEvents: RawEvent[] = [];
const MAX_RECENT = 200;

let connection: HubConnection | null = null;
let connecting = false;
let restartTimer: number | null = null;
let fallbackEvents: EventSource | null = null;
let fallbackWeather: EventSource | null = null;
let economyNotifyScheduled = false;

const getEventType = (ev: RawEvent): string => {
  const value =
    ev.type ??
    ev.Type ??
    (ev as { eventType?: string }).eventType ??
    (ev as { EventType?: string }).EventType ??
    "";
  return String(value).toLowerCase();
};

// event id extraction handled by consumers

const notifyWeather = (snapshot: unknown) => {
  weatherListeners.forEach((listener) => {
    try {
      listener(snapshot);
    } catch (err) {
      console.error("Weather listener failed", err);
    }
  });
};

const triggerEconomyListeners = () => {
  if (economyNotifyScheduled) return;
  economyNotifyScheduled = true;
  window.setTimeout(() => {
    economyNotifyScheduled = false;
    economyListeners.forEach((listener) => {
      try {
        listener();
      } catch (err) {
        console.error("Economy listener failed", err);
      }
    });
  }, 400);
};

const notifyEvent = (event: RawEvent) => {
  recentEvents.unshift(event);
  if (recentEvents.length > MAX_RECENT) {
    recentEvents.length = MAX_RECENT;
  }

  eventListeners.forEach((listener) => {
    try {
      listener(event);
    } catch (err) {
      console.error("Event listener failed", err);
    }
  });

  const type = getEventType(event);
  if (type === "weather_snapshot") {
    const payload =
      (event.payloadJson as string | undefined) ??
      (event.PayloadJson as string | undefined);
    if (payload) {
      try {
        notifyWeather(JSON.parse(payload));
      } catch {
        notifyWeather(payload);
      }
    } else {
      notifyWeather(event.payload ?? event.Payload ?? event);
    }
  }

  if (ECONOMY_EVENT_TYPES.has(type)) {
    triggerEconomyListeners();
  }
};

const buildWeatherEvent = (snapshot: unknown): RawEvent => {
  const payloadJson =
    typeof snapshot === "string" ? snapshot : JSON.stringify(snapshot ?? {});
  return {
    Id: crypto.randomUUID(),
    Timestamp: new Date().toISOString(),
    Type: "weather_snapshot",
    PayloadJson: payloadJson,
  };
};

const ensureFallbackStopped = () => {
  if (fallbackEvents) {
    try {
      fallbackEvents.close();
    } catch {
      // ignore
    }
    fallbackEvents = null;
  }
  if (fallbackWeather) {
    try {
      fallbackWeather.close();
    } catch {
      // ignore
    }
    fallbackWeather = null;
  }
};

const startFallbackEvents = () => {
  if (fallbackEvents) return;
  const es = new EventSource(eventsStreamUrl);
  fallbackEvents = es;
  es.onmessage = (message) => {
    try {
      const parsed = JSON.parse(message.data) as RawEvent;
      notifyEvent(parsed);
    } catch (err) {
      console.warn("Failed to parse fallback event payload", err);
    }
  };
  es.onerror = () => {
    es.close();
    fallbackEvents = null;
    if (!connection) {
      window.setTimeout(startFallbackEvents, 3000);
    }
  };
};

const startFallbackWeather = () => {
  if (fallbackWeather) return;
  const es = new EventSource(weatherStreamUrl);
  fallbackWeather = es;
  es.onmessage = (message) => {
    try {
      const parsed = JSON.parse(message.data);
      const ev = buildWeatherEvent(parsed);
      notifyEvent(ev);
    } catch (err) {
      console.warn("Failed to parse fallback weather payload", err);
    }
  };
  es.onerror = () => {
    es.close();
    fallbackWeather = null;
    if (!connection) {
      window.setTimeout(startFallbackWeather, 5000);
    }
  };
};

const activateFallback = () => {
  startFallbackEvents();
  startFallbackWeather();
};

const scheduleRestart = () => {
  if (restartTimer != null) return;
  restartTimer = window.setTimeout(() => {
    restartTimer = null;
    startEventHub();
  }, 5000);
};

const handleHubEvent = (event: RawEvent) => {
  notifyEvent(event);
};

const handleHubWeather = (snapshot: unknown) => {
  const ev = buildWeatherEvent(snapshot);
  notifyEvent(ev);
};

export function startEventHub(): void {
  if (connection || connecting) return;
  connecting = true;

  const hub = new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Error)
    .build();

  hub.on("event", handleHubEvent);
  hub.on("weather", handleHubWeather);

  hub.onreconnecting(() => {
    activateFallback();
  });

  hub.onreconnected(() => {
    ensureFallbackStopped();
  });

  hub.onclose(() => {
    connection = null;
    connecting = false;
    activateFallback();
    scheduleRestart();
  });

  hub
    .start()
    .then(() => {
      connection = hub;
      connecting = false;
      ensureFallbackStopped();
    })
    .catch((err) => {
      console.error("SignalR connection failed", err);
      connection = null;
      connecting = false;
      activateFallback();
      scheduleRestart();
    });
}

export function subscribeGameEvents(listener: GameEventListener): () => void {
  eventListeners.add(listener);
  return () => {
    eventListeners.delete(listener);
  };
}

export function subscribeEconomy(listener: EconomyListener): () => void {
  economyListeners.add(listener);
  return () => {
    economyListeners.delete(listener);
  };
}

export function subscribeWeather(listener: WeatherListener): () => void {
  weatherListeners.add(listener);
  return () => {
    weatherListeners.delete(listener);
  };
}

export function getLatestEvent(
  type: string,
  fallbackTypes: string[] = [],
): RawEvent | null {
  const target = type.toLowerCase();
  const fallbackSet = new Set(fallbackTypes.map((t) => t.toLowerCase()));
  for (const ev of recentEvents) {
    const evType = getEventType(ev);
    if (evType === target || fallbackSet.has(evType)) {
      return ev;
    }
  }
  return null;
}
