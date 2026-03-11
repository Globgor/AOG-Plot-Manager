# 🌾 AgOpenGPS Plot Manager

**Автоматическая система управления секционным опрыскиванием для полевых агрономических испытаний.**

Программно-аппаратный комплекс, который интегрируется с [AgOpenGPS](https://github.com/farmerbriantee/AgOpenGPS) для точного нанесения препаратов по схеме делянок опыта. Управляет 14 электромагнитными клапанами через Teensy 4.1, принимая решения о включении/выключении на основе GPS-позиции, RTK-качества, скорости и давления воздуха.

---

## 📐 Архитектура системы

```
┌────────────────────────────────┐
│        AgOpenGPS (ПК)          │
│ GPS + RTK + Автопилот          │
└────────┬───────────┬───────────┘
         │ UDP 8888  │ UDP 8888
    PGN 253 (GPS)  PGN 229 (Sections)
         │           │
┌────────▼───────────▼───────────┐
│     PlotManager.UI (WinForms)  │
│  ┌──────────────────────────┐  │
│  │   PlotManager.Core       │  │
│  │ SpatialEngine             │  │
│  │ SectionController         │  │
│  │ PlotModeController        │  │
│  └──────────┬───────────────┘  │
└─────────────┼──────────────────┘
         USB Serial │ 115200 бод
    PlotProtocol    │ (0xAA 0x55)
              ┌─────▼─────┐
              │ Teensy 4.1 │
              │ Firmware    │
              └──┬──────┬──┘
     14 MOSFET   │      │ Ethernet 10Hz
     (D4184)     │      │ UDP 9999 JSON
  ┌──────────────▼┐  ┌──▼──────────────┐
  │ 14 Соленоидных│  │ SensorHub (PC)  │
  │ клапанов      │  │ ← Давление возд.│
  └───────────────┘  │ ← 10× расходомер│
                     └─────────────────┘
```

### Потоки данных

| Путь | Протокол | Частота | Данные |
|------|----------|---------|--------|
| AOG → PlotManager | UDP 8888 | 10 Hz | GPS (lat/lon/heading/speed/fix/COG) |
| AOG → PlotManager | UDP 8888 | 10 Hz | Section Control (14-бит маска) |
| PlotManager → Teensy | USB Serial | По событию | SET_VALVES, HEARTBEAT, E-STOP |
| Teensy → PlotManager | Ethernet UDP 9999 | 10 Hz | JSON {AirV, FlowHz[10], Estop} |
| Teensy → PlotManager | USB Serial | 10 Hz | STATUS (маска, ошибки) |

---

## 🗂️ Структура проекта

```
RoboCraft/
├── src/
│   ├── Firmware/                    # Teensy 4.1 (PlatformIO + Arduino)
│   │   ├── platformio.ini
│   │   └── src/
│   │       ├── Config.h             # Пины, константы, IP-адреса
│   │       ├── main.cpp             # Главный цикл (139 LOC)
│   │       ├── CommandParser.h      # Стейт-машина парсер пакетов
│   │       ├── ValveController.h    # Управление 14 MOSFET-клапанами
│   │       ├── SensorReader.h       # ISR-счётчики расходомеров + АЦП
│   │       ├── TelemetrySender.h    # UDP JSON телеметрия
│   │       └── Diagnostics.h        # Heartbeat, LED, статус
│   │
│   └── PlotManager/
│       ├── PlotManager.Core/        # .NET 8 Class Library
│       │   ├── Models/              # Доменные модели
│       │   ├── Services/            # Бизнес-логика
│       │   └── Protocol/            # Протоколы (AOG + PlotProtocol)
│       ├── PlotManager.UI/          # WinForms приложение
│       │   ├── Forms/               # Окна
│       │   └── Controls/            # Пользовательские контролы
│       └── PlotManager.Tests/       # xUnit тесты (293 тестов)
```

---

## ⚙️ Модули

### Firmware (Teensy 4.1)

| Файл | Назначение |
|------|-----------|
| [Config.h](src/Firmware/src/Config.h) | Пин-маппинг (14 клапанов → D2-D12,D24-D26), 10 расходомеров (D14-D23), давление воздуха (A14/D38), сетевые настройки (192.168.5.177), протокол (SYNC=0xAA55) |
| [main.cpp](src/Firmware/src/main.cpp) | Главный цикл: `setup()` → инициализация Ethernet/Serial/ISR;  `loop()` → парсинг команд, heartbeat watchdog, отправка телеметрии |
| [CommandParser.h](src/Firmware/src/CommandParser.h) | Стейт-машина (5 состояний: WaitSync1→WaitSync2→WaitCmd→WaitData→WaitCrc). Парсит пакеты `[0xAA][0x55][CMD][DATA...][CRC]`, CRC = XOR всех байт CMD+DATA |
| [ValveController.h](src/Firmware/src/ValveController.h) | Атомарное управление 14-бит маской клапанов через `digitalWrite`. Методы: `setValves(mask)`, `emergencyStop()`, `openValve(i)`, `closeValve(i)` |
| [SensorReader.h](src/Firmware/src/SensorReader.h) | 10 аппаратных ISR для расходомеров (RISING edge → volatile counter). `update()` каждые 100мс атомарно читает и обнуляет счётчики, вычисляет Hz. АЦП давления: `analogRead(A14)` → напряжение 0-3.3V |
| [TelemetrySender.h](src/Firmware/src/TelemetrySender.h) | JSON через NativeEthernet UDP: `{"T":millis,"AirV":1.75,"FlowHz":[25.0,...],"Estop":false}`. Авто-переподключение при потере link |
| [Diagnostics.h](src/Firmware/src/Diagnostics.h) | Heartbeat watchdog (2 сек timeout → E-STOP). LED: быстрое мигание=ошибка, горит=клапаны открыты, медленно=idle. Отправка STATUS каждые 100мс |

#### Команды PlotProtocol (PC → Teensy)

| Команда | Код | Payload | Описание |
|---------|-----|---------|----------|
| SET_VALVES | `0x01` | `[MSB][LSB]` (14 бит) | Установить маску клапанов |
| PRIME | `0x02` | `[Mask_MSB][Mask_LSB][Dur_MSB][Dur_LSB]` | Прокачка секций |
| HEARTBEAT | `0x03` | — | Keepalive (≤2 сек) |
| EMERGENCY_STOP | `0x04` | — | Закрыть все клапаны |

#### Ответы Teensy → PC

| Ответ | Код | Payload | Описание |
|-------|-----|---------|----------|
| STATUS | `0x80` | `[Mask_MSB][Mask_LSB][ErrFlags]` | Текущее состояние |
| ACK | `0x81` | `[AckedCmd]` | Подтверждение команды |

---

### PlotManager.Core — Модели

| Класс | Файл | Назначение |
|-------|------|-----------|
| `GeoPoint` | Models/GeoPoint.cs | Координаты WGS84 (lat/lon) + метод `DistanceTo()` |
| `Plot` | Models/PlotGrid.cs | Делянка: SouthWest/NorthEast углы, Row/Column, `Contains(point)` |
| `PlotGrid` | Models/PlotGrid.cs | 2D массив делянок с буферами, HeadingDegrees (для поворота) |
| `SpatialResult` | Models/SpatialResult.cs | Результат пространственного анализа: State/Plot/Product/ValveMask/Distance |
| `BoomState` | Models/SpatialResult.cs | Enum: OutsideGrid → InAlley → ApproachingPlot → InPlot → LeavingPlot |
| `MachineProfile` | Models/MachineProfile.cs | Полный профиль машины: геометрия, гидравлика, COG, RTK, калибровка сенсоров. JSON-сериализуемый |
| `BoomProfile` | Models/MachineProfile.cs | Конфигурация бума: Y-offset, X-offset, канал, задержки, overlap% |
| `HardwareSetup` / `Boom` | Models/HardwareSetup.cs | Runtime-структура для пространственного движка |
| `TrialMap` | Models/TrialMap.cs | Карта продуктов: `GetProduct(row, col)` → имя препарата |
| `HardwareRouting` | Models/HardwareRouting.cs | Маршрутизация: `GetSections(product)` → каналы клапанов |
| `SensorSnapshot` | Models/SensorSnapshot.cs | Калиброванные данные: AirPressureBar, FlowRatesLpm[10], IsStale |
| `PassState` | Models/PassState.cs | Состояние прохода: номер, колонка, направление (↑/↓), скорость |
| `WeatherSnapshot` | Models/WeatherSnapshot.cs | Метео перед опытом: t°C, влажность%, ветер м/с |
| `NozzleCatalog` | Models/NozzleCatalog.cs | Каталог форсунок: модель, расход по закону √P |
| `TrialDefinition` | Models/TrialDefinition.cs | Определение опыта: продукты, нормы, форсунки |

---

### PlotManager.Core — Сервисы

#### SpatialEngine — Пространственный движок

**Файл:** [SpatialEngine.cs](src/PlotManager/PlotManager.Core/Services/SpatialEngine.cs) (678 LOC)

Ядро системы. Принимает GPS-координату и определяет, какие клапаны открыть.

**Алгоритм `EvaluatePosition(boomCenter, heading, speed)`:**

```
1. FindPlot(boomCenter)
   ├── O(1) для heading=0: index = (coord - origin) / (plotSize + buffer)
   └── O(N²) fallback для повёрнутых сеток
2. Если ВНУТРИ делянки:
   ├── distToExit = DistanceToExitBoundary(pos, plot, heading)
   ├── Если distToExit ≤ deactivationDist → State=LeavingPlot, Mask=0 (сухое отсечение)
   └── Иначе → State=InPlot, Mask=ComputeValveMask(plot)
3. Если СНАРУЖИ:
   ├── forwardPoint = ProjectPoint(pos, heading, activationDist)
   ├── FindPlot(forwardPoint)? → State=ApproachingPlot, Mask=on (набор давления)
   ├── IsInGridArea(pos)? → State=InAlley, Mask=0
   └── → State=OutsideGrid, Mask=0
```

**Динамические расстояния look-ahead:**
- `activationDist = PreActivationMeters + (speed × SystemActivationDelayMs / 3600)`
- `deactivationDist = PreDeactivationMeters + (speed × SystemDeactivationDelayMs / 3600)`
- Учитывает ускорение через EMA-фильтр (`AccelerationSmoothingAlpha = 0.3`)

**Per-Boom путь (`EvaluatePerBoom`):**
- Оценивает каждый бум индивидуально по его GPS-позиции (с Y-offset)
- COG crab-walk коррекция: при |heading − COG| > порог (3°) задние бумы ориентируются по COG
- Overlap-гистерезис: разные пороги активации/деактивации (70%/30%)    

#### SectionController — Интерлоки безопасности

**Файл:** [SectionController.cs](src/PlotManager/PlotManager.Core/Services/SectionController.cs) (328 LOC)

Четыре уровня блокировки, каждый может обнулить маску:

| Интерлок | Триггер | Поведение |
|----------|---------|-----------|
| **Скорость** | speed > targetSpeed × (1+tolerance) | Маска = 0 |
| **E-STOP** | `EmergencyStopActive = true` | Маска = 0 |
| **RTK Loss** | Fix < RtkFix → таймаут 2 сек → | Маска = 0 |
| **Давление** | AirPressure < MinSafe → 2 сек → | Маска = 0 |

RTK и Air Pressure используют 3-phase state machine:
- **Normal** → **Degraded** (мгновенно при потере) → **Lost** (после таймаута)
- Recovery: Lost/Degraded → Normal (мгновенно при восстановлении)

#### PlotModeController — Оркестратор

**Файл:** [PlotModeController.cs](src/PlotManager/PlotManager.Core/Services/PlotModeController.cs) (280 LOC)

Центральный координатор — связывает GPS, SpatialEngine, SectionController и Teensy:

```
AOG GPS (10Hz) ──► HandleGpsUpdate()
                   ├── CheckRtkQuality(fix)          ← интерлок RTK
                   ├── UpdateAcceleration(speed)     ← фильтр ускорения
                   ├── EvaluatePosition(pos, heading, speed)
                   ├── ApplyInterlocks(mask, speed)   ← все 4 блокировки
                   └── SendValveMask(finalMask)       ← Teensy USB

SensorHub (10Hz) ─► HandleTelemetryForInterlocks()
                   └── CheckAirPressure(bar)         ← интерлок давления
```

**Thread Safety:** `_stateLock` на всех shared state (`_lastResult`, `_lastGps`, `_lastSentMask`, `_lastAogMask`).

#### Вспомогательные сервисы

| Сервис | Файл | Назначение |
|--------|------|-----------|
| `GridGenerator` | Services/GridGenerator.cs | Генерация PlotGrid из параметров (rows×cols, размеры, буферы, heading) |
| `TrialMapParser` | Services/TrialMapParser.cs | Парсинг CSV-файлов со схемой опыта → TrialMap |
| `SensorHub` | Services/SensorHub.cs | UDP-слушатель JSON от Teensy, калибровка: `Bar = (V - offset) × mult`, `Lpm = Hz×60/pulsesPerL` |
| `AsAppliedLogger` | Services/AsAppliedLogger.cs | CSV-логгер As-Applied: 19 колонок (8 основных + Air + 10×Flow) |
| `TrialLogger` | Services/TrialLogger.cs | 1Hz автолог с метео-шапкой и событиями входа/выхода из делянки |
| `PassTracker` | Services/PassTracker.cs | Детекция проходов: вход в колонку → lock скорости → трекинг отклонений → конец при выходе из сетки |
| `CleanController` | Services/CleanController.cs | Промывка: пульсы ON(2с)/OFF(1с) × 3 цикла, блокировка при speed>0.5 |
| `PrimeController` | Services/PrimeController.cs | Прокачка: все 14 клапанов OPEN, блокировка при speed>0.5 и PlotMode |
| `AutoWeatherService` | Services/AutoWeatherService.cs | Авто-запрос метео при остановке на X сек (для NMEA WIMWV) |
| `PlotLogger` | Services/PlotLogger.cs | Централизованный JSON-логгер: Info/Warn/Error, EntryCount, сессии |
| `IPlotLogger` | Services/IPlotLogger.cs | Интерфейс логгера для DI и тестирования |

---

### PlotManager.Core — Протоколы

| Класс | Файл | Назначение |
|-------|------|-----------|
| `PlotProtocol` | Protocol/PlotProtocol.cs | Формирование/парсинг пакетов PlotManager↔Teensy: `BuildSetValves`, `BuildHeartbeat`, `ParseResponse` |
| `AogProtocol` | Protocol/AogProtocol.cs | Работа с PGN 229/253 AgOpenGPS: `IsValidPacket`, `ExtractSectionMask`, `OverrideSectionPacket` |
| `AogUdpClient` | Protocol/AogUdpClient.cs | UDP-слушатель AOG: парсинг GPS (PGN 253) и Section Control (PGN 229), отправка override-пакетов |
| `ITransport` | Protocol/ITransport.cs | Абстракция транспорта: `SendAsync(byte[])`, `ReceiveAsync()` |

---

### PlotManager.UI — Формы

| Форма | Файл | Назначение |
|-------|------|-----------|
| `MainForm` | Forms/MainForm.cs | Главное окно: загрузка профиля, создание сетки, TrialMap, кнопки Prime/Clean/PassMonitor |
| `FormPassMonitor` | Forms/FormPassMonitor.cs | Real-time дашборд: карта делянок, статус бумов, телеметрия, интерлоки |
| `FormMachineProfile` | Forms/FormMachineProfile.cs | Редактор профиля: гидравлика, бумы (DataGridView), калибровка сенсоров |
| `FormTrialWizard` | Forms/FormTrialWizard.cs | Мастер создания опыта: продукты, норма, автоподбор форсунки |
| `FormWeatherSnapshot` | Forms/FormWeatherSnapshot.cs | Ввод метео перед началом опыта |
| `FormOperationSettings` | Forms/FormOperationSettings.cs | Настройки операций: скорость, Prime/Clean, автометео, Trial (4 вкладки) |

### PlotManager.UI — Контролы

| Контрол | Файл | Назначение |
|---------|------|-----------|
| `PlotGridPreview` | Controls/PlotGridPreview.cs | GDI+ превью сетки делянок с цветовой кодировкой по продуктам |
| `PlotMapControl` | Controls/PlotMapControl.cs | Real-time карта: позиция трактора, след, раскрашенные делянки |
| `BoomStatusPanel` | Controls/BoomStatusPanel.cs | 14 индикаторов состояний бумов (зелёный/красный/жёлтый) |
| `TelemetryPanel` | Controls/TelemetryPanel.cs | Давление (gauge), расход (bar chart), скорость |
| `InterlockStatusBar` | Controls/InterlockStatusBar.cs | 6 индикаторов: Speed, E-STOP, RTK, Air, Teensy Health, AOG Health |
| `FieldContextPanel` | Controls/FieldContextPanel.cs | Контекстная панель: GPS+компас, позиция, продукт+следующий, проход, trial/log |

---

## 🔧 Сборка и запуск

### Firmware

```bash
# Требования: PlatformIO CLI
cd src/Firmware
pio run                    # Компиляция
pio run --target upload    # Прошивка Teensy 4.1
```

**Аппаратная необходимость:**
- Teensy 4.1 с NativeEthernet
- 14× MOSFET D4184 на пинах D2-D12, D24-D26
- 10× расходомеров (hall-effect) на D14-D23
- Датчик давления 0.5-4.5V на A14 (D38)
- Ethernet-кабель к ПК (192.168.5.x subnet)

### PlotManager (ПК, Windows)

```bash
cd src/PlotManager

# Только Core (кросс-платформенный)
dotnet build PlotManager.Core

# Тесты
dotnet test PlotManager.Tests   # 254 тестов

# UI (только Windows — требует Windows Desktop SDK)
dotnet build PlotManager.UI
dotnet run --project PlotManager.UI
```

### Сетевые настройки

| Устройство | IP | Порт | Направление |
|------------|-----|------|-------------|
| AgOpenGPS ПК | 127.0.0.1 | UDP 8888 → | PGN 229/253 broadcast |
| PlotManager | 0.0.0.0 | ← UDP 8888 | Слушает AOG |
| Teensy 4.1 | 192.168.5.177 | → UDP 9999 | JSON телеметрия |
| PlotManager | 0.0.0.0 | ← UDP 9999 | SensorHub слушает |
| PlotManager | COM3 | Serial 115200 | PlotProtocol → Teensy |

> ⚠️ **Firewall:** Входящий UDP на портах 8888 и 9999 должен быть разрешён в Windows Firewall.

---

## 🧪 Тестирование

```
254 тестов (xUnit), покрывают:
├── SpatialEngine: EvaluatePosition, FindPlot, ComputeValveMask, COG, look-ahead
├── SectionController: все 4 интерлока, таймауты, восстановление
├── SensorHub: калибровка, JSON парсинг, staleness
├── PassTracker: определение проходов, направление, speed deviation
├── GridGenerator: генерация, nudge, повороты
├── TrialSystem: Trial→MachineProfile, Rate∝Speed, NozzleCatalog
├── MachineProfile: JSON round-trip, BoomDelay, HardwareSetup converter
├── FieldHardening: RTK watchdog, EMA filter, COG threshold
├── Sprint4: CleanController, PrimeController, AutoWeather, WIMWV parser
├── EdgeCases: граничные значения, конкурентность, overflow-защита
└── Observability: PlotLogger, ServiceHealth, IPlotLogger контракты
```

---

## 📁 Формат данных

### MachineProfile (JSON)

```json
{
  "profileName": "Gevax 10-boom / Water",
  "preActivationMeters": 0.5,
  "preDeactivationMeters": 0.2,
  "systemActivationDelayMs": 300,
  "systemDeactivationDelayMs": 150,
  "cogHeadingThresholdDegrees": 3.0,
  "rtkLossTimeoutSeconds": 2.0,
  "minSafeAirPressureBar": 2.0,
  "booms": [
    {"boomId": 0, "yOffsetMeters": -0.30, "valveChannel": 0, "enabled": true}
  ]
}
```

### As-Applied Log (CSV, 19 колонок)

```
Timestamp,Latitude,Longitude,PlotId,Product,SpeedKmh,ValveMask,Notes,Air_Bar,Flow_1_Lpm,...,Flow_10_Lpm
2026-03-11T08:30:00.123,50.12345678,30.12345678,R2C3,Herbicide_A,5.20,0x000F,,3.50,1.200,...,0.000
```

### Teensy Telemetry (JSON, UDP 9999)

```json
{"T":345098,"AirV":1.75,"FlowHz":[25.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0],"Estop":false}
```

---

## 📜 Лицензия

Приватный проект. Все права защищены.
