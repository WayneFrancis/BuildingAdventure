# BuildingAdventure

**BuildingAdventure** is a Windows editor for realistic tabletop RPG floor
plans. The goal is a hybrid architectural designer: generate a building from
requirements, draw one by hand, or combine manual rooms with procedural design.

## v0.2 — Manual room layout and doors (development branch)

The editor currently supports:

- Closed building footprints of arbitrary shape, including angled walls.
- **Split Perimeter** tool: click any existing exterior wall to add a corner,
  then switch to Select / Edit and drag it into an angled wall or extension.
  Splitting a straight wall preserves doors and propagates matching vertices
  into any rooms sharing that exterior edge.
- **Remove Corner** tool: click an existing exterior or interior room corner
  to join its two adjacent walls into one segment, including angled walls.
  Doors on those walls follow the replacement segment where possible. Shared
  room boundary vertices are removed together when geometrically appropriate.
  A corner can also be selected and removed with Delete.
- **Split Room** tool: select an existing room, then click two different points
  on its boundary to divide it. The new rooms inherit its type and are named
  with A/B suffixes so they can be renamed individually.
- **Move Wall** tool: click and drag a wall perpendicular to its direction.
  Coincident shared vertices and affected doors follow the edit. The entire
  result is validated; invalid movements are rejected.
- Room-splitting endpoints snap precisely to an existing boundary even when
  the wall is diagonal or does not align with the measurement grid.
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

**Not yet implemented:** automatic room generation; merging entire adjacent
rooms; free-standing unenclosed partition walls; multiple floors; window
placement; door width editing; hidden passages; furnishing; and VTT export.
Move Wall currently moves a chosen segment perpendicular to itself, not an
unrestricted wall rotation or a whole chain of connected non-collinear walls. Doors are simple openings and are not
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
| Add perimeter segments | Split Perimeter, click a wall, then Select / Edit and drag new corner |
| Join two wall segments | Remove Corner, click the shared corner |
| Split a room into two | Select a room, Split Room, click two points on its boundary |
| Move a wall | Move Wall, drag a wall perpendicular to itself; Shift disables distance snap |
| Split any existing wall | Double-click in Select / Edit; exterior walls take priority |
| Draw room | Draw Room, click room corners inside the building, then Finish |
| Rename/type room | Select/Edit, click its interior, edit details, click Apply room details |
| Move corner | Select/Edit, drag an existing corner |
| Insert corner | Double-click a wall with Select/Edit |
| Delete corner | Select a corner and press Delete; the adjacent walls join |
| Add door | Add Door, then click on a sufficiently long wall |
| Delete room or door | Select it and use the appropriate side-panel Delete button |
| Pan | Middle-mouse drag, or Pan and left-drag |
| Zoom | Mouse wheel, or toolbar +/- |
| Fit | Ctrl+0 |
| Temporarily bypass snapping | Hold Shift while placing/moving corners |
| Cancel drawing | Esc |
| Undo/redo | Ctrl+Z / Ctrl+Y |
| New/Open/Save | Ctrl+N / Ctrl+O / Ctrl+S |

**Important:** Draw Perimeter replaces the whole exterior outline, while
Split Perimeter changes an existing outline in place. You can also double-click
a wall in Select / Edit to insert a corner. A split alone does not change
building area; drag the new corner to create the new wall segments. Select
Remove Corner and click the shared vertex to merge the adjacent wall
segments back into a single wall (or select a vertex and press Delete).

If a door crosses a split corner on a still-straight wall, it remains valid.
A doorway cannot straddle a genuine bend, and edits are rejected if they
leave too little physical clearance. When removing a bent corner, doors
on the two affected walls move proportionally along the new straight
segment; unrelated doors stay where they are. Invalid edits are rejected.

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
