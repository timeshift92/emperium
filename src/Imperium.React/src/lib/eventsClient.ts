import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

type EventHandler<T> = (payload: T) => void;

class EventsClient {
  private connection: HubConnection | null = null;
  private url = "/hubs/events";
  private eventHandlers = new Map<string, Set<EventHandler<any>>>();
  // batched handlers receive arrays of events every flush interval
  private batchEventHandlers = new Map<string, Set<(events: any[]) => void>>();
  private batchQueues = new Map<string, any[]>();
  private batchFlushTimer: number | null = null;
  private readonly batchFlushIntervalMs = 180;
  private weatherHandlers = new Set<EventHandler<any>>();
  private connectionStateHandlers = new Set<(connected: boolean) => void>();
  private fallbackEventsSse?: EventSource;
  private fallbackWeatherSse?: EventSource;

  async start() {
    if (this.connection) return;
    this.connection = new HubConnectionBuilder()
      .withUrl(this.url)
      .configureLogging(LogLevel.Information)
      .withAutomaticReconnect()
      .build();

    this.connection.onreconnected(() => {
      this.stopFallback();
      this.notifyConnectionState(true);
    });

    this.connection.onreconnecting(() => {
      // start fallback immediately so UI keeps receiving updates while reconnecting
      this.startFallback();
      this.notifyConnectionState(false);
    });

    this.connection.onclose(() => {
      // start fallback stream on close
      this.startFallback();
      this.notifyConnectionState(false);
    });

    // bind default messages
    this.connection.on("event", (ev: any) => this.enqueueEvent(ev));
    this.connection.on("weather", (w: any) => this.emitWeather(w));

    try {
  await this.connection.start();
  this.stopFallback();
  this.notifyConnectionState(true);
    } catch (err) {
      this.startFallback();
      this.notifyConnectionState(false);
    }
  }

  stopFallback() {
    if (this.fallbackEventsSse) {
      try {
        this.fallbackEventsSse.close();
      } catch {}
      this.fallbackEventsSse = undefined;
    }
    if (this.fallbackWeatherSse) {
      try {
        this.fallbackWeatherSse.close();
      } catch {}
      this.fallbackWeatherSse = undefined;
    }
  }

  startFallback() {
  if (this.fallbackEventsSse || this.fallbackWeatherSse) return;
    try {
      const es = new EventSource("/api/events/stream");
      es.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data);
          this.enqueueEvent(data);
        } catch {}
      };
      this.fallbackEventsSse = es;
    } catch {}
    try {
      const es2 = new EventSource("/api/weather/stream");
      es2.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data);
          this.emitWeather(data);
        } catch {}
      };
      this.fallbackWeatherSse = es2;
    } catch {}
  }

  private enqueueEvent(ev: any) {
    // immediate per-type handlers
    const type = ev?.Type ?? ev?.type ?? "unknown";
    const handlers = this.eventHandlers.get(type);
    if (handlers) for (const h of handlers) h(ev);

    // queue for batched subscribers (per-type and wildcard)
    const q = this.batchQueues.get(type) ?? [];
    q.push(ev);
    this.batchQueues.set(type, q);

    const qAll = this.batchQueues.get("*") ?? [];
    qAll.push(ev);
    this.batchQueues.set("*", qAll);

    if (this.batchFlushTimer == null) {
      this.batchFlushTimer = window.setTimeout(() => this.flushBatches(), this.batchFlushIntervalMs);
    }
  }

  private flushBatches() {
    this.batchFlushTimer = null;
    const queues = this.batchQueues;
    this.batchQueues = new Map();
    for (const [type, arr] of queues) {
      if (!arr || arr.length === 0) continue;
      const handlers = this.batchEventHandlers.get(type);
      if (!handlers || handlers.size === 0) continue;
      for (const h of handlers) {
        try {
          h(arr.slice());
        } catch {}
      }
    }
  }

  emitWeather(w: any) {
    for (const h of this.weatherHandlers) h(w);
  }

  onEvent(type: string, cb: EventHandler<any>) {
    let set = this.eventHandlers.get(type);
    if (!set) {
      set = new Set();
      this.eventHandlers.set(type, set);
    }
    set.add(cb);
  return () => { set!.delete(cb); };
  }

  onEventBatch(type: string, cb: (events: any[]) => void) {
    let set = this.batchEventHandlers.get(type);
    if (!set) {
      set = new Set();
      this.batchEventHandlers.set(type, set);
    }
    set.add(cb);
    return () => { set!.delete(cb); };
  }

  onWeather(cb: EventHandler<any>) {
    this.weatherHandlers.add(cb);
  return () => { this.weatherHandlers.delete(cb); };
  }

  onConnectionChange(cb: (connected: boolean) => void) {
    this.connectionStateHandlers.add(cb);
    return () => { this.connectionStateHandlers.delete(cb); };
  }

  private notifyConnectionState(state: boolean) {
    for (const cb of this.connectionStateHandlers) cb(state);
  }

  stop() {
    try {
      this.stopFallback();
      this.connection?.stop();
    } catch {}
  this.connection = null;
  }
}

export const eventsClient = new EventsClient();

export default eventsClient;
