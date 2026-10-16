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
- A clockwise or counterclockwise ordered list of vertices defines the boundary.
- All geometric constraints run in the renderer-independent core library.

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
