import { cn } from "@/lib/utils";
import { useLatestEvent } from "@/lib/useLatestEvent";

type EconomyPanelProps = {
  className?: string;
};

export default function EconomyPanel({ className }: EconomyPanelProps) {
  const snapshot = useLatestEvent("economy_snapshot", { refreshMs: 45_000 });
  const transport = useLatestEvent("transport_job", { refreshMs: 45_000 });

  const payload =
    snapshot.payload && typeof snapshot.payload === "object"
      ? (snapshot.payload as Record<string, unknown>)
      : undefined;

  const transportPayload =
    transport.payload && typeof transport.payload === "object"
      ? (transport.payload as Record<string, unknown>)
      : undefined;

  const economyDetails = payload as {
    snapshotId?: string;
    avgPrecip?: number;
    descriptionRu?: string;
  };

  const transportDetails = transportPayload as {
    from?: string;
    to?: string;
    profit?: number | string;
    descriptionRu?: string;
  };

  return (
    <div
      className={cn(
        "rounded-xl border border-slate-200 bg-white/70 p-4 shadow-sm",
        "text-slate-800",
        className,
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-lg font-semibold text-slate-900">
            Экономический снимок
          </h3>
          <p className="text-xs text-slate-500">
            Последнее событие economy_snapshot и связанные транспортные задачи.
          </p>
        </div>
        <div className="text-xs uppercase tracking-wide text-slate-400">
          {snapshot.timestamp
            ? new Date(snapshot.timestamp).toLocaleString()
            : "—"}
        </div>
      </div>

      {snapshot.loading && (
        <div className="mt-4 rounded border border-dashed border-slate-200 bg-white/60 px-3 py-2 text-sm text-slate-500">
          Загрузка экономических данных...
        </div>
      )}

      {snapshot.error && (
        <div className="mt-4 rounded border border-red-200 bg-red-50/80 px-3 py-2 text-sm text-red-600">
          Ошибка: {snapshot.error}
        </div>
      )}

      {!snapshot.loading && !payload && !snapshot.error && (
        <div className="mt-4 rounded border border-dashed border-slate-200 bg-white/60 px-3 py-2 text-sm text-slate-500">
          Снимков экономики пока нет.
        </div>
      )}

      {payload && (
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
            <div className="text-xs uppercase tracking-wide text-slate-400">
              Средние осадки
            </div>
            <div className="mt-1 text-xl font-semibold text-slate-900">
              {typeof economyDetails?.avgPrecip === "number"
                ? `${economyDetails.avgPrecip.toFixed(2)} мм`
                : "—"}
            </div>
            <div className="mt-1 text-xs text-slate-500">
              Влияют на стоимость зерна
            </div>
          </div>
          <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
            <div className="text-xs uppercase tracking-wide text-slate-400">
              Snapshot Id
            </div>
            <div className="mt-1 text-sm font-medium text-slate-900 break-all">
              {typeof economyDetails?.snapshotId === "string"
                ? economyDetails.snapshotId
                : "—"}
            </div>
            <div className="mt-1 text-xs text-slate-500">
              Используйте для запроса подробностей из dev-инструментов
            </div>
          </div>
        </div>
      )}

      {typeof economyDetails?.descriptionRu === "string" && (
        <div className="mt-3 rounded-lg border border-slate-200 bg-white/60 px-4 py-3 text-sm text-slate-700">
          {economyDetails.descriptionRu}
        </div>
      )}

      <div className="mt-6 rounded-lg border border-indigo-100 bg-indigo-50/70 px-4 py-3 text-sm text-indigo-800">
        <div className="flex items-center justify-between gap-3">
          <div className="font-semibold">Транспортные задачи</div>
          <div className="text-xs uppercase tracking-wide text-indigo-500">
            {transport.timestamp
              ? new Date(transport.timestamp).toLocaleString()
              : "—"}
          </div>
        </div>
        {transport.loading && (
          <div className="mt-2 text-xs text-indigo-600">
            Загрузка последних задач...
          </div>
        )}
        {!transport.loading && transportPayload ? (
          <ul className="mt-2 space-y-1 text-xs">
            <li>
              Из{" "}
              <span className="font-medium">
                {String(transportDetails?.from ?? "—")}
              </span>{" "}
              в{" "}
              <span className="font-medium">
                {String(transportDetails?.to ?? "—")}
              </span>
            </li>
            {transportDetails?.profit != null && (
              <li>
                Прибыль:{" "}
                <span className="font-semibold">
                  {String(transportDetails.profit)}
                </span>
              </li>
            )}
            {typeof transportDetails?.descriptionRu === "string" && (
              <li>{transportDetails.descriptionRu}</li>
            )}
          </ul>
        ) : (
          !transport.loading && (
            <div className="mt-2 text-xs text-indigo-600">
              Нет активных задач.
            </div>
          )
        )}
      </div>
    </div>
  );
}
