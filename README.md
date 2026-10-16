# BuildingAdventure

**BuildingAdventure** is a Windows floor-plan editor for believable tabletop
RPG buildings. The eventual goal is a hybrid architectural designer:
automatically generate a building from requirements, draw one by hand, or
manually place rooms and let the application complete the remaining space.

## v0.1 — Perimeter editor (development branch)

This first milestone includes:

- Drawing closed, irregular perimeters with walls at arbitrary angles.
- Moving vertices and inserting/removing corners.
- A metre-based grid with optional 0.5 m snapping (hold Shift to override).
- Mouse-wheel zoom, middle-mouse panning, a Pan tool, and fit-to-window.
- Undo and redo for completed perimeter edits.
- Area and exterior-wall-length measurements.
- Self-intersection and invalid-outline rejection.
- New, Open, Save and Save As using a versioned JSON format.

**Not yet implemented:** internal rooms, multi-story layouts, generation,
doors/windows, secret passages, furnishings, or VTT exports.

## Requirements

- Windows 10/11
- .NET 10 SDK (or Visual Studio with .NET 10 / WPF desktop development support)

## Run

From a terminal in a checkout of the development branch:

    dotnet run --project src/BuildingAdventure.Desktop

Or open BuildingAdventure.sln in Visual Studio and set
BuildingAdventure.Desktop as the startup project.

To run the geometry checks without any external test packages:

    dotnet run --project tests/BuildingAdventure.Tests

## Editor controls

| Operation | Interaction |
| --- | --- |
| Draw a perimeter | Select Draw Perimeter; click consecutive corners |
| Finish drawing | Click the first corner, double-click, or click Finish |
| Cancel unfinished perimeter | Esc |
| Select a vertex | Select/Edit tool; click a vertex |
| Move a vertex | Drag a vertex |
| Insert a vertex | Double-click an existing wall |
| Remove a vertex | Select one and press Delete |
| Pan | Middle-mouse drag, or Pan tool + left drag |
| Zoom | Mouse wheel, or toolbar +/- |
| Fit building in view | Ctrl+0 |
| Disable snapping temporarily | Hold Shift while placing or moving a vertex |
| Undo/redo | Ctrl+Z / Ctrl+Y |
| New/Open/Save | Ctrl+N / Ctrl+O / Ctrl+S |

When drawing a new perimeter over an existing building, the previous footprint
is retained until you finish a valid replacement. Esc cancels the draft
without replacing it. JSON stores measurements in metres independently of zoom.

## Project structure

- src/BuildingAdventure.Core — models, polygon validation, JSON serialization
- src/BuildingAdventure.Desktop — WPF application and perimeter editor
- tests/BuildingAdventure.Tests — executable core geometry tests
- docs/ARCHITECTURE.md — design decisions and future stages

This is the initial implementation. See the architecture document for the
planned editor/generation split and future milestones.
