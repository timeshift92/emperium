// React automatic JSX transform is used; no default React import required
import { useSSE } from "@/lib/useSSE";
import { Button } from "@/components/ui/button";

export default function EventsList() {
  const events = useSSE<any>("/api/events/stream");

  return (
    <div className="p-4">
      <h2 className="text-lg font-semibold mb-2">События (stream)</h2>
      <div className="flex gap-2 mb-4">
        <Button>Фильтр: всё</Button>
      </div>
      <ul className="space-y-2">
        {events.map((e, idx) => (
          <li key={idx} className="p-3 border rounded bg-white/5">
            <div className="text-sm text-muted-foreground">{new Date(e.Timestamp).toLocaleString()}</div>
            <div className="font-medium">{e.Type} — {e.Location}</div>
            <pre className="text-xs mt-2 overflow-x-auto">{JSON.stringify(e.PayloadJson ? JSON.parse(e.PayloadJson) : e.Payload, null, 2)}</pre>
          </li>
        ))}
      </ul>
    </div>
  );
}
