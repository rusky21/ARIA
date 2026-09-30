# AI-SUPPORTER — PROJECT_SUMMARY

Два связанных проекта:

- **Бэкенд**: `C:\Users\fake_netrunner\Desktop\work\AI-SUPPORTER-BACKEND` — C# / .NET 10.
- **Фронтенд**: `C:\Users\fake_netrunner\Desktop\work\AI-SUPPORTER-FRONTEND` — React 18 + Vite + TypeScript (strict).

Русскоязычный UI. Железное правило: **фронт ничего не вычисляет — только транспорт** (send/receive, состояние сессии), вся логика — на бэкенде.

---

## 1. Архитектура бэкенда

DI-регистрация — `Host\ServiceRegistrar.cs`; запуск/конфигурация — `Program.cs`, `Host\AppHost.cs` (WebSocket- и SSE-коннекты, события, REST+JWT, распределение команд), `Host\AppInitializer.cs` (при старте — healthcheck LLM: доступность Ollama и наличие модели qwen2.5:7b). Дефолтная локальная модель — `qwen2.5:7b` (`Llm\LlmSettings.cs`), при 404 `LlmClient` логирует список доступных моделей из `/api/tags`.

### Сервисы (Services\)
- **Трафик**: `TrafficSnifferService` (захват пакетов), `TrafficStorageService` (кольцевой буфер), `TrafficAnalyzerService` (анализ/паттерны), `TrafficFilterService` (серверный фильтр/снапшоты), `TrafficReportService` (отчёты: instant/daily/weekly), `TrafficStatsEngine`.
- **События**: `WsEventBus` (общая шина событий для всех каналов).
- **Память IP↔имя**: `IpRegistryService` (+ файл `ip_registry.json`). Двусторонний резолв: `Lookup(ip)→имя`, `GetByName(имя)`, `ResolveToIp(имя|IP)`. `Register` отбрасывает пустые ip/name; `Load` вычищает мусорные записи и автоперезаписывает файл. Тулы `open_camera`/`open_device`/`refresh_device`/`send_artnet` принимают IP **или зарегистрированное имя**; `TaskRouter` передаёт LLM список зарегистрированных устройств в промпт.
- **Устройства**: `DeviceDiscoveryService` (сканирование сети, MJPEG-детект, BasicAuth-креды; кандидаты на пробу = ICMP + UPnP + ONVIF (ARP — только источник MAC/vendor); параллельная TCP-проба 8 портов, стриминг найденных через `devices:found`; единый построитель MJPEG-URL `BuildCameraMjpegUrl`/`ResolveTarget`/`CameraLabel`, per-device креды `SetCameraCredentials`/`CameraCredentialsFor`, чистый snapshot-URL `CameraSnapshotEndpoint`), `DeviceService`, `DeviceTools`, `Host\DeviceTestService` (ping/http/osc/artnet-тесты).
- **Сеть**: `NetworkTools` (интерфейсы, экспорт CSV/PCAP, stats).
- **Протоколы**: `ArtnetService`, `ArtNetTools`, `OscServerService`, `OscService`, `OscTools`.
- **Задачи**: `TaskRegistryService`, `TaskRouter/TaskExecutor/ToolRegistry/ToolDescriptor` (Routing\), `TaskCallParser`.
- **Чат/пользователи**: `ChatHistoryService`, `UserStore` (PBKDF2-SHA256). `ChatAssistant`/`TaskRouter`/`LlmClient` поддерживают многоходовой контекст: в LLM уходит system + последние 30 сообщений истории (user/assistant) + текущий запрос. `LlmClient` имеет messages-оверлоады `GenerateAsync`/`GenerateStreamingAsync` (local — Ollama `/api/chat`, api — OpenAI-совместимый).
- **Голос**: `WhisperService` (whisper.cpp CLI, модель `ggml-base.bin` по умолчанию, env `AI_SUPPORTER_WHISPER_MODEL` = tiny/base/small; beam search `-bs 5`; VAD-обрезка тишины перед транскрипцией через `WakeDsp.ExtractSpeech`; пост-фильтр галлюцинаций `CleanTranscription`), `WakeWordService` (VAD → MFCC+CMVN → DTW; сглаживание score по 3 последним проверкам), `WakeDsp` (ресемплинг WAV любого формата → 16 кГц/моно, адаптивный VAD `max(vadFloor, maxRms×0.1)`, CMVN-нормализация), `WakeSettings` (`wake.json`, поле `VadFloor`), `TtsService`.
- **ASR (распознавание речи)**: выбор движка на пользователя из `UserLlmProfile.Speech` (`SpeechSelection`: `provider` local|api, `model`, `lang` ru|en|auto). Local — whisper.cpp: короткие id `tiny/base/small/medium/large-v3` маппятся в `ggml-{id}.bin`, бин/модель качаются из кэша в `%LocalAppData%\whisper`, на лету пере-инициализация модели только при смене имени; `-l ru/en` ставится только для заданного языка, иначе автодетект. API — multipart `POST {Api.BaseUrl}/audio/transcriptions` (proxyapi OpenAI-совместимый), Bearer-ключ из `llm.json`, фолбэк-модель `openai/gpt-4o-mini-transcribe`, отметка `-ключ не задан` без сети. WS-команды: `asr:models` (списки local/api моделей), `asr:check` (`provider`/`model` → честная проверка: для `api` реальный пинг `GET {BaseUrl}/models` со стоп-ватчем (ms в `asr:check:result`), при пустом ключе — «API-ключ не задан», при сбое — «API недоступен (проверьте ключ и ссылку)»; для local — `CheckEngine`: готовность движка/модели). Транскрипция возвращает `(text, error)` через `TranscribeWithMessageAsync` (`TranscribeAsync` делегирует ей); при неуспехе на WS-бинарном пути шлётся событие `voice:error` («API-ключ не задан — распознавание невозможно», «API-транскрипция недоступна ({status})», «Распознавание не удалось (whisper.cpp)», «whisper.cpp не смог распознать аудио»), `VoiceTranscription("")`. Применяется на всех голосовых путях: WS-бинарные аудио, REST `/api/voice/transcribe` (для api-провайдера локальная модель НЕ качается — `EnsureModelAsync` только при `provider != "api"`).
- **TTS (синтез речи)**: два движка из `tts.json` (`TtsSettings`: `Engine` local|api, `Voice`; `%LocalAppData%\piper`). Local — Piper: бинарь `%LocalAppData%\piper\bin\piper(.exe)`, голоса — любые `*.onnx` из `%LocalAppData%\piper\models\` (фактический список отдаётся в `tts:models`, файлов нет — голоса не показываются; автоскачивания Голосов НЕТ). API — `POST {BaseUrl}/audio/speech` с Bearer из `llm.json`, модель `openai/gpt-4o-mini-tts` (если `Api.Model` не содержит `tts`), голос из набора `alloy/echo/fable/onyx/nova/shimmer` (пустой → `alloy`), `response_format=wav`, `speed` = clamp(rate, 0.25, 4.0); пустой ключ → мгновенный отказ «API-ключ не задан». `SynthesizeAsync` (local, кэш) делегирует в `SynthesizeWithMessageAsync`; `SynthesizeViaApiAsync` возвращает `byte[]?`. WS-команды: `tts:models` (событие `tts:models`), `tts:get` (событие `tts:conf` — engine/voice/ok), `tts:set` {engine, voice} (валидация движка, `SetVoice`, сохранение `tts.json` → `tts:set:result` + `tts:conf`), `tts:check` {engine, voice} (local — «Готов»/«Piper не установлен»/«Голос не найден», api — «API-ключ задан/не задан» → `tts:check:result`); `voice:speak` использует выбранный в `ttsSettings` движок. REST `/api/voice/speak` остаётся на local-движке.
- **Конфиг/путя**: `ConfigService`, `Config\ConfigEditor`, `Config\ConfigModels`, `CameraCredentials`, `AppPaths`, `AppJson`.
- **Прочее**: `WebHostServices`, `AdminTools`, `DevService`, `FileBrowserService`, `GpuMonitor`, `VulnerabilityCache`, `Host\SystemStatsService`, `Host\TestDataService`, `Llm\{ChatAssistant,LlmClient,LlmSettings}`, `Diagnostics\Log`.

### Каналы передачи данных
- **WebSocket**: live-потоки (пакеты, метрики, события). URL и протокол — в `AppHost.cs`.
- **SSE** (Server-Sent Events): `SseService.cs` — лог/события.
- **REST**: `/api/*` — авторизация через JWT Bearer (`MCP\ApiEndpoints.cs`). Примеры: `/auth/login`, `/auth/verify`, `/users/*`, `/ip-registry`.
- **MCP-тулы**: методы, помеченные `[AiTool]` (+ `[AiToolProvider]`, `[AiParameter]`) — вызываются ИИ-ассистентом (`Attributes\`, `MCP\ModelToolAdapter`, `McpToolAdapter`, `McpServer`).

### ВАЖНО по `AppHost.cs`
- В `AppHost.cs` **ДВА** `OnWsConnect`-блока: SSE (~стр. 953) и WS (~стр. 1771). Оба уже рассылают `eventBus.Devices(...)` при подключении. При правках событий синхронизировать **оба** блока.

### LLM-профили пользователей
- Per-user `user_llm_profiles.json` — JSON-массив `UserLlmProfile` в camelCase (см. `Models\UserLlmProfile.cs`, `UserLlmProfileJson.Options` — `PropertyNameCaseInsensitive`); путь строится от `AppPaths.ProjectDir`. `LoadLlmProfiles` мигрирует легаси-файл с одиночным объектом (не массивом). Дефолтный профиль (`CreateDefaultProfile`) кэшируется в `UserStore._llmProfiles`, чтобы не перечитывать файл на каждый `GetLlmProfile`.
- WS-команды: `llm:profile:get`, `llm:profile:save`, `llm:capabilities:get`, `llm:ping` (`provider`+`model` → `LlmClient.PingAsync`, событие `llm:ping:result`), `llm:resolve` (`task` → `LlmRouter.ResolveBriefAsync`, событие `llm:resolve:result`), `asr:models` (список моделей ASR), `asr:check` (`provider`/`model` → событие `asr:check:result`, для api — реальный пинг `{BaseUrl}/models`), `camera:snapshot` (кадр → base64, событие `camera:snapshot:result`), `camera:analyze` (кадр → VisionTools, событие `camera:analyze:result`), `tts:models`, `tts:get`, `tts:set` (`engine`+`voice` → `tts.json`), `tts:check` (`engine`+`voice`), `voice:speak` (движок из `tts.json`), WS-бинарные аудио (→ `voice:transcription`, при неуспехе → `voice:error`).
- События: `llm:profile` (`GetLlmProfile` на подключении и по `llm:profile:get`), `llm:capabilities` (`LlmRouter.GetCapabilitiesDtoAsync`, кэш обновляется при `refresh:true`), `llm:profile:saved`, `llm:ping:result`, `llm:resolve:result`, `asr:models`, `asr:check:result`, `camera:snapshot:result`, `camera:analyze:result`, `tts:models`, `tts:conf`, `tts:set:result`, `tts:check:result`, `voice:error`.
- `llm:profile:save` десериализует `profile` через `UserLlmProfileJson.Options` (JsonException → `ok:false`), `UserStore.SaveLlmProfile` возвращает `true` только если username задан и файл записан (`llm:profile:saved` шлётся честно: при пустом ключе/ошибке диска — `ok:false`); после успешного сохранения вызывается `llmRouter.InvalidateCapabilities()` в обоих блоках `AppHost.cs`.
- Per-task роутинг через `LlmRouter.ResolveAsync` подключён для `ChatAssistant`, `TaskRouter` (chat/voice/tools) и `VisionTools` (vision) — выбранные `Model`/`UseApi` проставляются в `LlmRequestOptions`, `LlmClient` их применяет (иначе используются глобальные `LlmSettings`). При `AutoSelect=true` авто-выбор учитывает выбранную в профиле модель как приоритет (если она есть в списке доступных у провайдера), иначе (`PickModel`) приоритет — **дефолтная** модель (`Api.Model`/`EffectiveLocalModel`, если есть в списке), затем первая доступная, затем дефолт. Семантика провайдера **единая для всех задач**: `api`/`auto` при заданном API-ключе предпочитают API, при ручном выборе `api` с пустым ключом — фолбэк на local с логом. `ResolveBriefAsync` маппит и `fastToolCalling` (→ `LlmTask.FastToolCalling`).
- Чат с изображением (`imageDataUrl != null`) маршрутизируется как `LlmTask.Vision` (оба блока `AppHost.cs`), а не `Chat`, чтобы изображения не уходили в не-мультимодальную модель.
- Кэш capabilities в `LlmRouter` (`_capsCache`, 5 мин) сбрасывается вызовом `InvalidateCapabilities()` при `settings:llm:set` и при `llm:profile:save`. При сбое `GET {BaseUrl}/models` / пустом списке API выдаёт капсы `null` (не «жив с пустым списком»).
- Pacing/кулдаун `LlmClient.WaitForPacingAsync` считает интервалы и кулдаун 429 по **пер-запросному** признаку провайдера (`options.UseApi`), а не по глобальному `LlmSettings.Provider`, чтобы локальные задачи при глобальном api-провайдере не тормозились интервалом API.
- `TaskRouter.RouteWithStreamingAsync`/`ProcessAsync` при пустом ответе tool-модели (или `clarify`/`none`) **фолбэкят на чат-модель** (`ChatAssistant` через `chatTask`) с `LogWarning` «tool-проба не удалась — фолбэк на чат-модель» вместо возврата null/«Готово.»; проба логирует реально выбранные транспорт и модель (`toolOpts.UseApi == true ? "api" : "local"` + resolved model).
- `LlmClient` и `TaskRouter` подключены к `ILogger` (Serilog → консоль; `ServiceRegistrar.cs`, `AppHost.cs` DI-блок) — «Request/Streaming request» показывают **реальный** транспорт (`ShouldUseApi(options)`) и модель (`options?.Model ?? CurrentModel`), ошибки генерации (HTTP/Timeout/Network/«Ollama недоступна») больше не проглатываются.
- Транспортный фолбэк в `LlmClient.GenerateApiAsync`/`GenerateApiMessagesAsync` (включая json_object-ветку): при 400/404 с телом-признаком неподдерживаемой модели («Model not supported»/`model_not_found`/«does not exist»/…) и моделью, отличной от дефолтной, запрос **один раз** повторяется с `_settings.Api.Model` (пересборка body, `LogWarning`) — провайдер, чей `GET /models` содержит не-генерируемые ID, не ломает авто-выбор/тулы.
- Кэш отвергнутых моделей: `LlmClient` при «Model not supported» помечает ID в `_blockedApiModels` (в т.ч. дефолтную, если и она отклонена). `PickModel` исключает заблокированные из пула, `llm:ping` отвечает `ok=false` без запроса и без pacing-ожидания, `InvalidateCapabilities`/`UpdateSettings` сбрасывают кэш. «Проверить всё» и `llm:resolve` перестают гонять заведомо мёртвые ID (`tts-1`, `gpt-4` из /models) — тише и быстрее старт задач.
- `WakeSettings` — убран параметризованный ctor (`ILogger? logger`, мёртвый): System.Text.Json выбирал его для десериализации и падал («Each parameter… must bind»), `wake.json` молча игнорировался. Теперь parameterless — файл читается.
- Ошибки внешних услуг логируют тело ответа провайдера: `TtsService`/`WhisperService` API-пути — `{Status}: {Body}` (обрезка 500); локальный whisper при крэше CLI возвращает «код выхода {ExitCode}» в `voice:error`.
- Локальный whisper при ненулевом коде выхода **один раз** повторяется с облегчёнными параметрами (`-bs 2 -t 1` вместо `-bs 5 -t 4`); из аргументов CLI убран `-np` (multi-processor на Windows нестабилен — `GGML_ASSERT(ctx->mem_buffer != NULL)` в `ggml.c`), потоки `-t 4` → `-t 2`. Свежий бинарь автоматически перекачивается при отсутствии `whisper-cli.exe` в `%LOCALAPPDATA%\whisper\bin`.
- `OscServerService` при остановке (`UdpClient.Close` → `SocketException` 995 OperationAborted) логирует INFO «Остановлен» и завершает цикл без ERR-стектрейса.
- API-модели для UI/выбора подтягиваются живьём из `GET {BaseUrl}/models` (провайдер proxyapi / OpenAI-совместимый; ID двухчастные `openai/…`, `anthropic/…`) через `LlmClient.GetAvailableApiModelsAsync`; Vision-метка модели — по эвристике имени (`gpt-4o`, `gpt-5`, `gemini`, `claude`, `vision`, `vl`, `llava`, `moondream`, `glm`, `pixtral`, `reka`) в `LlmRouter.IsApiVisionModel`. При сбое/пустом списке API выдаёт пустой список моделей (без фолбэка). Дефолтная API-модель — `openai/gpt-5-mini` (`Llm\LlmSettings.cs`).
- `DeviceDiscoveryService`: скан ускорен — ICMP-свип 750 мс (SemaphoreSlim 64), SSDP/WS-Discovery и ARP (`Task.Run`) параллельны свипу (окна multicast 3с→1.5с), TCP-проба 8 портов — `Task.WhenAll` с общим дедлайном 2 с/хост, `DetectMjpegAsync` 11 эндпоинтов параллельно (1.2 с), Header/VAPIX — общие дедлайны 1.5с/3с; ARP больше не пополняет пул кандидатов (только MAC/vendor для живых хостов). Найденные устройства **стримятся**: по завершении пробы хоста `_devices` обновляется (upsert по IP + save) и шлётся событие `devices:found` (`WsEventBus.ProjectDevice` — та же проекция, что `devices:list`), список кликабелен прямо во время скана, финальный `devices:list` — reconcile (известные, не найденные — `Online=false`). `device:scan` — fire-and-forget `Task.Run` в обоих `AppHost`-блоках (WS-цикл не блокируется; `IsScanning` защищает от дублей).
- Креды камер: per-device на сессию (`SetCameraCredentials(ip,user,pass)` → `_deviceCredentials`, приоритет над env `AI_SUPPORTER_CAM_USER/PASS`); `CameraSnapshotEndpoint` отдаёт чистый URL без `user:pass@`, все HTTP-запросы ходят с заголовком `Authorization: Basic` (`SendWithBasicAuth`) — **креды больше не светятся в URL/логах**. Снимок идёт на single-frame `axis-cgi/jpg/image.cgi` (MJPEG-поток остаётся только для `camera:open`: `BuildCameraMjpegUrl` вшивает креды в URL — их понимает браузер).
- `camera:snapshot` принимает опц. `user`/`pass` (сохраняются через `SetCameraCredentials`, переиспользуются далее); `camera:snapshot:result` получил поля `ok` и `message` — причина сбоя («Камера отклонила логин/пароль (Unauthorized / Forbidden).», «Ошибка HTTP N», текст исключения, «Камера не вернула кадр (пустой ответ).»). Фронт (`Devices.tsx`) при неудаче показывает причину и кнопку «Указать логин/пароль» (window.prompt-алерты) → повтор снимка с кредами. `Host\DeviceTestService` camera-тест тоже ходит с Basic-заголовком и чистым URL.

---

## 2. Фронтенд

### Хуки (`src\hooks\`)
- `useAssistant.ts` — монолит (~1300+ строк): сырое WS-состояние, REST-запросы, subscription, весь транспорт. Интерфейс `AssistantState` + `registerIp(ip, name)`. `openFloatingWindow` центрирует первое окно и раскладывает последующие ограниченным каскадом внутри вьюпорта. Стриминг `devices:found` (upsert по IP в `devices`), `camera:snapshot:result` нёсет `ok`/`message`.
- `useOrb`, `useWakeChunks`, все остальные хуки в `src\hooks\`.

### Компоненты (`src\components\`)
- `Shell.tsx` (каркас, адаптивный sidebar 78/60px, topbar, навигация), `Home.tsx` (bento-дашборд), `Chat.tsx` (+ `gpt.tsx`, `ChatSidebar`, `ContextMenu`, `BrowserPanel`).
- `Network.tsx` — все вкладки (Захват/Паттерны/Сейчас/Суточный/Недельный/Тест), виртуализация таблицы, иконки вместо эмодзи.
- `Devices.tsx` — таблица через `Table.tsx`-примитивы, 2 вкладки, контекст-меню, «Задать имя», показ причины ошибки снимка + алерт-промпты «Указать логин/пароль».
- `FloatingWindows.tsx` — плавающие окна (камера/браузер), drag/resize/minimize, LIVE-бейдж для камеры; токены `C.panelAlt`, `C.panelSolid`, `acc()`, `w()`, `R`.
- `System.tsx` — диспетчер задач через `Table.tsx`-примитивы (Процессы/Производительность/Задачи ARIA).
- `Tasks.tsx` — карточки задач через `Table.tsx`- композиты.
- `Files.tsx`, `Terminal.tsx`, `Settings.tsx`, `Dev.tsx`.
- `ReportInstant.tsx` / `ReportPeriod.tsx` — анализ трафика, северити-индикаторы через токены (`C.red/amber/green/purple/cyan`, `cyanA()`), `ariaFadeFast`.
- `PacketDetail.tsx` — разбор пакетов (layers/hex), `C.tableDim`, `C.inputBg`, `R.m`; `hexView`/`buildLayers` экспортируются.
- `Icon.tsx` — SVG-иконки (chat, network, system, terminal, files, tasks, settings, folder, file, clock, shield, refresh, tool, send, close, audio, wifi, camera, like, dislike, copy, edit, share, play, check, stop).

### lib /
- `theme.ts` — токены: `C` (цвета/шрифты), `R` (скругления xs–xxl–pill), `SP` (spacing 1–6), `FS` (размеры шрифта x–xxl), хелперы `w(a)`/`acc(a)`/`cyanA(a)`, семантические цвета (`onAccent`, `accentText`, `headerSolid`, `bgCrust`, `panelAlt`, `panelHi`, `rowHover`, `inputBg`, `tableText`, `tableDim`), `protoColor`, `levelColor`.
- `Table.tsx` — композиты таблиц: `TablePanel`, `TableHead`, `SortHead`, `TableRow`, `Td`, `MiniBar`, `Badge`, `Dot`, `GlowDot`, `StatCard`.
- `viewport.ts` — хук `useViewport()` → `{ w, h, compact(<1280), wide(≥1680), ultra(≥2560) }`.
- `ui.ts` — общие стилевые примитивы `ui.s.{btn,btnGhost,btnActive,panel,card,input,chip}`, `ui.nav.tab`, `ui.th`, `ui.td`, `ui.tr`, единый transition `.18s cubic-bezier(.4,0,.2,1)`.
- `auth.ts` — сессия, `getSession`, `apiFetch` (Bearer + JSON), `saveSession`, `clearSession`.
- `data.ts` — типы (`Packet`, `Device`, `Message`, `TaskLoad`, `TaskItem`, ...).
- `config.ts` — `API_URL` и т.п.

### Стили
Илайн через токены `C`/`ui`/`var(--)` (theme.ts + Table.tsx + ui.ts). `src/style.css` — CSS-переменные `--fs-*`, `--sp-*`, `--r-*`; медиа-брейкпойнты (1366/1100/2560); `@keyframes ariaFadeFast` (.22s вход экранов), `ariaSpin`, `ariaDot`; `@media (prefers-reduced-motion: reduce)` отключает анимации. НЕ создавать новые стилевые компоненты наугад.

---

## 3. `env`-переменные (бэкенд)

| Переменная | Назначение | Дефолт |
|---|---|---|
| `AI_SUPPORTER_HTTP_PORT` | HTTP-порт | 5000 |
| `AI_SUPPORTER_HTTPS` | вкл. HTTPS | — |
| `AI_SUPPORTER_HTTPS_PORT` | HTTPS-порт | 5001 |
| `AI_SUPPORTER_JWT_KEY` | секрет JWT | dev-ключ (см. `jwt_key.txt`) |
| `AI_SUPPORTER_ADMIN_PASS` | пароль дефолтного админа | admin |
| `AI_SUPPORTER_CAM_USER` | логин для камер | root |
| `AI_SUPPORTER_CAM_PASS` | пароль для камер | root |
| `AI_SUPPORTER_DEV_PASSWORD` | Dev-режим | — |
| `AI_SUPPORTER_FILES_ROOT` | корень файлового браузера | — |
| `AI_SUPPORTER_OLLAMA_URL` | LLM URL | локальный |
| `AI_SUPPORTER_LLM_MODEL` | модель LLM | локальная |
| `AI_SUPPORTER_WAKE_THRESHOLD` | порог wake-word | — |
| `AI_SUPPORTER_DEBUG` | debug-логи | — |

Дефолтный пользователь: `admin` / `admin` (MustChangePassword=true при первом входе). Хранилище пользователей — `users.json` (PBKDF2-SHA256).

---

## 4. Потоки данных

```
chat (WS) → ChatHistoryService (история) → TaskRouter → ChatAssistant (system + последние 30 сообщений + запрос; для изображений — `imageDataUrl`, конвертация в OpenAI content-array или Ollama `images[]`)
                                          → LlmClient (messages) → Ollama /api/chat | OpenAI chat/completions
захват (сеть) → TrafficSnifferService → TrafficStorageService
                                    ↓
                            WsEventBus (events)
                                    ↓
            WebSocket/SSE → useAssistant (фронт) → компоненты/таблицы
                                    ↓
                          REST /api/* (действия, JWT Bearer)
                                    ↓
                        MCP-тулы [AiTool] → ИИ-ассистент
```

Сводка типов событий шины: `traffic:log`, `devices:list`, `ui:window:open`, `chat`, `traffic:patterns`, `device:test-result`, `traffic:interface:set`, `voice:transcription`, `voice:response`, `voice:audio`, `voice:speak:done`, `voice:error`, `tts:models`, `tts:conf`, `tts:set:result`, `tts:check:result`, `asr:check:result` и т.п. Запросы через WS: `device:scan`, `camera:open` (открывает камеру тем же путём, что и тул ИИ `open_camera`, — единый URL в `DeviceDiscoveryService`), `camera:snapshot`, `camera:analyze`, `device:test`, `voice:process`, `voice:speak`, `llm:ping`, `llm:resolve`, `tts:models`, `tts:get`, `tts:set`, `tts:check`, `asr:models`, `asr:check` и др. (есть в обоих `OnWsConnect`).

---

## 5. Правила редактирования кода (обязательно)

1. **Фронт никогда не выполняет бизнес-логику/вычисления** — только send/receive, состояние сессии, транспорт. Всё остальное — на бэкенде.
2. **UI-тексты только на русском.**
3. **Без комментариев в коде** (только если явно попросят). Именование как в проекте.
4. **Стили инлайн** через токены `C`/`ui`/`var(--)` (theme.ts); для таблиц — примитивы `Table.tsx`; адаптив — `useViewport()` из `viewport.ts`. Не плодить стилевые компоненты наугад.
5. **После каждой правки** — обновлять этот `PROJECT_SUMMARY.md` (архитектура/потоки/правила), если правка их касается.
6. **Проверки всегда**:
   - Бэкенд: `dotnet build` (0 ошибок) + `dotnet test` (161 тест).
   - Фронт: `npx tsc --noEmit` (чисто) + `npm test` (vitest, 13) + `npm run build` (vite).
7. В `AppHost.cs` **два** `OnWsConnect` (SSE и WS) — синхронизировать оба при правках событий.
8. `traffic:filter` с пустой строкой НЕ отправлять на входе; `trafficFiltered` отдаётся только при активном фильтре.
