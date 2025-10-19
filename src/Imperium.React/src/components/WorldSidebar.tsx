import WeatherCard from "@/components/WeatherCard";
import { cn } from "@/lib/utils";
import { useLatestEvent } from "@/lib/useLatestEvent";

type WorldSidebarProps = {
  className?: string;
};

function formatTime(payload: unknown) {
  if (!payload || typeof payload !== "object") return null;
  const data = payload as Record<string, unknown>;
  const hour = typeof data.hour === "number" ? data.hour : undefined;
  const day = typeof data.day === "number" ? data.day : undefined;
  const year = typeof data.year === "number" ? data.year : undefined;
  const tick = typeof data.tick === "number" ? data.tick : undefined;

  if (hour == null && day == null && year == null && tick == null) return null;

  return {
    hour,
    day,
    year,
    tick,
  };
}

function formatSeason(payload: unknown) {
  if (!payload || typeof payload !== "object") return null;
  const data = payload as Record<string, unknown>;
  return {
    season: typeof data.season === "string" ? data.season : undefined,
    avgTemp:
      typeof data.avgTemp === "number"
        ? data.avgTemp
        : typeof data.temperatureC === "number"
          ? data.temperatureC
          : undefined,
    avgPrecip:
      typeof data.avgPrecip === "number"
        ? data.avgPrecip
        : typeof data.precipitationMm === "number"
          ? data.precipitationMm
          : undefined,
  };
}

export default function WorldSidebar({ className }: WorldSidebarProps) {
  const timeInfo = useLatestEvent("time_tick", { refreshMs: 10_000 });
  const seasonInfo = useLatestEvent("season_change", {
    fallbackTypes: ["season_set"],
    refreshMs: 60_000,
  });

  const time = formatTime(timeInfo.payload);
  const season = formatSeason(seasonInfo.payload);

  return (
    <aside
      className={cn(
        "flex h-full w-80 shrink-0 flex-col border-r border-slate-200 bg-white/70 backdrop-blur-md",
        className,
      )}
    >
      <div className="px-5 pt-6 pb-4 border-b border-slate-200">
        <div className="text-xs uppercase tracking-wide text-slate-500">
          Статус мира
        </div>
        <div className="mt-2 text-lg font-semibold text-slate-900">
          Imperium — живой тик
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-5 py-4 space-y-6">
        <section>
          <div className="text-sm font-semibold text-slate-700">
            Время мира
          </div>
          {timeInfo.loading ? (
            <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
          ) : time ? (
            <dl className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-slate-500">Год</dt>
                <dd className="font-medium text-slate-900">
                  {time.year ?? "—"}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">День</dt>
                <dd className="font-medium text-slate-900">
                  {time.day ?? "—"}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">Час</dt>
                <dd className="font-medium text-slate-900">
                  {time.hour ?? "—"}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">Тик</dt>
                <dd className="font-medium text-slate-900">
                  {time.tick ?? "—"}
                </dd>
              </div>
            </dl>
          ) : (
            <div className="mt-2 text-sm text-slate-500">
              Нет данных о времени
            </div>
          )}
          {timeInfo.error && (
            <div className="mt-2 text-xs text-red-500">{timeInfo.error}</div>
          )}
        </section>

        <section>
          <div className="text-sm font-semibold text-slate-700">Сезон</div>
          {seasonInfo.loading ? (
            <div className="mt-2 text-sm text-slate-500">Загрузка...</div>
          ) : season ? (
            <div className="mt-3 rounded-lg border border-slate-200 bg-white/80 p-3 text-sm shadow-sm">
              <div className="text-base font-medium text-slate-900">
                {season.season ?? "Не определён"}
              </div>
              <div className="mt-2 grid grid-cols-2 gap-2 text-xs text-slate-600">
                <div>
                  <div className="uppercase tracking-wide text-slate-400">
                    t°C ср.
                  </div>
                  <div className="text-sm font-medium text-slate-900">
                    {season.avgTemp != null ? `${season.avgTemp.toFixed(1)}` : "—"}
                  </div>
                </div>
                <div>
                  <div className="uppercase tracking-wide text-slate-400">
                    Осадки
                  </div>
                  <div className="text-sm font-medium text-slate-900">
                    {season.avgPrecip != null
                      ? `${season.avgPrecip.toFixed(1)} мм`
                      : "—"}
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <div className="mt-2 text-sm text-slate-500">
              Нет данных о сезоне
            </div>
          )}
          {seasonInfo.error && (
            <div className="mt-2 text-xs text-red-500">{seasonInfo.error}</div>
          )}
        </section>

        <section>
          <div className="text-sm font-semibold text-slate-700">
            Погодная витрина
          </div>
          <div className="mt-3">
            <WeatherCard />
          </div>
        </section>
      </div>
    </aside>
  );
}
