
import { create } from "zustand"
interface GameEvent { id: string; type: string; payloadJson: any }
interface NpcReply { name: string; reply: string }
interface WorldState {
  events: GameEvent[];
  npcReplies: NpcReply[];
  economyVersion: number;
  addEvent: (ev: GameEvent) => void;
  addNpcReply: (json: string) => void;
  bumpEconomy: () => void;
}

export const useWorldStore = create<WorldState>((set) => ({
  events: [],
  npcReplies: [],
  economyVersion: 0,
  addEvent: (ev) => set((s) => ({ events: [ev, ...s.events].slice(0, 30) })),
  addNpcReply: (json) => {
    try {
      const obj = JSON.parse(json);
      set((s) => ({ npcReplies: [{ name: obj.name, reply: obj.reply }, ...s.npcReplies].slice(0, 20) }));
    } catch {}
  },
  bumpEconomy: () => set((s) => ({ economyVersion: s.economyVersion + 1 })),
}));
