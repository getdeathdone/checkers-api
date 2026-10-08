# Checkers REST Web API (Chinook & KingsRow Integration)

RESTful Web API сервис для игры в шашки (English Draughts / American 8x8 Checkers) на платформе **.NET 8 (ASP.NET Core)** с интеграцией движка **Chinook / KingsRow** и 2–8 шашечных эндшпильных баз (tablebases), хостируемый под **IIS на Windows Server**, с встроенным интерактивным веб-интерфейсом доски 8x8.

---

## 📋 О проекте и соответствие ТЗ

Сервис полностью реализует требования технического задания:
* **Платформа:** Windows Server, IIS + ASP.NET Core (.NET 8).
* **Интеграция с движками:** Windows-адаптер `KingsRowCliAdapter` для **KingsRow / Chinook** с эндшпильными базами 2–8 фигур ([Ed Gilbert KingsRow](https://edgilbert.org/Checkers/KingsRow.htm), [KingsRow English](https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm)) + встроенный высокоточный Alpha-Beta движок `BuiltInCheckersEngine` (работает сразу «из коробки»).
* **Пул воркеров (Worker Pool):** Пул долгоживущих процессов (по умолчанию 2 воркера, без создания процесса на каждый запрос) с асинхронной блокировкой `SemaphoreSlim(1, 1)` на каждый воркер и диспетчеризацией Round-Robin.
* **Прогрев на старте (Warmup):** Инициализация и прогрев воркеров при запуске приложения (`WorkerPoolWarmupService`).
* **Кэширование:** Потокобезопасный LRU-кэш на 20,000 позиций с TTL 15 минут по каноническому ключу PDN.
* **Эндшпильные базы (Tablebases):** Мгновенный ответ (<50 мс) с флагом `tablebaseHit: true` для позиций с $\le 8$ фигурами.
* **Правила шашек:** Строгое соблюдение английских шашек (обязательное взятие, серии взятий `22-18x11-7`, дамки, превращение).
* **Интерактивный UI:** Встроенный веб-интерфейс доски 8x8 (`/index.html`) с полями 1–32, пресетами, выбором сложности и игрой против бота.
* **Логирование:** JSON-логирование каждого запроса (`requestId`, `timeMs`, `depth`, `nodes`, `tablebaseHit`), логирование IIS stdout и ошибок в каталог `logs/`.

---

## 🏛️ Архитектура решения

```
┌────────────────────────────────────────────────────────────────────────┐
│                      HTTP Client / Web Browser UI                      │
└────────────────────────────────────┬───────────────────────────────────┘
                                     │
                                     ▼
┌────────────────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Web API (IIS / Kestrel)                │
│                                                                        │
│  [RequestLoggingMiddleware] ──► Логирует JSON телеметрию в logs/       │
│                                                                        │
│  [Controllers]                                                         │
│    ├── POST /v1/move/suggest  (Soft/Hard Timeouts, 422, 504)           │
│    ├── POST /v1/move/validate (Проверка легальности хода)              │
│    └── GET  /healthz          (Мониторинг пула воркеров)               │
└────────────────────────────────────┬───────────────────────────────────┘
                                     │
                                     ▼
┌────────────────────────────────────────────────────────────────────────┐
│                        Checkers Business Service                       │
│                                                                        │
│   1. PdnParser: Валидация нотации, полей 1-32, количества шашек        │
│   2. MemoryLruCache: Проверка кэша (20,000 позиций, TTL 15 минут)      │
│   3. MoveGenerator: Генерация ходов (обязательные взятия, дамки)       │
└──────────────────┬───────────────────────────────────┬─────────────────┘
                   │                                   │
         (<= 8 фигур)                                  │ (> 8 фигур)
                   ▼                                   ▼
┌──────────────────────────────────────┐  ┌──────────────────────────────┐
│  Endgame Tablebase Prober (< 50 ms)  │  │  Engine Worker Pool          │
│  Chinook 2-8 piece DBs (tablebaseHit)│  │  (2 воркера, SemaphoreSlim,  │
└──────────────────────────────────────┘  │   Round-Robin балансировка)  │
                                          └──────────────┬───────────────┘
                                                         │
                                                         ▼
                                          ┌──────────────────────────────┐
                                          │ KingsRow / Chinook Engine    │
                                          │ stdin/stdout CLI Adapter     │
                                          │ (Fallback: BuiltIn AlphaBeta)│
                                          └──────────────────────────────┘
```

---

## 📁 Структура проекта

```text
├── CheckersApi.sln                     # Решение .NET 8
├── deploy.bat                          # Единый CLI/GUI скрипт деплоя для Windows
├── deploy-iis.ps1                      # Скрипт конфигурации IIS и прав доступа
├── README.md                           # Документация проекта
├── src/
│   ├── CheckersApi.Core/               # Ядро бизнес-логики и движка
│   │   ├── Cache/
│   │   │   ├── ILruCache.cs            # Интерфейс потокобезопасного кэша
│   │   │   └── MemoryLruCache.cs       # Реализация LRU (20,000 позиций, TTL 15 мин)
│   │   ├── Engine/
│   │   │   ├── IEngineAdapter.cs       # Контракт адаптера движка
│   │   │   ├── KingsRowCliAdapter.cs   # Долгоживущий CLI-адаптер KingsRow/Chinook
│   │   │   ├── BuiltInCheckersEngine.cs# Встроенный Alpha-Beta движок + эндшпильные базы
│   │   │   └── EngineWorkerPool.cs     # Пул воркеров с Round-Robin и SemaphoreSlim
│   │   ├── Logic/
│   │   │   ├── BoardGeometry.cs        # Геометрия полей 1-32 (соседи, диагонали)
│   │   │   ├── MoveGenerator.cs        # Генератор ходов, взятия, мульти-прыжки
│   │   │   └── PdnParser.cs            # Парсер и канонический нормализатор PDN
│   │   └── Models/
│   │       ├── ApiDtos.cs              # DTO запросов/ответов по ТЗ
│   │       ├── BoardPosition.cs        # Битовое/структурное представление доски
│   │       └── Move.cs                 # Структура хода
│   │
│   └── CheckersApi.Web/                # ASP.NET Core Web API приложение
│       ├── Configuration/
│       │   └── AppOptions.cs           # Конфигурация Engine, Cache, Limits
│       ├── Controllers/
│       │   ├── MoveController.cs       # /v1/move/suggest, /v1/move/validate
│       │   └── HealthController.cs     # /healthz
│       ├── Middleware/
│       │   └── RequestLoggingMiddleware.cs # JSON логирование каждого запроса
│       ├── Services/
│       │   ├── CheckersService.cs      # Сервис бизнес-логики и координации
│       │   ├── FileLoggerProvider.cs   # Надежный файловый логгер (logs/app-*.log)
│       │   └── WorkerPoolWarmupService.cs # Фоновый прогрев пула воркеров
│       ├── wwwroot/                    # Интерактивная веб-доска 8x8
│       │   ├── index.html
│       │   ├── css/style.css
│       │   └── js/app.js
│       ├── Program.cs                  # Точка входа, DI, перехват падений
│       ├── appsettings.json            # Настройки движка, баз и таймаутов
│       └── web.config                  # Конфигурация IIS AspNetCoreModuleV2
│
└── tests/
    └── CheckersApi.Tests/              # 19 Unit-тестов xUnit
        ├── MoveGeneratorTests.cs       # Тесты взятий, серии прыжков, дамок
        ├── PdnParserTests.cs           # Тесты валидации и парсинга PDN
        └── ServiceAndCacheTests.cs     # Тесты кэша, уровней сложности, API
```

---

## 🛠️ Конфигурация (`appsettings.json`)

Файл конфигурации полностью соответствует спецификации ТЗ:

```json
{
  "Engine": {
    "Type": "chinook",
    "Path": "C:\\engines\\chinook\\chinook.exe",
    "Workers": 2,
    "Databases": "D:\\tb\\chinook"
  },
  "Cache": {
    "Capacity": 20000,
    "TtlMinutes": 15
  },
  "Limits": {
    "DefaultSoftTimeMs": 300,
    "DefaultHardTimeMs": 1200
  }
}
```

### Параметры конфигурации:
* **`Engine.Type`**: Имя движка (`"chinook"`).
* **`Engine.Path`**: Путь к исполняемому файлу движка KingsRow / Chinook на Windows Server (например, `C:\engines\kingsrow\kingsrow.exe`). Если движок по указанному пути не установлен, сервис **автоматически переключается на встроенный высокопроизводительный движок `BuiltInCheckersEngine`**, поэтому сервис работает стабильно в любых условиях.
* **`Engine.Workers`**: Количество долгоживущих процессов воркеров в пуле (по умолчанию `2`).
* **`Engine.Databases`**: Путь к папке с 2–8 фигурными базами эндшпилей Chinook (tablebases).
* **`Cache.Capacity`**: Максимальное количество позиций в LRU кэше (`20000`).
* **`Cache.TtlMinutes`**: Время жизни кэшированной позиции (`15` минут).
* **`Limits.DefaultSoftTimeMs`**: Рекомендуемое мягкое время расчета хода (`300` мс).
* **`Limits.DefaultHardTimeMs`**: Жесткий лимит времени (`1200` мс), при превышении которого возвращается `504 Gateway Timeout`.

---

## 📖 Спецификация API эндпоинтов

### 1. `POST /v1/move/suggest`
Расчет лучшего хода для переданной позиции.

#### Уровни сложности (`level`):
* **`weak`**: глубина 6–8, время хода ~100 мс. Детерминированный расчет без случайности.
* **`medium`**: глубина 10–12, время хода ~250 мс.
* **`strong`**: сначала опрашивает эндшпильные базы ($\le 8$ фигур). Если позиция не в базе — глубина 14–18, время хода 500–600 мс.

#### Пример запроса (Request):
```http
POST /v1/move/suggest HTTP/1.1
Content-Type: application/json

{
  "gameId": "checkers-8x8",
  "state": { 
    "notation": "PDN", 
    "position": "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16" 
  },
  "level": "weak",
  "limits": { 
    "maxDepth": 12, 
    "softTimeMs": 250, 
    "hardTimeMs": 1200 
  }
}
```

#### Пример ответа (Response 200 OK):
```json
{
  "engine": "chinook",
  "bestMove": "22-18x11-7",
  "pv": [
    "22-18",
    "5-9",
    "18x11",
    "7-16",
    "30-26"
  ],
  "scoreOrWDL": 0,
  "depth": 12,
  "nodes": 153201,
  "positionKey": "pdn:B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
  "info": { 
    "tablebaseHit": false, 
    "timeMs": 248 
  }
}
```

#### Поведение и коды ответа:
* `200 OK`: Успешный расчет хода.
* `422 Unprocessable Entity`: Ошибка валидации PDN нотации, неверные номера полей, превышено число шашек или у текущего игрока нет доступных ходов.
* `504 Gateway Timeout`: Время расчета превысило `hardTimeMs` (срабатывает отмена по `CancellationToken`).
* `500 Internal Server Error`: Критическая внутренняя ошибка движка.

---

### 2. `POST /v1/move/validate`
Проверка хода на соответствие правилам английских шашек (включая обязательное взятие).

#### Пример запроса (Request):
```http
POST /v1/move/validate HTTP/1.1
Content-Type: application/json

{
  "position": "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
  "move": "12-19"
}
```

#### Пример ответа (Response 200 OK):
```json
{
  "legal": true
}
```
*Если ход нелегален (например, пропущено обязательное взятие):*
```json
{
  "legal": false,
  "error": "Capture is mandatory in English Draughts."
}
```

---

### 3. `GET /healthz`
Эндпоинт мониторинга состояния сервиса и пула воркеров.

#### Пример ответа (Response 200 OK):
```json
{
  "ok": true,
  "workers": 2
}
```

---

## 💻 Развертывание и управление (`deploy.bat`)

Для развертывания под Windows подготовлен единый скрипт **[`deploy.bat`](file:///Users/getdeathdone/Downloads/Checkers%20REST%20Web%20API%20using%20Chinook/deploy.bat)**.

### Обычный запуск (в 1 клик):
Просто дважды кликните `deploy.bat` в проводнике Windows:
1. Скрипт сам запросит права Администратора через системный UAC (если они требуются).
2. Автоматически скачает и установит .NET 8 SDK, если он отсутствует в системе.
3. Опубликует проект в `C:\inetpub\CheckersApi`.
4. Настроит сайт и пул приложений `CheckersApiPool` в IIS на порту 5000 (а если IIS недоступен — автоматически запустит в режиме Standalone Kestrel).
5. Откроет браузер с интерактивной доской `http://localhost:5000/index.html`.
6. Окно консоли **никогда не закрывается автоматически** (`pause`), пока вы не нажмете клавишу.

### Параметры командной строки (CLI):
```cmd
deploy.bat [options]
```

* `-m, --mode <mode>`: Режим работы:
  * `iis` *(по умолчанию)* — автоматическая публикация и запуск сайта под IIS на Windows Server.
  * `standalone` — автономный запуск через Kestrel без необходимости IIS (порт 5000).
  * `test` — запуск всех 19 xUnit unit-тестов.
  * `install-bundle` — скачивание и установка .NET 8 Hosting Bundle для IIS.
* `-p, --port <number>`: Порт HTTP для привязки (по умолчанию `5000`).
* `--path <dir>`: Папка публикации для IIS (по умолчанию `C:\inetpub\CheckersApi`).
* `--site-name <name>`: Имя сайта в IIS (по умолчанию `CheckersApi`).
* `--app-pool <name>`: Имя пула приложений в IIS (по умолчанию `CheckersApiPool`).
* `--no-browser`: Не открывать браузер автоматически.
* `-y, --no-pause`: Не ждать нажатия клавиши при завершении (для автоматических CI/CD скриптов).
* `-h, --help`: Вывод справки по аргументам.

#### Примеры команд:
```cmd
:: Развертывание в IIS на стандартном порту 5000
deploy.bat

:: Развертывание в IIS на 80 порту без ожидания нажатия клавиш
deploy.bat --mode iis --port 80 --no-pause

:: Автономный запуск Kestrel на порту 5000 (без IIS)
deploy.bat --mode standalone

:: Запуск unit-тестов
deploy.bat --mode test

:: Установка .NET 8 Hosting Bundle для IIS
deploy.bat --mode install-bundle
```

---

## 📝 Логирование (Logs)

Логирование работает непрерывно и сохраняется в каталог `logs/`:

1. **`logs/deploy.log`**: Полный журнал этапов развертывания (вывод компилятора `dotnet publish`, настройки IIS, статус проверки `/healthz`).
2. **`logs/app-YYYYMMDD.log`**: Непрерывный рабочий лог приложения:
   * Жизненный цикл (запуск, остановка, PID, версия .NET).
   * Инициализация и прогрев пула воркеров Chinook.
   * Каждый входящий запрос в формате JSON:
     ```json
     {"requestId":"d2b1f8...","timeMs":248,"depth":12,"nodes":153201,"tablebaseHit":false,"statusCode":200,"path":"/v1/move/suggest"}
     ```
3. **`logs/stdout_*.log`**: Лог вывода процесса из IIS (активирован через `web.config` модуль `AspNetCoreModuleV2`).
4. **`logs/startup_error.log`**: Фиксация фатальных необработанных исключений через хуки `AppDomain.CurrentDomain.UnhandledException` и `TaskScheduler.UnobservedTaskException`.

Права на запись (`Modify`) в каталог `logs/` автоматически выдаются для `IIS AppPool\CheckersApiPool` и `IIS_IUSRS`.

---

## ♟️ Интерактивный веб-интерфейс (Checkers Board UI)

Интерфейс доступен по адресу: **`http://localhost:5000/index.html`**

### Возможности интерфейса:
* **Классическая доска 8x8**: Темные игровые поля пронумерованы стандартными номерами **1–32**.
* **Пресеты позиций (Presets)**:
  * *Начальная позиция*: стандартная расстановка шашек.
  * *Эндшпиль 4 фигур (Tablebase Demo)*: позиция с 4 шашками для мгновенной демонстрации работы эндшпильных баз (<50 мс, `tablebaseHit: true`).
  * *Тактический удар (Multi-jump)*: позиция с серией обязательных взятий шашек.
* **Выбор уровня сложности**: переключатели *Weak*, *Medium*, *Strong*.
* **Режим игры против бота**: возможность делать ходы мышью на доске и получать автоматический ответ от движка.
* **Панель телеметрии реального времени**: отображение рассчитанного хода, PV-линии, оценки позиции, глубины поиска, количества просмотренных узлов, времени расчета и статуса попадания в базы (Tablebase Hit).

---

## 🧪 Запуск модульных тестов (xUnit)

Проект включает 19 модульных тестов, покрывающих все аспекты ТЗ:
* Правила ходов и обязательных взятий (включая сложные серии прыжков).
* Превращение простой в дамку и правила хода дамок.
* Парсинг, валидацию и нормализацию PDN позиций.
* Потокобезопасность и TTL инвалидацию LRU-кэша.
* Валидацию API эндпоинтов и кодов ответа (200, 422, 504).

Запуск тестов через `deploy.bat`:
```cmd
deploy.bat --mode test
```
Или стандартной командой .NET:
```bash
dotnet test CheckersApi.sln -c Release --nologo
```

---

## 🎯 Критерии приемки (Acceptance Criteria Verification)

| Критерий ТЗ | Ожидаемый результат | Статус |
|---|---|:---:|
| **Health Check на старте** | `GET /healthz` возвращает `{ "ok": true, "workers": 2 }` | ✅ Пройдено |
| **Эндшпильная позиция $\le 8$ фигур** | Ответ за $< 50$ мс с `tablebaseHit: true` | ✅ Пройдено |
| **Миттельшпиль (уровень Strong)** | Ответ за $< 600$ мс с легальным ходом | ✅ Пройдено |
| **Некорректная PDN нотация** | Возвращает HTTP код `422 Unprocessable Entity` | ✅ Пройдено |
| **Превышение таймаута (Hard timeout)** | Возвращает HTTP код `504 Gateway Timeout` | ✅ Пройдено |
| **Интерфейс доски** | Интерактивная доска 8x8 на `/index.html` с номерами 1–32 | ✅ Пройдено |

---

## 📞 Контакты

По техническим вопросам и обратной связи:
* **Telegram:** [@Y_M_tech](https://t.me/Y_M_tech)
