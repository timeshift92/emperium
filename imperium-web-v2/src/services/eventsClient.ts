import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useWorldStore } from "../store/useWorldStore";
import { startFallbackStream } from "./mockStream";

const ECONOMY_EVENT_TYPES = new Set(["order_placed", "trade_executed", "economy_snapshot", "order_cancelled", "inventory_change", "economy_alert"]);
const RETRY_DELAY_MS = 5000;

let connection: HubConnection | null = null;
let stopFallback: (() => void) | null = null;

const ensureFallbackStopped = () => {
  if (stopFallback) {
    stopFallback();
    stopFallback = null;
  }
};

const activateFallback = () => {
  if (!stopFallback) {
    stopFallback = startFallbackStream();
  }
};

const scheduleRestart = () => {
  window.setTimeout(() => {
    void startEventStream();
  }, RETRY_DELAY_MS);
};

export async function startEventStream(): Promise<void> {
  if (connection) return;
  const state = useWorldStore.getState();
  const addEvent = state.addEvent;
  const addNpcReply = state.addNpcReply;
  const bumpEconomy = state.bumpEconomy;

  const baseUrl = (import.meta.env.VITE_API_URL ?? "http://localhost:5000").replace(/\/$/, "");
  const hubUrl = `${baseUrl}/hubs/events`;

  connection = new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Error)
    .build();

  connection.on("event", (ev: any) => {
    if (!ev) return;
    const payload = normalizePayload(ev.payloadJson);
    const type = typeof ev.type === "string" ? ev.type : "unknown";

    addEvent({ id: ev.id ?? crypto.randomUUID(), type, payloadJson: payload });

    if (type === "npc_reply" && ev.payloadJson) {
      try {
        addNpcReply(typeof ev.payloadJson === "string" ? ev.payloadJson : JSON.stringify(ev.payloadJson));
      } catch (err) {
        console.error("Failed to add npc reply", err);
      }
    }

    if (ECONOMY_EVENT_TYPES.has(type.toLowerCase())) {
      bumpEconomy();
    }
  });

  connection.on("weather", (weather: any) => {
    addEvent({
      id: crypto.randomUUID(),
      type: "weather_snapshot",
      payloadJson: normalizePayload(weather)
    });
  });

  connection.onreconnecting(() => {
    activateFallback();
  });

  connection.onreconnected(() => {
    ensureFallbackStopped();
  });

  connection.onclose(() => {
    connection = null;
    activateFallback();
    scheduleRestart();
  });

  try {
    await connection.start();
    ensureFallbackStopped();
  } catch (err) {
    console.error("SignalR connection failed", err);
    connection = null;
    activateFallback();
    scheduleRestart();
  }
}

function normalizePayload(payload: unknown) {
  if (payload == null) return {};
  if (typeof payload === "string") {
    try {
      return JSON.parse(payload);
    } catch {
      return { text: payload };
    }
  }
  return payload;
}
