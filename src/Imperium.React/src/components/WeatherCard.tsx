import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";

type WeatherSnapshot = {
  id?: string;
  Id?: string;
  timestamp?: string;
  Timestamp?: string;
  condition?: string;
  Condition?: string;
  temperatureC?: number;
  TemperatureC?: number;
  windKph?: number;
  WindKph?: number;
  precipitationMm?: number;
  PrecipitationMm?: number;
};

type WeatherCardProps = {
  className?: string;
};

const WEATHER_STREAM_URL = "/api/weather/stream";
const WEATHER_LATEST_URL = "/api/weather/latest/db";

export default function WeatherCard({ className }: WeatherCardProps) {
  const [snapshot, setSnapshot] = useState<WeatherSnapshot | null>(null);
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">(
    "idle",
  );
  const [error, setError] = useState<string | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;

    const loadLatest = async () => {
      setStatus((prev) => (prev === "ready" ? prev : "loading"));
      try {
        const res = await fetch(WEATHER_LATEST_URL);
        if (!res.ok) throw new Error(`Статус ${res.status}`);
        const data = (await res.json()) as WeatherSnapshot;
        if (!cancelled) {
          setSnapshot(data);
          setStatus("ready");
          setError(undefined);
        }
      } catch (err) {
        if (!cancelled) {
          setStatus("error");
          setError(
            err instanceof Error ? err.message : "Не удалось загрузить погоду",
          );
        }
      }
    };

    loadLatest();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const es = new EventSource(WEATHER_STREAM_URL);

    es.onmessage = (ev) => {
      try {
        const parsed = JSON.parse(ev.data) as WeatherSnapshot;
        setSnapshot(parsed);
        setStatus("ready");
        setError(undefined);
      } catch {
        // ignore malformed payloads
      }
    };

    es.onerror = () => {
      es.close();
    };

    return () => {
      es.close();
    };
  }, []);

  const normalized = useMemo(() => {
    if (!snapshot) return null;
    return {
      timestamp: snapshot.timestamp ?? snapshot.Timestamp ?? null,
      condition: snapshot.condition ?? snapshot.Condition ?? "—",
      temperatureC:
        snapshot.temperatureC ?? snapshot.TemperatureC ?? undefined,
      windKph: snapshot.windKph ?? snapshot.WindKph ?? undefined,
      precipitationMm:
        snapshot.precipitationMm ?? snapshot.PrecipitationMm ?? undefined,
    };
  }, [snapshot]);

  if (status === "loading" && !normalized) {
    return (
      <div
        className={cn(
          "rounded-lg border border-slate-200 bg-white/60 p-4 text-sm text-slate-500 shadow-sm",
          className,
        )}
      >
        Загрузка последнего снимка погоды...
      </div>
    );
  }

  if (status === "error" && !normalized) {
    return (
      <div
        className={cn(
          "rounded-lg border border-red-200 bg-red-50/80 p-4 text-sm text-red-600",
          className,
        )}
      >
        Погода недоступна: {error ?? "Ошибка загрузки"}
      </div>
    );
  }

  if (!normalized) {
    return (
      <div
        className={cn(
          "rounded-lg border border-slate-200 bg-white/60 p-4 text-sm text-slate-500 shadow-sm",
          className,
        )}
      >
        Нет погодных данных
      </div>
    );
  }

  return (
    <div
      className={cn(
        "rounded-xl border border-slate-200 bg-gradient-to-b from-white/80 via-white/70 to-slate-50/60 p-4 shadow-sm",
        "text-slate-800",
        className,
      )}
    >
      <div className="text-xs uppercase tracking-wide text-slate-500">
        Обновлено{" "}
        {normalized.timestamp
          ? new Date(normalized.timestamp).toLocaleString()
          : "неизвестно"}
      </div>
      <div className="mt-2 text-2xl font-semibold">
        {normalized.condition}{" "}
        {normalized.temperatureC != null
          ? `— ${normalized.temperatureC}°C`
          : ""}
      </div>
      <div className="mt-3 grid grid-cols-2 gap-3 text-sm text-slate-600">
        <div className="rounded-lg border border-slate-100 bg-white/70 px-3 py-2">
          <div className="text-xs uppercase tracking-wide text-slate-400">
            Осадки
          </div>
          <div className="text-base font-medium text-slate-900">
            {normalized.precipitationMm != null
              ? `${normalized.precipitationMm} мм`
              : "—"}
          </div>
        </div>
        <div className="rounded-lg border border-slate-100 bg-white/70 px-3 py-2">
          <div className="text-xs uppercase tracking-wide text-slate-400">
            Ветер
          </div>
          <div className="text-base font-medium text-slate-900">
            {normalized.windKph != null
              ? `${normalized.windKph} км/ч`
              : "—"}
          </div>
        </div>
      </div>
      {status === "error" && (
        <div className="mt-2 text-xs text-red-500">{error}</div>
      )}
    </div>
  );
}
