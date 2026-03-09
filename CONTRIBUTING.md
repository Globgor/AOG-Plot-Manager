# Contributing to AOG Plot Manager

## Development Setup

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Install [PlatformIO CLI](https://platformio.org/install/cli)
3. Clone the repository

## Code Style

- **C#**: 4-space indentation, `dotnet_sort_system_directives_first`, see `.editorconfig`
- **C++**: 2-space indentation, header-only where practical
- All files: UTF-8, LF line endings

## Branching Strategy

- `main` — stable, tested code
- `develop` — integration branch
- `feature/*` — individual features
- `fix/*` — bug fixes

## Pull Request Guidelines

1. One logical change per PR
2. All C# tests must pass: `dotnet test src/PlotManager/PlotManager.sln`
3. Firmware must compile: `pio run -d src/Firmware`
4. Include test coverage for new logic in `PlotManager.Core`
5. Update documentation if public API changes

## Architecture Rules

- **PlotManager.Core** must remain cross-platform (no WinForms references)
- All business logic goes in `Core`, never in `UI`
- Protocol changes must be mirrored in both C# (`PlotProtocol.cs`) and firmware (`Config.h`, `CommandParser.h`)
- Safety interlocks must be tested with edge cases

## Testing

```bash
# Run C# tests
dotnet test src/PlotManager/PlotManager.sln --verbosity normal

# Run firmware tests (Unity framework, no hardware needed)
pio test -d src/Firmware --without-uploading
```
