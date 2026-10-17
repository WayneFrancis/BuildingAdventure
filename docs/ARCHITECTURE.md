# BuildingAdventure architecture

## Product direction

BuildingAdventure is a tabletop RPG building designer which combines fully
automatic layouts with precise manual editing. Its design goals are realistic
buildings with one to four stories and support for homes, shops, inns,
workshops, warehouses, mixed-use occupancy, and optionally hidden rooms.

The first milestone implemented a perimeter editor. The second milestone adds
manual polygonal rooms, shared-wall rendering, simple door openings and
validation. Procedural generation now has initial geometry and connectivity suggestion passes, with more sophisticated architectural planning still outstanding.

## Coordinate system

- Geometry uses double-precision metres in a 2D Cartesian coordinate system.
- The desktop viewport maps metres to WPF device-independent pixels.
- The user can draw straight walls at any angle, not just orthogonal edges.
- The current footprint is a single, closed, simple polygon; no internal holes
  or multiple disconnected structures are supported yet.
- Rooms are also simple polygons. Each room must fit inside the exterior, with
  no interior overlap with other rooms. Shared edges and boundary contact are allowed.
- A derived wall arrangement subdivides room and exterior edges at collinear
  endpoints and de-duplicates coincident fragments.
- Door centres and widths are stored in metres; the current renderer associates
  a door with a suitable wall fragment geometrically.
- Door validation and rendering resolve continuous collinear wall runs across
  split vertices; inserting a vertex in the middle of a door does not break it.
  A true corner (non-collinear bend) still requires the door to fit one run.
- Splitting an exterior/room edge propagates the same new vertex to all
  coincident room and exterior edges. This preserves shared-edge topology and
  does not change the actual outline until the new point is dragged.
- Splitting a room inserts the two cut endpoints along its existing wall
  edges (including coincident boundaries), then traces both boundary arcs
  and closes them with a straight partition. Both resulting polygons must
  remain inside the source room and meet the minimum usable room area.
- Moving a wall applies a signed displacement along its normal to all
  source vertices coincident with that selected wall fragment. All polygons
  sharing those points are updated. Door centres on the moving segment
  translate with it; doors on adjoining segments retain their fractional
  positions as the adjoining edges change.
- Interactive wall dragging uses a fixed original snapshot throughout the
  preview to avoid cumulative drift; only a valid final position enters
  the undo stack. Exterior restrictions and room overlap validation still
  apply to every completed edit.
- Merging rooms normalizes both polygon windings, subdivides existing edges
  at coincident endpoints, removes pairs of oppositely-directed shared wall
  fragments, and traces the remaining directed outer boundary into one polygon.
  A merge must preserve the combined area, produce one simple outline, and
  pass whole-floor validation. The first room keeps its identity and metadata.
  Doors on the deleted common partition are removed; other openings remain.
  Partial shared walls and non-orthogonal room outlines are supported.
- During vertex dragging, snapping prefers pre-existing non-moving corner
  coordinates, then projected positions on non-incident edges, and finally
  the optional world-space grid. This maintains exact shared geometry and
  avoids snapping back onto the vertex's own original incident walls.
- Removing a corner joins its two incident segments. Matching shared
  room/perimeter vertices are removed together when both adjacent edges are
  coincident with the affected old edges. Door centres on those segments
  are remapped by proportional original wall-path distance to the joined
  segment. All resulting geometry is validated before accepting the edit.
  A failed merge changes no saved or in-memory drawing state.
- A clockwise or counterclockwise ordered list of vertices defines the boundary.
- All geometric constraints run in the renderer-independent core library.

## Storeys and stair reservations (v0.5)

- Version-1 JSON buildings still have one floor, with perimeter, rooms and
  doors at the root. Version-2 files retain that ground-floor representation
  and introduce upperFloors and staircases arrays.
- An upper floor stores an independent perimeter, rooms and doors. Adding a
  floor initially copies the previous floor's perimeter, not its partitions.
  Later edits can give that floor a different outline.
- The editor handles one floor at a time. Switching floors snapshots the
  current editor geometry into its correct storey, loads the new geometry,
  and resets the editor's transient interaction state for the new floor.
  Building metadata and stair definitions are held by the main window.
  The separate building-wide history persists across floor changes.
- A stair flight has a lower-floor index, centre, length, width and rotation.
  A rotated four-corner polygon defines its footprint on both connected
  floors, where the same world coordinates guarantee vertical alignment.
- Validation checks each floor independently and the stair footprint inside
  both connected floor outlines. Stairs may occupy a room or hallway region,
  but cannot cross an existing interior wall. Stairwell holes, rise/run,
  structural floor slabs and automated vertical circulation are future work.
- Up to four floors are currently supported; only the highest floor can be
  removed. Removing it also removes stair flights leading to that floor.

## Interactive stairs (v0.5.1)

- The WPF viewport obtains a proposed stair template from the parent window's
  current input fields, snaps the centre to the world grid when requested, and
  paints a green or red footprint preview using the same whole-building
  validator that accepts/rejects commits. Invalid placement doesn't modify
  the document.
- In Select / Edit mode stair hit testing starts a drag preserving the local
  cursor grab offset. The viewport previews a candidate staircase on every
  mouse move and commits only on a valid release. On invalid release it
  preserves the original staircase coordinates and reports why.
- The main window owns staircase mutations because a stair spans two floors.
  Core TryAddStaircase and TryUpdateStaircase return validated independent
  copies, retaining IDs and floor connections when editing existing stairs.
  The viewport overlay preserves selection after a committed update.
- Rotation and size are editable after placement, including arbitrary
  rotation angles. The sidebar supports exact centre coordinates and quick
  15-degree rotation steps. Existing JSON schema version 2 is unchanged.
- Stair creation, movement and property edits are committed through the
  building-wide undo/redo journal introduced in v0.7.

## Drag-to-rotate staircase controls (v0.5.2)

- The selected staircase has a circular rotation grip at the positive end
  of its local long axis, offset by a fixed number of screen pixels so it
  stays accessible at any zoom.
- Mouse-down on the handle records the initial world-space pointer angle.
  Rotation derives from the initial staircase angle plus the change in
  pointer angle. The cursor need not start exactly in the handle centre,
  eliminating angle jumps when the user grabs near its edge.
- Shift during rotation constrains the result to 15-degree increments.
  Without Shift the staircase rotates freely through 360 degrees, including
  across 0/360 wraparound.
- The original stair is not mutated during a drag. Each preview copy
  runs the existing whole-building validation, visualized green for valid
  or red for invalid. Only a valid mouse release emits the same committed
  edit command used by the numeric controls, preserving floor alignment.
- Each floor already maintains an independent polygonal perimeter. An
  added floor initially copies the previous footprint, then stores future
  edits independently. Stair flights are checked against both outlines;
  a floor change may be rejected if it would invalidate an existing flight.

## Stairwell slab openings and alignment guides (v0.6)

- A staircase owns its linked opening in the slab of the floor immediately
  above it. This is a derived 2D cutout rather than an independent movable
  object. Default opening size equals the staircase footprint, maintaining
  compatibility with earlier v2 JSON files. Optional OpeningExtraWidth and
  OpeningExtraLength enlarge the upper opening without changing the stairs.
- BuildingStoreys.GetOpenings returns derived polygon rectangles by floor,
  and SlabArea subtracts their areas from the floor footprint. The viewport
  uses WPF CombinedGeometry Exclude for the floor and room fill surfaces,
  paints the void boundary, and labels it OPEN TO BELOW. The stairs still
  display on both connected floors.
- Validation checks enlarged opening clearance inside the upper floor,
  prohibits crossing interior walls, and rejects overlapping enlarged voids.
  Existing v2 buildings with old overlapping stair reservations but no
  explicit extra clearance can still be loaded.
- Reference overlays use BuildingStoreys.AdjacentFloors, which returns
  detached snapshots of the floors immediately above and/or below. The
  viewport paints the referenced perimeter and interior walls with dashed,
  translucent strokes behind the active floor. Reference opacity and floor
  visibility are view-only UI settings, not part of building JSON.
- Stair validation applies consistently to mouse drags, rotations, numeric
  property edits and perimeter adjustments. Global undo/redo now covers
  these committed operations as of v0.7.

## Document-wide undo and redo (v0.7)

- Core BuildingEditHistory retains up to 200 detached snapshots of the
  complete multi-floor BuildingDocument together with the active floor index.
  The journal itself is session-only and is not serialized to JSON.
- FloorPlanSurface emits DocumentEditCommitted(previousFloor) once from its
  common successful-mutation path. The host combines the previous floor
  geometry with all current other floors and staircase definitions to
  construct the actual full-building prior state. This avoids capturing a
  half-finished drag, preview or temporary polygon draft.
- When hosted by MainWindow, FloorPlanSurface does not accumulate its
  legacy duplicate local history stacks. MainWindow owns the authoritative
  undo and redo. An incomplete outline/partition selection is still undone
  locally before considering committed building-history entries.
- The host explicitly records a pre-change snapshot for stair placement,
  relocation, resizing, rotation and removal; floor addition/removal; and
  accepted building rename, committed once on leaving the name field.
  Switching floor view, panning, zooming and rejected changes are not edits.
- Undo/redo restores the entire detached snapshot and selects the floor on
  which the recorded change happened. A new committed edit invalidates the
  redo branch. Loading or creating a new document resets the history.
- Snapshots are immutable by ownership convention: every stored and
  returned building is deep-copied. Changes on one floor cannot mutate
  previously saved history states or an unrelated floor.

## Unassigned floor space and exterior windows (v0.8)

- Unassigned usable floor space is the difference between the current
  exterior polygon and the union of named rooms and stairwell voids.
  This is purely a classification, not an auto-generated corridor or a
  virtual wall. FloorSpaceAnalysis.Classify supports Outside, DefinedRoom,
  UnassignedInterior and FloorOpening. On the WPF surface the exact
  highlighted polygon is produced with CombinedGeometry.Exclude; its
  GetArea gives a displayed metric in square metres after zoom scaling.
- The Show unassigned option is purely a visualization; the document
  remains unchanged. Draw Room is the explicit tool for creating named
  space inside an unassigned region.
- Windows are WindowDefinition objects (Id, Centre, Width) stored
  independently per floor and serialized in the existing building JSON
  schema. Older JSON documents default to an empty windows collection.
- A window can exist only on the exterior wall. It must have adequate
  clearance from outside corners, nearby doors and other windows.
  WindowPlacement and the shared door-attachment geometry preserve its
  relative location when wall corners or whole walls move. Validation
  rejects any operation that would strand a window away from its
  supporting exterior wall.
- Windows participate in snapshot-based floor and global undo/redo,
  and survive independent upper-floor editing. The WPF editor displays
  glazed windows as blue double-stroke segments; clicking selects them
  for width edits or removal.
- Physical wall cross-sections, elevation/height, window glazing style,
  full window swing or sash detail are intentionally deferred.

## Shared perimeter propagation and interactive window editing (v0.8.1)

- SharedBoundaryEditing.MoveVertex applies a perimeter endpoint edit to
  all coincident points and interpolates interior-room vertices lying on the
  two exterior edges touching the dragged corner. Geometry is recomputed from
  the drag-start snapshot to avoid cumulative drift.
- Exterior wall movement resolves the maximal connected collinear perimeter
  run rather than a single physical fragment (which can be subdivided by room
  endpoints). All perimeter and interior-room vertices lying on that run move
  together. True angled joints remain boundaries of the moved run.
- SharedBoundaryEditing.MoveOpenings maps doors and windows onto the changed
  counterparts of their original supporting polygon edges. Windows prefer
  perimeter edges; doors use the shortest changed room or perimeter edge.
  The final geometry is validated before committing to global undo.
- Select / Edit on a window line initiates a drag along exterior walls.
  Clicking the selected window's square endpoints initiates a length resize
  that anchors the opposite endpoint. All candidates are validated, and
  invalid mouse releases restore the saved window without creating history.
- WindowPlacement.ResizeFromEndpoint and TryUpdateWindow expose testable,
  renderer-independent geometry and validation. Windows have a thick, bright
  cyan line with contrasting white/dark casing for improved visibility.
- Room connectivity is still represented by matching polygon coordinates,
  not by shared structural wall object IDs. This milestone preserves the
  geometry of walls already lying on the same exterior boundary; a future
  topology layer could provide explicit persistent wall associations.

## Wall construction and physical openings (v0.9)

- WallThicknessSettings is stored per floor, with a 0.24 metre exterior and
  0.12 metre interior default. Individual floors can override these two
  dimensions, and a newly added floor inherits the previous floor's sizes.
  Both settings are validated and included in deep copies and global history.
  Older saved JSON automatically receives the defaults.
- Existing polygon edges remain the structural wall centre lines and are
  unchanged by thickness edits. This avoids perturbing adjacency, corner
  snapping, window positions, door positions, and stairwell validation.
- WallConstruction.SolidPieces derives continuous solid subsegments of each
  physical wall from FloorGeometry.BuildWalls, subtracting door and window
  intervals before rendering. Intervals cross collinear split vertices
  correctly. Windows can only subtract material from exterior segments;
  doors can interrupt exterior or interior segments.
- The WPF surface renders these remaining segments as thickness-scaled
  dark-edged structural strokes. It then draws door swing and window
  glazing symbols at the original opening locations. No wall is painted
  through an opening, and a floor's view can show different wall sizes
  without changing the editing geometry.
- The new WALL CONSTRUCTION panel validates and applies both dimensions
  atomically, records an undo snapshot, and updates the drawing immediately.
  Wall dimensions and floor association persist through save/load.

**Scope limitation:** This is a 2D architectural wall-width representation,
not a structural building-information model. Junction joins are strokes
centred on editable wall lines; they do not yet use explicit inner/outer
offset polygons, structural corner mitres, load-bearing classifications,
ceiling heights, or 3D wall meshes.

## Solid corners and wall junctions (v0.9.1)

- Physical wall strokes used to terminate with flat caps, leaving triangular
  gaps on the outside of angled corners and at interior-wall T-junctions.
- WallConstruction.JunctionPatches now detects true solid structural nodes
  from WallConstruction.SolidPieces. It uses arm directions and their
  independent physical half-widths to construct a bounded convex miter
  polygon. Purely collinear split vertices do not generate a patch.
- The WPF drawing surface renders all dark structural outlines first and
  fills their junction patches, then paints the wall material strokes and
  their smaller junction patches. This seals the corner gap while preserving
  the visible border and avoiding dark seams between joined wall pieces.
- Junction detection is performed on *solid pieces*, not raw polygon corners.
  Door and window cut ends with only one solid arm therefore receive no
  accidental end caps, so the openings remain clear, including openings
  across collinear wall splits.
- Sharp miter intersections are bounded to three times the adjoining wall
  radius to prevent extreme protrusions at nearly parallel walls. Editing,
  snapping, collision rules, JSON schema, and wall centre lines are unchanged.

## Collapsible sidebar and interactive openings (v0.10)

- MainWindow uses WPF Expanders for nine sections of the existing scrolling
  right panel. Collapsing a section only hides its controls and does not
  alter the building document, undo history, or editing mode. BUILDING,
  FLOORS, PERIMETER and ROOMS start expanded; advanced sections start
  collapsed for easier navigation.
- OpeningEditing.OnWall handles editor snapping for both door and window
  operations. When enabled, it rounds the pointer in world XY to a regular
  grid and then projects to the target wall. Angled walls therefore still
  receive a point on the wall, while Shift temporarily disables rounding.
- Existing window placement, movement and endpoint resizing now use this
  helper, honouring the existing Snap to 0.5 m grid checkbox.
- Door movement/resize uses the same snapshot-based drag lifecycle as
  windows. OpeningEditing.ResizeDoor keeps one endpoint fixed and updates
  the centre and width. OpeningEditing.TryUpdateDoor validates a detached
  candidate independently of WPF, preserving the source on failure.
- Selected doors show square endpoint handles and an orange centre marker.
  The centre is used for dragging; handles are used for resizing. Movement
  can use both interior and exterior walls, and FloorGeometry.Validate
  rejects unsupported, oversized or overlapping doorway placements.
- All successful interactive changes use the existing floor edit commit
  callback and global history; preview-only operations remain transient.

## Segment-centred openings and persistent UI layout (v0.11)

- OpeningEditing.TryCentreWindow looks for room-outline edges containing
  the selected window centre and fully coincident with the exterior
  physical boundary. Those room segments take precedence over any
  longer shared perimeter edge. If none exists, exterior physical
  segments are used as the fallback for unassigned interior space.
- OpeningEditing.TryCentreDoor uses the actual subdivided wall pieces
  from FloorGeometry.BuildWalls, including shared interior partitions.
  Neither centring operation treats the entire continuous collinear
  validation run as the target segment.
- Candidate segments are ordered by distance from the original opening
  to the segment midpoint. A candidate must be large enough for the
  opening plus required clearance, and all usual FloorGeometry.Validate
  conditions must pass before a move is committed. Both functions
  return detached copies and leave their sources unchanged on failure.
  Centring uses the exact midpoint even if grid snapping is enabled.
- The selected-door and selected-window controls trigger a full floor
  commit only when the centre changes, so global undo/redo and
  persistence work without introducing redundant history entries.
- EditorLayoutSettings is an independent desktop-only JSON preference
  model under the user's LocalApplicationData/BuildingAdventure
  directory. It stores the window normal bounds/maximized state,
  expanded sidebar section flags, sidebar scroll offset, and view
  settings. Closing first performs the ordinary unsaved-work check;
  settings save only after closing is accepted.
- Startup bounds are validated and clamped to the available virtual
  desktop. The preferences file can be missing, malformed, or read-only
  without blocking building editing. No building geometry or saved
  document JSON schema changes are required.

## Generator suggestions, secret visibility and floor silhouettes (v0.12)

- BuildingGenerator.TrySuggest creates a detached, validated building
  candidate for only the selected floor indices. It generates starting
  rectangular footprints where the selected floor is empty, then
  attempts room additions on a 0.5-metre lattice using a seeded RNG.
  Proposed rooms are accepted one at a time only if whole-building
  validation succeeds. Existing stairs, rooms, doors and windows remain.
- TrySuggestWholeBuilding creates a fresh 1–4 floor candidate; the ground
  is generated first, subsequent floors inherit its footprint, and all
  floors get independently generated room layouts.
- Regeneration always works against CurrentDocument, never the previous
  preview. The editor displays green dashed suggested perimeters and
  new rooms on the active floor without editing the actual model.
  Users can switch existing floors to inspect proposals before Accept.
  Accept records a single complete-building undo step.
- SuggestionMemory stores complete candidate buildings, random seeds,
  floor selections and source fingerprints in separate AppData JSON.
  Remember/Remove/Clear persist immediately; Recall produces another
  preview. Applying an older candidate after the underlying building
  changed requires user confirmation. Stored entries are capped at 50.
- The first generator is deliberately geometry-first; architectural
  circulation, door placement, room-purpose constraints, procedural
  passages and sophisticated completion are future improvements.
- DoorDefinition has backward-compatible IsSecret/IsRevealed booleans.
  SecretDoorVisibility.PhysicalPlanForPlayer constructs a separate
  rendering copy omitting unrevealed secret door *openings*, so the wall
  becomes physically continuous in Player preview, not just visually
  missing a symbol. Editor and GM modes show marked secret door symbols.
  Player preview is not a security boundary or exported player map.
- Optional all-floor silhouettes show the perimeters of every *other*
  storey, labelled and dashed, without their interior walls. They
  complement rather than replace detailed adjacent floor references.

## Signed floor elevations, keyboard navigation and partial suggestions (v0.13)

- Buildings retain the ground floor at index 0. BasementFloors stores the
  closest basement at array index 0, floor index -1; lower basements have
  index -2, -3. UpperFloors retains positive floor indices. The public
  helper methods FloorIndexes, LowestFloorIndex, HighestFloorIndex, and
  HasFloor address the complete contiguous signed range.
- FloorCount is the total count of above/below-ground storeys. The current
  four-level cap includes both. The floor picker is ordered from lowest to
  highest, maps a displayed selection back to signed floor indices, and
  supports Page Up/Down without renumbering existing upper-floor links.
- TryAddBasement creates a detached snapshot with a new lowest basement
  copying the outline and wall thickness from the immediately higher floor.
  TryRemoveBottomBasement removes the deepest level and only staircases
  attached to it. Both are undoable actions in the desktop editor.
- Stairs always connect LowerFloorIndex to LowerFloorIndex+1, so a staircase
  at -1 joins basement 1 and ground floor. A ground-floor slab opening is
  therefore now possible; GetOpenings no longer assumes floor 0 cannot
  have a staircase void. Existing staircase floor indices remain stable.
- Old JSON documents without BasementFloors deserialize with an empty
  default collection. The existing schema version 2 covers multi-storey
  documents, including basements; schema version 1 remains ground-only.
- The room generator accepts any valid signed floor index; checkbox
  selections and candidate display work across basement/ground/upper
  floors. A cycling button makes multi-floor suggestions easier to inspect.
- The editor distinguishes proposed rooms by GUID and allows selecting
  one with a click. Accept selected suggested room runs whole-building
  validation on a detached copy, commits only that room to the active
  floor, and records one global undo snapshot. Any other proposed rooms
  remain non-destructive overlays. Whole-building proposals must still
  be applied atomically.

## Flexible floor deletion and generator reliability (v0.14)

- HasGroundFloor defaults to true for backwards-compatible JSON. In
  basement-only buildings it is false; root-level geometry is cleared and
  floor elevation indices span -N through -1 with no floor 0.
- BuildingStoreys.TryRemoveFloor deletes a chosen signed floor index.
  Every surviving floor above the target shifts down one elevation;
  basement-only structures are rebased to contiguous negative indices.
  The operation reconstructs floor collections in their correct storage
  order, preserving rooms, doors, windows and wall thickness. Flights
  whose two endpoints do not survive are removed, while other stair
  endpoints are remapped through the original-to-new floor index map.
  The complete rebuilt building is validated atomically before use.
- Add Floor on a basement-only building creates a new ground floor
  using the top basement's footprint and wall thickness. Old documents
  without HasGroundFloor remain fully compatible.
- Two staircase flights on opposite sides of the same intermediate level
  can share footprint coordinates. Their slab holes belong to distinct
  floor elevations, so this is valid geometry. FloorPlanSurface uses
  offset labels and cycles coincident stair selections on repeated
  clicks. MainWindow provides a list of flights touching the active
  level for unambiguous direct selection.
- BuildingGenerator.TrySuggest uses larger randomized candidate rooms
  first, then scans a 0.5m lattice with decreasing dimensions (down to
  1m × 1m) before declaring no fit. It rejects empty-room suggestions,
  preserves pre-existing geometry, and reports counts/skipped floors.
- The whole-building draft preview uses a separate read-only floor-plan
  window with a selector over all candidate elevations, including
  floors not yet present in the current building. This is separate
  from the actual model until Accept All.

## Connected layout suggestion passes (v0.15)

- GenerationOptions controls five independent candidate categories:
  Rooms, Corridors, Doors, Windows, and Stairs. Existing generator
  overloads stay available for room-only compatibility. The desktop
  reads the feature checkboxes and includes selected settings in the
  user's private UI preferences.
- BuildingConnectivity.TryAddCorridor proposes a straight central
  hallway across a selected floor's rectangular bounding region.
  Proposed halls are whole-building validated and cannot overwrite
  existing rooms or stairwell constraints. Random room proposals are
  constrained to share actual wall length with newly proposed corridors.
- BuildingConnectivity.ShareWall tests collinear edge overlap, not
  corner touching. AddDoors uses physical segments from
  FloorGeometry.BuildWalls; it prioritises an exterior entrance and
  hall-to-room connections, and each candidate is validated before
  accepting it into a detached proposal.
- AddWindows proposes up to two valid exterior openings per selected
  floor along existing room frontage. AddStairs considers adjacent
  selected floor pairs and validates the full staircase footprint,
  rotation, interior walls and slab opening before adding a flight.
- The surface renders differences between accepted and proposed
  doors/windows/stairs alongside new room outlines. Both on-screen
  previews and the saved-candidate list hold complete building
  snapshots; acceptance is explicitly user-driven and undoable.
- Completion remains intentionally conservative. A hall may not fit
  a partially drawn floor, and some rooms may remain without a door.
  The proposal description reports remaining rooms without openings.
  Irregular-corridor routing, guaranteed traversability, exits,
  architectural constraints and building-use grammars are still
  separate planned generator stages.

## Purpose-aware frontage partitioning (v0.16)

- RoomLayoutPlanner.FillHallFrontage recognises axis-aligned straight
  hallway rectangles. It calculates strips on both sides of the hall,
  divides each frontage into contiguous half-metre-aligned spans, and
  proposes full-depth rectangular rooms. Each candidate passes
  BuildingStoreys.Validate on a detached document; rejected candidates
  never modify existing rooms or the live editor.
- Random variations change the number and widths of frontage
  partitions. Unlike the earlier random scattering pass, a clear
  rectangular perimeter and hallway can be fully covered without
  leaving large arbitrary gaps. For complex polygons or obstructed
  strips, the existing geometry-checked fallback remains available.
- GenerationOptions.Purpose selects Residence, Inn, Shop, Dungeon or
  Mixed room-role vocabularies. The type selector considers floor
  elevation and room area, avoids repeating a room function when
  alternatives exist, and does not override user-authored names.
- RoomLayoutPlanner.UnassignedArea computes unassigned floor area,
  excluding rooms but not attempting to model furnishings or other
  unusable space. Generator descriptions include coverage percentage
  and unused square metres for each selected floor, so a user can
  assess each proposal before Accept.
- Building purpose is an option for the generator, not a building JSON
  schema change. Local UI preferences retain the selected purpose.

## Purpose-first residential frontage (v0.17)

- On a sufficiently large, clear rectangular residential ground floor, the
  frontage planner reserves one room of at least 23 square metres facing the
  hall before splitting the remaining available width. The other hall side
  is then laid out using the updated room list, so the large-space
  reservation is made once. No existing room is moved or overwritten.
- A room-use-specific priority order allocates important unique functions
  before repeating bedrooms or guest accommodation. The area thresholds
  still avoid making a bathroom as large as a living room, or naming an
  undersized rectangle a living room. Floor elevation influences selection.
- Proposed room names omit a numeric suffix until a duplicate name appears.
  User-authored names and room IDs remain unchanged.
- Existing whole-building validation, detached previews, undo and JSON
  document schemas are unmodified.
- Floor deletion must distinguish the root-level ground floor from the
  UpperFloors collection when promoting a floor across elevation zero.
  Room-only generation failures now distinguish lack of usable space from
  absent optional generator features.

## Current project boundaries

- BuildingAdventure.Core: polygon, room, shared-wall, door and JSON geometry operations.
- BuildingAdventure.Desktop: WPF viewport, interaction, and file commands.
- BuildingAdventure.Tests: a dependency-free executable geometry test harness.

## Design rules for later milestones

1. Keep the geometry representation separate from drawing and interaction.
2. Represent adjoining rooms with shared wall edges, not separate overlapping walls.
3. Preserve both manually authored and generated parts in the same document model.
4. Support per-object edit policies: locked, preferred and generated.
5. Reserve aligned stairs, shafts and vertical access before room partitioning.
6. Run explicit validation before accepting generated or manually edited layouts.
7. Generate deterministic candidate layouts given the same seed and constraints.
8. Never silently discard locked geometry when regenerating nearby rooms.
9. Keep a distinction between building exterior, usable interior, and room regions.
10. Store all geometry in world units; grid scale and export resolution belong to the view.

## Proposed next milestones

1. More capable topology editing: partition/extend walls, doors spanning wall
   fragments, openings, windows, resize tools and constrained room-edge moves.
2. Movable rooms, partial regeneration, and lock/preference states.
3. Floors, stairs and vertical constraints.
4. Building-type definitions and architectural adjacency rules.
5. Constraint-based room allocation and optimization.
6. Furnishing, secret features, player and GM visibility, and VTT exports.

## Current JSON format

The file is a UTF-8 JSON object. The model uses camelCase keys with schema
version 1, a human-readable name, perimeter points and optionally rooms and doors in metres.

Example:

    {
      "schemaVersion": 1,
      "name": "Corner Merchant Shop",
      "perimeter": [
        { "x": 0, "y": 0 },
        { "x": 10, "y": 0 },
        { "x": 12, "y": 4 },
        { "x": 10, "y": 9 },
        { "x": 0, "y": 9 }
      ]
    }

The current file extends the original version 1 document with optional arrays,
for example a room with an id, name, type and outline, and a door with an id,
centre and width. Documents saved with v0.1 that omit these arrays still load.

An empty perimeter is allowed for a blank document, but rooms or doors require
a closed perimeter. Saving a draft does not save its unclosed vertices; finish
each outline before saving. Version 1 does not yet support stories or furnishings. New fields
should be introduced with an explicit schema migration plan.
