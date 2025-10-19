
export type DecreeResult = { ok: boolean; message: string }
export async function sendDecree(text: string): Promise<DecreeResult> {
  await new Promise(r => setTimeout(r, 1000))
  const messages = [
    "⚖️ Совет: Указ принят. Налог на зерно снижен на 10%.",
    "⚖️ Совет: Вопрос отправлен на доработку. Ждём расчёты EconomyAI.",
    "⚖️ Совет: Указ принят частично. Введена отсрочка на месяц."
  ]
  return { ok: true, message: messages[Math.floor(Math.random()*messages.length)] }
}
