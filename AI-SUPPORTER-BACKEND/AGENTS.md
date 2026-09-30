# AGENTS.md

Архитектура, сервисы, env, потоки и правила: см. **[PROJECT_SUMMARY.md](./PROJECT_SUMMARY.md)**.

## Ключевые правила
- Фронт — только транспорт, вся логика на бэкенде.
- UI-тексты только на русском.
- Без комментариев в коде; именование как в проекте.
- Стили инлайн через `C` (theme.ts) + `ui.ts` (фронт).
- После правок архитектуры — обновить PROJECT_SUMMARY.md.
- Проверки всегда: `dotnet build` + `dotnet test` (161) — бэкенд; `npx tsc --noEmit` + `npm test` (13) + `npm run build` — фронт.
- В `AppHost.cs` **два** `OnWsConnect` (SSE и WS) — синхронизировать оба.
