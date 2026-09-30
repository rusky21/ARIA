# AGENTS.md

Фронтенд-часть AI-SUPPORTER (React 18 + Vite + TS strict).

Архитектура, сервисы, env, потоки, сокеты и все правила редактирования: см. **[PROJECT_SUMMARY.md](../../AI-SUPPORTER-BACKEND/PROJECT_SUMMARY.md)**.

## Ключевые правила
- Фронт — только транспорт: send/receive, состояние сессии; НИКАКОЙ бизнес-логики/вычислений — всё на бэкенде.
- UI-тексты только на русском.
- Без комментариев в коде; именование как в проекте.
- Стили инлайн через `C` (src/lib/theme.ts); повторяющиеся — примитивы `ui.s.*` из src/lib/ui.ts.
- Проверки всегда: `npx tsc --noEmit` + `npm test` (13) + `npm run build` (vite).
