import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

type EventHandler<T> = (payload: T) => void;

class EventsClient {
  private connection: HubConnection | null = null;
  private url = "/hubs/events";
  private eventHandlers = new Map<string, Set<EventHandler<any>>>();
  private weatherHandlers = new Set<EventHandler<any>>();
  private connectionStateHandlers = new Set<(connected: boolean) => void>();
  private fallbackEventsSse?: EventSource;
  private fallbackWeatherSse?: EventSource;
  private batchedTypes = new Set<string>(["trade_executed", "order_placed"]);
  private batchIntervalMs = 300;
  private batchBuffers = new Map<string, any[]>();
  private batchTimers = new Map<string, number>();
  private batchHandlers = new Map<string, Set<(events: any[]) => void>>();

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

    this.connection.onclose(() => {
      // start fallback stream on close
      this.startFallback();
      this.notifyConnectionState(false);
    });

  // bind default messages
  this.connection.on("event", (ev: any) => this.receiveEvent(ev));
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
          this.receiveEvent(data);
        } catch {}
      };
      this.fallbackEventsSse = es;
    } catch {}
    try {
      const es2 = new EventSource("/api/weather/stream");
      es2.onmessage = (e) => {

  receiveEvent(ev: any) {
    const type = ev?.Type ?? ev?.type ?? "unknown";
    if (this.batchedTypes.has(type)) {
      let buf = this.batchBuffers.get(type);
      if (!buf) { buf = []; this.batchBuffers.set(type, buf); }
      buf.push(ev);
      if (!this.batchTimers.has(type)) {
        const timer = window.setTimeout(() => this.flushBatch(type), this.batchIntervalMs);
        this.batchTimers.set(type, timer as unknown as number);
      }
    } else {
      this.emitEvent(ev);
    }
  }

  flushBatch(type: string) {
    const buf = this.batchBuffers.get(type) ?? [];
    if (buf.length === 0) {
      this.batchBuffers.delete(type);
      this.batchTimers.delete(type);
      return;
    }
    // notify batch handlers
    const handlers = this.batchHandlers.get(type);
    if (handlers) {
      for (const h of handlers) h(buf.slice());
    }
    // also notify generic handlers bound to "*batch"
    const g = this.batchHandlers.get("*batch");
    if (g) for (const h of g) h(buf.slice());
    // clear buffer and timer
    this.batchBuffers.delete(type);
    const t = this.batchTimers.get(type);
    if (t) { window.clearTimeout(t); this.batchTimers.delete(type); }
  }
        try {
          const data = JSON.parse(e.data);
          this.emitWeather(data);
        } catch {}
      };
      this.fallbackWeatherSse = es2;
    } catch {}
  }

  emitEvent(ev: any) {
    const type = ev?.Type ?? ev?.type ?? "unknown";
    const handlers = this.eventHandlers.get(type);
    if (handlers) {
      for (const h of handlers) h(ev);
    }
    // also emit generic handlers bound to "*"
    const all = this.eventHandlers.get("*");
    if (all) for (const h of all) h(ev);
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

  onWeather(cb: EventHandler<any>) {

  onBatch(type: string, cb: (events: any[]) => void) {
    let set = this.batchHandlers.get(type);
    if (!set) { set = new Set(); this.batchHandlers.set(type, set); }
    set.add(cb);
    return () => { set!.delete(cb); };
  }

  getBatchedTypes() {
    return Array.from(this.batchedTypes);
  }
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
