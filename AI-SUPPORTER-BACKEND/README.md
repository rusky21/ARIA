# AI Supporter — Backend

Серверная часть проекта. Запускается самостоятельно, без фронтенда: поднимает серверы и WebSocket на порту 5000 и работает всегда, независимо от подключённых клиентов.

## Запуск

```bat
run.bat
```

или

```bat
dotnet run
```

При старте ничего не спрашивает: молча поднимает все сервисы и ждёт подключений.

Горячие клавиши во время работы:
- **Пробел** — пауза/продолжение вывода логов.
- **Ctrl+Q** — остановка сервера.

## Куда подключается фронтенд

Фронтенд подключается по WebSocket на `ws://<IP сервера>:5000/ws`. При подключении сервер сразу отдаёт текущее состояние: список устройств (IP-реестр), последние пакеты трафика и найденные паттерны.

## Настройки через переменные окружения

| Переменная | Значение по умолчанию | Что делает |
|---|---|---|
| `AI_SUPPORTER_HTTP_PORT` | `5000` | Порт HTTP/WebSocket-сервера |
| `AI_SUPPORTER_JWT_KEY` | dev-ключ | Секрет для JWT-токенов |
| `AI_SUPPORTER_ADMIN_USER` | `admin` | Логин администратора |
| `AI_SUPPORTER_ADMIN_PASS` | `admin` | Пароль администратора |
| `AI_SUPPORTER_DEV_PASSWORD` | `developer` | Пароль dev-режима (чат с opencode) |
| `AI_SUPPORTER_HTTPS` | — | `true` — включить HTTPS |
| `AI_SUPPORTER_HTTPS_PORT` | `5001` | Порт HTTPS |
| `AI_SUPPORTER_OLLAMA_URL` | `http://localhost:11434/api/generate` | URL Ollama API (override local-провайдера) |
| `AI_SUPPORTER_LLM_MODEL` | `qwen2.5:7b` | Модель Ollama (override local-провайдера) |
| `AI_SUPPORTER_WHISPER_MODEL` | `ggml-base.bin` | Модель Whisper: `ggml-tiny.bin` / `ggml-base.bin` / `ggml-small.bin` |
| `AI_SUPPORTER_DEBUG` | — | `1`/`true` — подробные логи (каждый пакет, сырые ответы LLM, все HTTP-запросы) |
| `AI_SUPPORTER_FILES_ROOT` | рабочая папка процесса | Корень браузера файлов на вкладке «Файлы» (read-only) |
| `AI_SUPPORTER_CAM_USER` | `root` | Логин для VAPIX/MJPEG-запросов к камерам (обзор сети) |
| `AI_SUPPORTER_CAM_PASS` | `root` | Пароль для VAPIX/MJPEG-запросов к камерам (обзор сети) |

При первом запуске с дефолтным `admin/admin` включается обязательная смена пароля:
фронтенд не пустит дальше, пока пользователь не задаст новый пароль.

## ИИ-провайдер (`llm.json`)

LLM переключается «на лету» между локальной Ollama и облачным OpenAI-совместимым API
(вкладка «Настройки → ИИ-модель», либо файл `llm.json` рядом с exe).

```json
{
  "provider": "local",
  "local": {
    "url": "http://localhost:11434/api/generate",
    "model": "qwen2.5:7b",
    "minIntervalMs": 300,
    "maxRetries": 2
  },
  "api": {
    "baseUrl": "https://api.proxyapi.ru/openai/v1",
    "model": "gpt-4o-mini",
    "key": "ВАШ_КЛЮЧ",
    "minIntervalMs": 2500,
    "backoff429Ms": 60000,
    "maxRetries": 1
  }
}
```

- `provider` — `local` (Ollama) или `api` (OpenAI-совместимый: proxyapi, OpenRouter и т.п.).
- `api.key` хранится только на сервере; через WebSocket обратно не передаётся.
- Rate-limit задаётся для обоих провайдеров:
  - `minIntervalMs` — минимальная пауза между запросами (local ≈ 300, api ≈ 2500).
  - `backoff429Ms` — пауза-кулдаун после ответа API `429 Too Many Requests` (только api; по умолчанию 60 000).
  - `maxRetries` — число повторных попыток при ошибке сети/5xx/таймауте (по умолчанию local = 2, api = 1). При `429` повторные попытки не делаются — сразу уходит в кулдаун.
  - Запросы к LLM идут строго по одному (глобальный шлюз), фон не рвёт лимиты провайдера.
- Распознавание речи (Whisper) и синтез (Piper) всегда локальные.
- API-ключ можно оставить пустым и ввести через интерфейс «Настройки → ИИ-модель» (поле «API-ключ»); ввод ключа не требуется каждый раз при сохранении.

## Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Ollama](https://ollama.com/) с моделью `qwen2.5:7b` (`ollama pull qwen2.5:7b`) — для локальных ИИ-функций
- Для облака: API-ключ proxyapi/OpenRouter (или другого OpenAI-совместимого провайдера)
- Для захвата сетевого трафика нужны права администратора
- Whisper, Piper и прочие локальные модели загружаются автоматически при первом использовании
