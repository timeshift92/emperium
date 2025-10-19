import { useEffect, useRef, useState } from "react";

export function useSSE<T>(url: string) {
  const [messages, setMessages] = useState<T[]>([]);
  const esRef = useRef<EventSource | null>(null);

  useEffect(() => {
    const es = new EventSource(url, { withCredentials: false } as any);
    esRef.current = es;
    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data) as T;
        setMessages(prev => [data, ...prev].slice(0, 200));
      } catch {
        // ignore
      }
    };
    es.onerror = () => {
      es.close();
    };
    return () => { es.close(); };
  }, [url]);

  return messages;
}
