# Project State

## 1. Current State
**Maturity:** In-Progress
**Last Milestone:** Refactored backend (MixCalculator, HardwareRouter, PneumaticRateController, ITrialDesign). Conducted Research Audit on external integrations.
**Active Blockers:** None.
**Current Focus:** UI/UX Journey implementation for the Sprayer MVP. Implementing interactive "drag-and-drop / snap-to-GPS" plot generation.

## 2. Tech Stack & Architecture
- **Language:** C# (.NET 8.0)
- **Frontend UI:** **Avalonia UI** (Cross-platform desktop) paired with `CommunityToolkit.Mvvm`
- **Mapping:** **Mapsui** (`Mapsui.Avalonia`) for high-performance GIS rendering. (WinForms is dead legacy).
- **Domain Core:** `PlotManager.Core` (strictly decoupled from UI)
- **Hardware Controller:** Teensy 4.1 via `PlotProtocol` (Serial/UDP) + `PneumaticRateController`
- **Data Persistence:** **GeoPackage (.gpkg)** (via SQLite / NetTopologySuite) as the unified format for profiles, trial geometry, and As-Applied high-frequency logs.

---

## 3. Vision
Create a universal, all-in-one Field Trial Application (AOG Plot Manager) that manages agronomic field experiments for planting, spraying, and harvesting. The application functions as an intelligent "Passive Navigator" and dynamic task controller. 

**Key Differentiator:** The system supports **Interactive A-B Line & Visual Trial Generation**. 
1. **A-B Line:** Automate grid generation by driving to Point A, then Point B to establish heading and snap the mathematical grid using `NetTopologySuite.Geometries.AffineTransformation`.
2. **Drag & Drop:** Fine-tune or completely manually position/rotate the generated grid on the map interface for edge cases where math isn't enough.
*Grid configuration includes plot dimensions (Length x Width), Alley buffers (dead zones for flushing/stopping), and customizable machine anchor points (e.g., attach grid to Left Boom, Right Boom, or Center).*

Initially targeting pneumatic spraying operations with customizable hardware integrations (AgOpenGPS, Ardupilot/Pixhawk). Future evolutions will include automated steering (Active Pilot).

---

## 4. Current Milestone Map (Sprayer MVP Focus)

### Milestone 1: Core Domain (Completed)
- [x] Plot calculation logic (`PlotGrid`, `ExperimentDesigner`)
- [x] Hardware profiles (`MachineProfile`, boom offsets)
- [x] AgOpenGPS PGN parsing & UTM projection
- [x] Base serial protocol format (`PlotProtocol`)

### Milestone 2: Preparation & Field Layout (The "Setup")
- [ ] SG-2.1: Implement Local Storage strategy - GeoPackage `.gpkg` replacing JSON for session/spatial persistence. 
- [ ] **SG-2.2: Trial Grid Configuration UI (Set Plot L/W, Alley dimensions, Rows/Cols, Anchor Point [L/C/R]).**
- [ ] **SG-2.3: A-B Line Generation Logic (Calculate Heading from A->B, auto-generate NTS polygons).**
- [ ] **SG-2.4: AffineTransformation Service (Snap, translate, and rotate entire `ITrialDesign` matrix to GPS origin using NetTopologySuite).**
- [ ] **SG-2.5: Map Editor Tools (Visual boundary generation, Plot Exclusion/Skip manual toggles on map).**

### Milestone 3: Execution Engine & Guidance (The "Drive")
- [ ] SG-3.1: Implement `PneumaticRateController` UI binding (Speedometer with Red/Orange/Green zones).
- [ ] **SG-3.2: Spatial Engine live execution (Read GPS via `System.Threading.Channels`, calculate LookAhead (Предварение) for pneumatic delay, render locally).**
- [ ] SG-3.3: Link Task Manager (HardwareRouter) to Teensy serial. **Include Pre-checks (Calibration, Flow meters) and Flush Mode (Чистая вода).**

### Milestone 4: Post-Op & Logging (The "Report")
- [ ] SG-4.1: High-Frequency As-Applied Datalogging (SQLite/GeoPackage 10Hz).
- [ ] SG-4.2: Export to Ardupilot/AOG Navigation format.

---

## 5. Risk Register / Open Questions

| Risk / Question | Impact | Probability | Mitigation Strategy | Status |
|------|--------|-------------|------------|--------|
| Mapsui Performance with many plots | High | Med | Load geometries directly from GeoPackage SQLite index. | Open |
| Math errors in shifting 1000s of plots | Med | Low | Use standard `NetTopologySuite.Geometries.Utilities.AffineTransformation` instead of custom math. | Addressed |
| UI thread blocking & Race Conditions (10Hz GPS) | High | Med | Use `System.Threading.Channels` for GPS stream. `SpatialEngine` runs as `BackgroundService`. Marshall updates via `Dispatcher.UIThread.InvokeAsync`. | Addressed |

---

## 6. Change Log

- **Milestone Re-alignment:** Moved external navigation APIs (Ardupilot/AOG) to Milestone 4. Focused UI creation (Mapsui Drag & Drop, GeoPackage local data) to Milestone 2 to serve the primary workflow.
- **Architectural Shift:** Removed spherical Haversine math. Bounded all Cartesian math exclusively inside UTM/ProjNet. All internal representations run in Meters via `NetTopologySuite`.
- **Framework Rectification:** Mandated Avalonia UI + MVVM Toolkit. Removed all references to legacy WinForms.
