# 🌾 AOG Plot Manager

**Open-source precision small-plot spraying system for agricultural R&D trials.**

Replaces proprietary systems (Haldrup, Wintersteiger) with open hardware + software — at a fraction of the cost.

## What It Does

Manages a 14-section boom sprayer system that automatically applies different chemical products to a grid of research plots with centimeter-level RTK GPS accuracy. Zero overlap, precise boundary cut-off, scientific-grade logging.

## Architecture

```
AgOpenGPS ──[UDP PGN 0x80 0x81]──► PlotManager (C# App) ──[USB Serial 0xAA 0x55]──► Teensy 4.1
     │                                    │                                            │
  GPS/RTK                            ┌────┴────┐                                 14× D4184 MOSFET
  Section ctrl                       │ Services│                                       │
                                     │─────────│                                14× Gevax 1961
Teensy 4.1 ──[UDP 9999 JSON]──►     │SensorHub│                                Solenoid Valves
  {"AirV":1.7,"FlowHz":[25...]}     │         │
                                     └─────────┘
```

### Communication Protocols

| Link | Protocol | Port | Format |
|------|----------|------|--------|
| AOG → PlotManager | UDP PGN | 8888 | `0x80 0x81` preamble, PGN 229/253 |
| PlotManager → AOG | UDP PGN | 9999 | Section feedback |
| PlotManager → Teensy | USB Serial | COM | `0xAA 0x55` custom binary |
| Teensy → PlotManager | UDP JSON | 9999 | `{"AirV": float, "FlowHz": float[10]}` |

## Project Structure

```
RoboCraft/
├── src/
│   ├── PlotManager/                  # C# .NET 8 application
│   │   ├── PlotManager.Core/        #   Business logic (cross-platform)
│   │   │   ├── Models/              #     14 model classes
│   │   │   ├── Services/            #     12 service classes
│   │   │   └── Protocol/            #     AOG PGN + PlotProtocol + SensorHub
│   │   ├── PlotManager.UI/          #   WinForms GUI (Windows-only)
│   │   └── PlotManager.Tests/       #   192 xUnit tests
│   └── Firmware/                    # Teensy 4.1 PlatformIO project
│       └── src/                     #   main.cpp, ValveController, etc.
├── docs/                            # Architecture & wiring docs
└── README.md
```

## Features

### Phase 1–3: Core Engine ✅

- **Grid Generator** — GPS-anchored plot grid with configurable rows, columns, buffers
- **Spatial Engine** — O(1) plot lookup, look-ahead activation, speed-adaptive valve delays
- **Section Controller** — RTK quality interlock (2s timeout), speed hysteresis, E-STOP
- **Trial System** — Product/nozzle catalog, rate calculator (square-root law), pass tracker
- **As-Applied Logger** — 1Hz CSV with GPS, valve mask, plot ID, product, weather header
- **AOG Protocol** — UDP PGN 229 (sections), PGN 253 (GPS), CRC, section override
- **PlotProtocol** — Binary serial for Teensy valve control, heartbeat, emergency stop

### Phase 4: Sensor Hub & Telemetry ✅

- **SensorHub** — UDP JSON listener, converts raw voltage/Hz to Bar/Lpm
- **Air Pressure E-STOP** — `< 2 Bar × 2s sustained → all valves OFF`
- **Flow Monitoring** — 10 flow meter channels calibrated from pulse frequency
- **Calibration in MachineProfile** — `Bar = (V - 0.5) × 2.5`, `Lpm = (Hz × 60) / 400`
- **Extended CSV Logging** — 11 extra sensor columns: `Air_Bar, Flow_1..10_Lpm`

## Tech Stack

| Component | Technology |
|-----------|------------|
| Desktop App | C# / .NET 8 / WinForms |
| Business Logic | .NET 8 classlib (cross-platform) |
| Tests | xUnit (192 tests) |
| Firmware | C++ / Arduino / PlatformIO |
| MCU | Teensy 4.1 |
| Power Switching | D4184 MOSFET modules |
| Valves | Gevax 1961 / 2V025-08 (12V DC, NC) |
| GPS | AgOpenGPS (PGN/UDP protocol) |
| Air Pressure | 0.5V–4.5V = 0–10 Bar sensor |
| Flow Meters | 10× pulse-output flow sensors |

## Quick Start

### Prerequisites

- .NET 8 SDK
- PlatformIO CLI (for firmware)

### Build & Test

```bash
cd src/PlotManager
dotnet build
dotnet test        # 192 tests, ~0.5s
```

### Build Firmware

```bash
cd src/Firmware
pio run
pio run --target upload    # Flash to Teensy
```

## Safety Features

| Interlock | Trigger | Action |
|-----------|---------|--------|
| RTK Loss | GPS quality < RtkFix for 2s | All valves OFF |
| Speed | Outside ±10% of target | All valves OFF (with hysteresis) |
| Air Pressure | < 2 Bar for 2s | All valves OFF |
| E-STOP | Manual button | All valves OFF (requires reset) |
| Telemetry Loss | No UDP for 2s | Sensor data → NaN in logs |

## License

GPL-3.0 — compatible with the AgOpenGPS ecosystem.
