import { useCallback, useEffect, useMemo, useState } from "react";
import { Card, CardHeader, CardTitle, CardContent } from "./ui/card";
import { Button } from "./ui/button";
import { buildRelationshipGraph } from "../utils/graph";
import {
  CharacterSummary,
  fetchCharacters,
  fetchHousehold,
  fetchLocations,
  fetchMarketOrders,
  fetchTrades
} from "../services/api";

type RelationshipRow = {
  otherId: string;
  otherName: string;
  trust: number;
  love: number;
  hostility: number;
};

type CommunicationRow = {
  id: string;
  timestamp: string;
  type: string;
  payloadJson: string;
  location: string | null;
};

type CharacterFocus = {
  id: string;
  name: string;
  relationships: RelationshipRow[];
};

const DEFAULT_COMM_LIMIT = 30;

export default function CharacterFocusPanel() {
  const [characters, setCharacters] = useState<CharacterSummary[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [focus, setFocus] = useState<CharacterFocus | null>(null);
  const [communications, setCommunications] = useState<CommunicationRow[]>([]);
  const [commPartner, setCommPartner] = useState<string>("any");
  const [sameLocationOnly, setSameLocationOnly] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const loadCharacters = useCallback(async () => {
    const list = await fetchCharacters();
    setCharacters(list);
    if (list.length > 0 && !selected) {
      setSelected(list[0].id);
    }
  }, [selected]);

  const loadFocus = useCallback(
    async (characterId: string) => {
      setLoading(true);
      setError(null);
      try {
        const [relationshipsRes, communicationsRes] = await Promise.all([
          fetch(`/api/characters/${characterId}/relationships`).then(res => res.json()),
          fetch(
            `/api/characters/${characterId}/communications?count=${DEFAULT_COMM_LIMIT}${sameLocationOnly ? "&sameLocationOnly=true" : ""}`
          ).then(res => res.json())
        ]);

        const relationships = Array.isArray(relationshipsRes)
          ? relationshipsRes.map((item: any) => ({
              otherId: item.other?.id ?? item.otherId ?? "",
              otherName: item.other?.name ?? "Неизвестно",
              trust: item.trust ?? 0,
              love: item.love ?? 0,
              hostility: item.hostility ?? 0
            }))
          : [];

        setFocus({
          id: characterId,
          name: characters.find(c => c.id === characterId)?.name ?? "Персонаж",
          relationships
        });

        const comms = Array.isArray(communicationsRes)
          ? communicationsRes.map((item: any) => ({
              id: item.id ?? "",
              timestamp: item.timestamp ?? "",
              type: item.type ?? "",
              payloadJson: item.payloadJson ?? "{}",
              location: item.location ?? null
            }))
          : [];
        setCommunications(comms);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Не удалось загрузить данные о персонаже");
      } finally {
        setLoading(false);
      }
    },
    [characters, sameLocationOnly]
  );

  useEffect(() => {
    void loadCharacters();
  }, [loadCharacters]);

  useEffect(() => {
    if (!selected) return;
    void loadFocus(selected);
  }, [selected, loadFocus]);

  const filteredCommunications = useMemo(() => {
    if (commPartner === "any") return communications;
    return communications.filter(comm => comm.payloadJson.includes(commPartner));
  }, [communications, commPartner]);

  const graphData = useMemo(() => {
    if (!focus) return null;
    return buildRelationshipGraph(
      focus.id,
      focus.relationships.map(rel => ({
        otherId: rel.otherId,
        otherName: rel.otherName,
        trust: rel.trust,
        love: rel.love,
        hostility: rel.hostility
      }))
    );
  }, [focus]);

  return (
    <Card className="shadow-md">
      <CardHeader>
        <div className="flex items-center justify-between gap-3">
          <CardTitle>🔎 Фокус персонажа</CardTitle>
          <div className="flex items-center gap-2 text-xs uppercase tracking-wide text-zinc-500">
            <label className="flex items-center gap-1">
              <span>Персонаж</span>
              <select
                value={selected ?? ""}
                onChange={event => setSelected(event.target.value)}
                className="rounded-md border border-zinc-200 bg-white px-2 py-1 text-sm focus:border-emerald-500 focus:outline-none"
              >
                {characters.map(ch => (
                  <option key={ch.id} value={ch.id}>
                    {ch.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="flex items-center gap-1">
              <input type="checkbox" checked={sameLocationOnly} onChange={event => setSameLocationOnly(event.target.checked)} />
              <span>Только в той же локации</span>
            </label>
            <Button variant="outline" size="sm" onClick={() => selected && loadFocus(selected)} disabled={loading}>
              Обновить
            </Button>
            {loading && <span className="text-emerald-600">загрузка…</span>}
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        {error && <div className="rounded-md border border-red-200 bg-red-50 p-3 text-xs text-red-600">{error}</div>}

        {focus ? (
          <>
            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="text-xs uppercase text-zinc-500">Связи</div>
              {focus.relationships.length === 0 ? (
                <div className="text-xs text-zinc-500">Связи не найдены</div>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-zinc-500">
                      <th className="pb-1 text-left font-semibold">Персонаж</th>
                      <th className="pb-1 text-left font-semibold">Trust</th>
                      <th className="pb-1 text-left font-semibold">Love</th>
                      <th className="pb-1 text-left font-semibold">Hostility</th>
                    </tr>
                  </thead>
                  <tbody>
                    {focus.relationships.map(rel => (
                      <tr key={rel.otherId} className="odd:bg-white even:bg-white/40">
                        <td className="py-1 pr-2">{rel.otherName}</td>
                        <td className="py-1 pr-2">{rel.trust}</td>
                        <td className="py-1 pr-2">{rel.love}</td>
                        <td className="py-1">{rel.hostility}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            {graphData && graphData.nodes.length > 1 && (
              <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
                <div className="mb-2 text-xs font-semibold uppercase text-zinc-600">Мини-граф связей</div>
                <svg width={280} height={260} className="bg-white/40 rounded">
                  {graphData.edges.map(edge => {
                    const source = graphData.nodes.find(n => n.id === edge.source)!;
                    const target = graphData.nodes.find(n => n.id === edge.target)!;
                    return (
                      <line
                        key={edge.id}
                        x1={source.x}
                        y1={source.y}
                        x2={target.x}
                        y2={target.y}
                        stroke="#a8acc3"
                        strokeWidth={Math.max(1, Math.min((edge.weight ?? 1) / 20, 4))}
                        strokeOpacity={0.7}
                      />
                    );
                  })}
                  {graphData.nodes.map(node => (
                    <g key={node.id} transform={`translate(${node.x},${node.y})`}>
                      <circle r={12} fill={node.id === focus.id ? "#0f766e" : "#1d4ed8"} fillOpacity={node.id === focus.id ? 0.95 : 0.75} />
                      <text
                        textAnchor="middle"
                        alignmentBaseline="middle"
                        fontSize={node.id === focus.id ? 10 : 8}
                        fill="white"
                        fontWeight={node.id === focus.id ? "700" : "500"}
                      >
                        {node.label.length > 8 ? node.label.slice(0, 7) + "…" : node.label}
                      </text>
                    </g>
                  ))}
                </svg>
              </div>
            )}

            <div className="rounded-md border border-zinc-200 bg-white/70 p-3">
              <div className="mb-2 flex flex-wrap items-center gap-3 text-xs uppercase text-zinc-600">
                <span className="font-semibold">Коммуникации</span>
                <label className="flex items-center gap-1 text-zinc-500 normal-case">
                  <span>Фильтр собеседника (GUID)</span>
                  <input
                    type="text"
                    placeholder="any"
                    value={commPartner === "any" ? "" : commPartner}
                    onChange={event => setCommPartner(event.target.value.trim() === "" ? "any" : event.target.value.trim())}
                    className="rounded border border-zinc-200 px-2 py-1"
                  />
                </label>
              </div>
              {filteredCommunications.length === 0 ? (
                <div className="text-xs text-zinc-500">Коммуникаций не найдено</div>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="text-zinc-500">
                      <th className="pb-1 text-left font-semibold">Время</th>
                      <th className="pb-1 text-left font-semibold">Тип</th>
                      <th className="pb-1 text-left font-semibold">Локация</th>
                      <th className="pb-1 text-left font-semibold">Payload</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredCommunications.map(comm => (
                      <tr key={comm.id} className="odd:bg-white even:bg-white/40">
                        <td className="py-1 pr-2">{new Date(comm.timestamp).toLocaleString("ru-RU")}</td>
                        <td className="py-1 pr-2">{comm.type}</td>
                        <td className="py-1 pr-2">{comm.location ?? "—"}</td>
                        <td className="py-1 font-mono text-[10px] break-words">{comm.payloadJson}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </>
        ) : (
          <div className="text-xs text-zinc-500">Выберите персонажа, чтобы увидеть связи и коммуникации.</div>
        )}
      </CardContent>
    </Card>
  );
}
