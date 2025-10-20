import { useEffect, useMemo, useState } from "react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";

type HouseholdSummary = {
  id: string;
  name: string;
  locationId?: string | null;
  headId?: string | null;
  members: string[];
  wealth?: number;
};

type HouseholdMember = {
  id: string;
  name?: string;
  status?: string;
  locationName?: string;
};

type HouseholdDetail = {
  id: string;
  name: string;
  locationId?: string | null;
  headId?: string | null;
  memberIds: string[];
  members: HouseholdMember[];
  wealth?: number;
};

type HouseholdsPanelProps = {
  className?: string;
  refreshVersion?: number;
  onSelectCharacter?: (id: string) => void;
};

export default function HouseholdsPanel({
  className,
  refreshVersion = 0,
  onSelectCharacter,
}: HouseholdsPanelProps) {
  const [households, setHouseholds] = useState<HouseholdSummary[]>([]);
  const [loadingList, setLoadingList] = useState(false);
  const [listError, setListError] = useState<string | null>(null);
  const [filter, setFilter] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<HouseholdDetail | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoadingList(true);
      try {
        const res = await fetch("/api/households");
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = (await res.json()) as HouseholdSummary[];
        if (!cancelled) {
          setHouseholds(data);
          setListError(null);
          if (!selectedId && data.length > 0) {
            setSelectedId(data[0].id);
          }
        }
      } catch {
        if (!cancelled) {
          setHouseholds([]);
          setListError("Не удалось загрузить домохозяйства");
        }
      } finally {
        if (!cancelled) {
          setLoadingList(false);
        }
      }
    };
    load();
    return () => {
      cancelled = true;
    };
  }, [refreshVersion, reloadToken]);

  const filtered = useMemo(() => {
    const term = filter.trim().toLowerCase();
    if (!term) return households;
    return households.filter((h) =>
      `${h.name}`.toLowerCase().includes(term),
    );
  }, [households, filter]);

  useEffect(() => {
    if (filtered.length === 0) {
      setSelectedId(null);
      return;
    }
    if (selectedId && filtered.some((h) => h.id === selectedId)) {
      return;
    }
    setSelectedId(filtered[0].id);
  }, [filtered, selectedId]);

  useEffect(() => {
    if (!selectedId) {
      setDetail(null);
      setDetailError(null);
      return;
    }
    let cancelled = false;
    const loadDetail = async () => {
      setLoadingDetail(true);
      try {
        const res = await fetch(`/api/households/${selectedId}`);
        if (res.status === 404) {
          if (!cancelled) {
            setDetail(null);
            setDetailError(null);
          }
          return;
        }
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = (await res.json()) as HouseholdDetail;
        if (!cancelled) {
          setDetail(data);
          setDetailError(null);
        }
      } catch (err) {
        if (!cancelled) {
          setDetail(null);
          setDetailError("Не удалось загрузить данные домохозяйства");
        }
      } finally {
        if (!cancelled) {
          setLoadingDetail(false);
        }
      }
    };
    loadDetail();
    return () => {
      cancelled = true;
    };
  }, [selectedId, refreshVersion, reloadToken]);

  const members = useMemo(() => {
    if (!detail) return [] as HouseholdMember[];
    return detail.members ?? [];
  }, [detail]);

  const selectedSummary = selectedId
    ? households.find((h) => h.id === selectedId)
    : null;

  const handleReload = () => {
    setReloadToken((prev) => prev + 1);
  };

  const handleMemberClick = (id: string) => {
    if (!id) return;
    onSelectCharacter?.(id);
  };

  const headId = detail?.headId ?? null;

  const headMember = useMemo(() => {
    if (!headId) return null;
    return members.find((m) => m.id === headId) ?? null;
  }, [headId, members]);

  return (
    <div
      className={cn(
        "flex h-full min-h-0 overflow-hidden rounded-xl border border-slate-200 bg-white/70 shadow-sm",
        className,
      )}
    >
      <div className="flex h-full w-72 flex-col border-r border-slate-200 bg-white/80">
        <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
          <div>
            <div className="text-sm font-semibold text-slate-900">Домохозяйства</div>
            <div className="text-xs text-slate-500">
              {loadingList
                ? "Загрузка..."
                : `${filtered.length} из ${households.length}`}
            </div>
          </div>
          <Button variant="ghost" size="sm" onClick={handleReload} disabled={loadingList}>
            Обновить
          </Button>
        </div>
        <div className="px-4 py-3">
          <input
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Поиск по названию"
            className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 placeholder:text-slate-400 focus:border-slate-400 focus:outline-none focus:ring-2 focus:ring-slate-200"
          />
        </div>
        <div className="flex-1 overflow-y-auto px-2 pb-4">
          {loadingList ? (
            <div className="px-2 text-sm text-slate-500">Загрузка...</div>
          ) : listError ? (
            <div className="px-2 text-sm text-red-500">{listError}</div>
          ) : filtered.length === 0 ? (
            <div className="px-2 text-sm text-slate-500">Домохозяйства не найдены.</div>
          ) : (
            <ul className="space-y-1">
              {filtered.map((h) => {
                const isSelected = selectedId === h.id;
                return (
                  <li key={h.id}>
                    <button
                      type="button"
                      onClick={() => setSelectedId(h.id)}
                      className={cn(
                        "w-full rounded-lg border px-3 py-2 text-left text-sm transition",
                        isSelected
                          ? "border-slate-500 bg-slate-100/80 text-slate-900"
                          : "border-transparent bg-white hover:border-slate-200 hover:bg-slate-50",
                      )}
                      >
                        <div className="font-medium">{h.name}</div>
                        <div className="text-xs text-slate-500">
                          {`Участников: ${h.members.length}`}
                        </div>
                      </button>
                    </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>

      <div className="flex flex-1 flex-col">
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <div>
            <div className="text-xs uppercase tracking-wide text-slate-500">
              Домохозяйство
            </div>
            <div className="text-xl font-semibold text-slate-900">
              {detail?.name ?? selectedSummary?.name ?? "-"}
            </div>
            {selectedSummary && (
              <div className="text-sm text-slate-500">
                Участников: {selectedSummary.members.length}
                {typeof selectedSummary.wealth === "number"
                  ? ` · Состояние: ${selectedSummary.wealth}`
                  : null}
              </div>
            )}
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {loadingDetail ? (
            <div className="text-sm text-slate-500">Загрузка данных...</div>
          ) : detailError ? (
            <div className="text-sm text-red-500">{detailError}</div>
          ) : !detail ? (
            <div className="text-sm text-slate-500">Выберите домохозяйство для просмотра.</div>
          ) : (
            <div className="space-y-4 text-sm text-slate-700">
              <div className="grid gap-3 md:grid-cols-2">
                <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
                  <div className="text-xs uppercase tracking-wide text-slate-400">
                    Локация
                  </div>
                  <div className="mt-1 text-slate-800">
                    {detail.locationId ?? "-"}
                  </div>
                </div>
                <div className="rounded-lg border border-slate-200 bg-white/70 px-4 py-3">
                  <div className="text-xs uppercase tracking-wide text-slate-400">
                    Состояние
                  </div>
                  <div className="mt-1 text-slate-800">
                    {typeof detail.wealth === "number"
                      ? detail.wealth.toLocaleString()
                      : "-"}
                  </div>
                </div>
              </div>

              {headId ? (
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-xs uppercase tracking-wide text-slate-400">
                    Глава
                  </span>
                  <button
                    type="button"
                    onClick={() => handleMemberClick(headId)}
                    className="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50"
                  >
                    {headMember?.name ?? headId.slice(0, 8)}
                  </button>
                </div>
              ) : null}

              <div>
                <div className="text-xs uppercase tracking-wide text-slate-400">
                  Участники
                </div>
                {members.length === 0 ? (
                  <div className="mt-2 text-sm text-slate-500">Участники отсутствуют.</div>
                ) : (
                  <ul className="mt-2 space-y-2">
                    {members.map((member) => (
                      <li key={member.id}>
                        <button
                          type="button"
                          onClick={() => handleMemberClick(member.id)}
                          className="w-full rounded-lg border border-slate-200 bg-white/70 px-4 py-2 text-left transition hover:border-slate-300 hover:bg-white"
                        >
                          <div className="flex items-center justify-between">
                            <span className="font-medium text-slate-900">
                              {member.name ?? member.id.slice(0, 8)}
                            </span>
                            <span className="text-xs text-slate-500">
                              {member.status ?? "-"}
                            </span>
                          </div>
                          <div className="text-xs text-slate-500">
                            {member.locationName ?? "Неизвестная локация"}
                          </div>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}


