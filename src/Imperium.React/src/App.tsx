import { useEffect, useMemo, useState } from "react";
import EventsList from "@/components/EventsList";
import EconomyPanel from "@/components/EconomyPanel";
import NpcProfiles from "@/components/NpcProfiles";
import WorldSidebar from "@/components/WorldSidebar";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type TabKey = "events" | "characters" | "economy";

const TABS: { key: TabKey; title: string; description: string }[] = [
  {
    key: "events",
    title: "События",
    description: "Живая лента GameEvent.",
  },
  {
    key: "characters",
    title: "Персонажи",
    description: "Профили NPC и их реплики.",
  },
  {
    key: "economy",
    title: "Экономика",
    description: "Снимки экономики и логистика.",
  },
];

type ActionStatus = {
  text: string;
  tone: "info" | "success" | "error";
};

function App() {
  const [activeTab, setActiveTab] = useState<TabKey>("events");
  const [pending, setPending] = useState<{ seed: boolean; tick: boolean }>({
    seed: false,
    tick: false,
  });
  const [status, setStatus] = useState<ActionStatus | null>(null);

  useEffect(() => {
    if (!status) return;
    const timer = window.setTimeout(() => setStatus(null), 5_000);
    return () => window.clearTimeout(timer);
  }, [status]);

  const runDevAction = async (action: "seed" | "tick") => {
    setPending((prev) => ({ ...prev, [action]: true }));
    setStatus({
      tone: "info",
      text:
        action === "seed"
          ? "Запускаю сидирование персонажей..."
          : "Запрашиваю цикл тика...",
    });

    const endpoint =
      action === "seed" ? "/api/dev/seed-characters" : "/api/dev/tick-now";

    try {
      const res = await fetch(endpoint, { method: "POST" });
      if (!res.ok) throw new Error(`Статус ${res.status}`);
      const payload = await res.json().catch(() => ({}));
      setStatus({
        tone: "success",
        text:
          action === "seed"
            ? `Персонажи обновлены (${payload.seeded ?? "ok"})`
            : `Тик выполнен (${payload.ticks ?? "готово"})`,
      });
    } catch (err) {
      setStatus({
        tone: "error",
        text:
          err instanceof Error
            ? `Ошибка dev-операции: ${err.message}`
            : "Не удалось выполнить dev-запрос",
      });
    } finally {
      setPending((prev) => ({ ...prev, [action]: false }));
    }
  };

  const statusToneClass = useMemo(() => {
    if (!status) return "text-slate-500";
    if (status.tone === "success") return "text-emerald-600";
    if (status.tone === "error") return "text-red-600";
    return "text-slate-600";
  }, [status]);

  return (
    <div className="h-screen overflow-hidden bg-slate-100 text-slate-900">
      <div className="flex h-full">
        <WorldSidebar />
        <div className="flex h-full flex-1 flex-col overflow-hidden">
          <header className="border-b border-slate-200 bg-white/80 px-6 py-5 shadow-sm">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
              <div>
                <h1 className="text-2xl font-semibold text-slate-900">
                  Imperium — Мир в развитии
                </h1>
                <p className="text-sm text-slate-500">
                  Мониторинг тика: время, события, экономика и NPC.
                </p>
              </div>
              <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center">
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    onClick={() => runDevAction("seed")}
                    disabled={pending.seed}
                  >
                    {pending.seed ? "Seed..." : "Seed персонажей"}
                  </Button>
                  <Button onClick={() => runDevAction("tick")} disabled={pending.tick}>
                    {pending.tick ? "Tick..." : "Tick сейчас"}
                  </Button>
                </div>
                <div className={cn("text-xs", statusToneClass)}>
                  {status?.text ?? "Dev-инструменты готовы."}
                </div>
              </div>
            </div>
          </header>

          <nav className="border-b border-slate-200 bg-white/70 px-6">
            <div className="flex flex-wrap gap-2 py-3">
              {TABS.map((tab) => {
                const active = activeTab === tab.key;
                return (
                  <button
                    key={tab.key}
                    type="button"
                    onClick={() => setActiveTab(tab.key)}
                    className={cn(
                      "rounded-lg px-4 py-2 text-sm transition",
                      active
                        ? "bg-slate-900 text-white shadow-sm"
                        : "bg-white text-slate-700 hover:bg-slate-100",
                    )}
                  >
                    <div className="font-semibold">{tab.title}</div>
                    <div className="text-[11px] text-slate-500">
                      {tab.description}
                    </div>
                  </button>
                );
              })}
            </div>
          </nav>

          <main className="flex-1 min-h-0 overflow-hidden bg-slate-50/60 p-4">
            {activeTab === "events" && (
              <div className="h-full min-h-0">
                <EventsList className="h-full" />
              </div>
            )}
            {activeTab === "characters" && (
              <div className="h-full">
                <NpcProfiles className="h-full" />
              </div>
            )}
            {activeTab === "economy" && (
              <div className="flex h-full flex-col gap-4 overflow-y-auto">
                <EconomyPanel className="max-w-3xl" />
                <div className="rounded-lg border border-dashed border-slate-300 bg-white/60 px-4 py-5 text-sm text-slate-500">
                  В следующих итерациях сюда добавим графики цен, инвентарь
                  ресурсов и связи с агентами (CraftAI, MarketAI).
                </div>
              </div>
            )}
          </main>
        </div>
      </div>
    </div>
  );
}

export default App;
