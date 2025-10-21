import { useEffect, useState } from "react";
import eventsClient from "./eventsClient";

export type RawEvent = Record<string, any>;

export function useEvents() {
  const [events, setEvents] = useState<RawEvent[]>([]);
  useEffect(() => {
    let mounted = true;
    const handle = (evs: RawEvent[]) => {
      if (!mounted) return;
      setEvents((prev) => [...evs, ...prev].slice(0, 500));
    };
    // batched generic subscribe for all events
    const off = eventsClient.onEventBatch("*", handle);
    // try to connect
    eventsClient.start().catch(() => {});
    return () => {
      mounted = false;
      off();
    };
  }, []);

  return {
    events,
  };
}

export function useWeather(initial?: any) {
  const [weather, setWeather] = useState(initial ?? null);
  useEffect(() => {
    const off = eventsClient.onWeather((w) => setWeather(w));
    eventsClient.start().catch(() => {});
    return () => off();
  }, []);
  return weather;
}
