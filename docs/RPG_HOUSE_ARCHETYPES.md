# Fantasy RPG residential map archetypes

The principal reference is **fantasy tabletop battle maps**, with real-world
and historical architecture informing plausibility rather than dictating
floorplans. Prefer a visually readable, explorable house to a rigid medieval
building typology.

## 1. Compact two-storey house — first implemented archetype

Inspired by the example two-floor *Empty House Robbery* battle map shared
in the design conversation. The useful features of that reference are
**functional zoning and concise circulation**, not exact geometric copying.

### Ground floor (social / work)
- **Short entrance lobby** between a **guest room** and **study**.
  Each has its own entrance from the lobby.
- **Living room** connects to the lobby and the **dining room**.
  It also contains the rear stair, so a whole separate stair hall is not
  imposed on the plan.
- **Kitchen** sits at the rear, near the dining room, with an optional
  back-garden door.
- The living room, dining room and kitchen form a navigable social core.
  No isolated storeroom blocks travel between living accommodation and
  the entrance.

### Upper floor (private / sleeping)
- **Three distinct bedrooms**, including a named **Master Bedroom**.
- **Bathroom** and **Sitting room**.
- **Narrow upper landing** allows bedrooms and bathroom to have private,
  independent doors. The stair enters the sitting room at the rear.
- No bedroom should be the only route into another bedroom.

### Global constraints
- **One aligned staircase** connects the two floor levels. Flight,
  upper-floor opening and landings must clear every internal wall.
- **Complete geometry validation** after rooms, stairs, doors, chimneys
  and optional windows. No disjoint room or zero-width corridor.
- **Compact circulation**: the ground foyer occupies only a small
  fraction of the footprint; upstairs circulation is a slim landing.
- A reproducible seed varies room proportions, mirror orientation,
  front/back direction, guest/study location and back-door choice.
- Standard two-floor rectangular footprints with roughly 8.2 m usable
  width and 8 m depth (orientation-independent) are supported in the
  initial implementation. Irregular outlines fall back to another
  generator with an explicit indication in the description.
- Generating a suggestion never mutates the accepted building.
  Regeneration preserves an existing staircase where it safely fits.
- The plan does not invent "Great halls" or mandatory oversized
  storerooms in a modest family house.

### Initial limitations
- Ground and upper floor perimeters must be identical rectangles.
- Three bedrooms and a bathroom are the starting RPG-friendly template,
  not a universal housing rule or exact recreation of the example image.
- The first implementation uses an L-free zoning skeleton, with the
  stair embedded in a living/sitting space. Future generators can use
  true L-shaped social areas, split-level/irregular houses and alternate
  stair footprints.
- When compact planning is geometrically impossible, the current
  generator retains its historical/corridor fallback instead of
  returning invalid stairs.

## Future archetypes (not implemented by this release)
1. **Cottage** — single storey, 1–2 bedrooms, common room and hearth,
   kitchen/service niche, one short entrance route.
2. **Merchant town house** — commercial rooms on ground floor, family
   accommodation upstairs, compact stairwell without intermingling
   customer and private paths.
3. **Small manor** — larger entertaining rooms, service wing,
   guest rooms and chambers, possibly a genuine Great hall.
4. **Inn/tavern** — public bar and dining below, guestrooms above,
   staff/service routes and at least two plausible escape routes
   where building size requires them.
5. **Farmhouse/workshop** — work and domestic rooms with direct
   service access and sensible external doors.

Archetype choices should control **room-role graphs first**, then place
those graphs within a perimeter. Cosmetic randomisation cannot substitute
for distinct architecture.
