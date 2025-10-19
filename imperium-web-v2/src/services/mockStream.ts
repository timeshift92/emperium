
import { useWorldStore } from '../store/useWorldStore'
export function startMockStream() {
  const addEvent = useWorldStore.getState().addEvent
  const addNpcReply = useWorldStore.getState().addNpcReply
  const npcNames = ["Микон","Тит","Клитий","Сострат","Гирта","Лукиан"]
  const npcPhrases = [
    "Хвала богам, дождь пришёл вовремя.",
    "Торговые пошлины тянут кошель вниз.",
    "Легионеры шумят у ворот, но порядок соблюдают.",
    "Хлеб сегодня мягок, как речь оратора.",
    "Рыбаки принесли сварливые вести о ветрах."
  ]
  const worldEvents = [
    { type: "weather_update", text: "Облака затянули небо, обещая прохладу." },
    { type: "market_news", text: "Цена масла упала из-за обильного урожая." },
    { type: "rumor", text: "Говорят, купец из Массалии везёт редкие ткани." },
    { type: "festival", text: "Жрецы объявили день благодарения." }
  ]
  setInterval(()=>{
    const e = worldEvents[Math.floor(Math.random()*worldEvents.length)]
    addEvent({ id: crypto.randomUUID(), type: e.type, payloadJson: { text: e.text, at: new Date().toISOString() } })
  }, 5000)
  setInterval(()=>{
    const name = npcNames[Math.floor(Math.random()*npcNames.length)]
    const reply = npcPhrases[Math.floor(Math.random()*npcPhrases.length)]
    addNpcReply(JSON.stringify({ name, reply }))
  }, 7000)
}
