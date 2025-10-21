import { useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";

type Loc = { id: string; name: string; latitude?: number; longitude?: number };
type Char = { id: string; name: string; locationId?: string | null; locationName?: string | null; latitude?: number | null; longitude?: number | null };
type Vec2 = { x: number; y: number };

type Props = {
  className?: string;
  height?: number;
  backgroundUrl?: string;
  focusCharacterId?: string | null;
  onFocusCharacter?: (id: string) => void;
  routeFromLocationId?: string | null;
  routeToLocationId?: string | null;
  showSidebar?: boolean;
};

export default function NpcMap({ className, height = 480, backgroundUrl, focusCharacterId = null, onFocusCharacter, routeFromLocationId = null, routeToLocationId = null, showSidebar = false }: Props) {
  const cvsRef = useRef<HTMLCanvasElement | null>(null);
  const [locs, setLocs] = useState<Loc[]>([]);
  const [chars, setChars] = useState<Char[]>([]);
  const [scale, setScale] = useState(1.2);
  const [offset, setOffset] = useState<Vec2>({ x: 0, y: 0 });
  const [hover, setHover] = useState<{ name: string; p: Vec2 } | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [routePath, setRoutePath] = useState<string[]>([]);
  const [filter, setFilter] = useState<string>("");

  const bg = useRef<HTMLImageElement | null>(null);
  useEffect(() => { if (!backgroundUrl) return; const i = new Image(); i.src = backgroundUrl; bg.current = i; }, [backgroundUrl]);

  const bounds = useMemo(() => {
    const lat = locs.map(l => l.latitude ?? 0);
    const lon = locs.map(l => l.longitude ?? 0);
    if (lat.length === 0) return { minLat: 0, maxLat: 1, minLon: 0, maxLon: 1 };
    return { minLat: Math.min(...lat), maxLat: Math.max(...lat), minLon: Math.min(...lon), maxLon: Math.max(...lon) };
  }, [locs]);

  // positions tween
  const last = useRef<Map<string, Vec2>>(new Map());
  const next = useRef<Map<string, Vec2>>(new Map());
  const animStart = useRef<number>(0);
  const DUR = 900;

  function geoToLocal(lat?: number | null, lon?: number | null, locId?: string | null, locName?: string | null): Vec2 {
    const pad = 40; const W = cvsRef.current?.width ?? 640; const H = cvsRef.current?.height ?? 480;
    const latSpan = (bounds.maxLat - bounds.minLat) || 1; const lonSpan = (bounds.maxLon - bounds.minLon) || 1;
    if (lat == null || lon == null) {
      let l = locId ? locs.find(x => x.id === locId) : undefined;
      if (!l && locName) {
        const nm = locName.toLowerCase();
        l = locs.find(x => (x.name || "").toLowerCase() === nm);
      }
      lat = l?.latitude ?? bounds.minLat; lon = l?.longitude ?? bounds.minLon;
    }
    const x = pad + ((lon - bounds.minLon) / lonSpan) * (W - pad * 2);
    const y = pad + (1 - (lat - bounds.minLat) / latSpan) * (H - pad * 2);
    return { x, y };
  }

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [rl, rc] = await Promise.all([fetch("/api/locations"), fetch("/api/characters")]);
      const L = rl.ok ? ((await rl.json()) as Loc[]) : [];
      const C = rc.ok ? ((await rc.json()) as Char[]) : [];
      if (cancelled) return;
      setLocs(L); setChars(C);
      const init = new Map<string, Vec2>();
      C.forEach(c => init.set(c.id, geoToLocal(c.latitude, c.longitude, c.locationId ?? undefined, c.locationName ?? undefined)));
      last.current = init; next.current = new Map(init);
      // Auto-fit world to canvas once data is ready
      const cvs = cvsRef.current; if (cvs && L.length > 0) {
        const b = bounds;
        const pad = 40;
        const worldW = (cvs.width - pad * 2);
        const worldH = (cvs.height - pad * 2);
        const latSpan = (b.maxLat - b.minLat) || 1;
        const lonSpan = (b.maxLon - b.minLon) || 1;
        // choose scale so that world fits viewport approximately 80%
        const sx = worldW / (lonSpan || 1);
        const sy = worldH / (latSpan || 1);
        const s = Math.min(sx, sy);
        setScale(Math.max(0.6, Math.min(3, s * 0.8)));
        // center
        setOffset({ x: cvs.width/2 - (pad + worldW/2) * Math.max(0.6, Math.min(3, s * 0.8)) / (worldW/worldW), y: cvs.height/2 - (pad + worldH/2) * Math.max(0.6, Math.min(3, s * 0.8)) / (worldH/worldH) });
      }
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false; let timer: number | null = null;
    const poll = async () => {
      try {
        const rc = await fetch("/api/characters");
        if (!rc.ok) return; const C = (await rc.json()) as Char[]; if (cancelled) return;
        setChars(C);
        const cur = new Map<string, Vec2>();
        C.forEach(c => cur.set(c.id, geoToLocal(c.latitude, c.longitude, c.locationId ?? undefined, c.locationName ?? undefined)));
        last.current = renderPositions();
        next.current = cur; animStart.current = performance.now();
      } finally { if (!cancelled) timer = window.setTimeout(poll, 4000); }
    };
    poll();
    return () => { cancelled = true; if (timer) window.clearTimeout(timer); };
  }, [locs]);

  // Track external focus changes
  useEffect(() => {
    if (focusCharacterId) setSelected(focusCharacterId);
  }, [focusCharacterId]);

  // Fetch and update route path when from/to provided
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (routeFromLocationId && routeToLocationId) {
        try {
          const r = await fetch(`/api/locations/route?from=${routeFromLocationId}&to=${routeToLocationId}`);
          const data = r.ok ? (await r.json()) as { path: string[] } : { path: [] as string[] };
          if (!cancelled) setRoutePath(data.path ?? []);
        } catch { if (!cancelled) setRoutePath([]); }
      } else {
        setRoutePath([]);
      }
    })();
    return () => { cancelled = true; };
  }, [routeFromLocationId, routeToLocationId]);

  function renderPositions(): Map<string, Vec2> {
    const t = Math.min(1, (performance.now() - animStart.current) / DUR);
    const e = t < 0.5 ? 2*t*t : -1 + (4 - 2*t)*t;
    const map = new Map<string, Vec2>();
    next.current.forEach((to, id) => {
      const from = last.current.get(id) ?? to; map.set(id, { x: from.x + (to.x - from.x) * e, y: from.y + (to.y - from.y) * e });
    });
    return map;
  }

  useEffect(() => {
    const cvs = cvsRef.current; if (!cvs) return; const ctx = cvs.getContext("2d"); if (!ctx) return;
    let raf = 0; const draw = () => { paint(ctx); raf = requestAnimationFrame(draw); }; raf = requestAnimationFrame(draw); return () => cancelAnimationFrame(raf);
  }, [scale, offset, chars, locs, selected]);

  function paint(ctx: CanvasRenderingContext2D) {
    const W = ctx.canvas.width, H = ctx.canvas.height; ctx.clearRect(0,0,W,H);
    if (bg.current && bg.current.complete) {
      // draw background to cover canvas (maintain aspect)
      const img = bg.current; const ir = img.width / img.height; const cr = W / H;
      let dw = W, dh = H, dx = 0, dy = 0;
      if (ir > cr) { // image wider -> fit height
        dh = H; dw = H * ir; dx = (W - dw) / 2; dy = 0;
      } else { // image taller -> fit width
        dw = W; dh = W / ir; dx = 0; dy = (H - dh) / 2;
      }
      ctx.drawImage(img, dx, dy, dw, dh);
    } else { ctx.fillStyle = "#f8fafc"; ctx.fillRect(0,0,W,H); }

    const cur = renderPositions();
    const toScreen = (p: Vec2): Vec2 => ({ x: p.x*scale + offset.x, y: p.y*scale + offset.y });

    // Locations (and roads)
    ctx.fillStyle = "#334155"; ctx.font = "12px sans-serif";
    // draw neighbor roads as light lines
    ctx.strokeStyle = "#cbd5e1"; ctx.lineWidth = 1;
    locs.forEach(l => {
      const nbs: string[] = (l as any).neighborsJson ? JSON.parse((l as any).neighborsJson) : [];
      nbs.forEach(id => {
        const other = locs.find(x => x.id === id); if (!other) return;
        const p1 = toScreen(geoToLocal(l.latitude, l.longitude, l.id));
        const p2 = toScreen(geoToLocal(other.latitude, other.longitude, other.id));
        ctx.beginPath(); ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); ctx.stroke();
      });
    });
    locs.forEach(l => {
      const p = toScreen(geoToLocal(l.latitude, l.longitude, l.id, l.name));
      ctx.beginPath(); ctx.arc(p.x, p.y, 4, 0, Math.PI*2); ctx.fill(); ctx.fillText(l.name, p.x+6, p.y-6);
    });

    // highlighted route path
    if (routePath.length >= 2) {
      ctx.strokeStyle = "#10b981"; ctx.lineWidth = 3;
      for (let i = 0; i < routePath.length - 1; i++) {
        const a = locs.find(l => l.id === routePath[i]);
        const b = locs.find(l => l.id === routePath[i+1]);
        if (!a || !b) continue;
        const p1 = toScreen(geoToLocal(a.latitude, a.longitude, a.id));
        const p2 = toScreen(geoToLocal(b.latitude, b.longitude, b.id));
        ctx.beginPath(); ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); ctx.stroke();
      }
    }

    // NPCs
    chars.forEach(c => { const p = toScreen(cur.get(c.id) ?? geoToLocal(c.latitude, c.longitude, c.locationId ?? undefined, c.locationName ?? undefined)); const r = selected === c.id ? 5 : 3; ctx.fillStyle = selected === c.id ? "#ef4444" : "#2563eb"; ctx.beginPath(); ctx.arc(p.x, p.y, r, 0, Math.PI*2); ctx.fill(); });

    if (hover) { const pad = 6; ctx.font = "12px sans-serif"; const w = Math.min(220, ctx.measureText(hover.name).width + pad*2); const h = 20; ctx.fillStyle = "rgba(0,0,0,0.75)"; ctx.fillRect(hover.p.x+10, hover.p.y-h-10, w, h); ctx.fillStyle = "#fff"; ctx.fillText(hover.name, hover.p.x+10+pad, hover.p.y-14); }
  }

  // interactions
  useEffect(() => {
    const cvs = cvsRef.current; if (!cvs) return;
    const onWheel = (e: WheelEvent) => { e.preventDefault(); const r = cvs.getBoundingClientRect(); const mx = e.clientX - r.left, my = e.clientY - r.top; const zoom = Math.exp(-e.deltaY*0.0015); const ns = Math.min(6, Math.max(0.4, scale*zoom)); const sx = (mx - offset.x)/scale, sy = (my - offset.y)/scale; setScale(ns); setOffset({ x: mx - sx*ns, y: my - sy*ns }); };
    let dragging = false; let start: Vec2 = { x:0, y:0 }; let startOff = offset;
    const onDown = (e: MouseEvent) => { dragging = true; start = { x: e.clientX, y: e.clientY }; startOff = offset; };
    const onMove = (e: MouseEvent) => {
      const rect = cvs.getBoundingClientRect(); const mx = e.clientX - rect.left, my = e.clientY - rect.top;
      if (dragging) { setOffset({ x: startOff.x + (e.clientX - start.x), y: startOff.y + (e.clientY - start.y) }); return; }
      // hover pick
      const cur = renderPositions();
      let hit: { id: string; p: Vec2; name: string } | null = null;
      for (const c of chars) {
        const p = cur.get(c.id) ?? geoToLocal(c.latitude, c.longitude, c.locationId ?? undefined);
        const sp = { x: p.x * scale + offset.x, y: p.y * scale + offset.y };
        const d = Math.hypot(mx - sp.x, my - sp.y);
        if (d < 7 && !hit) {
          hit = { id: c.id, p: sp, name: c.name };
          break;
        }
      }
      if (hit) {
        setHover({ name: hit.name, p: hit.p });
      } else {
        setHover(null);
      }
    };
    const onUp = () => { dragging = false; };
  const onClick = () => { if (hover) { const id = chars.find(c => c.name === hover.name)?.id ?? null; setSelected(id); onFocusCharacter?.(id!); const focus = 2.2; const mx = hover.p.x, my = hover.p.y; const sx = (mx - offset.x)/scale, sy = (my - offset.y)/scale; const ns = Math.max(scale, focus); setScale(ns); setOffset({ x: mx - sx*ns, y: my - sy*ns }); } };
    cvs.addEventListener("wheel", onWheel, { passive: false }); cvs.addEventListener("mousedown", onDown); window.addEventListener("mousemove", onMove); window.addEventListener("mouseup", onUp); cvs.addEventListener("click", onClick);
    return () => { cvs.removeEventListener("wheel", onWheel); cvs.removeEventListener("mousedown", onDown); window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); cvs.removeEventListener("click", onClick); };
  }, [scale, offset, chars, hover]);

  const filteredChars = useMemo(() => {
    const t = filter.trim().toLowerCase();
    const list = t
      ? chars.filter(c => (c.name ?? "").toLowerCase().includes(t) || (c as any).status?.toLowerCase?.().includes(t))
      : chars;
    // de-duplicate by name to avoid duplicates from repeated seeding
    const seen = new Set<string>();
    const uniq: Char[] = [];
    for (const c of list) { const key = (c.name || "").toLowerCase(); if (!seen.has(key)) { seen.add(key); uniq.push(c); } }
    return uniq;
  }, [chars, filter]);

  return (
    <div className={cn("rounded-xl border border-slate-200 bg-white/70 p-3", className)}>
      <div className="mb-2 text-sm font-semibold text-slate-700">Карта NPC — зум/панорама/анимация</div>
      <div className="flex gap-3">
        {showSidebar && (
          <div className="hidden w-64 shrink-0 flex-col gap-2 rounded-lg border border-slate-200 bg-white/70 p-2 md:flex">
            <input value={filter} onChange={(e)=>setFilter(e.target.value)} placeholder="Фильтр по имени/статусу" className="w-full rounded border border-slate-300 px-2 py-1 text-sm" />
            <div className="flex-1 overflow-y-auto">
              <ul className="space-y-1 text-sm">
                {filteredChars.map(c => (
                  <li key={c.id}>
                    <button type="button" onClick={()=> onFocusCharacter?.(c.id)} className={cn("w-full rounded px-2 py-1 text-left", focusCharacterId===c.id?"bg-slate-900 text-white":"hover:bg-slate-100")}>{c.name}</button>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )}
        <div className="flex-1">
          <canvas ref={cvsRef} width={640} height={height} className="w-full cursor-crosshair select-none" />
          <div className="mt-1 text-xs text-slate-500">Колесо — зум, перетаскивание — панорама, клик — фокус</div>
        </div>
      </div>
    </div>
  );
}
