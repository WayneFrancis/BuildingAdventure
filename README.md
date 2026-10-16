# BuildingAdventure

**BuildingAdventure** is a Windows editor for realistic tabletop RPG floor
plans. The goal is a hybrid architectural designer: generate a building from
requirements, draw one by hand, or combine manual rooms with procedural design.

## v0.2 — Manual room layout and doors (development branch)

The editor currently supports:

- Closed building footprints of arbitrary shape, including angled walls.
- Drawing interior rooms as polygons, with grid, corner and edge snapping.
- Selecting and naming rooms, and assigning room types (shop, workshop, kitchen,
  living room, bedroom, hall, storeroom, and others).
- Dragging room and building vertices; coincident shared junctions move together.
- Doors follow their supporting walls as endpoints move or rotate, including
  diagonal walls. They retain their relative position along the wall.
  An edit is still rejected if it leaves too little clearance for an opening.
- Derived shared physical wall segments, avoiding doubled wall drawing.
- Manually adding and removing 0.9 metre openings on existing walls.
- Rejecting invalid rooms: outside the perimeter, self-crossing, or overlapping.
- Undo/redo for completed edits, panning, mouse-wheel zoom, fit-to-window.
- Measuring area and exterior perimeter; saving and reopening all geometry as JSON.
- Loading older v0.1 perimeter-only JSON documents.

**Not yet implemented:** automatic room generation; free-standing, unenclosed
partition walls; multiple floors; window placement; door width editing; hidden
passages; furnishing; and VTT export. Doors are simple openings and are not
yet a complete circulation/accessibility model.

## Requirements

- Windows 10/11
- .NET 10 SDK (or Visual Studio with .NET 10 / WPF desktop development support)

## Get the development branch

First clone:

    git clone -b development https://github.com/WayneFrancis/BuildingAdventure.git
    cd BuildingAdventure

Already checked out on development:

    git pull --ff-only

Run:

    dotnet run --project src/BuildingAdventure.Desktop

Or open BuildingAdventure.sln in Visual Studio and set
BuildingAdventure.Desktop as the startup project.

Run the geometry checks without third-party test packages:

    dotnet run --project tests/BuildingAdventure.Tests

## Editor controls

| Operation | Interaction |
| --- | --- |
| Draw building | Draw Perimeter, click exterior corners, then Finish |
| Draw room | Draw Room, click room corners inside the building, then Finish |
| Rename/type room | Select/Edit, click its interior, edit details, click Apply room details |
| Move corner | Select/Edit, drag an existing corner |
| Insert corner | Double-click a wall with Select/Edit |
| Delete corner | Select a corner and press Delete |
| Add door | Add Door, then click on a sufficiently long wall |
| Delete room or door | Select it and use the appropriate side-panel Delete button |
| Pan | Middle-mouse drag, or Pan and left-drag |
| Zoom | Mouse wheel, or toolbar +/- |
| Fit | Ctrl+0 |
| Temporarily bypass snapping | Hold Shift while placing/moving corners |
| Cancel drawing | Esc |
| Undo/redo | Ctrl+Z / Ctrl+Y |
| New/Open/Save | Ctrl+N / Ctrl+O / Ctrl+S |

Rooms are separate enclosed polygons inside the exterior footprint. Shared room
edges render as one interior wall and can be snapped together. Room boundaries
may touch one another, but their interiors cannot overlap. The current model
does not automatically resize neighbouring entire wall edges when one room
changes; an edit that would create overlap is rejected.

When drawing a replacement exterior perimeter, existing rooms and doors must
still fit or the change is rejected. When deleting a room, doors attached only
to its removed walls are deleted too. Existing files remain compatible.

## Project layout

- src/BuildingAdventure.Core — polygon validation, room/wall/door geometry, JSON
- src/BuildingAdventure.Desktop — WPF floor-plan editor and file commands
- tests/BuildingAdventure.Tests — executable geometry checks
- docs/ARCHITECTURE.md — design rationale and future milestones

See the [architecture notes](docs/ARCHITECTURE.md).
