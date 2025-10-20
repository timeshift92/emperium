# 📖 Imperium — Книга живого мира (Design Bible v1)

> **Кредо:** *«Мир Imperium создан не для победы, а для понимания.»*

Этот репозиторий содержит главы книги мира *Imperium*: философию проекта, логику живой симуляции, архитектуру AI и техническое ТЗ.

## 📂 Структура
- `Cover.png` — обложка (A4)
- `Imperium_Book_Index.md` — оглавление
- `Imperium_Licence_and_CreatorNote.md` — лицензия и подпись
- `Chapters/*` — главы книги в расширенном Markdown
 - `Inheritance_Distribution.md` — описание правил распределения наследства (wealth и assets)

## 🛠 Сборка PDF
Рекомендуется использовать `pandoc`:
```bash
pandoc -s Imperium_Book_Index.md -o Imperium_Design_Bible_v1.pdf --from markdown --pdf-engine=xelatex
```
Для красивого стиля можно использовать шаблон `eisvogel` (опционально).

## 🧠 Для Copilot
- Вся документация на русском.
- Следуй логике мира и архитектуре из глав 2 и 5.
- При генерации кода и промптов LLM — **только структурированный JSON**.
