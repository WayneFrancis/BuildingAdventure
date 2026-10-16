# BuildingAdventure architecture

## Product direction

BuildingAdventure is a tabletop RPG building designer which combines fully
automatic layouts with precise manual editing. Its design goals are realistic
buildings with one to four stories and support for homes, shops, inns,
workshops, warehouses, mixed-use occupancy, and optionally hidden rooms.

The first milestone implemented a perimeter editor. The second milestone adds
manual polygonal rooms, shared-wall rendering, simple door openings and
validation. Procedural generation remains **not implemented yet**.

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
  and resets the local undo stack for the new floor. Building metadata and
  stair definitions are held by the main window.
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
- Stair creation, movement and property edits are not yet in the global
  undo/redo history. Future work should unify them with floor-edit commands.

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
