import { useEffect, useMemo, useState } from "react";
import EventsList from "@/components/EventsList";
import CharacterFocus from "@/components/CharacterFocus";
import EconomyPanel from "@/components/EconomyPanel";
import NpcMap from "@/components/NpcMap";
import NpcProfiles from "@/components/NpcProfiles";
import WorldSidebar from "@/components/WorldSidebar";
import HouseholdsPanel from "@/components/HouseholdsPanel";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import InheritancePanel from "@/components/InheritancePanel";
import eventsClient from "@/lib/eventsClient";

type TabKey = "events" | "characters" | "economy" | "households" | "inheritance" | "focus";

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
  {
    key: "households",
    title: "Домохозяйства",
    description: "Состав семей и членов.",
  },
  {
    key: "inheritance",
    title: "Наследование",
    description: "Записи наследств и резолюции.",
  },
  {
    key: "focus",
    title: "Фокус",
    description: "Генеалогия, связи и коммуникации персонажа.",
  },
];

type ActionStatus = {
  text: string;
  tone: "info" | "success" | "error";
};

type DevAction = "seedCharacters" | "seedWorld" | "tick" | "tickAdvance";

function App() {
  const [activeTab, setActiveTab] = useState<TabKey>("events");
  const [pending, setPending] = useState<Record<DevAction, boolean>>({
    seedCharacters: false,
    seedWorld: false,
    tick: false,
    tickAdvance: false,
  });
  const [status, setStatus] = useState<ActionStatus | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [focusedCharacterId, setFocusedCharacterId] = useState<string | null>(null);
  const [eventsCharacterIdFilter, setEventsCharacterIdFilter] = useState<string | null>(null);
  const [focusCharacterId, setFocusCharacterId] = useState<string | null>(null);

  useEffect(() => {
    if (!status) return;
    const timer = window.setTimeout(() => setStatus(null), 5_000);
    return () => window.clearTimeout(timer);
  }, [status]);

  useEffect(() => {
    // attempt to start SignalR connection on app boot
    eventsClient.start().catch(() => {
      // ignore start errors; fallback will be used
    });
    return () => eventsClient.stop();
  }, []);

  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(() => setToast(null), 4000);
    return () => window.clearTimeout(t);
  }, [toast]);

  const runDevAction = async (action: DevAction) => {
    setPending((prev) => ({ ...prev, [action]: true }));
    setStatus({
      tone: "info",
      text:
        action === "seedCharacters"
          ? "Создаю dev-персонажей..."
          : action === "seedWorld"
            ? "Заполняю домохозяйства и локации..."
            : "Запускаю такт симуляции...",
    });

    const endpoint =
      action === "seedCharacters"
        ? "/api/dev/seed-characters"
        : action === "seedWorld"
          ? "/api/dev/seed-world"
          : action === "tick"
            ? "/api/dev/tick-now"
            : action === "tickAdvance"
              ? "/api/dev/tick-now?advanceTime=true"
              : "/api/dev/tick-now";

    try {
      const res = await fetch(endpoint, { method: "POST" });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const payload = await res.json().catch(() => ({}));
      setStatus({
        tone: "success",
        text:
          action === "seedCharacters"
            ? `Персонажи созданы (${payload.seeded ?? "ok"})`
            : action === "seedWorld"
              ? `Мир прогрет (${(payload.created ?? []).join(", ") || "ok"})`
              : `Такт обработан (${payload.ticks ?? "n/a"})`,
      });
      setRefreshVersion((prev) => prev + 1);
      if (action === "tickAdvance") {
        const wt = payload.worldTime as any;
        if (wt) {
          setToast(`Время продвинуто: ${wt.year} год, месяц ${wt.month}, день ${wt.dayOfMonth}, тик ${wt.tick}`);
        }
      }
    } catch (err) {
      setStatus({
        tone: "error",
        text:
          err instanceof Error
            ? `Ошибка dev-действия: ${err.message}`
            : "Dev-действие не выполнено",
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

  const handleHouseholdCharacterSelect = (id: string) => {
    setFocusedCharacterId(id);
    setActiveTab("characters");
  };

  // Listen for requests to show character events from child components
  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail as { id?: string } | undefined;
      if (detail?.id) {
        setEventsCharacterIdFilter(detail.id);
        setActiveTab("events");
      }
    };
    window.addEventListener("imperium:show-character-events", handler as any);
    return () => window.removeEventListener("imperium:show-character-events", handler as any);
  }, []);

  // Listen for summary clicks (WorldSidebar)
  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail as { type?: string } | undefined;
      if (!detail?.type) return;
      // map summary types to event types
      const mapping: Record<string, string> = {
        npc_reactions: "npc_reaction",
        conflict_started: "conflict_started",
        inheritance_recorded: "inheritance_recorded",
        legal_rulings: "legal_ruling",
      };
      const evType = mapping[detail.type] ?? detail.type;
      // set events tab filter and switch to events
      setActiveTab("events");
      // set the filter to event type
      setEventsCharacterIdFilter(null);
      // send message to EventsList via custom event
      window.dispatchEvent(new CustomEvent("imperium:filter-events-by-type", { detail: { type: evType } }));
    };
    window.addEventListener("imperium:show-summary", handler as any);
    return () => window.removeEventListener("imperium:show-summary", handler as any);
  }, []);

  // Focus character event
  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail as { id?: string } | undefined;
      if (detail?.id) {
        setFocusCharacterId(detail.id);
        setActiveTab("focus");
      }
    };
    window.addEventListener("imperium:focus-character", handler as any);
    return () => window.removeEventListener("imperium:focus-character", handler as any);
  }, []);

  const clearEventsFilter = () => setEventsCharacterIdFilter(null);

  const handleInheritanceSelect = (id: string) => {
    setFocusedCharacterId(id);
    setActiveTab("characters");
  };

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
                    onClick={() => runDevAction("seedCharacters")}
                    disabled={pending.seedCharacters}
                  >
                    {pending.seedCharacters ? "Создаю..." : "Создать персонажей"}
                  </Button>
                  <Button
                    variant="outline"
                    onClick={() => runDevAction("seedWorld")}
                    disabled={pending.seedWorld}
                  >
                    {pending.seedWorld ? "Создаю мир..." : "Заполнить мир"}
                  </Button>
          <Button onClick={() => runDevAction("tick")} disabled={pending.tick}>
            {pending.tick ? "Такт..." : "Такт симуляции"}
                  </Button>
          <Button onClick={() => runDevAction("tickAdvance")} disabled={pending.tick}>Такт + время</Button>
                </div>
                <div className={cn("text-xs", statusToneClass)}>
                  {status?.text ?? "Dev-операции готовы."}
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

          {toast && (
            <div className="fixed right-6 top-6 z-50 rounded-md bg-slate-900 px-4 py-2 text-sm text-white shadow">
              {toast}
            </div>
          )}

          <main className="flex-1 min-h-0 overflow-hidden bg-slate-50/60 p-4">
            {activeTab === "events" && (
              <div className="h-full min-h-0">
                <EventsList className="h-full" characterIdFilter={eventsCharacterIdFilter} onClearFilter={clearEventsFilter} />
              </div>
            )}
            {activeTab === "characters" && (
              <div className="h-full">
                <NpcProfiles
                  className="h-full"
                  refreshVersion={refreshVersion}
                  focusCharacterId={focusedCharacterId}
                  onFocusConsumed={() => setFocusedCharacterId(null)}
                />
              </div>
            )}
            {activeTab === "economy" && (
              <div className="flex h-full flex-col gap-4 overflow-y-auto">
                <EconomyPanel className="max-w-3xl" />
                <NpcMap
                  className="max-w-5xl"
                  showSidebar
                  backgroundUrl="/assets/imperium-map.jpg"
                  focusCharacterId={focusCharacterId}
                  onFocusCharacter={(id)=>{ setFocusCharacterId(id); setActiveTab("focus"); }}
                />
                <div className="rounded-lg border border-dashed border-slate-300 bg-white/60 px-4 py-5 text-sm text-slate-500">
                  В следующих итерациях сюда добавим графики цен, инвентарь
                  ресурсов и связи с агентами (CraftAI, MarketAI).
                </div>
              </div>
            )}
            {activeTab === "households" && (
              <div className="h-full">
                <HouseholdsPanel
                  className="h-full"
                  refreshVersion={refreshVersion}
                  onSelectCharacter={handleHouseholdCharacterSelect}
                />
              </div>
            )}
            {activeTab === "focus" && (
              <div className="flex h-full flex-col gap-4 overflow-y-auto">
                {focusCharacterId ? (
                  <CharacterFocus className="h-full" characterId={focusCharacterId} />
                ) : (
                  <div className="rounded border border-dashed border-slate-300 bg-white/60 px-4 py-5 text-sm text-slate-500">Выберите персонажа для фокуса на карте или в списках.</div>
                )}
                <NpcMap
                  className="max-w-5xl"
                  showSidebar
                  backgroundUrl="/assets/imperium-map.jpg"
                  focusCharacterId={focusCharacterId}
                  onFocusCharacter={(id)=> setFocusCharacterId(id)}
                />
              </div>
            )}
            {activeTab === "inheritance" && (
              <div className="h-full">
                <InheritancePanel className="max-w-3xl" onSelectCharacter={handleInheritanceSelect} />
              </div>
            )}
          </main>
        </div>
      </div>
    </div>
  );
}

export default App;



