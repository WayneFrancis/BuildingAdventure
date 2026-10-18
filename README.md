# BuildingAdventure

**BuildingAdventure** is a Windows editor for realistic tabletop RPG floor
plans. The goal is a hybrid architectural designer: generate a building from
requirements, draw one by hand, or combine manual rooms with procedural design.

## v0.41 — Physically accessible L-house stairs and opposite up/down arrows (development)

The accepted `example6(3).json` makes two stair problems visible:

- The renderer drew the arrow from the stored rotation on **both**
  floors, so **UP** and **DOWN** pointed the same way. The new
  floor-aware direction helper draws ascent along the stored axis on
  the lower floor and descent along its **reverse** upstairs.
  Stair rotation, saved JSON and user-authored placement are unchanged.
- The previous stair was centred at (2.5m, 0.7m), along an outer wall.
  Its 2.8m flight and two 1m landings were inside the valid room
  geometry, but the surrounding 1.6m-deep stair bay had **only 0.4m
  of rear access** past the flight. Merely checking that both landings
  fit inside a hall was insufficient to make either landing reachable.
- **New stepped-house stairs:** the default planner now allows wider
  end landing pockets, places the new flight farther from the outer
  wall, orients ascent inward toward the house, and reserves at least
  **1.1m continuous clear bypass** behind the entire flight and both
  landings, connecting their ends to the hallway on each storey.
  Room areas, all stair landings and exterior fit are revalidated.
- **Preserving existing stairs:** two-floor regeneration continues
  to respect an existing stair's ID, position and rotation. The
  stepped planner enlarges the surrounding access area where it can;
  it never silently moves or reverses a hand-placed or previously
  accepted staircase.
- **Tests:** five rotations of up/down arrows, exact 0.4m pinch in
  the uploaded example, newly generated rotated stair geometry,
  the full 1.1m bypass, and existing-stair regeneration.

To use the improved newly positioned stair, start with a blank
two-storey footprint **without an existing flight**, or intentionally
delete the old staircase and regenerate both floors. Simply running
Regenerate on an accepted old flight does not move it.

## v0.40 — RPG house layouts for six-corner L-shaped exteriors (development)

The uploaded `example6(1).json` is a two-storey house whose outer
perimeter has a 2m-wide step on one side, **88.75m²** on each level.
The earlier corridor-first generator allocated **68.125m² (77%)** on
*each floor* to one L-shaped hallway and left only a kitchen downstairs
and a bedroom upstairs. This was not a useful house, even though the
polygons and staircase passed geometric validation.

Two complementary fixes have been added:

- **Direct stepped-house archetype:** when both floors have a matching
  six-corner L-shaped/stepped perimeter, the compact RPG layout option
  attempts to plan both floors together *before* invoking the legacy
  corridor generator. It places a narrow stair passage, one usable
  room alongside the stairs, a kitchen/bedroom in the neighbouring
  wing, and two rooms beyond the short cross-passage.
- **Useful RPG rooms:** downstairs, kitchen, living, dining and study.
  Upstairs, three separately accessible bedrooms and a bathroom.
  The staircase remains aligned on both floors, with its full opening
  and two landings clear. Generated building occupancy covers the
  entire stepped perimeter, including its exterior notch.
- **Reproducible variation:** the seed changes corridor thickness,
  accommodation splits and mirrored/rotated configurations where
  stairs and the actual perimeter permit it.
- **Existing-stair preservation:** regenerating both selected floors
  does not move an accepted staircase. If a new blank two-storey
  L-house lacks stairs, the archetype attempts to position a valid
  shared flight before adding doors.
- **Fallback hallway correction:** for other staged layouts that
  still produce a 6-corner 50%+ L-shaped *interior hallway*, carve
  out functional rooms around the preserved stair instead of letting
  its side wing absorb the entire floor.
- **Generation quality threshold:** an irregular residential
  suggestion with only one non-hall room and 55%+ hallway area is
  rejected with a useful explanation rather than being silently
  presented as a successful floor plan.
- **Historical layout opt-out:** the new direct RPG archetype follows
  the existing **Compact RPG two-storey house** checkbox. Uncheck it
  to use the historical/legacy planning path instead.
- **Regression coverage:** recreated accepted `example6(1).json`
  geometry, stair clearance, door access, protected user rooms,
  whole-perimeter coverage, nine-seed regeneration of both floors,
  and creation of stairs in otherwise blank L-shaped houses.

This release deliberately targets **simple six-corner stepped outlines**.
Arbitrarily complex orthogonal footprints, courtyards and polygonal
wings require a more general constraint-based graph/layout planner;
they are not claimed to be solved by this archetype.

## v0.39 — Reclaim awkward stair wings on L-shaped generated halls (development)

The uploaded `example5.json` demonstrates a valid but poor layout:
an 11 × 10 m rectangular outer perimeter containing **42 m² of
L-shaped corridor on each floor** (38% of its 110 m² floor area).
The old hallway repair requires the entire stair flight and both
landings inside a straight, 2m spine; the side bay here cannot satisfy
that condition, so its oversized hallway was never reduced.

- **Stair-aware L-hall repair:** detect generated orthogonal halls
  composed of one full-length 2m corridor and one large rectangular
  perpendicular stair wing. Preserve a compact cap around the entire
  stair reservation and a 1.3m connecting approach, rather than
  retaining the entire deep wing as corridor.
- **Functional reclamation:** turn the spare rectangular pocket
  (at least 7.5 m²) into a **Utility** room on the ground floor
  or a **Bathroom** upstairs rather than another inexplicable
  storeroom or windowless bedroom.
- **Conservative validation:** preserve all doors, windows, other
  rooms, chimneys, the original staircase location and both landings.
  Reject the transformation if geometry, opening positions, cross-floor
  reservations or the needed connecting passage cannot validate.
- **Protected artwork:** only newly generated halls can be reclaimed.
  Existing manually authored rooms are never automatically reshaped,
  and applying this pass twice cannot repeatedly subdivide an already
  repaired stair wing.
- **Two generator paths:** run this pass during both whole-building
  and selected-floor generation before the final access-door pass.
- **Regression:** reproduce the real hallway, room partitions, all
  floor-door and window coordinates and connected staircase from
  `example5.json`; check both floors, surviving openings, the new
  room's own hall door, stairs and unchanged source document.
- **Scope:** this solves one class of wasteful *interior* nonrectangular
  hallway. The uploaded example's *outer building* is rectangular.
  Truly stepped, angled and asymmetrical exterior footprints still
  require continuing archetype- and footprint-aware improvements;
  this is not a universal irregular-layout generator.

## v0.38 — Door selection matches the visible symbol (development)

Clicking a rendered door's open leaf previously selected the room beneath
it. The Select tool recognised door hits only 11–12 screen pixels from its
centre marker, despite drawing an open swing into the room. Room vertices
also took priority over the door selection fallback, and proposed-room
overlays could intercept clicks on actual doors.

- Door hit-testing now covers the centre, the wall opening and the full
  **swinging leaf**, using a small pixel-based tolerance independent of zoom.
- The nearest valid door is selected where symbols are close; invalid or
  stale openings without a real supporting wall are ignored.
- In Select mode, existing doors take priority over room vertices and
  polygons. Double-clicking a door cannot accidentally insert a room
  corner into its supporting wall.
- Pending room suggestions no longer intercept a click on an existing
  door. Room selection still works outside the visible door symbol.
- Drag and resize handles for the selected door remain unchanged.
- Regression tests check the leaf, gap, centre, zoom levels, diagonal
  walls, overlapping selection priorities and misses in ordinary room
  space.

## v0.37 — Compact two-storey RPG house archetype (development)

The default two-floor residence suggestion in the desktop editor can now
use **Compact RPG two-storey house (preferred)**. This is the first
architectural archetype inspired chiefly by fantasy tabletop battle
maps, with historical floor plans used as plausibility references.

- **Ground floor:** a small **Entrance lobby** between a **Guest room**
  and **Study**, connecting to the **Living room** and **Dining room**.
  The **Kitchen** sits at the back, with a possible second exterior door.
- **First floor:** **three bedrooms** (including **Master Bedroom**),
  **Bathroom**, **Sitting room** and a narrow **Upper landing** for
  separate bedroom access. No bedroom is the only way to reach another.
- **Staircase:** one straight flight, aligned between the two floors,
  runs along the rear of the ground-floor living room and arrives in
  the upper sitting room. Both landings and the slab opening are
  checked against the complete partition layout.
- **No giant hallway:** the ground floor uses a small foyer and
  circulation through normal social rooms; only the upper level
  needs a narrow bedroom-access landing.
- **Regeneration:** seeded variants change room proportions, left/right
  and front/back orientation, guest/study position and back-door
  provision. Existing stair geometry is protected during regeneration,
  and a selected-floor proposal never mutates the accepted plan.
- **Safety/fallback:** initially supported on matching rectangular
  ground/first-floor outlines with sufficient space, not arbitrary
  notched or hand-drawn polygons. If the archetype cannot safely fit,
  the existing residential generator remains available, with the
  fallback stated in the whole-building suggestion.
- **Editor:** the new checkbox is enabled by default for two-storey
  Residence generation, takes priority over the historical hall-house
  option when appropriate, and is saved in UI settings. Uncheck it
  to retain the prior historical or classic generator behaviour.
- **Tests:** direct geometry, room-door connectivity, bedroom count,
  staircase landing protection, multiple seeds, whole-building
  suggestions and regeneration around an already accepted flight.

See [RPG house archetype design notes](docs/RPG_HOUSE_ARCHETYPES.md)
for the room-role graph, constraints and planned cottage, merchant,
manor, inn and farmhouse variants.

## v0.36 — Bedrooms in one-storey homes; common rooms in cottages (development)

One-storey residence suggestions must contain somewhere to sleep. The
previous historical house planner created a kitchen, pantry, screens
passage and a Great hall, sometimes a parlour, but **never a bedroom**.
It also used "Great hall" for ordinary small homes, which is
inappropriate for the building's scale.

- **Mandatory sleeping accommodation:** every accepted one-storey
  historical residence gets at least one named Bedroom with a private
  doorway directly to its communal living space. A sufficiently large
  footprint (85m²+ and appropriate dimensions) can generate two distinct
  bedrooms. Bedrooms must each be at least 8m², and door positions are
  checked against the complete floor plan.
- **Historically proportional domestic rooms:** footprints under 140m²
  use a **Common room** for everyday living rather than a lord's Great
  hall. The Great hall role is reserved for larger manor-scale
  residences.
- **Bedrooms take priority over parlours:** the high/private end of a
  one-storey home is dedicated to bedrooms. On multi-storey homes,
  the optional parlour remains possible because sleeping rooms
  normally occur on the upper storeys.
- **No misleading incomplete previews:** if a one-storey residential
  draft cannot fit even one bedroom, both whole-building suggestions
  and regeneration of blank/cleared floors report failure instead
  of offering a kitchen/pantry/common-room-only house.
- **Regression coverage:** small 8×6m, 9×7m and 12×9m residences
  over multiple seeds; private doors and bedroom area checks;
  a manor-scale 19×12m Great hall; one-storey accepted-building
  regeneration retaining beds; upper-storey staircase preservation.
- The editor's Generator / Suggestions explanation reflects the
  cottage/large-manor distinction. The hall-house checkbox continues
  to control the architectural style; legacy generation is preserved
  when unchecked.

## v0.35 — Meaningfully different room layouts on each regeneration (development)

Previously, Regenerate chose a new random seed but the medieval
hall-house planner did **not use that seed**. It always tried the same
first valid orientation and room configuration, producing the same
plan repeatedly with new room identifiers.

- **Seeded architectural choices:** regenerated residences now vary
  which end holds service rooms, hall orientation where supported,
  kitchen-to-storage partition position, pantry versus buttery,
  screens-passage width, service-bay depth, private parlour inclusion
  and parlour size. Every accepted option still passes whole-building
  geometry, stair and door validation.
- **Different room layouts, not different IDs:** the Regenerate button
  compares the proposed room roles and geometry with both the accepted
  building and any previous preview. It retries with fresh seeds up
  to 10 times rather than presenting an identical arrangement.
- **Honest failure on overconstrained footprints:** if a different
  valid room layout cannot be found around the retained perimeter,
  stairs and chosen features, the editor reports this rather than
  pretending a fresh identical suggestion is a new design.
- **Reproducibility:** a specified seed gives the same historical
  arrangement when the building constraints are unchanged. The
  unseeded direct planner remains compatible with its earlier
  deterministic behaviour.
- **Historical purpose preserved:** pantry and buttery are both
  service spaces, usually with a single internal door. A buttery
  does not automatically receive a living-room window.
- **Regression tests** exercise multiple seeds and actual
  replacement of an already accepted house, verifying changes to
  genuine room geometry, service-room roles and/or room count.
  Existing stair, hand-drawn-room and whole-building checks remain.

## v0.34 — A historical hall-house layout, not a central corridor (development)

The editor now offers **Medieval hall-house layout (residences)** under
Generator / Suggestions, **enabled by default** for new users and saved
with other generator settings. Uncheck it to retain the legacy central
corridor algorithm. Non-residential uses continue with their existing
layouts. Generating a proposal never overwrites the accepted plan.

The new architecture is a complete alternative floor partition rather
than another correction to storeroom placement:

- **Great hall:** a substantial common living and dining space instead
  of a full-length 2m modern corridor.
- **Screens passage:** a short cross-passage at the service end with
  the building's exterior entrance and a direct connection to the hall.
- **Service end:** a kitchen and separately enclosed pantry, with
  independent access from the passage or neighbouring kitchen.
  The pantry normally receives just one door.
- **High/private end:** a parlour where sufficient floor area remains;
  on compact houses or those with stair landings occupying that end,
  the great hall extends into this area instead.
- **Multi-storey preservation:** the existing stair reservations and
  landings are validated *before* accepting a hall-house replacement.
  Upper-floor rooms and their footprints are not changed. If the
  historical partition would cross a staircase or fail any door/shape
  check, retain the legacy plan and **say so in the suggestion**.
- **Irregular plans:** the orthogonal room zoning uses exact exterior
  corner coordinates (including 5ft/10ft-grid outlines) to build valid
  nonrectangular rooms around stepped/notched wings. Angled outlines
  fall back to legacy generation.
- **Direct small houses:** single-storey historic residences can be
  laid out directly from the perimeter without first constructing a
  modern corridor. The existing checked-floor and whole-building
  workflows remain available.
- **Editing support:** the room editor recognises Great hall, Parlour,
  Pantry and Buttery as editable types. Chimney placement prefers the
  Great hall where the historical archetype is present.
- **Tests:** rectangular and transposed footprints, the stepped
  `example2(2).json` outline and staircase landing positions,
  full-area coverage, service doors, preview immutability, legacy
  opt-out, and at least one valid two-storey hall-house suggestion.

This is an **English-style hall-house archetype**, not a claim that all
medieval buildings followed one universal plan. Historical guides and
examples informing the service/private-end layout:
[Looking at Buildings: The Medieval House](https://lookingatbuildings.org.uk/types/domestic/the-medieval-house.html),
[Historic England: The Hermitage, Heston](https://historicengland.org.uk/research/results/reports/7160/THEHERMITAGE17UPPERSUTTONLANELBHOUNSLOW-ALATE-MEDIEVALTIMBER-FRAMEDHALLHOUSEATHESTON),
[Early Dunster Project: Cross Passage House](https://sanhs.org/early-dunster-project/).

**Scope:** v0.34 changes the newly generated *ground-floor* architecture.
First-floor sleeping chambers still use the existing upper-floor
subdivision method. Distinct inn, merchant townhouse, workshop and
fortified-manor templates are future architectural variants.

## v0.33 — Final-stage circulation and misplaced storage corrections (development)

The uploaded `example2(2).json` showed that v0.32's corridor fix was
not reliably applied to a **finished generated building**. Both ~14 m²
storerooms remained between the main hallway and the dining room/bedroom.
A pair of doors existed only *between each storeroom and the room behind
it*, so living spaces had no separate route from the hallway.

- **Apply accessibility repair at the end of generation.** Preliminary
  hallway and stairwell stages may add doors before the final storage
  partitions are in place. The finalizer now removes only those doors
  attached to *newly generated* obstructing stores, reshapes a narrow
  access spur into the hallway, and replans doors when the complete
  staircase geometry is available.
- **Prefer usable principal rooms over storage barricades.** On
  generated plans, oversized stores that separate a dining room or
  bedroom from the hall without serving any kitchen/workshop can be
  absorbed into those rooms after restoring independent access. Choose
  the room behind the store's **long wall**, not a sideways neighbour
  that grew during an earlier merge. Compact, purpose-built entrance
  stores and kitchen/workshop storage are retained.
- **Door-network validation.** Before returning a generated ground
  floor, verify that a bedroom or other ordinary room isn't reachable
  *only through storage*. This is stronger than ensuring that every
  room has at least one doorway. A proposal with an unresolved service
  barrier is rejected rather than presented as usable.
- **Regression on the finished JSON arrangement.** Tests reproduce
  the two-story stepped layout, the six existing ground-floor doors,
  bedroom/dining storage obstruction, and stair geometry. They verify
  both door replacement and the optional redistribution of badly
  positioned storage into directly accessible L-shaped rooms.
- **Limit:** the traditional long 2 m-wide central corridor is still
  the underlying building-generation template. Replacing that template
  with historically inspired hall houses, townhouses, inns and other
  archetypes requires a separate architectural layout strategy.

## v0.32 — Historically informed service rooms and independent access (development)

The uploaded `example2(1).json` exposed a circulation problem: two shallow
storerooms separated a ground-floor dining room and bedroom from the hallway,
and the only doors into those rooms went **through** the stores. Storing
supplies in a bedroom's sole access route is a poor default, even though
the floor geometry is technically valid.

- **No compulsory walk-through storerooms:** when a new rectangular shallow
  store is the only intervening room between a main hallway and a substantive
  living, dining or sleeping room, the generator can reclaim a 1.3 m-wide
  edge strip from that store and merge it into the hallway as a short
  access spur. Both the main room and storage room can then receive
  their own access door without crossing the other.
- **Service-room access preference:** prioritize a direct door from the
  kitchen, workshop, brewery or scullery if adjoining; otherwise prefer
  the hallway. Avoid bedroom access for general stores unless no other
  valid connection is possible. Adding a kitchen/service connection
  must not create an unnecessary second door in a store that already
  has a usable entrance.
- **Keep useful storage:** service rooms at least 8 m² and 1.6 m wide
  are not indiscriminately absorbed into an L-shaped bedroom or dining
  room. Very small generated fragments can still be merged.
- **Non-destructive:** the generator changes only newly proposed
  storage-room/hallway geometry and validates every attempt against
  walls, doors, stairs, landings, and all floors. Previously authored
  geometry remains untouched.
- **New regressions:** recreate the precise stepped ground and upper
  footprints and stacked stair from `example2(1).json`; verify
  direct hallway access for dining and bedroom, one storeroom door per
  store, preserved staircase, and single-door access from an adjoining
  kitchen in another scenario.

Historic design references: English medieval hall houses often grouped
pantry and buttery at the service end of a hall, reached via a short
screens passage, with family rooms at the more private end.
This inspires sensible adjacency choices rather than imposing one
universal plan on all medieval buildings:
- [Historic England: Tyneham medieval manor house service bay](https://historicengland.org.uk/listing/the-list/list-entry/1491507)
- [BBC History: The Great Hall and its screens passage](https://bbc.com.im/history/british/middle_ages/great_hall_01.shtml)
- [Sarah Pearson, *The Provision of Services in Medieval Houses in Kent* (2012)](https://doi.org/10.1179/0305547712Z.0000000004) cautions that service layouts were more varied than the familiar buttery/pantry stereotype.

## v0.31 — Entrance storage rooms rather than oversized hallways (development)

The user's `example2.json` is a two-storey stepped residence with an
exterior entrance at (16.764, 3.048). Both sides of that doorway were
unnecessarily included in a 43.8 m² L-shaped hallway. The extra strip is
1.786 m deep and wide enough for two small storage rooms.

- On a **newly generated ground floor** with an oversized orthogonal
  entrance wing, reserve a clear 2 m-wide path from the exterior door to
  the existing main corridor, then turn the pockets on its left and right
  into named **Storage** rooms with interior access doors.
- Keep the existing exterior entrance and its opening, room outlines,
  floor perimeter, staircase flights, upper-floor stairwell openings and
  landing clearances unchanged. Storage pocket depth may be less than the
  minimum for bedrooms; 1.786 m is useful for a closet or storeroom.
- Geometry is based on the original world coordinates; both horizontal
  and vertical exterior wall orientations work. A narrow/noisy or
  hand-edited entrance is left alone if the partition does not validate.
- Applies to generated whole buildings and selected blank floors,
  without modifying previously drawn rooms. The existing broad hallway
  reclamation follows this more targeted pass.
- Regression recreates `example2.json`'s entry, rooms, stairs and
  different upper-floor perimeter. Verifies two ~6.38 m² stores, the
  ~31 m² remaining hall, two new interior doors, and safe rotational
  placement.

## v0.30 — Recover useful rooms from oversized hallways (development)

The uploaded `example(1).json` showed a 50.2 m² ground-floor hallway in an
88.3 m² house (56.8%). Its complete area was technically assigned, but
long merged `Passage` regions had swallowed much of the usable building.

- **Keep the circulation spine compact.** On generated orthogonal floors,
  a hallway exceeding both 28 m² and 36% of the usable floor may be
  repartitioned into a continuous 2 m-wide corridor and useful adjoining
  rooms. Existing external outlines, hand-drawn rooms, stairs and their
  1 m landings remain protected. Imperial coordinates are preserved exactly.
- **Recover narrow service wings.** An elongated 2 m-deep region is divided
  into realistic utility-sized rooms instead of becoming another long passage.
  New rooms receive interior doors when door generation is enabled.
- **Wall-hugging stairs.** Generated flights now meet a common hallway
  wall instead of floating 15 cm away from it, while retaining a walking
  lane on the other side. The generator avoids creating doors over a flight
  or its end landings.
- **Applies to checked-floor and whole-building generation.** Hand-drawn
  and pre-existing hallways are never automatically resized.
- **Regression:** recreates the uploaded two-storey stepped footprint and
  overlarge 50.2 m² hall; tests reclaiming three rooms, staircase and
  upper-floor preservation, and the staircase's shared-wall alignment.

## v0.29 — Universal selection, fireplace deletion and L-shaped generated rooms (development)

The example `example.json` shows a deliberately hand-shaped six-vertex
**Room A** adjoining the nearly rectangular **Room B** along a slightly
angled common wall. Such nonrectangular rooms are valid and are
preserved by editing and JSON round-tripping. Automatic generation is
no longer restricted to keeping every residual rectangle separate.

- **Click to select fireplaces or chimney flues** on any affected level.
  Both fireplace faces on a shared chimney select the same stack.
  An orange border/fill identifies the selected masonry. A new
  **Fireplaces & Chimneys** panel provides a one-click delete command.
  Deleting a stack removes its fireplace(s) and all upper/lower flues
  together, preserving the unrelated rooms, doors, windows and stairs.
  Removal is validated and recorded in global Undo/Redo.
- **One Delete command for the selected item** (keyboard Delete,
  Edit → Delete selected item, or toolbar Delete Selected):
  fireplace/chimney, staircase, window, door, selected vertex, or
  selected room. Selecting a whole room deletes it; selecting one
  of its corners deletes the corner. Explicit per-type Delete buttons
  continue working. If a deleted room contained a fireplace, its
  orphaned chimney is removed in the same undoable edit.
- **Strong room selection feedback:** selected room fill and a thick
  blue/white wall outline are overlaid *after* all room-wall drawing,
  with a visible `SELECTED: Room name` label. The toolbar also displays
  the exact current selection so the effect of Delete is clear.
- **Avoid redundant passages:** the generator attempts to join new
  narrow `Passage` rectangles to their adjacent main hallway before
  creating unrelated circulation spaces. Where a small generated
  service fragment cannot be kept usefully on its own, it can be
  joined to an adjacent generated room. The existing robust
  polygon-union algorithm retains concave/L-shaped outlines, the
  larger room's role/name and legitimate external doors, but removes
  any doorway left on the dissolved partition. Every result is
  validated on the full multi-storey building, and **existing user
  rooms remain unchanged**.
- **Multi-floor generation preserved:** passages are consolidated
  only *after* temporary staircase enclosures have been merged into
  the hallway; this avoids disrupting the stairwell reservation
  algorithm.
- Added tests for passage consolidation, generating nonrectangular
  bedroom/service-wing unions, preserving hand-drawn L-shaped room
  outlines, the exact six-corner Room A / angled boundary of
  `example.json`, and validated multi-floor chimney stack deletion.

## v0.28 — Proportion-aware rooms and slim L-wing circulation (development)

The user-supplied `L.json` revealed a ground-floor 25-foot-long,
approximately 5-foot-10-inch-wide bedroom (7.62 × 1.786m). The
hall-frontage planner correctly rejected the shallow strip as a normal
2m-deep room, but the residual filler later placed a 13.6m² rectangle
and named it Bedroom based on area alone.

- **Absorb slim, elongated circulation wings before room allocation.**
  The existing exterior-to-hallway merger now handles clear strips up
  to 2.0m deep, provided the exposed run is at least 3.5m long and
  its length/depth ratio is at least 2.5. It runs immediately after the
  main hallway-frontage subdivision, *before* residual rooms can claim
  the narrow wing. The corridor becomes a legitimate stepped hallway
  rather than a fabricated bedroom; no user-authored room is reshaped.
- **Room roles respect minimum usable dimensions.** A long narrow
  rectangle below 2.35m wide with aspect >=2.8 is identified as
  Passage when it cannot be joined into an existing hallway. A sleeping
  room narrower than 2.5m is instead assigned a storage/closet role,
  even when its total square metres are bedroom-sized. Other building
  functions retain their existing naming priorities.
- **Stairs remain protected.** The corridor expansion is skipped while
  temporary stairwell reservations are present, preserving the
  whole-building planner's required rectangular stair-bay geometry.
- **Regression tests:** reconstruct the exact L.json perimeter and
  ground-floor room dividers, verify its 7.62 × 1.786m gap joins hallway
  circulation, ensure acceptable sleeping-room proportions, and
  generate multiple room drafts from the original L-shaped two-storey
  footprints with the first floor smaller than ground.

## v0.27 — Shared staircase planning for unequal empty storeys (development)

Previously, **Suggest on checked floors** placed an independently centred
hallway on each unallocated storey, then tried to install a staircase.
Even small first-floor perimeter reductions could shift the two hallways
apart, leaving no common passage wide/long enough for a 2.8m stair
flight and 1m landings, despite abundant empty floor area. Generation
would fail with a misleading request to regenerate the same empty floors.

The generator now jointly lays out both empty adjoining storeys:
- Computes their common overlapping footprint, searches aligned
  two-metre corridor bands in both orientations, with several offsets,
  and tests each hall against its own actual exterior outline.
- Where possible, extends each floor's hallway along that floor's own
  full length, so an inset first floor need not shrink the ground-floor
  corridor. It also tries a common-length fallback on irregular plans.
- Places a real staircase and validates its entire flight, two landings
  and upper opening **before** generating any other room partitions.
- Keeps original hand-drawn perimeters unchanged, and falls back to
  the existing furnished-floor workflow when rooms already exist.
- Reports more specific reasons when there genuinely is no overlapping
  space suitable for a staircase.

Regressions cover multiple upper-floor inset/offset positions on
metric, 5-foot and 10-foot grids, plus standalone paired hallway/stair
reservation. Both **Suggest** and **Regenerate** use this common
building generator path.

## v0.26 — Upper-floor grid snapping and resuggesting edited outlines (development)

- **Perimeter corners now use the displayed map grid:** dragging corners
  and moving walls formerly snapped in fixed 0.5m increments even on a
  5/10-foot tabletop map. They now use 0.5m, 1.524m, or 3.048m
  consistently with the selected mode. A newly inserted corner on a
  straight wall also snaps along that wall. Shift still bypasses snapping.
  Explicit corner-to-corner and wall snapping remain higher priority
  when near existing geometry.
- **Reserve stairs before room subdivision:** when two adjacent checked
  existing floors are requested with rooms/corridors/stairs, first
  attempt corridors on both floors and reserve their connecting flight
  before assigning new rooms. The old rooms-first order often used up
  every suitable landing space, then rejected the entire suggestion.
- **Offset perimeters:** an upper storey may have a different footprint.
  The staircase search additionally tries the intersection of its hall
  extents with the lower hall, validating the complete flight/landings
  against the actual geometry on both levels.
- **Step-shaped floorplans:** if a rectangular full-width corridor
  cannot fit a notched perimeter, find shorter axis-aligned hall spans
  between actual exterior vertices. Room candidates are then planned
  around that accessible hall rather than declaring the whole floor
  unusable.
- **No silent failures:** Suggest and Regenerate now display the exact
  reason when a proposed layout cannot be made, both in a dialog and
  in the generator panel. If a requested floor has substantial
  unallocated space but receives no rooms, that is explicitly
  reported instead of returning a misleading hallway-only draft.
- Added regressions for 5/10-foot corner move precision, regenerating
  two checked storeys after shrinking the upper perimeter, reserving
  stairs before room filling, and partial corridors inside notched
  upper-floor outlines.

## v0.25 — LShape2 uploaded example, 2.5-foot hall strip and checked-floor stair access (development)

The supplied `LShape2.json` (5-foot grid, two floors) exposed a
specific gap: an L-wing's external boundary at y=4.572m comes within
0.762m of the hall at y=5.334m between x=13.716m and x=21.336m.
That leaves 5.80644m² unassigned **on each floor** even though the
hallway is otherwise connected to all the rooms.

- **Allocate thin corridor-side strips:** when an external horizontal or
  vertical wall lies within 1.25m of a rectangular hall, the generator
  can add the valid intervening strip to the hall outline as an L-shaped
  open circulation alcove. Existing room outlines and doors are
  preserved and the complete building is validated on a draft copy.
  This pass also works on already accepted rooms when using *Suggest on
  checked floors*; it does not depend on removing rooms first.
- **Fit stairs into furnished hallways:** the previous stair pass skipped
  any corridor lacking a separately reserved stairwell. The new pass
  attempts a 0.9m-wide, 2.8m-long flight and 1m landings at both ends
  alongside an existing hall, and validates the flight, both landings,
  upstairs opening and nearby doors on **both** connected floors. It
  maintains at least 0.8m of corridor bypass space. When no safe
  flight fits, a requested multi-floor suggestion reports why it
  cannot connect the floors instead of succeeding silently.
- **Newly created floor checkboxes are selected by default**, not only
  the ground floor. Clicking *Suggest on checked floors* or *Regenerate
  checked floors* with two adjacent levels selected automatically
  enables stair generation, as *Suggest entire building* already did.
  Users can still manually uncheck individual floors for incremental
  editing; to connect floors, both adjoining levels must be selected.
- Added regression coverage reconstructing the exact LShape2 dimensions,
  existing room/door/window distribution, downstairs kitchen fireplace,
  both hallway gaps and the proposed connecting stair and landing
  geometry on the preserved plan.

## v0.24 — Complete two-storey access, hallway seams and fireplace glazing clearances (development)

- **Multi-floor drafts always include staircase access:** the user-facing
  *Suggest entire building* action now enables Stairs automatically for a
  2–4-floor draft, regardless of an earlier saved unchecked preference.
  Stairs default to on in new generator settings. If stairs are requested
  but not every adjacent floor can be connected, the generator reports a
  failure rather than quietly returning a disconnected building.
  Existing-floor generation still respects the manually selected feature
  toggles.
- **No hall end-cap strips:** central hallways preserve their exact
  exterior endpoints (including non-metric and 5/10-foot-grid dimensions)
  rather than rounding each end inward to half-metre cells.
- **Open, assigned stair alcove:** temporary staircase reservation bays are
  merged into a single hallway outline. This creates a real open L/T-shaped
  circulation area around the stairs without a fake partition or floating
  doorway, and does not count the staircase bay as unassigned floor space.
  Geometry validation is repeated after the merge.
- **Full fireplace-to-window clearance:** glazing checks now use the actual
  *opening width* projected along the exterior wall and *both* fireplace
  faces' polygon footprints, with 0.2m clearance. Windows are generated
  **after** fireplaces/chimneys, not before. The same whole-span rule
  applies in building validation and when creating new windows.
- **No long label covering a window:** the floor drawing uses compact
  symbols *FP* (fireplace) and *FL* (flue), centred within the masonry
  footprint. Shared kitchen/living hearth faces each show an FP.
- **Regression tests:** two-storey stair connectivity for metric/5ft/10ft,
  hall and stair alcove allocation, exact hall endpoints on imperial
  perimeter coordinates, and fireplace overlap at the outer end of a
  window opening, not only window-centre proximity.

## v0.23 — Safe circulation, kitchen service access, shared hearths and gap completion (development)

- **Safe upper-floor doors:** the automatic doorway pass can add an
  exterior entry only at ground level. Upper floors receive internal
  doors, avoiding bedrooms that open directly over a drop.
- **Every reachable room needs a route in:** a room's existing exterior
  door no longer suppresses its internal doorway to the hall. Internal
  room-to-room connections are preferred when an actual shared wall
  exists. Kitchen-to-dining, kitchen-to-pantry/storeroom, and
  kitchen-to-living connections receive a second pass, which may insert
  another door even when those rooms already have hall access.
- **Smarter culinary placement:** when a room has a genuine partition
  shared with the kitchen, the planner prioritises a dining room or
  pantry if the building still needs one and the room dimensions fit.
- **Vertical partition awareness:** where possible, upstairs bedroom
  dividing lines inherit actual partition positions from the preceding
  floor, instead of being independently randomised. This improves
  chimney adjacency and more believable bearing-wall alignment.
  The generator does not yet include structural load calculations.
- **Shared kitchen/living fireplaces:** a shared partition can support
  two facing fireplaces attached to one chimney definition. Both
  hearth faces are saved in JSON, with their two ground-floor room IDs
  and positions; the upstairs footprint is labelled **FLUE**, never a
  fireplace in the bedroom above. When a shared fireplace is
  impossible, the generator chooses a suitable individual hearth or
  leaves the feature out. Newly placed chimney stacks must remain
  adjacent to a wall on each covered upper level.
- **Filling actual L-wings and drawn outlines:** a secondary orthogonal
  floor-space pass decomposes still-empty accessible pockets and tries
  larger rooms first, without redrawing the user's perimeter. Long
  pockets are divided into plausible room-sized spans. Another pass
  closes thin (under 1.25m) external gaps by stretching only newly
  generated rectangular rooms; it never moves accepted manual rooms.
  Tiny inaccessible slivers and nonorthogonal pockets can still remain.
- **Two distinct suggestion workflows:**
  * **Suggest on checked floors** adds candidates to available room space
    without deleting anything already accepted.
  * **Regenerate checked floors** starts a fresh *replacement preview*,
    clearing rooms/doors/windows only on the selected levels in the
    proposed copy. Existing walls' perimeter, other floors, stairs and
    chimney flues are retained. Stale room-specific hearth links become
    flue-only when those hearth rooms are regenerated. Nothing changes
    until **Accept All**, which shows an explicit replacement warning.
    Undo restores the earlier design.
- Regression tests cover orphaned exterior upstairs doors, the living
  room's hall access, adjoining kitchen service doors, multi-floor
  partitions, two-sided hearth serialization, repeated regeneration,
  orthogonal L-wing filling and no alteration of hand-drawn rooms.

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
