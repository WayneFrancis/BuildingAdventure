# BuildingAdventure

**BuildingAdventure** is a Windows editor for realistic tabletop RPG floor
plans. The goal is a hybrid architectural designer: generate a building from
requirements, draw one by hand, or combine manual rooms with procedural design.

## v0.22 — Historically scaled plans and usable hearth/stair layouts (development branch)

- **Imperial perimeter seam fixed:** planned rooms now preserve the exact
  exterior boundary rather than rounding their last walls to the nearest
  half-metre. This eliminates the narrow unassigned strips appearing along
  right/bottom façades of 5ft / 10ft grid buildings.
- **Medieval-style footprint ranges:** single-storey residence ~7–10m by
  6–9m; more substantial multi-storey residence ~8.5–11.5m by 8–11m;
  inn/tavern ~12–18m by 9–13m; shop/workshop ~6.5–10m by 8–12m;
  mixed-use ~9–15m by 8–12m. Dungeon sizes remain game-oriented rather
  than derived from a house. These are **design heuristics**, not medieval
  building codes or claims that every house of a type had those dimensions.
  The whole-building generator uses the full requested floor count to
  choose the appropriate profile.
- **Not all rectangles:** some larger or specialised buildings grow a
  modest L-shaped rear wing, on exact tabletop cells in imperial mode.
  Concave footprint filling still has room for improvement; wing areas
  can remain partly unassigned rather than producing invalid partitions.
- **Open domestic stairs:** small wall-hugging stair runs (2.8m × 1.0m),
  with the original 1m landing at both ends, are staged inside a temporary
  side bay so the room planner doesn't build walls through the flight.
  Once generated, its enclosing *Stairwell* room is removed, leaving
  an **open staircase alcove off the hallway**, not a 6m dedicated room.
- **Fireplace location and size:** heating is placed inside a living,
  common, cooking or other inhabited room if there is a suitable wall,
  **never inside a hallway**. The domestic schematic fireplace mass is
  approx. 1.4m along a wall by 0.7m deep for a modest residence,
  increasing to 2.4m by 0.9m for an inn's large communal hearth.
  A shared chimney crosses the floors at one world coordinate; the
  drawing labels a room's hearth *FIREPLACE* and a nonhabitable space's
  chimney continuation *FLUE*. Since period hearths ranged from open
  floor fires to large hooded masonry structures, size is a useful
  planning default, **not a fixed medieval standard**. A fireplace
  option can decline placement if no safe wall section exists.
- **Reference examples for these heuristics:**
  - Newbury Farm, Tonge, Kent: early aisled hall about 9.32 × 5.13m,
    not necessarily the full building. Kent Archaeological Society:
    https://www.kentarchaeology.org.uk/journal/123/newbury-farm-tonge-kents-earliest-known-aisled-hall-house
  - Walderton, Sussex: excavated house plan at least 10 × 6.3m:
    https://www.wealddown.co.uk/wp-content/uploads/2014/10/Walderton_article_SAC_v120_1982.pdf
  - Medieval hall-house cross passages and room arrangements:
    https://www.gutorglyn.net/gutoswales/en/tai-pensaer-cynllun.php
  - Open hearths, wall fireplaces and chimney history:
    https://www.medieval.eu/open-hearths-ovens-fireplaces/
    and https://www.wealddown.co.uk/museum-news/history-of-the-hearth/
  - Large medieval kitchen hearths are not a fixed size: Historic England
    records a circa 3m fireplace at Padley Hall and a circa 3.5m-wide
    fireplace at Scargill. Our 1.4m/2.4m widths are tabletop-scaled
    planning defaults, not assertions about a historical average:
    https://historicengland.org.uk/listing/the-list/list-entry/1017587
    https://historicengland.org.uk/listing/the-list/list-entry/1017320

## v0.21 — Tabletop grids, habitable-room glazing, landings and chimney stacks (development branch)

- **RPG map scales:** select metric, **5 ft squares** or **10 ft squares**
  in BUILDING → Tabletop map grid. The drawing grid and point snapping
  use exact 1.524 m / 3.048 m cells. Room areas, exterior-wall dimensions
  and other floor summaries display feet / square feet in imperial mode.
  New whole-building **exterior footprints** align exactly to those
  squares. The choice survives JSON save/reopen, Undo and Redo.
  **Known limitation:** the current automatic internal room/corridor
  partitioner still works in metric increments, so interior walls are
  not yet guaranteed to follow tabletop grid lines. Existing geometry
  is not rescaled when the grid choice changes.
- **Windows by habitable room:** Windows are enabled by default for new
  editor preferences. Generation attempts one exterior window for each
  eligible exterior-facing room, rather than a maximum of two windows
  per floor. Hallways, stairwells, storerooms, cellars, closets, pantries,
  utilities and similar enclosed spaces are exempt. Kitchens normally
  get windows, but one containing a fireplace may omit them. No window
  is invented for an internal room lacking a valid exterior wall.
  Proposed glazing avoids the perimeter around a stairwell and a chimney.
  Windows are now processed **after** stairs in multi-feature passes.
- **Real landing reservations:** newly generated stairs require 1 m
  of clear platform at **both ends** on **both connected storeys**;
  they cannot overlap interior walls or pass beyond the perimeter.
  Generated stairwell bays are now 6 m long rather than 5 m.
  The landing size can be edited in the STAIRCASES panel and is shown
  as a dashed area on the plan. Previously saved stairs with zero landing
  clearance remain loadable, and can be upgraded in the editor.
- **Linked fireplaces/chimneys:** optional *Fireplace / chimney stack*
  generation tries exterior walls first and then interior ones. One
  chimney definition is saved for the whole building, rendered at the
  same world position on every level whose floor plate covers its
  footprint, including a basement if the footprint is fully beneath
  the chimney. Validation excludes overlapping stair runs/landings,
  interior walls and nearby door/window openings. This is a schematic
  fireplace/flue marker, not a full smoke, flue, structural or fire-code
  simulation. Automatic chimney insertion can be skipped if no
  suitable wall position passes all validation.
- Added regression tests for two clear stair landings, per-room window
  coverage and exemptions, exact imperial footprint sizes, stairwell
  glazing clearance, grid JSON persistence, and basement chimney
  continuity.

## v0.20 — Visible floor count, editable acceptance and wall-hugging stairs (development branch)

- **Always-visible working-floor navigator:** the drawing area now shows how
  many levels the committed building actually contains, with a selector and
  Previous/Next buttons independent of the sidebar. This avoids mistaking a
  single displayed ground floor for a single generated floor.
- **Clear draft-to-editor transition:** the in-plan whole-building preview
  shows every proposed level and has Accept All / Discard buttons. Accepting
  validates the full draft floor count, hides the read-only canvas, restores
  Editor mode, and activates the editable Select tool.
- **No silent downgrade to one floor:** the whole-building command rejects
  an unset or invalid floor-count selection rather than treating it as one.
  The proposed building must contain every expected floor before acceptance.
- **Wall-adjacent stair flights:** newly generated stairwells reserve a 0.2m
  gap beside the outside wall and leave access space toward the hallway.
  On layouts without a dedicated stairwell, optional stair suggestions
  consider wall-adjacent positions instead of the centre of a room.
  Whole-building stairs stay aligned between floors.
- **Keyboard navigation:** Page Up/Page Down now browse proposed floors
  during an unaccepted whole-building preview, too.
- **Validation:** two new geometry regression tests confirm stair-footprint
  proximity to a wall and cross-floor geometry; the existing three-floor
  creation and JSON round-trip tests remain in place.
- **Limitations:** these are geometry / access improvements, not guarantees
  of code-compliant stairs, doorway landings or structural clearance.

## v0.19 — Browsable draft floors and side stairwells (development branch)

- **Browse before accepting:** generating a whole building displays a
  read-only floor plan directly in the main editor area. Its dedicated
  floor dropdown and Previous/Next controls show every proposed floor,
  including levels not yet part of the working building. Accept All commits
  the draft; Discard preview restores the live editor unchanged. Saved
  whole-building proposals can be inspected in the same way.
- **Stairs off the hall:** for a new multi-floor building with corridors
  and stairs enabled, the generator builds central halls on every floor,
  reserves a vertically aligned *Stairwell* room beside the hall on each
  level, and places connecting flights inside that space. Each stairwell
  shares a wall with the hall and receives a candidate door.
- **Fill around stairs:** remaining frontage is subdivided on either side of
  existing stairwell reservations rather than being skipped because a
  full-width proposed room would overlap the reserved area.
- **Don't block existing corridors:** when completing existing floors,
  automatic stair placement uses matching stairwell rooms if available,
  otherwise declines to insert stairs through a hallway.
- **Limits:** this is a 2D stairwell reservation and connected-floor
  layout, not a building-code-compliant staircase design. Realistic
  landings, treads/risers, guardrails and multi-level fire egress still
  require separate validation.

## v0.18 — Visible whole-building preview and floor-count confirmation (development branch)

- **See every generated floor immediately:** choosing 1–4 floors and clicking
  **Suggest entire building** opens the all-floor viewer automatically. The
  floor dropdown in that viewer includes room counts for each proposed level.
  Close the viewer and choose **Accept All** to actually replace the current
  building with the entire generated draft.
- **Separate generator actions:** **Suggest on checked floors** edits only the
  already-existing checked floors. It does not add floors, regardless of the
  separate new-building floor-count choice. The labels now make this explicit.
- **Confirm creation:** the generator rejects an unexpected draft floor count,
  the preview panel distinguishes the number of proposed floors from those in
  the current document, and acceptance reports the resulting floor count.
- **Regression coverage:** a three-floor draft is verified to keep all three
  levels after copying and JSON round-trip, without modifying the prior
  one-floor building.

## v0.17 — Room priorities and sensible residential proportions (development branch)

- **Reserve a properly sized main living space:** on a clear ground-floor
  residence with a suitable hall, reserve a 23 m² or larger living room
  before dividing the rest of that hallway frontage. The leftover part can
  form a small service room, while the other side is subdivided independently.
  Buildings too small for this layout retain the original conservative split.
- **Essential rooms first:** allocate a home's kitchen and dining room ahead
  of repeated bedrooms; favour bedrooms and studies upstairs. Other building
  purposes receive ordered priorities for their relevant rooms, while
  appropriate repeatable spaces such as guest bedrooms remain possible.
- **Readable names:** use "Kitchen" or "Living room" when the name is still
  available, and add a number only to disambiguate a duplicate.
- **Regression fixes:** deleting a basement no longer tries to read the
  source ground floor from the upper-floor collection. A room-only suggestion
  that cannot fit a room reports an explicit no-valid-room explanation.
- **Scope:** the room assignment is more deliberate, but automatic circulation
  between floors, architectural codes, furnishing, natural light and full
  whole-building room adjacency remain future work.

## v0.16 — Space-efficient, purpose-aware room layouts (development branch)

- **Fill frontage rather than scatter rooms:** on a clear, rectangular floor
  with a straight central hallway, the generator divides both sides into
  contiguous rooms using the available space, instead of stopping after
  a few randomly positioned rectangles. Existing room outlines remain
  untouched; each new room must pass the existing geometry checks.
- **Building purpose:** select Residence, Inn or tavern, Shop or workshop,
  Dungeon, or Mixed use in the generator. Room roles now depend on building
  purpose, available area and floor level: for instance, upstairs inn
  rooms favour guest accommodation, basement rooms favour storage, and
  compact residential rooms can become bathrooms or pantries. Repeated
  room names are avoided where possible.
- **Review floor usage:** the suggestion description reports the assigned
  floor area as a percentage and the square metres still unused for
  every selected floor. Rectangular hallway layouts are intended to use
  most or all of the remaining frontage, while irregular/partly
  completed plans remain conservative.
- **Still a first planning pass:** this does not yet enforce realistic
  room adjacency rules across building purposes, windows/daylighting,
  accessible circulation, exits or construction standards. Generation
  remains a preview, with the existing Accept/Remember/Regenerate
  controls unchanged.

## v0.15 — Configurable connected-layout suggestions (development branch)

- **Generate features independently** in GENERATOR / SUGGESTIONS:
  Rooms, Corridors, Doors, Windows, and Stairs have separate checkboxes.
  Rooms, Corridors and Doors default to on; Windows and Stairs require
  opt-in. The selections are saved in local UI preferences.
- **Corridor-first floor layouts**: where a straight, 2-metre central
  corridor fits within unassigned space, new rooms are placed beside
  it with actual shared walls. Existing rooms, doors, windows, and
  staircase geometry remain unchanged until Accept.
- **Connectivity pass**: proposed doors are added as actual validated
  wall openings, favouring an outside entrance and then interior
  connections. A candidate description reports how many rooms still
  lack door access. This is an initial access planner, not a claim
  that every room on every irregular floor is reachable.
- **Optional windows and stairs**: exterior windows can be proposed
  for named rooms; staircase flights can be proposed between adjacent
  **selected** floors, with existing floor geometry, wall clearance,
  and cross-floor validation respected. No feature is forced if
  validation rejects its placement.
- **Full feature previews**: green room/corridor outlines are joined by
  dashed orange proposed doors, blue proposed windows and green stair
  outlines. The pending proposal and saved-candidate memory store all
  generated components together; Accept All commits the selected
  changes as one undoable building snapshot.
- **Compatibility**: existing callers of the generator retain
  room-only behaviour through the legacy overloads. This pass
  deliberately does not yet handle complex winding corridors,
  guaranteed graph-wide connectivity, purpose-aware room assignments
  or multi-floor egress rules.

## v0.14 — Flexible floor deletion, stacked stairs and reliable suggestions (development branch)

- **Delete any selected floor** in the FLOORS panel, including intermediate
  levels and ground. Surviving levels above the removed one shift down.
  The deletion preview states how many connected stair flights disappear,
  then confirms before committing. Unaffected stair flights are reindexed
  and validated; the entire operation supports Ctrl+Z/Ctrl+Y.
  If the last above-ground floor is removed while a cellar remains,
  the building becomes basement-only. Add Floor can create a new ground
  level later. The total building limit is still four levels.
- **Stacked stairs**: an up flight and a down flight may occupy the same
  plan footprint at an intermediate level. Separate slab openings remain
  on their respective upper floors. The stair selector in the sidebar
  lists both UP and DOWN flights; repeatedly clicking an overlapping
  stair footprint cycles the selected flight. Plan labels for stacked
  flights are offset to avoid hiding each other.
- **Four-floor building preview**: Suggest entire building creates a full
  1–4-floor draft. Inspect all proposed floors opens a separate
  floor-by-floor viewer of the candidate before acceptance, even when
  the current building contains fewer levels. All floors are created
  when Accept All commits the draft.
- **Generator no-room fallback**: random placement is followed by a
  deterministic scan for remaining rectangular space, down to 1 × 1 m.
  A suggestion containing zero new rooms now fails clearly rather
  than quietly returning an empty plan. Multi-floor result descriptions
  report room counts per floor and identify selected levels that could
  not fit additional rooms.



The editor currently supports:

- **Page Up / Page Down floor navigation**: when not editing text, Page Up
  selects the next higher floor and Page Down the next lower floor.
  The keys work across basement, ground and upper levels and stop at
  the highest/lowest available level.
- **Basements and cellars**: use Add Basement to create the next level
  below the lowest existing floor and Remove Deepest Basement to remove
  it. The ground floor retains index 0; basement 1 is -1, basement 2
  is -2, etc. A basement starts with the footprint and wall thickness
  of the level directly above but is independently editable afterward.
  Stairs placed on a basement can lead up through the slab of the
  level above, including through the ground floor. Four levels total
  (above and below ground combined) are currently supported.
  Files without basements remain compatible.
- **Multi-floor generation review**: floor checkboxes can include
  basement and upper levels. If the current floor was not selected,
  the editor opens the first selected floor after generating.
  Use View next selected floor or Page Up / Page Down to examine each
  floor's green preview; generator changes still apply only to the
  checked levels.
- **Accept one proposed room**: in Select / Edit, click a green dashed
  room proposal; it is highlighted red/orange. Choose Accept selected
  suggested room to commit only that room (and the initial perimeter
  if needed), without accepting the other proposed rooms. The rest of
  the preview remains available, and the change gets its own global
  undo step. Accept All still applies the whole suggestion.
  New whole-building drafts must be accepted together.

- **Procedural building suggestions**: GENERATOR / SUGGESTIONS lets you
  select existing floors by checkbox, suggest new rooms within each
  selected perimeter, regenerate alternatives, preview green dashed
  additions, and Accept one validated candidate as a single undoable
  change. For empty floors it suggests a starting perimeter too.
  Suggest entire building drafts a completely new 1–4 floor building
  with independent room layouts on each floor, subject to confirmation
  before replacing the current building.
- **Remember generated candidates**: Remember stores an entire proposed
  building for Preview saved, Remove, or Clear list. Saved variants persist
  at \`%LOCALAPPDATA%\BuildingAdventure\suggestion-memory.json\` (up to
  50 entries). Recalled candidates remain previews until accepted, and
  accepting an old variant warns when the underlying building has changed.
- **Secret doors and visibility previews**: select a door and mark it
  secret, then optionally reveal it. Editor and GM mode highlight secret
  doors; Player preview displays undiscovered doors as solid wall without
  a doorway symbol. Secret rooms, passages, and player map export are
  planned future stages.
- **All-floor silhouettes**: the FLOORS panel can show every other floor's
  exterior footprint as a dashed labelled silhouette, distinct from
  detailed Above/Below adjacent-floor alignment guides.
- **Current generator limits**: this initial pass checks polygon and
  staircase geometry, and preserves hand-authored structures when
  completing selected floors. It does not yet generate circulation
  routes, entrances, guaranteed room access, furniture or realistic
  architectural space-use plans. Those are subsequent generator passes.

- **Centre selected openings on a wall segment**: select a window and
  click *Centre window on room wall segment*, or select a door and click
  *Centre door on wall segment*. Windows use the exterior edge of the
  adjoining room (so a shop window is centred on that shop's frontage,
  not the entire building facade). If no room is defined at the window,
  the exterior wall segment is used instead. Doors use the actual
  physical wall section, including interior partitions. The action
  uses the geometric midpoint rather than the snap grid; invalid
  centres are rejected without changing the building. Successes
  participate in global undo and redo.
- **Remember application layout**: on accepted close, save the normal
  window location and dimensions, maximized state, the open/collapsed
  sidebar sections, sidebar scroll position, grid snapping,
  unassigned-space highlighting, adjacent-floor overlays and their
  visibility. Restore these at next launch, keeping this file separate
  from saved building JSON. A removed display or invalid settings
  cannot prevent normal startup. Preferences are stored locally in
  `%LOCALAPPDATA%\BuildingAdventure\ui-settings.json`.
  This does not automatically reopen unsaved building edits.

- **Collapsible settings panel**: BUILDING, FLOORS, STAIRCASES,
  WALL CONSTRUCTION, PERIMETER, ROOMS, WINDOWS, DOORS, and HOW TO USE
  are individually expandable sections in the right-hand scrolling panel.
  Less frequently used sections start collapsed, and their open/closed
  state remains in place as you switch floors.
- **Drag-editable doors**: in Select / Edit, drag the door's centre
  marker to reposition it on an interior or exterior wall. Once selected,
  drag its square endpoint handles to change opening width while keeping
  the opposite endpoint fixed. The DOORS section also provides a numeric
  width field. Invalid placements and overlaps are rejected and do not
  create undo entries; completed changes use global Ctrl+Z/Ctrl+Y.
- **Grid-snapped window and door editing**: with Snap to 0.5 m grid
  enabled, placing, dragging, or resizing a window snaps the pointer
  position to the 0.5 m grid before projecting onto its supporting wall.
  Hold Shift during the drag for unsnapped movement. Door movements and
  handle resizing use the same grid behaviour. Snapping is view/editor
  behaviour; saved window and door coordinates remain ordinary metres.

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
- **Gap-free wall joins**: solid wall junction patches close the angled
  wedge between the separate thick wall strokes at every real corner and
  T-junction. Join shape follows the actual interior or exterior thickness;
  right-angle and diagonal corners receive sharp bounded miters instead
  of an empty notch. Straight split points are not thickened, and door
  and window gaps remain unobstructed. Structural reference lines and
  room/perimeter editing behaviour are unchanged.
- **Physical wall thickness**: set separate exterior and interior wall
  thicknesses in metres in the WALL CONSTRUCTION panel. Defaults are
  0.24 m exterior and 0.12 m interior. Walls render as filled,
  double-stroked structural runs centred on the existing editing lines.
  The editor geometry, snapping and room definitions are unchanged.
  Doors and windows subtract real gaps from these rendered wall runs,
  including when they span room-induced wall splits. Each floor can
  have its own thickness, with inheritance on floor creation,
  save/reload and full undo/redo.
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
- Physical-width wall strokes with explicit door/window gaps, including split or angled walls.
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
| Change wall thickness | In WALL CONSTRUCTION set exterior/interior thickness in metres and choose Apply wall thickness |
| Move a wall | Move Wall, drag a wall perpendicular to itself; Shift disables distance snap |
| Split any existing wall | Double-click in Select / Edit; exterior walls take priority |
| Draw room | Draw Room, click room corners inside the building, then Finish |
| Rename/type room | Select/Edit, click its interior, edit details, click Apply room details |
| Move corner | Select/Edit, drag an existing corner; it snaps to nearby corners or walls |
| Insert corner | Double-click a wall with Select/Edit |
| Delete corner | Select a corner and press Delete; the adjacent walls join |
| Add door | Add Door, then click on a sufficiently long wall |
| Centre window within its room facade | Select window, expand WINDOWS and click Centre window on room wall segment |
| Centre door in a wall section | Select door, expand DOORS and click Centre door on wall segment |
| Preserve window size and position | Close the application normally; the window and sidebar layout restore on next launch |
| Navigate between floors | Page Up goes one level up; Page Down goes one level down |
| Create a cellar or basement | Expand FLOORS, click Add Basement, then select its floor and draw/edit rooms |
| Remove lowest cellar/basement | Expand FLOORS and click Remove Deepest Basement |
| Review multi-floor suggestions | Choose View next selected floor or Page Up/Down to see previews on the checked floors |
| Accept a single generated room | Select / Edit, click its green dashed proposal, then Accept selected suggested room |
| Generate selected floors | Check floor boxes in GENERATOR, select Suggest / Regenerate and Accept the preview |
| Suggest a complete new building | Select a floor count and choose Suggest entire building; Accept replaces the current building after confirmation |
| Save/recall variants | Remember, Preview saved, Remove and Clear list in the generator panel |
| View floor silhouettes | Enable Show silhouettes of every other floor in FLOORS |
| Preview secret doors | Select a door, mark Secret door, choose Player preview to see the concealed wall |
| Collapse sidebar settings | Click a section's header to expand or collapse it |
| Move a door | In Select / Edit, drag the door centre marker to a valid wall position |
| Resize a door with handles | Select the door, then drag either square endpoint; the other endpoint remains fixed |
| Set door width precisely | Select the door, expand DOORS, enter metres and click Apply width |
| Place and move snapped windows | Enable Snap to 0.5 m grid; hold Shift to disable snapping temporarily |
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
