import { useEffect, useMemo, useRef, useState } from "react";

export type GameEventDto = {
  id?: string;
  Id?: string;
  timestamp?: string;
  Timestamp?: string;
  type?: string;
  Type?: string;
  location?: string;
  Location?: string;
  payloadJson?: string;
  PayloadJson?: string;
  payload?: unknown;
  Payload?: unknown;
};

type LatestEventState = {
  event: GameEventDto | null;
  loading: boolean;
  error?: string;
};

type UseLatestEventOptions = {
  /**
   * Additional event types to try if the primary type has no recent entries.
   */
  fallbackTypes?: string[];
  /**
   * Interval in milliseconds for background refresh. Use 0 to disable polling.
   */
  refreshMs?: number;
};

async function fetchLatest(type: string): Promise<GameEventDto | null> {
  if (!type) {
    return null;
  }

  const params = new URLSearchParams({
    type,
    count: "1",
  });

  const res = await fetch(`/api/events?${params.toString()}`);
  if (!res.ok) {
    throw new Error(`api/events?type=${type} вернул статус ${res.status}`);
  }

  const data = await res.json();
  if (!Array.isArray(data) || data.length === 0) {
    return null;
  }

  return data[0] as GameEventDto;
}

function parsePayload(event: GameEventDto | null): unknown {
  if (!event) return null;
  const raw =
    event.payloadJson ??
    event.PayloadJson ??
    event.payload ??
    event.Payload ??
    null;

  if (raw == null) return null;

  if (typeof raw !== "string") return raw;

  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

export function useLatestEvent(
  type: string,
  options: UseLatestEventOptions = {},
) {
  const { fallbackTypes = [], refreshMs = 30_000 } = options;
  const [state, setState] = useState<LatestEventState>({
    event: null,
    loading: true,
  });
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const fallbackKey = useMemo(() => fallbackTypes.join(","), [fallbackTypes]);

  useEffect(() => {
    let timer: number | undefined;
    const run = async () => {
      try {
        if (mountedRef.current) {
          setState((prev) => ({ ...prev, loading: true, error: undefined }));
        }

        let latest = await fetchLatest(type);

        if (!latest && fallbackTypes.length > 0) {
          for (const extra of fallbackTypes) {
            latest = await fetchLatest(extra);
            if (latest) break;
          }
        }

        if (mountedRef.current) {
          setState({ event: latest ?? null, loading: false, error: undefined });
        }
      } catch (err) {
        const message =
          err instanceof Error ? err.message : "Не удалось загрузить событие";
        if (mountedRef.current) {
          setState({ event: null, loading: false, error: message });
        }
      }
    };

    run();
    if (refreshMs > 0) {
      timer = window.setInterval(run, refreshMs);
    }

    return () => {
      if (timer) {
        window.clearInterval(timer);
      }
    };
  }, [type, refreshMs, fallbackKey]);

  const payload = useMemo(() => parsePayload(state.event), [state.event]);

  const timestamp =
    state.event?.timestamp ?? state.event?.Timestamp ?? undefined;
  const typeName = state.event?.type ?? state.event?.Type ?? type;

  return {
    event: state.event,
    payload,
    loading: state.loading,
    error: state.error,
    timestamp,
    type: typeName,
  };
}
