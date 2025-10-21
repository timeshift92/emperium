import { useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";

type NpcCanvasProps = {
  className?: string;
  height?: number;
};

type Loc = { id: string; name: string; latitude?: number; longitude?: number };
type Char = { id: string; name: string; locationId?: string | null };

export default function NpcCanvas({ className, height = 320 }: NpcCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [locations, setLocations] = useState<Loc[]>([]);
  const [charsByLoc, setCharsByLoc] = useState<Record<string, Char[]>>({});
  const [hover, setHover] = useState<{ x: number; y: number; loc?: Loc } | null>(null);
  const [selectedLoc, setSelectedLoc] = useState<Loc | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const r = await fetch("/api/locations");
        if (!r.ok) return;
        const locs = (await r.json()) as Loc[];
        if (cancelled) return;
        setLocations(locs);
        // fetch characters per location
        const entries: [string, Char[]][] = [];
        for (const l of locs) {
          const rc = await fetch(`/api/locations/${l.id}/characters`);
          const data = rc.ok ? ((await rc.json()) as Char[]) : [];
          entries.push([l.id, data]);
        }
        if (!cancelled) setCharsByLoc(Object.fromEntries(entries));
      } catch {}
    })();
    return () => { cancelled = true; };
  }, []);

  const bounds = useMemo(() => {
    const lat = locations.map(l => l.latitude ?? 0);
    const lon = locations.map(l => l.longitude ?? 0);
    if (lat.length === 0) return { minLat: 0, maxLat: 1, minLon: 0, maxLon: 1 };
    return { minLat: Math.min(...lat), maxLat: Math.max(...lat), minLon: Math.min(...lon), maxLon: Math.max(...lon) };
  }, [locations]);

  useEffect(() => {
    const cvs = canvasRef.current; if (!cvs) return;
    const ctx = cvs.getContext("2d"); if (!ctx) return;
    const W = cvs.width, H = cvs.height;
    ctx.clearRect(0, 0, W, H);
    // background
    ctx.fillStyle = "#f8fafc";
    ctx.fillRect(0,0,W,H);

    const pad = 24;
    const latSpan = bounds.maxLat - bounds.minLat || 1;
    const lonSpan = bounds.maxLon - bounds.minLon || 1;
    const xOf = (lon: number) => pad + ((lon - bounds.minLon) / lonSpan) * (W - pad * 2);
    const yOf = (lat: number) => pad + (1 - (lat - bounds.minLat) / latSpan) * (H - pad * 2);

    // draw locations
    ctx.fillStyle = "#334155";
    ctx.font = "12px sans-serif";
    locations.forEach(l => {
      const x = xOf(l.longitude ?? 0);
      const y = yOf(l.latitude ?? 0);
      // location point
      ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI*2); ctx.fill();
      ctx.fillText(l.name, x + 6, y - 6);
      if (selectedLoc && selectedLoc.id === l.id) {
        ctx.strokeStyle = "#10b981";
        ctx.lineWidth = 2;
        ctx.beginPath(); ctx.arc(x, y, 8, 0, Math.PI*2); ctx.stroke();
      }
      // NPCs in small cluster near location
      const list = charsByLoc[l.id] ?? [];
  list.slice(0, 12).forEach((_, idx) => {
        const ang = (idx / 12) * Math.PI * 2;
        const r = 12 + (idx % 4) * 3;
        const nx = x + Math.cos(ang) * r;
        const ny = y + Math.sin(ang) * r;
        ctx.fillStyle = "#2563eb";
        ctx.beginPath(); ctx.arc(nx, ny, 3, 0, Math.PI*2); ctx.fill();
      });
    });

    if (hover?.loc) {
      const l = hover.loc;
      const x = hover.x, y = hover.y;
      const list = (charsByLoc[l.id] ?? []).slice(0, 8);
      const text = list.length > 0 ? list.map(c => c.name).join(", ") : "NPC нет";
      const pad2 = 6;
      ctx.font = "12px sans-serif";
      const w = Math.min(260, ctx.measureText(text).width + pad2 * 2);
      const h = 22;
      ctx.fillStyle = "rgba(0,0,0,0.75)";
      ctx.fillRect(x + 10, y - h - 10, w, h);
      ctx.fillStyle = "#fff";
      ctx.fillText(text, x + 10 + pad2, y - 18);
    }
  }, [locations, charsByLoc, bounds, hover, selectedLoc]);

  // Mouse interactions
  useEffect(() => {
    const cvs = canvasRef.current; if (!cvs) return;
    const rectOf = () => cvs.getBoundingClientRect();
    const toCanvas = (e: MouseEvent) => {
      const r = rectOf();
      return { x: e.clientX - r.left, y: e.clientY - r.top };
    };
    const pad = 24;
    const latSpan = bounds.maxLat - bounds.minLat || 1;
    const lonSpan = bounds.maxLon - bounds.minLon || 1;
    const xOf = (lon: number) => pad + ((lon - bounds.minLon) / lonSpan) * (cvs.width - pad * 2);
    const yOf = (lat: number) => pad + (1 - (lat - bounds.minLat) / latSpan) * (cvs.height - pad * 2);
    const nearestLoc = (x: number, y: number): { l: Loc; d: number; cx: number; cy: number } | null => {
      let bestL: Loc | null = null;
      let bestD = Number.POSITIVE_INFINITY;
      let bestCx = 0;
      let bestCy = 0;
      for (const l of locations) {
        const cx = xOf(l.longitude ?? 0);
        const cy = yOf(l.latitude ?? 0);
        const dx = x - cx;
        const dy = y - cy;
        const d = Math.hypot(dx, dy);
        if (d < bestD) {
          bestD = d;
          bestL = l;
          bestCx = cx;
          bestCy = cy;
        }
      }
      if (bestL && bestD < 12) return { l: bestL, d: bestD, cx: bestCx, cy: bestCy };
      return null;
    };
    const onMove = (e: MouseEvent) => {
      const { x, y } = toCanvas(e);
  const hit = nearestLoc(x, y);
  if (hit) setHover({ x: hit.cx, y: hit.cy, loc: hit.l }); else setHover(null);
    };
    const onLeave = () => setHover(null);
    const onClick = (e: MouseEvent) => {
      const { x, y } = toCanvas(e);
  const hit = nearestLoc(x, y);
  if (hit) setSelectedLoc(hit.l); else setSelectedLoc(null);
    };
    cvs.addEventListener("mousemove", onMove);
    cvs.addEventListener("mouseleave", onLeave);
    cvs.addEventListener("click", onClick);
    return () => {
      cvs.removeEventListener("mousemove", onMove);
      cvs.removeEventListener("mouseleave", onLeave);
      cvs.removeEventListener("click", onClick);
    };
  }, [locations, bounds]);

  return (
    <div className={cn("rounded-xl border border-slate-200 bg-white/70 p-3", className)}>
      <div className="mb-2 text-sm font-semibold text-slate-700">Карта NPC (по локациям)</div>
      <canvas ref={canvasRef} width={640} height={height} className="w-full cursor-crosshair" />
      <div className="mt-1 text-xs text-slate-500">Точки — локации, синие точки вокруг — NPC (до 12 на локацию)</div>
      {selectedLoc && (
        <div className="mt-2 rounded border border-slate-200 bg-white/80 p-2 text-xs">
          <div className="font-semibold text-slate-700">{selectedLoc.name}</div>
          <div className="text-slate-500">NPC: {(charsByLoc[selectedLoc.id]?.length ?? 0)}</div>
        </div>
      )}
    </div>
  );
}
