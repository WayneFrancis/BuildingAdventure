# BuildingAdventure

**BuildingAdventure** is a Windows editor for realistic tabletop RPG floor
plans. The goal is a hybrid architectural designer: generate a building from
requirements, draw one by hand, or combine manual rooms with procedural design.

## v0.8.1 — Shared wall propagation and drag-editable windows (development branch)

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
- **Multiple floors (1–4)**: use Add Floor to create an upper floor with
  the preceding story's outline but independent rooms and doors. Switch floors
  with the selector in the right panel; remove only the highest floor.
- **Unassigned interior**: space inside a floor perimeter that isn't part
  of a defined room or stairwell slab void remains usable floor space.
  Toggle Highlight unassigned interior to shade those areas amber.
  The panel shows their actual remaining surface area in square metres.
  You can use Draw Room to give part of that space a name or purpose
  without automatically generating unwanted walls.
- **Improved exterior wall propagation**: moving a connected straight
  exterior wall run also moves all room-wall points lying along it, even when
  multiple rooms subdivide that wall into smaller segments. Moving a
  perimeter corner interpolates the positions of attached room vertices,
  instead of moving only corners at exactly the same XY coordinate.
  Exterior windows and doors follow their supporting walls during these edits.
- **Mouse-editable windows**: existing windows have a high-contrast thick
  cyan line with a white casing. In Select / Edit, drag the window along an
  exterior wall to reposition it. Select a window to expose square endpoint
  handles; drag one endpoint to change width while anchoring the other.
  Width, clearances and positions are validated on release, with rejected
  edits leaving the original geometry untouched. Mouse-based edits support
  the global undo/redo history.
- **Exterior windows**: Add Window places a glazed opening on the exterior
  wall in the active floor, including sloped walls. Default width is 1.2 m
  but can be set in the WINDOWS panel. Click a window in Select / Edit
  to resize or remove it. Invalid widths and clashes with doors, other
  windows, or wall corners are rejected. Windows track their wall during
  corner and whole-wall edits and are saved independently per floor.
- **Global undo/redo**: Ctrl+Z and Ctrl+Y now restore full-building snapshots
  for completed perimeter, room and door edits, stair placement/movement/
  rotation/opening changes, adding or removing a floor, and building renames
  (one step on leaving the name field). Edits can be undone after switching
  to another floor; the correct editing floor is restored. Pure view
  changes, rejected edits and unfinished drawing strokes do not create
  document-history entries. Temporary drawing corners can still be undone
  individually before completing the outline. History stores up to 200
  committed document states and is reset on New or Open.
- **True 2D stairwell slab openings**: each stair flight now cuts a linked
  opening in the floor above (not the lower floor). The room and floor
  surface fills exclude these voids. Openings follow staircase translation,
  rotation and dimension changes. Extra opening width/length can be specified
  independently; these larger clearances are validated against the upper
  perimeter, room partitions and other expanded openings.
- **Adjacent-floor alignment overlay**: check Above, Below, or both in the
  FLOORS panel to see reference outlines and partitions as translucent dashed
  guides. Adjust their visibility with the slider. Reference geometry is
  never editable and stays in the same world coordinates across floors.
- **Usable slab area**: the current floor displays its gross footprint area
  as well as floor area less computed stairwell voids.
- **Linked staircases with live placement preview**: select the lower floor,
  enter dimensions and an arbitrary rotation angle, choose Place Stairs, and
  move the cursor around the plan. The footprint previews in green where valid
  and red where invalid. Click once to place, then the editor automatically
  returns to Select / Edit with the new staircase selected.
- **Reposition existing stairs**: in Select / Edit, click and drag a staircase,
  including when viewing the floor above. An offset is preserved from where
  you grabbed it, with optional 0.5 m snapping (hold Shift to bypass). The
  new location is validated on both floors on release.
- **Rotation grip on selected stairs**: the orange circular handle just beyond
  the staircase's long end behaves like a rotation handle in a graphics editor.
  Drag it around the centre to rotate freely; hold Shift to constrain rotation
  to 15-degree increments. The preview turns green for valid orientations or
  red for blocked ones. Invalid releases leave the original angle unchanged.
  The handle remains easy to grab at different zoom levels.
- **Edit stair properties**: select existing stairs to populate the side
  panel with length, width, rotation, and precise X/Y centre coordinates.
  Click Apply stair changes to resize, rotate or reposition the same flight,
  or use the ±15° rotation buttons for one-click rotation.
  If a selected staircase's rotation, position or size fails validation,
  the proposed change is rejected, the original values return to the
  property fields, and the reason is displayed next to the controls.
  Manually typing a value is only a proposal until you click Apply.
  Stairs stay vertically aligned on both floors and cannot cross
  internal partition walls.
- **Merge Rooms** tool: select a room and click Merge Rooms, then click an
  adjoining room (or click the two rooms in succession). The shared partition
  disappears and the first room retains its name, type, and ID. Doors on the
  removed partition are deleted; doors on surviving walls are retained.
  Arbitrarily angled and partially shared walls are supported.
- **Corner-to-wall snapping**: moving an existing corner can snap to another
  corner or a nearby angled wall, before falling back to the 0.5 m grid.
  Hold Shift while dragging to bypass snapping.
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

**Not yet implemented:** automatic room generation; free-standing unenclosed
partition walls; wall thickness; door width editing; hidden passages;
furnishing; and VTT export. Stairwell cutouts are represented in the 2D
floor-slab plan; realistic 3D structural slabs, rise/run calculations,
landings, guardrails and automatic stairwell rooms are still future work.
Global undo/redo now covers completed edits across floors and stairs.
Undo history is session-only and is not written into JSON files.
Move Wall moves a continuous straight exterior wall run perpendicular to itself. Internal wall movement still follows the selected segment, not an
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
| Merge adjacent rooms | Select a room, Merge Rooms, click the second room; or click both in Merge Rooms mode |
| Add an upper floor | Add Floor in the right panel (copies only exterior footprint) |
| Change floors | Select a floor in the right-hand floor selector |
| Compare floor alignment | Turn on Above and/or Below; adjust Reference visibility |
| Remove upper floor | Remove Top Floor in the right panel |
| Add stairs | Select lower floor, set width/length/rotation, Place Stairs; green/red live ghost guides placement |
| Move stairs | Select / Edit, drag staircase on either connected floor; Shift bypasses grid snapping |
| Rotate stairs with mouse | Select / Edit, select the staircase, drag its orange circular handle; Shift snaps to 15° |
| Rotate or resize stairs | Select stairs, enter dimensions and arbitrary angle, Apply stair changes |
| Rotate quickly | Use Rotate −15° or Rotate +15° (updates a selected flight immediately) |
| Position precisely | Select stairs, enter centre X/Y coordinates in metres, then Apply |
| Widen stairwell opening | Select stairs, set extra opening width/length, then Apply |
| View slab openings | Switch to the upper floor; a dashed 'OPEN TO BELOW' void follows each arriving flight |
| Remove stairs | Select / Edit, click staircase footprint, Remove selected staircase |
| Move a wall | Move Wall, drag a wall perpendicular to itself; Shift disables distance snap |
| Split any existing wall | Double-click in Select / Edit; exterior walls take priority |
| Draw room | Draw Room, click room corners inside the building, then Finish |
| Rename/type room | Select/Edit, click its interior, edit details, click Apply room details |
| Move corner | Select/Edit, drag an existing corner; it snaps to nearby corners or walls |
| Insert corner | Double-click a wall with Select/Edit |
| Delete corner | Select a corner and press Delete; the adjacent walls join |
| Add door | Add Door, then click on a sufficiently long wall |
| Add an exterior window | Choose window width, Add Window, click on an exterior wall |
| Move a window | Select / Edit, drag the thick cyan glazing line to another valid location on an exterior wall |
| Resize a window with handles | Select / Edit, select the window, drag either square endpoint to resize with its opposite end fixed |
| Resize or remove a window | Select / Edit, click the cyan window, then Apply width or Delete window |
| Highlight unassigned spaces | Tick Highlight unassigned interior in the sidebar; amber shading excludes rooms and stairwell voids |
| Delete room or door | Select it and use the appropriate side-panel Delete button |
| Pan | Middle-mouse drag, or Pan and left-drag |
| Zoom | Mouse wheel, or toolbar +/- |
| Fit | Ctrl+0 |
| Temporarily bypass snapping | Hold Shift while placing/moving corners |
| Cancel drawing | Esc |
| Undo/redo | Ctrl+Z / Ctrl+Y; includes stairs, upper floors and switching back to the edited floor |
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

**Unassigned space is not a special room.** It is usable floor area
inside the perimeter, excluding defined room footprints and floor-slab openings.
No automatic walls or room names are added. The highlighting is a view option,
not a change to the saved building. Windows are explicit exterior wall
features, stored in the existing JSON schema; earlier files without a
windows list remain compatible.

**Per-floor perimeters are independent.** Add Floor starts with a copy of the
previous floor's footprint for convenience. Select the upper floor from the
floor picker, then use Split Perimeter to add vertices, Select / Edit to drag
them, or Remove Corner to simplify that story alone. You can make the upper
floor smaller, larger or differently shaped (subject to normal polygon
validation and keeping existing stairs inside both connected floor outlines).

Each staircase carries optional **OpeningExtraWidth** and
**OpeningExtraLength** dimensions (0 by default for existing files). The cutout
is centred and rotated with its stairs; the upper-floor slab and filled rooms
are geometrically excluded from that rectangle. The opening has no separate
floating position to become detached. Stairwells cannot cut through interior
walls or leave the upper floor. Legacy v2 files with zero extra clearance
remain readable. Document history is maintained in memory independently
of the building JSON schema, so existing saved files remain compatible.

A multi-floor document uses JSON schema version 2 while preserving ground
floor fields from previous files. Its upperFloors array stores independent
floor plans; its staircases array stores connected lower-floor index, centre,
width, length and angle. Existing schema-1 files continue to load as a
single-story building. A floor can have a different shape after it is added;
new stair placements are validated against both connected floors.

Rooms are separate enclosed polygons inside the exterior footprint. Shared room
edges render as one interior wall and can be snapped together. Merge Rooms
reconstructs a new outline for two edge-adjacent rooms, including non-rectangular
layouts, while retaining the first room's identity. Room boundaries
may touch one another, but their interiors cannot overlap. The current model
does not automatically resize neighbouring entire wall edges when one room
changes; an edit that would create overlap is rejected.

When drawing a replacement exterior perimeter, existing rooms and doors must
still fit or the change is rejected. When deleting a room, doors attached only
to its removed walls are deleted too. Existing files remain compatible.

## Project layout

- src/BuildingAdventure.Core — polygon validation, floor-space classification, window/door geometry, JSON
- src/BuildingAdventure.Desktop — WPF floor-plan editor and file commands
- tests/BuildingAdventure.Tests — executable geometry checks
- docs/ARCHITECTURE.md — design rationale and future milestones

See the [architecture notes](docs/ARCHITECTURE.md).
