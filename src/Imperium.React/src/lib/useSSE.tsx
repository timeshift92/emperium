import { useEffect, useRef, useState } from "react";

export function useSSE<T>(url: string) {
  const [messages, setMessages] = useState<T[]>([]);
  const esRef = useRef<EventSource | null>(null);

  useEffect(() => {
    let stopped = false;
    let retryMs = 1500;
    let retryTimer: number | undefined;

    const cleanup = () => {
      if (retryTimer) {
        window.clearTimeout(retryTimer);
        retryTimer = undefined;
      }
      if (esRef.current) {
        try {
          esRef.current.close();
        } catch {
          // ignore close issues
        }
        esRef.current = null;
      }
    };

    const connect = () => {
      if (stopped) return;
      cleanup();
      const es = new EventSource(url, { withCredentials: false } as any);
      esRef.current = es;
      es.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data) as T;
          setMessages((prev) => [data, ...prev].slice(0, 200));
          retryMs = 1500;
        } catch {
          // игнорируем некорректные payload
        }
      };
      es.onerror = () => {
        if (stopped) return;
        retryTimer = window.setTimeout(() => {
          retryMs = Math.min(30000, Math.floor(retryMs * 1.5));
          connect();
        }, retryMs);
      };
    };

    connect();

    return () => {
      stopped = true;
      cleanup();
    };
  }, [url]);

  return messages;
}
