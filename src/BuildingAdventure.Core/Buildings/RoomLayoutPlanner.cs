using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Building purpose influences useful room roles; it never overrides
/// geometry or the user's existing room definitions.
/// </summary>
public enum BuildingUse
{
    Residence,
    Inn,
    Shop,
    Dungeon,
    Mixed
}

/// <summary>
/// Partition complete strips on either side of a straight hallway instead
/// of scattering small random rectangles. All candidates are individually
/// validated and only newly proposed rooms are changed.
/// </summary>
public static class RoomLayoutPlanner
{
    public static int FillHallFrontage(BuildingDocument source, int floorIndex,
        Guid hallId, BuildingUse use, Random random,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Id == hallId);
        if (hall is null || hall.Outline.Count != 4 ||
            floor.Perimeter.Count < 4) return 0;

        const double eps = 1e-5;
        var x = hall.Outline.Select(p => p.X).ToArray();
        var y = hall.Outline.Select(p => p.Y).ToArray();
        double hallLeft = x.Min(), hallRight = x.Max();
        double hallTop = y.Min(), hallBottom = y.Max();
        bool horizontal = hallRight - hallLeft > hallBottom - hallTop;

        // A corridor placed diagonally or inside a concave shape must not
        // force rectangular rooms through an exterior corner. Candidates
        // below are still validated, but only axis-aligned hallways qualify.
        bool axisAligned = hall.Outline.All(p =>
            (Math.Abs(p.X - hallLeft) < eps || Math.Abs(p.X - hallRight) < eps) &&
            (Math.Abs(p.Y - hallTop) < eps || Math.Abs(p.Y - hallBottom) < eps));
        if (!axisAligned) return 0;

        double minX = floor.Perimeter.Min(p => p.X);
        double maxX = floor.Perimeter.Max(p => p.X);
        double minY = floor.Perimeter.Min(p => p.Y);
        double maxY = floor.Perimeter.Max(p => p.Y);
        double lengthLow = horizontal ? hallLeft : hallTop;
        double lengthHigh = horizontal ? hallRight : hallBottom;
        if (lengthHigh - lengthLow < 2) return 0;

        int added = 0;
        // Keep the exact external wall coordinates. In 5/10-foot mode
        // those edges seldom coincide with the half-metre subdivision
        // lattice. Rounding them inwards left visible strips along the
        // right and bottom exterior walls.
        var bands = horizontal
            ? new (double Near, double Far)[]
            {
                (hallTop, minY), (hallBottom, maxY)
            }
            : new (double Near, double Far)[]
            {
                (hallLeft, minX), (hallRight, maxX)
            };

        foreach (var (near, far) in bands)
        {
            double depthMetres = Math.Abs(near - far);
            int depth = (int)Math.Round(depthMetres * 2);
            if (depthMetres < 2) continue; // avoid rooms under 2m deep

            // Existing rooms (including reserved stairwells) occupy frontage.
            // Split the free portions at those boundaries rather than
            // proposing one room that overlaps the stairwell and abandoning
            // the entire remainder of the hallway side.
            var free = new List<(double Start, double End)> { (lengthLow, lengthHigh) };
            double sideLow = Math.Min(near, far), sideHigh = Math.Max(near, far);
            foreach (var existing in BuildingStoreys.GetFloor(updated, floorIndex).Rooms)
            {
                if (existing.Id == hallId) continue;
                double crossMin = horizontal
                    ? existing.Outline.Min(p => p.Y) : existing.Outline.Min(p => p.X);
                double crossMax = horizontal
                    ? existing.Outline.Max(p => p.Y) : existing.Outline.Max(p => p.X);
                if (crossMin >= sideHigh - 1e-6 ||
                    crossMax <= sideLow + 1e-6) continue;

                double occupiedStart = horizontal
                    ? existing.Outline.Min(p => p.X)
                    : existing.Outline.Min(p => p.Y);
                double occupiedEnd = horizontal
                    ? existing.Outline.Max(p => p.X)
                    : existing.Outline.Max(p => p.Y);
                var remaining = new List<(double Start, double End)>();
                foreach (var (start, finish) in free)
                {
                    if (occupiedEnd <= start || occupiedStart >= finish)
                    {
                        remaining.Add((start, finish));
                        continue;
                    }
                    if (occupiedStart > start)
                        remaining.Add((start, occupiedStart));
                    if (occupiedEnd < finish)
                        remaining.Add((occupiedEnd, finish));
                }
                free = remaining;
            }
            foreach (var (start, end) in free)
            {
                double length = end - start;
                if (length < 2) continue;
                int cells = Math.Max(1, (int)Math.Round(length * 2));
                int target = Math.Clamp((int)Math.Round(cells / 9.0) +
                    random.Next(-1, 2), cells >= 16 ? 2 : 1, Math.Max(1, cells / 5));
                var widths = ChooseFrontageWidths(cells, depth, target,
                    use, floorIndex,
                    BuildingStoreys.GetFloor(updated, floorIndex).Rooms, random);
                // Reuse lower-floor divider positions when the rooms occupy
                // the same corridor-side band. This gives bedrooms upstairs
                // sensible walls above living/kitchen partitions below.
                var partitions = new List<double>();
                if (BuildingStoreys.HasFloor(updated, floorIndex - 1))
                {
                    var below = BuildingStoreys.GetFloor(updated, floorIndex - 1);
                    foreach (var lowerRoom in below.Rooms.Where(r =>
                        r.Type is not ("Hallway" or "Stairwell")))
                    {
                        double lo = horizontal
                            ? lowerRoom.Outline.Min(p => p.Y)
                            : lowerRoom.Outline.Min(p => p.X);
                        double hi = horizontal
                            ? lowerRoom.Outline.Max(p => p.Y)
                            : lowerRoom.Outline.Max(p => p.X);
                        if (lo >= sideHigh - 0.1 || hi <= sideLow + 0.1)
                            continue;
                        double axisLo = horizontal
                            ? lowerRoom.Outline.Min(p => p.X)
                            : lowerRoom.Outline.Min(p => p.Y);
                        double axisHi = horizontal
                            ? lowerRoom.Outline.Max(p => p.X)
                            : lowerRoom.Outline.Max(p => p.Y);
                        partitions.Add(axisLo);
                        partitions.Add(axisHi);
                    }
                }
                var boundaries = new List<double> { start };
                var aligned = partitions.Where(p => p > start + 1.25 &&
                    p < end - 1.25).Distinct().OrderBy(p => p).ToList();
                foreach (double cut in aligned)
                    if (cut - boundaries[^1] >= 1.25 &&
                        end - cut >= 1.25) boundaries.Add(cut);
                if (boundaries.Count == 1)
                {
                    double position = start;
                    foreach (int width in widths.Take(widths.Count - 1))
                    {
                        position = Math.Min(end, position + width / 2.0);
                        if (position - boundaries[^1] >= 1.0 && end - position >= 1.0)
                            boundaries.Add(position);
                    }
                }
                boundaries.Add(end);
                for (int part = 0; part < boundaries.Count - 1; part++)
                {
                    double position = boundaries[part];
                    double boundary = boundaries[part + 1];
                    if (boundary - position < 1) continue;
                    double from = Math.Min(near, far);
                    double to = Math.Max(near, far);
                    List<Point2> outline = horizontal
                        ? Rectangle(position, from, boundary, to)
                        : Rectangle(from, position, to, boundary);
                    double area = PolygonGeometry.Area(outline);
                    var current = BuildingStoreys.GetFloor(updated, floorIndex);
                    string type = ChooseRoomTypeForOutline(use, floorIndex, area,
                        current.Rooms, outline, random);
                    var candidate = BuildingStoreys.Copy(updated);
                    var edited = BuildingStoreys.GetFloor(candidate, floorIndex);
                    edited.Rooms.Add(new RoomDefinition
                    {
                        Name = NextRoomName(type, edited.Rooms),
                        Type = type,
                        Outline = outline
                    });
                    BuildingStoreys.SetFloor(candidate, floorIndex, edited);
                    if (BuildingStoreys.Validate(candidate, out _))
                    {
                        updated = candidate;
                        added++;
                    }
                }
            }
        }
        return added;
    }

    /// <summary>
    /// Fill orthogonal pockets on irregular or hand-drawn outlines using
    /// the actual vertices of exterior walls and existing partitions. This
    /// is a second pass after hallway-frontage planning; it preserves every
    /// existing room and never crosses a staircase/landing. It deliberately
    /// leaves tiny unusable slivers unassigned rather than drawing fake rooms.
    /// </summary>
    public static int FillUnassignedRegions(BuildingDocument source,
        int floorIndex, BuildingUse use, Random random, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        int added = 0;
        for (int iteration = 0; iteration < 16; iteration++)
        {
            var floor = BuildingStoreys.GetFloor(updated, floorIndex);
            if (floor.Perimeter.Count < 3) break;
            var all = floor.Rooms.SelectMany(r => r.Outline)
                .Concat(floor.Perimeter).ToList();
            var xs = SplitLongSpans(all.Select(p => p.X).Distinct()
                .OrderBy(x => x).ToArray());
            var ys = SplitLongSpans(all.Select(p => p.Y).Distinct()
                .OrderBy(y => y).ToArray());
            if (xs.Length > 32 || ys.Length > 32) break;

            bool[,] empty = new bool[xs.Length - 1, ys.Length - 1];
            for (int x = 0; x < xs.Length - 1; x++)
            for (int y = 0; y < ys.Length - 1; y++)
            {
                var middle = new Point2((xs[x] + xs[x+1]) / 2,
                    (ys[y] + ys[y+1]) / 2);
                empty[x, y] = PolygonGeometry.ContainsPoint(floor.Perimeter, middle) &&
                    !floor.Rooms.Any(r => PolygonGeometry.ContainsPoint(r.Outline, middle));
            }

            var proposals = new List<(double Area, List<Point2> Outline)>();
            for (int y = 0; y < ys.Length - 1; y++)
            for (int x = 0; x < xs.Length - 1; x++)
            {
                if (!empty[x, y]) continue;
                for (int right = x + 1; right < xs.Length; right++)
                {
                    if (!empty[right - 1, y]) break;
                    for (int bottom = y + 1; bottom < ys.Length; bottom++)
                    {
                        bool full = true;
                        for (int col = x; col < right && full; col++)
                            if (!empty[col, bottom - 1]) full = false;
                        if (!full) break;
                        double width = xs[right] - xs[x];
                        double height = ys[bottom] - ys[y];
                        double area = width * height;
                        if (width < 1.25 || height < 1.25 ||
                            area < 3 || area > 65) continue;
                        var shape = Rectangle(xs[x], ys[y], xs[right], ys[bottom]);
                        if (floor.Rooms.Count > 0 &&
                            !floor.Rooms.Any(r => BuildingConnectivity.ShareWall(
                                r.Outline, shape, 0.9)))
                            continue;
                        if (updated.Staircases.Any(s =>
                            (s.LowerFloorIndex == floorIndex ||
                             s.LowerFloorIndex + 1 == floorIndex) &&
                            (PolygonGeometry.ContainsPoint(shape, s.Centre) ||
                             BuildingStoreys.StairLandings(s).Any(l =>
                                PolygonGeometry.ContainsPoint(shape,
                                    new Point2(l.Average(p => p.X), l.Average(p => p.Y)))))))
                            continue;
                        proposals.Add((area, shape));
                    }
                }
            }
            bool placed = false;
            foreach (var proposal in proposals.OrderByDescending(p => p.Area)
                .Take(150))
            {
                var trial = BuildingStoreys.Copy(updated);
                var editable = BuildingStoreys.GetFloor(trial, floorIndex);
                string type = ChooseRoomTypeForOutline(use, floorIndex, proposal.Area,
                    editable.Rooms, proposal.Outline, random);
                editable.Rooms.Add(new RoomDefinition
                {
                    Type = type, Name = NextRoomName(type, editable.Rooms),
                    Outline = proposal.Outline
                });
                BuildingStoreys.SetFloor(trial, floorIndex, editable);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                updated = trial;
                added++;
                placed = true;
                break;
            }
            if (!placed) break;
        }
        return added;
    }

    /// <summary>
    /// Replace redundant generated Passage rectangles with open hallway
    /// space, and join awkward small leftover rooms to a larger neighbour
    /// to make a natural orthogonal L-shaped room. Uses the existing
    /// polygon-union algorithm; never remodels a hand-drawn room passed
    /// in <paramref name="originalRoomIds"/> (except to extend a hall
    /// with a newly generated passage). The entire multi-storey building
    /// is validated after every accepted union.
    /// </summary>
    public static int ConsolidateGeneratedRooms(BuildingDocument source,
        int floorIndex, IReadOnlyCollection<Guid> originalRoomIds,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var original = originalRoomIds.ToHashSet();
        int mergedCount = 0;
        for (int pass = 0; pass < 24; pass++)
        {
            var floor = BuildingStoreys.GetFloor(updated, floorIndex);
            bool changed = false;
            var candidates = floor.Rooms.Where(r => !original.Contains(r.Id) &&
                    r.Type is not ("Hallway" or "Stairwell"))
                .OrderBy(r => r.Type == "Passage" ? 0 : 1)
                .ThenBy(r => PolygonGeometry.Area(r.Outline))
                .ToArray();

            foreach (var candidate in candidates)
            {
                double w = candidate.Outline.Max(p => p.X) -
                    candidate.Outline.Min(p => p.X);
                double h = candidate.Outline.Max(p => p.Y) -
                    candidate.Outline.Min(p => p.Y);
                double area = PolygonGeometry.Area(candidate.Outline);
                bool passage = candidate.Type == "Passage";
                // A 2m-deep, 5m-wide storeroom is intentional: it can
                // hold shelves on both sides and needs only a single door.
                // Do not absorb useful service space into bedrooms or
                // dining rooms just because it is slimmer than a bedroom.
                bool usefulStore = candidate.Type is "Storeroom" or
                    "Storage" or "Pantry" or "Buttery" or "Stockroom" &&
                    area >= 8 && Math.Min(w, h) >= 1.6;
                bool awkward = !usefulStore &&
                    (Math.Min(w, h) < 2.5 ||
                     (area < 9 && candidate.Type is "Storage" or "Closet" or
                         "Storeroom" or "Pantry" or "Other"));
                if (!passage && !awkward) continue;

                var neighbours = floor.Rooms.Where(r => r.Id != candidate.Id &&
                        (passage && r.Type == "Hallway" ||
                         r.Type is not ("Hallway" or "Stairwell" or "Passage") &&
                         !original.Contains(r.Id)))
                    .Where(r => BuildingConnectivity.ShareWall(
                        r.Outline, candidate.Outline, 1.0))
                    .OrderBy(r => passage && r.Type == "Hallway" ? 0 : 1)
                    .ThenByDescending(r => PolygonGeometry.Area(r.Outline))
                    .ToArray();
                foreach (var host in neighbours)
                {
                    double joinedArea = area + PolygonGeometry.Area(host.Outline);
                    if (host.Type != "Hallway" &&
                        (joinedArea > 60 || PolygonGeometry.Area(host.Outline) < 8))
                        continue;
                    // Merge the disposable generated fragment INTO the
                    // meaningful room, retaining its type, name and ID.
                    if (!RoomMerging.TryMergeRooms(floor, host.Id, candidate.Id,
                            out var united, out _))
                        continue;
                    var after = united.Rooms.First(r => r.Id == host.Id);
                    // For a service fragment, an actual L-shape is the
                    // point; a rectangular union would be better handled
                    // during the original frontage partition pass.
                    if (!passage && after.Outline.Count < 6) continue;
                    var trial = BuildingStoreys.Copy(updated);
                    BuildingStoreys.SetFloor(trial, floorIndex, united);
                    if (!BuildingStoreys.Validate(trial, out _)) continue;
                    updated = trial;
                    mergedCount++;
                    changed = true;
                    break;
                }
                if (changed) break;
            }
            if (!changed) break;
        }
        return mergedCount;
    }

    /// <summary>
    /// Absorb thin (under 1.25m) residual strips into a newly proposed
    /// rectangular room when the facing exterior wall spans that side.
    /// Never reshape pre-existing user rooms or move the drawn perimeter.
    /// </summary>
    public static int ExtendSuggestedRoomsToExterior(BuildingDocument original,
        BuildingDocument proposed, int floorIndex, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(proposed);
        var unchangedIds = BuildingStoreys.GetFloor(original, floorIndex)
            .Rooms.Select(r => r.Id).ToHashSet();
        int extended = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            var floor = BuildingStoreys.GetFloor(updated, floorIndex);
            var walls = FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
                .Where(w => w.IsExterior).ToList();
            bool changed = false;
            foreach (var room in floor.Rooms.Where(r => !unchangedIds.Contains(r.Id)
                && r.Type != "Hallway" && r.Outline.Count == 4))
            {
                double left = room.Outline.Min(p => p.X),
                    right = room.Outline.Max(p => p.X),
                    top = room.Outline.Min(p => p.Y),
                    bottom = room.Outline.Max(p => p.Y);
                if (!room.Outline.All(p =>
                    (Math.Abs(p.X - left) < 1e-6 ||
                     Math.Abs(p.X - right) < 1e-6) &&
                    (Math.Abs(p.Y - top) < 1e-6 ||
                     Math.Abs(p.Y - bottom) < 1e-6)))
                    continue;

                foreach (var wall in walls)
                {
                    double lx = left, rx = right, ty = top, by = bottom;
                    if (Math.Abs(wall.A.X - wall.B.X) < 1e-6)
                    {
                        double x = wall.A.X;
                        if (Math.Min(wall.A.Y, wall.B.Y) > top + 1e-6 ||
                            Math.Max(wall.A.Y, wall.B.Y) < bottom - 1e-6)
                            continue;
                        if (x < left && left - x < 1.25 && left - x > 1e-5)
                            lx = x;
                        else if (x > right && x - right < 1.25 && x - right > 1e-5)
                            rx = x;
                        else continue;
                    }
                    else if (Math.Abs(wall.A.Y - wall.B.Y) < 1e-6)
                    {
                        double y = wall.A.Y;
                        if (Math.Min(wall.A.X, wall.B.X) > left + 1e-6 ||
                            Math.Max(wall.A.X, wall.B.X) < right - 1e-6)
                            continue;
                        if (y < top && top - y < 1.25 && top - y > 1e-5)
                            ty = y;
                        else if (y > bottom && y - bottom < 1.25 && y - bottom > 1e-5)
                            by = y;
                        else continue;
                    }
                    else continue;

                    var trial = BuildingStoreys.Copy(updated);
                    var next = BuildingStoreys.GetFloor(trial, floorIndex);
                    var edited = next.Rooms.First(r => r.Id == room.Id);
                    edited.Outline = Rectangle(lx, ty, rx, by);
                    BuildingStoreys.SetFloor(trial, floorIndex, next);
                    if (!BuildingStoreys.Validate(trial, out _)) continue;
                    updated = trial;
                    extended++;
                    changed = true;
                    break;
                }
                if (changed) break;
            }
            if (!changed) break;
        }
        return extended;
    }

    private static double[] SplitLongSpans(IReadOnlyList<double> cuts)
    {
        var result = new List<double>();
        for (int i = 0; i < cuts.Count; i++)
        {
            if (i > 0)
            {
                double from = cuts[i - 1], to = cuts[i];
                // Do not create huge undivided bedrooms inside an otherwise
                // empty plan. Intermediate cuts are layout guides only;
                // they never move existing walls or the exterior perimeter.
                int parts = (int)Math.Ceiling((to - from) / 4.5);
                for (int part = 1; part < parts; part++)
                    result.Add(from + (to - from) * part / parts);
            }
            result.Add(cuts[i]);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Prefer culinary service relationships over an arbitrary sequence of
    /// room names. A shared partition is the prerequisite for a direct
    /// door; room naming alone does not invent an impossible connection.
    /// </summary>
    public static string ChooseRoomTypeForOutline(BuildingUse use, int floorIndex,
        double area, IReadOnlyList<RoomDefinition> existing,
        IReadOnlyList<Point2> outline, Random random)
    {
        string basic = ChooseRoomType(use, floorIndex, area, existing, random);

        // Room roles must depend on usable proportions as well as area.
        // A 1.786 x 7.62m L-wing strip is 13.6 square metres but is not
        // a plausible bedroom: classify it as circulation instead of
        // consuming the next available residential room name.
        double width = outline.Max(p => p.X) - outline.Min(p => p.X);
        double depth = outline.Max(p => p.Y) - outline.Min(p => p.Y);
        double shortSide = Math.Min(width, depth);
        double longSide = Math.Max(width, depth);
        if (shortSide > 1e-6 && shortSide < 2.35 &&
            longSide >= 4.0 && longSide / shortSide >= 2.8)
            return "Passage";

        // Bedrooms and other sleeping accommodation need usable width
        // for a bed and access around it, not simply enough total area.
        if (shortSide < 2.5 && basic is "Bedroom" or "Guest room" or "Suite")
            return floorIndex > 0 ? "Closet" : "Storeroom";

        if (floorIndex != 0 || use is not (BuildingUse.Residence or BuildingUse.Inn))
            return basic;
        bool Has(string type) => existing.Any(r =>
            r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        bool Borders(string type) => existing.Any(r =>
            r.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
            BuildingConnectivity.ShareWall(r.Outline, outline, 1.2));

        if (!Has("Kitchen") && area >= 8 &&
            (Borders("Living room") || Borders("Common room")))
            return "Kitchen";
        if (Borders("Kitchen"))
        {
            if (!Has("Dining room") && area >= 7) return "Dining room";
            if (!Has("Pantry") && area <= 14) return "Pantry";
            if (!Has("Storeroom") && area <= 22) return "Storeroom";
        }
        return basic;
    }

    /// <summary>
    /// Convert a temporary enclosed stair bay to an OPEN side alcove in
    /// the hallway. A rectangular hall and a rectangular bay sharing a
    /// wall become one orthogonal, sometimes L-shaped, hallway polygon.
    /// This avoids leaving an unassigned "gap" around the stairs while
    /// not introducing an artificial separate stair-room enclosure.
    /// </summary>
    public static bool TryMergeStairBayIntoHall(BuildingDocument building,
        int floorIndex, out BuildingDocument updated, out string reason)
    {
        updated = BuildingStoreys.Copy(building);
        reason = "";
        var floor = BuildingStoreys.GetFloor(updated, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
        var bay = floor.Rooms.FirstOrDefault(r => r.Type == "Stairwell");
        if (hall is null || bay is null ||
            hall.Outline.Count != 4 || bay.Outline.Count != 4)
        {
            reason = "An open stair alcove needs a rectangular hall and reserved bay.";
            return false;
        }
        static bool Eq(double x, double y) => Math.Abs(x - y) < 1e-5;
        double hl = hall.Outline.Min(p => p.X), hr = hall.Outline.Max(p => p.X);
        double ht = hall.Outline.Min(p => p.Y), hb = hall.Outline.Max(p => p.Y);
        double pl = bay.Outline.Min(p => p.X), pr = bay.Outline.Max(p => p.X);
        double pt = bay.Outline.Min(p => p.Y), pb = bay.Outline.Max(p => p.Y);
        bool horizontal = hr - hl >= hb - ht;
        List<Point2>? merged = null;
        if (horizontal && pl >= hl - 1e-5 && pr <= hr + 1e-5)
        {
            if (Eq(pb, ht))
                merged = [new(hl,ht),new(pl,ht),new(pl,pt),new(pr,pt),
                    new(pr,ht),new(hr,ht),new(hr,hb),new(hl,hb)];
            else if (Eq(pt, hb))
                merged = [new(hl,ht),new(hr,ht),new(hr,hb),new(pr,hb),
                    new(pr,pb),new(pl,pb),new(pl,hb),new(hl,hb)];
        }
        else if (!horizontal && pt >= ht - 1e-5 && pb <= hb + 1e-5)
        {
            if (Eq(pr, hl))
                merged = [new(hl,ht),new(hr,ht),new(hr,hb),new(hl,hb),
                    new(hl,pb),new(pl,pb),new(pl,pt),new(hl,pt)];
            else if (Eq(pl, hr))
                merged = [new(hl,ht),new(hr,ht),new(hr,pt),new(pr,pt),
                    new(pr,pb),new(hr,pb),new(hr,hb),new(hl,hb)];
        }
        if (merged is null)
        {
            reason = "The stair pocket does not share a valid hallway side.";
            return false;
        }

        // Remove straight-line vertices and repeated end points that arise
        // when the stair bay reaches the end of the hall.
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < merged.Count && merged.Count > 4; i++)
            {
                var a = merged[(i + merged.Count - 1) % merged.Count];
                var b = merged[i];
                var d = merged[(i + 1) % merged.Count];
                double cross = (b.X - a.X) * (d.Y - b.Y) -
                    (b.Y - a.Y) * (d.X - b.X);
                if (a.DistanceTo(b) > 1e-5 && Math.Abs(cross) > 1e-5)
                    continue;
                merged.RemoveAt(i);
                changed = true;
                break;
            }
        } while (changed);

        // The former hall/stair-bay door is inside this single open space.
        // Do not retain a floating door with no wall under it.
        var oldHall = hall.Outline.ToList();
        var oldBay = bay.Outline.ToList();
        static bool On(Point2 point, IReadOnlyList<Point2> outline) =>
            Enumerable.Range(0, outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(point,
                    outline[i], outline[(i + 1) % outline.Count])
                    .DistanceTo(point) < 1e-5);
        floor.Doors.RemoveAll(d => On(d.Centre, oldHall) &&
            On(d.Centre, oldBay));
        hall.Outline = merged;
        floor.Rooms.Remove(bay);
        BuildingStoreys.SetFloor(updated, floorIndex, floor);
        if (BuildingStoreys.Validate(updated, out reason))
            return true;
        reason = "Could not convert the enclosed stair bay to open hallway: " + reason;
        updated = BuildingStoreys.Copy(building);
        return false;
    }

    /// <summary>
    /// Absorb long shallow exterior strips beside a rectangular hall into
    /// its circulation area, rather than inventing bedrooms in floor
    /// strips only 0.762–2.0m deep. Keep wider / less elongated
    /// spaces available for normal rooms and service rooms.
    /// </summary>
    public static int AbsorbNarrowPerimeterGapsIntoHall(
        BuildingDocument source, int floorIndex, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        int absorbed = 0;
        // Each pass merges a single boundary strip. Further steps must
        // not assume the hall stays rectangular after the first merge.
        for (int pass = 0; pass < 8; pass++)
        {
            var level = BuildingStoreys.GetFloor(updated, floorIndex);
            var hall = level.Rooms.FirstOrDefault(r => r.Type == "Hallway");
            if (hall is null || hall.Outline.Count != 4) break;
            double left = hall.Outline.Min(p => p.X);
            double right = hall.Outline.Max(p => p.X);
            double top = hall.Outline.Min(p => p.Y);
            double bottom = hall.Outline.Max(p => p.Y);
            bool changed = false;

            for (int k = 0; k < level.Perimeter.Count; k++)
            {
                var a = level.Perimeter[k];
                var b = level.Perimeter[(k + 1) % level.Perimeter.Count];
                List<Point2>? merged = null;
                List<Point2>? sliver = null;
                if (Math.Abs(a.Y - b.Y) < 1e-6)
                {
                    double x0 = Math.Max(left, Math.Min(a.X, b.X));
                    double x1 = Math.Min(right, Math.Max(a.X, b.X));
                    if (x1 - x0 < 3.5) continue;
                    if (a.Y < top - 1e-6 && top - a.Y <= 2.0 &&
                        (x1 - x0) / (top - a.Y) >= 2.5)
                    {
                        sliver = Rectangle(x0, a.Y, x1, top);
                        merged = [new(left,top),new(x0,top),new(x0,a.Y),
                            new(x1,a.Y),new(x1,top),new(right,top),
                            new(right,bottom),new(left,bottom)];
                    }
                    else if (a.Y > bottom + 1e-6 && a.Y - bottom <= 2.0 &&
                        (x1 - x0) / (a.Y - bottom) >= 2.5)
                    {
                        sliver = Rectangle(x0, bottom, x1, a.Y);
                        merged = [new(left,top),new(right,top),new(right,bottom),
                            new(x1,bottom),new(x1,a.Y),new(x0,a.Y),
                            new(x0,bottom),new(left,bottom)];
                    }
                }
                else if (Math.Abs(a.X - b.X) < 1e-6)
                {
                    double y0 = Math.Max(top, Math.Min(a.Y, b.Y));
                    double y1 = Math.Min(bottom, Math.Max(a.Y, b.Y));
                    if (y1 - y0 < 3.5) continue;
                    if (a.X < left - 1e-6 && left - a.X <= 2.0 &&
                        (y1 - y0) / (left - a.X) >= 2.5)
                    {
                        sliver = Rectangle(a.X, y0, left, y1);
                        merged = [new(left,top),new(right,top),
                            new(right,bottom),new(left,bottom),new(left,y1),
                            new(a.X,y1),new(a.X,y0),new(left,y0)];
                    }
                    else if (a.X > right + 1e-6 && a.X - right <= 2.0 &&
                        (y1 - y0) / (a.X - right) >= 2.5)
                    {
                        sliver = Rectangle(right, y0, a.X, y1);
                        merged = [new(left,top),new(right,top),new(right,y0),
                            new(a.X,y0),new(a.X,y1),new(right,y1),
                            new(right,bottom),new(left,bottom)];
                    }
                }
                if (merged is null || sliver is null ||
                    !FloorGeometry.IsWithinPolygon(sliver, level.Perimeter))
                    continue;

                // Remove redundant straight-edge points, including when a
                // sliver touches an exterior corner of the hall.
                bool simplifying;
                do
                {
                    simplifying = false;
                    for (int i = 0; i < merged.Count && merged.Count > 4; i++)
                    {
                        var prev = merged[(i + merged.Count - 1) % merged.Count];
                        var mid = merged[i];
                        var next = merged[(i + 1) % merged.Count];
                        double cross = (mid.X - prev.X) * (next.Y - mid.Y) -
                            (mid.Y - prev.Y) * (next.X - mid.X);
                        if (prev.DistanceTo(mid) > 1e-5 &&
                            Math.Abs(cross) > 1e-5) continue;
                        merged.RemoveAt(i);
                        simplifying = true;
                        break;
                    }
                } while (simplifying);

                if (!PolygonGeometry.IsValidOutline(merged, out _)) continue;
                var trial = BuildingStoreys.Copy(updated);
                var floor = BuildingStoreys.GetFloor(trial, floorIndex);
                floor.Rooms.First(r => r.Id == hall.Id).Outline = merged;
                BuildingStoreys.SetFloor(trial, floorIndex, floor);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                updated = trial;
                absorbed++;
                changed = true;
                break;
            }
            if (!changed) break;
        }
        return absorbed;
    }

    /// <summary>
    /// Choose useful rooms in architectural priority order, not in a random
    /// order which may omit the kitchen or create multiple living rooms.
    /// Repeatable accommodation roles (bedrooms, guest rooms and cells) may
    /// recur after the essential one-per-floor spaces have been allocated.
    /// This method never renames existing rooms.
    /// </summary>
    public static string ChooseRoomType(BuildingUse use, int floorIndex,
        double squareMetres, IReadOnlyList<RoomDefinition> rooms, Random random)
    {
        string[] priorities;
        string[] repeats;
        if (use == BuildingUse.Dungeon)
        {
            priorities = squareMetres < 9
                ? ["Cell", "Guardroom", "Storage"]
                : ["Guardroom", "Chamber", "Barracks", "Vault"];
            repeats = squareMetres < 9 ? ["Cell"] : ["Chamber", "Barracks"];
        }
        else if (use == BuildingUse.Inn)
        {
            if (floorIndex > 0)
            {
                priorities = squareMetres < 9
                    ? ["Washroom", "Guest room"]
                    : ["Guest room", "Linen store", "Suite"];
                repeats = squareMetres < 9 ? ["Guest room"] : ["Guest room", "Suite"];
            }
            else if (squareMetres < 9)
            {
                priorities = ["Pantry", "Washroom", "Storage"];
                repeats = ["Storage"];
            }
            else
            {
                priorities = squareMetres >= 23
                    ? ["Common room", "Kitchen", "Dining room", "Guest room"]
                    : ["Kitchen", "Dining room", "Guest room", "Storage"];
                repeats = ["Guest room"];
            }
        }
        else if (floorIndex < 0 && use != BuildingUse.Dungeon)
        {
            priorities = squareMetres < 9
                ? ["Pantry", "Storage", "Utility room"]
                : ["Cellar", "Workshop", "Storage"];
            repeats = ["Storage"];
        }
        else if (use == BuildingUse.Shop)
        {
            priorities = squareMetres < 9
                ? ["Washroom", "Storeroom", "Office"]
                : ["Shop floor", "Stockroom", "Workshop", "Office"];
            repeats = ["Stockroom"];
        }
        else if (use == BuildingUse.Residence)
        {
            if (floorIndex > 0)
            {
                priorities = squareMetres < 9
                    ? ["Bathroom", "Closet", "Storage"]
                    : ["Bedroom", "Study"];
                repeats = squareMetres < 9 ? ["Closet"] : ["Bedroom"];
            }
            else if (squareMetres < 9)
            {
                priorities = ["Bathroom", "Pantry", "Laundry"];
                repeats = ["Pantry"];
            }
            else if (squareMetres >= 23)
            {
                priorities = ["Living room", "Kitchen", "Dining room", "Study", "Bedroom"];
                repeats = ["Bedroom"];
            }
            else
            {
                priorities = ["Kitchen", "Dining room", "Bedroom", "Study"];
                repeats = ["Bedroom"];
            }
        }
        else
        {
            priorities = squareMetres < 9
                ? ["Bathroom", "Pantry", "Storage"]
                : ["Living room", "Kitchen", "Workshop", "Office", "Bedroom"];
            repeats = squareMetres < 9 ? ["Storage"] : ["Bedroom", "Office"];
        }

        foreach (string role in priorities)
            if (!rooms.Any(r => r.Type.Equals(role, StringComparison.OrdinalIgnoreCase)))
                return role;
        return repeats[random.Next(repeats.Length)];
    }

    /// <summary>Use the plain room name first and number only duplicates.</summary>
    public static string NextRoomName(string type, IReadOnlyList<RoomDefinition> rooms)
    {
        if (!rooms.Any(r => r.Name.Equals(type, StringComparison.OrdinalIgnoreCase)))
            return type;
        for (int number = 2; ; number++)
        {
            string candidate = $"{type} {number}";
            if (!rooms.Any(r => r.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    /// <summary>
    /// Finalize an already furnished GENERATED floor. Earlier generation
    /// stages may have installed a door from a dining room or bedroom into
    /// its adjacent storeroom, before the final hallway/stair-bay merge.
    /// The access-spur pass cannot reshape that room while its old door is
    /// attached. Remove only doors touching NEW generated stores in the
    /// blocking strip, create independent corridor access, then regenerate
    /// doors and validate the entire multi-storey building. An unchanged
    /// copy is returned if no safe improvement can be made.
    /// </summary>
    public static int RepairGeneratedStorageAccess(BuildingDocument source,
        int floorIndex, IReadOnlyCollection<Guid> originalRoomIds,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var protectedIds = originalRoomIds.ToHashSet();
        var hall = floor.Rooms.FirstOrDefault(r =>
            r.Type == "Hallway" && !protectedIds.Contains(r.Id));
        if (hall is null) return 0;

        static bool Touches(IReadOnlyList<Point2> outline, Point2 point) =>
            Enumerable.Range(0, outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(point, outline[i],
                    outline[(i + 1) % outline.Count])
                    .DistanceTo(point) < 1e-5);

        var blocking = floor.Rooms.Where(store =>
            !protectedIds.Contains(store.Id) &&
            store.Type is ("Storeroom" or "Storage" or "Pantry" or "Buttery" or
                "Stockroom" or "Linen store") &&
            BuildingConnectivity.ShareWall(store.Outline, hall.Outline, 1.0) &&
            floor.Rooms.Any(other =>
                other.Id != store.Id && other.Id != hall.Id &&
                other.Type is not ("Storeroom" or "Storage" or "Pantry" or
                    "Buttery" or "Stockroom" or "Linen store" or "Stairwell") &&
                BuildingConnectivity.ShareWall(store.Outline, other.Outline, 1.0) &&
                !BuildingConnectivity.ShareWall(hall.Outline, other.Outline, 0.8)))
            .ToArray();
        if (blocking.Length == 0) return 0;

        var detached = BuildingStoreys.Copy(source);
        var editFloor = BuildingStoreys.GetFloor(detached, floorIndex);
        // These were added by this draft's own door pass, not by the
        // user. Doors on the new room's walls are rebuilt once the
        // partitions have changed.
        editFloor.Doors.RemoveAll(d => blocking.Any(store =>
            Touches(store.Outline, d.Centre)));
        BuildingStoreys.SetFloor(detached, floorIndex, editFloor);
        if (!BuildingStoreys.Validate(detached, out _)) return 0;

        int routes = OpenDirectAccessPastGeneratedStores(
            detached, floorIndex, protectedIds, out var rerouted);
        if (routes == 0) return 0;
        BuildingConnectivity.AddDoors(rerouted, floorIndex, out var withDoors);
        if (!BuildingStoreys.Validate(withDoors, out _)) return 0;

        // A geometrically valid document is not enough: every room
        // formerly isolated behind storage must now have a direct route
        // to circulation. Refuse partial repairs that remain unusable.
        var revised = BuildingStoreys.GetFloor(withDoors, floorIndex);
        var newHall = revised.Rooms.FirstOrDefault(r => r.Id == hall.Id);
        if (newHall is null) return 0;
        foreach (var store in blocking)
        foreach (var room in floor.Rooms.Where(r =>
            r.Id != store.Id && r.Id != hall.Id &&
            BuildingConnectivity.ShareWall(store.Outline, r.Outline, 1.0) &&
            !BuildingConnectivity.ShareWall(hall.Outline, r.Outline, 0.8) &&
            r.Type is not ("Storeroom" or "Storage" or "Pantry" or
                "Buttery" or "Stockroom" or "Stairwell")))
        {
            var after = revised.Rooms.FirstOrDefault(r => r.Id == room.Id);
            if (after is null ||
                !BuildingConnectivity.ShareWall(newHall.Outline, after.Outline, 0.8) ||
                !revised.Doors.Any(d =>
                    Touches(newHall.Outline, d.Centre) &&
                    Touches(after.Outline, d.Centre)))
                return 0;
        }
        updated = withDoors;
        return routes;
    }

    /// <summary>
    /// A shallow generated storeroom must not isolate the room behind it
    /// from the corridor. Turn a narrow strip along ONE end of that store
    /// into an open hallway spur, rather than giving the store two doors
    /// and making it a compulsory passage into a bedroom or living room.
    /// This is typical of a service passage providing independent access
    /// to a medieval buttery/pantry and to other rooms beyond.
    ///
    /// Only proposed storage rooms and proposed hallways may be reshaped.
    /// Existing doors, fireplaces, windows, stairs, rooms, and floors are
    /// left untouched. Every step passes whole-building validation.
    /// </summary>
    public static int OpenDirectAccessPastGeneratedStores(
        BuildingDocument source, int floorIndex,
        IReadOnlyCollection<Guid> originalRoomIds, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var original = originalRoomIds.ToHashSet();
        int newAccessRoutes = 0;
        const double epsilon = 1e-5;
        const double passageWidth = 1.3;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            var floor = BuildingStoreys.GetFloor(updated, floorIndex);
            var hall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway" &&
                !original.Contains(r.Id));
            if (hall is null) break;
            bool improved = false;

            foreach (var store in floor.Rooms.Where(r =>
                !original.Contains(r.Id) &&
                r.Type is "Storeroom" or "Storage" or "Pantry" or
                    "Buttery" or "Stockroom" or "Linen store").ToArray())
            {
                if (store.Outline.Count != 4) continue;
                double x0 = store.Outline.Min(p => p.X);
                double x1 = store.Outline.Max(p => p.X);
                double y0 = store.Outline.Min(p => p.Y);
                double y1 = store.Outline.Max(p => p.Y);
                if (!store.Outline.All(p =>
                    (Math.Abs(p.X - x0) < epsilon ||
                     Math.Abs(p.X - x1) < epsilon) &&
                    (Math.Abs(p.Y - y0) < epsilon ||
                     Math.Abs(p.Y - y1) < epsilon)))
                    continue;

                // Never change a storeroom with an existing doorway (which
                // may have been installed manually or accepted earlier).
                if (floor.Doors.Any(d => Enumerable.Range(0, 4).Any(i =>
                    PolygonGeometry.ClosestPointOnSegment(d.Centre,
                        store.Outline[i], store.Outline[(i + 1) % 4])
                        .DistanceTo(d.Centre) < epsilon)))
                    continue;

                // A storage band touches the hallway along one long edge,
                // and separates a substantive room from that hallway.
                bool horizontal = x1 - x0 >= y1 - y0;
                double length = horizontal ? x1 - x0 : y1 - y0;
                double depth = horizontal ? y1 - y0 : x1 - x0;
                if (length < 4.0 || depth < 1.6 || depth > 2.8 ||
                    length - passageWidth < 2.4)
                    continue;

                bool hallAtHigh;
                if (horizontal)
                {
                    if (Math.Abs(hall.Outline.Min(p => p.Y) - y1) < epsilon)
                        hallAtHigh = true;
                    else if (Math.Abs(hall.Outline.Max(p => p.Y) - y0) < epsilon)
                        hallAtHigh = false;
                    else if (BuildingConnectivity.ShareWall(hall.Outline,
                        store.Outline, 2.0))
                        hallAtHigh = hall.Outline.Average(p => p.Y) > (y0 + y1) / 2;
                    else continue;
                }
                else
                {
                    if (Math.Abs(hall.Outline.Min(p => p.X) - x1) < epsilon)
                        hallAtHigh = true;
                    else if (Math.Abs(hall.Outline.Max(p => p.X) - x0) < epsilon)
                        hallAtHigh = false;
                    else if (BuildingConnectivity.ShareWall(hall.Outline,
                        store.Outline, 2.0))
                        hallAtHigh = hall.Outline.Average(p => p.X) > (x0 + x1) / 2;
                    else continue;
                }
                if (!BuildingConnectivity.ShareWall(hall.Outline,
                    store.Outline, 1.5)) continue;

                var blocked = floor.Rooms.Where(r =>
                    r.Id != hall.Id && r.Id != store.Id &&
                    r.Type is not ("Hallway" or "Stairwell" or
                        "Storeroom" or "Storage" or "Pantry" or
                        "Buttery" or "Stockroom") &&
                    BuildingConnectivity.ShareWall(r.Outline,
                        store.Outline, 1.0) &&
                    !BuildingConnectivity.ShareWall(r.Outline,
                        hall.Outline, 0.8)).ToArray();
                if (blocked.Length == 0) continue;

                foreach (bool passageAtLow in new[] { true, false })
                {
                    double alongStart = horizontal ? x0 : y0;
                    double alongEnd = horizontal ? x1 : y1;
                    double cut = passageAtLow
                        ? alongStart + passageWidth
                        : alongEnd - passageWidth;
                    var spur = horizontal
                        ? Rectangle(passageAtLow ? x0 : cut, y0,
                            passageAtLow ? cut : x1, y1)
                        : Rectangle(x0, passageAtLow ? y0 : cut,
                            x1, passageAtLow ? cut : y1);
                    var shortened = horizontal
                        ? Rectangle(passageAtLow ? cut : x0, y0,
                            passageAtLow ? x1 : cut, y1)
                        : Rectangle(x0, passageAtLow ? cut : y0,
                            x1, passageAtLow ? y1 : cut);

                    if (!blocked.Any(room =>
                        BuildingConnectivity.ShareWall(room.Outline,
                            spur, 1.0))) continue;
                    if (PolygonGeometry.Area(shortened) < 5.0) continue;
                    if (!BuildingConnectivity.ShareWall(hall.Outline, spur, 1.0))
                        continue;

                    // The new hallway must be one contiguous polygon.
                    // Using the general polygon union supports a hall
                    // that already acquired another access spur.
                    var unionSource = new BuildingDocument
                    {
                        Perimeter = floor.Perimeter.ToList(),
                        Rooms =
                        [
                            new RoomDefinition
                            {
                                Name = "Circulation", Type = "Hallway",
                                Outline = hall.Outline.ToList()
                            },
                            new RoomDefinition
                            {
                                Name = "Access", Type = "Hallway", Outline = spur
                            }
                        ]
                    };
                    if (!RoomMerging.TryMergeRooms(unionSource,
                        unionSource.Rooms[0].Id, unionSource.Rooms[1].Id,
                        out var combined, out _)) continue;

                    var trial = BuildingStoreys.Copy(updated);
                    var edited = BuildingStoreys.GetFloor(trial, floorIndex);
                    edited.Rooms.First(r => r.Id == hall.Id).Outline =
                        combined.Rooms.Single().Outline.ToList();
                    edited.Rooms.First(r => r.Id == store.Id).Outline = shortened;
                    BuildingStoreys.SetFloor(trial, floorIndex, edited);
                    if (!BuildingStoreys.Validate(trial, out _)) continue;

                    updated = trial;
                    newAccessRoutes++;
                    improved = true;
                    break;
                }
                if (improved) break;
            }
            if (!improved) break;
        }
        return newAccessRoutes;
    }

    /// <summary>
    /// A broad entrance alcove often has a usable pocket on BOTH sides of
    /// its external door. Preserve a clear passage from that door to the
    /// corridor, and make the flanking pockets enclosed storage rooms.
    /// Works with north/south/east/west doors and imperial coordinates.
    /// Existing user rooms and other storeys are never reshaped.
    /// </summary>
    public static int PartitionGeneratedEntranceWing(BuildingDocument source,
        int floorIndex, IReadOnlyCollection<Guid> originalRoomIds,
        out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        if (floorIndex != 0) return 0; // only ground floor has external entrance
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway" &&
            !originalRoomIds.Contains(r.Id));
        if (hall is null || hall.Outline.Count < 6 ||
            floor.Doors.Count == 0) return 0;

        const double epsilon = 1e-5;
        const double accessWidth = 2.0;
        const double minimumStoreWidth = 2.2;
        foreach (var entrance in floor.Doors)
        foreach (int wallIndex in Enumerable.Range(0, floor.Perimeter.Count))
        {
            var a = floor.Perimeter[wallIndex];
            var b = floor.Perimeter[(wallIndex + 1) % floor.Perimeter.Count];
            bool horizontal = Math.Abs(a.Y - b.Y) < epsilon;
            bool vertical = Math.Abs(a.X - b.X) < epsilon;
            if (!horizontal && !vertical) continue;
            if (PolygonGeometry.ClosestPointOnSegment(entrance.Centre, a, b)
                    .DistanceTo(entrance.Centre) > epsilon) continue;

            // Long frontage, with the main doorway far enough from each
            // corner to house a practical storeroom on both sides.
            double axisLow = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
            double axisHigh = horizontal ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
            double entranceAxis = horizontal ? entrance.Centre.X : entrance.Centre.Y;
            double wallAxis = horizontal ? a.Y : a.X;
            double halfAccess = Math.Max(accessWidth, entrance.Width + 0.9) / 2;
            double leftEdge = entranceAxis - halfAccess;
            double rightEdge = entranceAxis + halfAccess;
            if (leftEdge - axisLow < minimumStoreWidth ||
                axisHigh - rightEdge < minimumStoreWidth) continue;

            Point2 World(double axis, double perpendicular) =>
                horizontal ? new Point2(axis, perpendicular) :
                new Point2(perpendicular, axis);
            List<Point2> Rect(double lowAxis, double highAxis,
                double lowPerpendicular, double highPerpendicular)
            {
                double lo = Math.Min(lowPerpendicular, highPerpendicular);
                double hi = Math.Max(lowPerpendicular, highPerpendicular);
                return horizontal
                    ? Rectangle(lowAxis, lo, highAxis, hi)
                    : Rectangle(lo, lowAxis, hi, highAxis);
            }

            // The exterior entrance must actually be on this hall's wall,
            // not on a room which only happens to share an exterior edge.
            if (!Enumerable.Range(0, hall.Outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(entrance.Centre,
                    hall.Outline[i], hall.Outline[(i + 1) % hall.Outline.Count])
                    .DistanceTo(entrance.Centre) < epsilon))
                continue;

            double[] extents = hall.Outline
                .Select(p => horizontal ? p.Y : p.X).ToArray();
            double minPerp = extents.Min(), maxPerp = extents.Max();
            double midAxis = (axisLow + axisHigh) / 2;
            double sign = PolygonGeometry.ContainsPoint(hall.Outline,
                World(midAxis, wallAxis + 0.05)) ? 1 : -1;
            if (!PolygonGeometry.ContainsPoint(hall.Outline,
                    World(midAxis, wallAxis + sign * 0.05)))
                continue;
            // The first internal step is the boundary between an oversized
            // entrance strip and the normal hallway. In example2.json this
            // is the 1.786m-deep wing above the 2m-wide main corridor.
            var steps = extents.Select(v => (v - wallAxis) * sign)
                .Where(d => d >= 1.25 && d <= 3.0)
                .OrderBy(d => d).ToArray();
            if (steps.Length == 0) continue;
            double innerAxis = wallAxis + steps[0] * sign;

            // The whole hall must be representable as a rectangular main
            // corridor plus THIS entrance strip. More elaborate/hand-shaped
            // halls are deliberately left untouched.
            double fullAxisLow = hall.Outline.Min(p =>
                horizontal ? p.X : p.Y);
            double fullAxisHigh = hall.Outline.Max(p =>
                horizontal ? p.X : p.Y);
            double farAxis = sign > 0 ? maxPerp : minPerp;
            var mainCorridor = Rect(fullAxisLow, fullAxisHigh, innerAxis, farAxis);
            var oldWing = Rect(axisLow, axisHigh, wallAxis, innerAxis);
            if (Math.Abs(farAxis - innerAxis) < 1.8 ||
                !FloorGeometry.IsWithinPolygon(mainCorridor, hall.Outline) ||
                !FloorGeometry.IsWithinPolygon(oldWing, hall.Outline) ||
                Math.Abs(PolygonGeometry.Area(mainCorridor) +
                    PolygonGeometry.Area(oldWing) -
                    PolygonGeometry.Area(hall.Outline)) > 0.02)
                continue;

            var approach = Rect(leftEdge, rightEdge, wallAxis, innerAxis);
            var leftStorage = Rect(axisLow, leftEdge, wallAxis, innerAxis);
            var rightStorage = Rect(rightEdge, axisHigh, wallAxis, innerAxis);

            // Keep all existing doors and stairs working; in particular the
            // user's front door must remain on the new entrance passage.
            if (floor.Doors.Any(d => d.Id != entrance.Id &&
                (PolygonGeometry.ContainsPoint(leftStorage, d.Centre) ||
                 PolygonGeometry.ContainsPoint(rightStorage, d.Centre))))
                continue;
            if (source.Staircases.Where(s => s.LowerFloorIndex == floorIndex ||
                    s.LowerFloorIndex + 1 == floorIndex).Any(stair =>
                !FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.StairFootprint(stair), mainCorridor) ||
                !FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.StairOpeningFootprint(stair), mainCorridor) ||
                BuildingStoreys.StairLandings(stair).Any(landing =>
                    !FloorGeometry.IsWithinPolygon(landing, mainCorridor))))
                continue;

            // Union two adjacent rectangular circulation pieces using the
            // existing validated polygon-union algorithm (no guessed vertex
            // order, self-touching corner, or synthetic extra corridor).
            var spine = new RoomDefinition
            {
                Type = "Hallway", Name = "Corridor spine", Outline = mainCorridor
            };
            var entry = new RoomDefinition
            {
                Type = "Hallway", Name = "Entrance approach", Outline = approach
            };
            var isolated = new BuildingDocument
            {
                Perimeter = floor.Perimeter.ToList(),
                Rooms = [spine, entry]
            };
            if (!RoomMerging.TryMergeRooms(isolated, spine.Id, entry.Id,
                    out var merged, out _)) continue;
            var trial = BuildingStoreys.Copy(updated);
            var edited = BuildingStoreys.GetFloor(trial, floorIndex);
            edited.Rooms.First(r => r.Id == hall.Id).Outline =
                merged.Rooms.Single().Outline.ToList();
            string role = "Storage";
            foreach (var pocket in new[] { leftStorage, rightStorage })
                edited.Rooms.Add(new RoomDefinition
                {
                    Type = role, Name = NextRoomName(role, edited.Rooms),
                    Outline = pocket
                });
            BuildingStoreys.SetFloor(trial, floorIndex, edited);
            if (!BuildingStoreys.Validate(trial, out _)) continue;
            updated = trial;
            return 2;
        }
        return 0;
    }

    /// <summary>
    /// A generated passage may have been merged into a hallway even though it
    /// contains usable accommodation space. Reclaim that space as rooms while
    /// retaining a continuous two-metre circulation spine and every staircase
    /// (including its landings). Only generated hallways are eligible: never
    /// resize a user's pre-existing or manually edited room.
    /// </summary>
    public static int ReclaimOversizedGeneratedHallway(BuildingDocument source,
        int floorIndex, IReadOnlyCollection<Guid> originalRoomIds, BuildingUse use,
        Random random, out BuildingDocument updated)
    {
        updated = BuildingStoreys.Copy(source);
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var hall = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway" &&
            !originalRoomIds.Contains(r.Id));
        if (hall is null || hall.Outline.Count < 4) return 0;

        double floorArea = PolygonGeometry.Area(floor.Perimeter);
        double hallArea = PolygonGeometry.Area(hall.Outline);
        // Normal 2m corridors, stair alcoves, and intentionally spacious
        // small rooms do not need to be split into invented partitions.
        if (hallArea < 28 || hallArea < floorArea * 0.36) return 0;

        const double epsilon = 1e-5;
        // The reclaim pass deliberately handles orthogonal polygons only.
        // Skewed or hand-shaped rooms must never be "straightened" by it.
        for (int i = 0; i < hall.Outline.Count; i++)
        {
            var a = hall.Outline[i];
            var b = hall.Outline[(i + 1) % hall.Outline.Count];
            if (Math.Abs(a.X - b.X) > epsilon &&
                Math.Abs(a.Y - b.Y) > epsilon) return 0;
        }

        double minX = hall.Outline.Min(p => p.X);
        double maxX = hall.Outline.Max(p => p.X);
        double minY = hall.Outline.Min(p => p.Y);
        double maxY = hall.Outline.Max(p => p.Y);
        var connectedStairs = source.Staircases.Where(s =>
            s.LowerFloorIndex == floorIndex ||
            s.LowerFloorIndex + 1 == floorIndex).ToArray();

        foreach (bool horizontal in maxX - minX >= maxY - minY
            ? new[] { true, false } : new[] { false, true })
        {
            double longLength = horizontal ? maxX - minX : maxY - minY;
            double shortLength = horizontal ? maxY - minY : maxX - minX;
            if (longLength < 5.0 || shortLength < 3.25) continue;

            // Try to keep a long outside edge of the original hall. In the
            // common 5-foot stepped footprint this preserves the original
            // stair corridor and converts its unnecessarily broad L-wing
            // back into real rooms.
            double[] bandStarts = horizontal
                ? [maxY - 2.0, minY]
                : [maxX - 2.0, minX];
            foreach (double bandStart in bandStarts.Distinct())
            {
                var spine = horizontal
                    ? Rectangle(minX, bandStart, maxX, bandStart + 2.0)
                    : Rectangle(bandStart, minY, bandStart + 2.0, maxY);
                if (!FloorGeometry.IsWithinPolygon(spine, hall.Outline))
                    continue;

                bool stairsFit = connectedStairs.All(stair =>
                    FloorGeometry.IsWithinPolygon(
                        BuildingStoreys.StairFootprint(stair), spine) &&
                    FloorGeometry.IsWithinPolygon(
                        BuildingStoreys.StairOpeningFootprint(stair), spine) &&
                    BuildingStoreys.StairLandings(stair).All(landing =>
                        FloorGeometry.IsWithinPolygon(landing, spine)));
                if (!stairsFit) continue;

                // Slice at the EXISTING polygon coordinates, not at an
                // arbitrary metric grid: imperial edges stay exact and no
                // sliver is silently dropped.
                var xs = hall.Outline.Select(p => p.X).Append(minX)
                    .Append(maxX)
                    .Append(horizontal ? minX : bandStart)
                    .Append(horizontal ? maxX : bandStart + 2.0)
                    .Distinct().OrderBy(v => v).ToArray();
                var ys = hall.Outline.Select(p => p.Y).Append(minY)
                    .Append(maxY)
                    .Append(horizontal ? bandStart : minY)
                    .Append(horizontal ? bandStart + 2.0 : maxY)
                    .Distinct().OrderBy(v => v).ToArray();

                var pieces = new List<List<Point2>>();
                bool validPieces = true;
                for (int x = 0; x < xs.Length - 1; x++)
                {
                    int y = 0;
                    while (y < ys.Length - 1)
                    {
                        bool IsReclaimable(int row)
                        {
                            var middle = new Point2((xs[x] + xs[x + 1]) / 2,
                                (ys[row] + ys[row + 1]) / 2);
                            return PolygonGeometry.ContainsPoint(hall.Outline, middle) &&
                                !PolygonGeometry.ContainsPoint(spine, middle);
                        }
                        if (!IsReclaimable(y)) { y++; continue; }
                        int top = y++;
                        while (y < ys.Length - 1 && IsReclaimable(y)) y++;
                        double left = xs[x], right = xs[x + 1];
                        double upper = ys[top], lower = ys[y];
                        if (right - left < 1.8 || lower - upper < 1.8)
                        {
                            validPieces = false;
                            break;
                        }
                        // A long 2m-deep wing can contain two useful small
                        // service rooms; one 6m x 2m "Passage" is not useful.
                        if (right - left > 5.0 && lower - upper < 2.5)
                        {
                            double mid = (left + right) / 2;
                            pieces.Add(Rectangle(left, upper, mid, lower));
                            pieces.Add(Rectangle(mid, upper, right, lower));
                        }
                        else if (lower - upper > 5.0 && right - left < 2.5)
                        {
                            double mid = (upper + lower) / 2;
                            pieces.Add(Rectangle(left, upper, right, mid));
                            pieces.Add(Rectangle(left, mid, right, lower));
                        }
                        else
                            pieces.Add(Rectangle(left, upper, right, lower));
                    }
                    if (!validPieces) break;
                }
                double reclaimed = pieces.Sum(PolygonGeometry.Area);
                if (!validPieces || pieces.Count == 0 || pieces.Count > 8 ||
                    reclaimed < 8 || Math.Abs(reclaimed + PolygonGeometry.Area(spine) -
                        hallArea) > 0.01) continue;

                var trial = BuildingStoreys.Copy(updated);
                var edited = BuildingStoreys.GetFloor(trial, floorIndex);
                edited.Rooms.First(r => r.Id == hall.Id).Outline = spine;
                foreach (var outline in pieces)
                {
                    double area = PolygonGeometry.Area(outline);
                    string type = ChooseRoomTypeForOutline(use, floorIndex,
                        area, edited.Rooms, outline, random);
                    // A reclaimed service room should never become another
                    // circulating passage that can be reabsorbed on rerun.
                    if (type == "Passage")
                        type = floorIndex == 0 ? "Storeroom" : "Storage";
                    edited.Rooms.Add(new RoomDefinition
                    {
                        Name = NextRoomName(type, edited.Rooms),
                        Type = type, Outline = outline
                    });
                }
                BuildingStoreys.SetFloor(trial, floorIndex, edited);
                if (!BuildingStoreys.Validate(trial, out _)) continue;
                updated = trial;
                return pieces.Count;
            }
        }
        return 0;
    }

    public static double UnassignedArea(BuildingDocument floor)
    {
        double total = PolygonGeometry.Area(floor.Perimeter);
        double covered = floor.Rooms.Sum(r => PolygonGeometry.Area(r.Outline));
        return Math.Max(0, total - covered);
    }

    private static List<int> ChooseFrontageWidths(int length, int depth,
        int target, BuildingUse use, int floorIndex,
        IReadOnlyList<RoomDefinition> existingRooms, Random random)
    {
        // Half-metre grid cells: a candidate with width W and depth D
        // covers W * D / 4 square metres. Reserve at least 23 m² for
        // one ground-floor residential living room, leaving >=2 m for
        // a small service room. Only do so on a sufficiently large
        // untouched frontage; irregular layouts keep the ordinary split.
        if (use == BuildingUse.Residence && floorIndex == 0 &&
            !existingRooms.Any(r => r.Type.Equals(
                "Living room", StringComparison.OrdinalIgnoreCase)) &&
            depth >= 5)
        {
            int minimumLivingWidth = (int)Math.Ceiling(92.0 / depth);
            if (length >= minimumLivingWidth + 4)
            {
                int livingWidth = Math.Clamp(
                    Math.Max(minimumLivingWidth, (int)Math.Round(length * 0.62)),
                    minimumLivingWidth, length - 4);
                int remainder = length - livingWidth;
                return [livingWidth, remainder];
            }
        }
        return SplitFrontage(length, target, random);
    }

    private static List<int> SplitFrontage(int cells, int requested, Random random)
    {
        int count = Math.Clamp(requested, 1, Math.Max(1, cells / 5));
        var segments = new List<int>(count);
        int remaining = cells;
        for (int i = 0; i < count; i++)
        {
            int left = count - i;
            int width = left == 1 ? remaining :
                Math.Clamp((int)Math.Round((double)remaining / left) +
                    random.Next(-2, 3), 5, remaining - 5 * (left - 1));
            segments.Add(width);
            remaining -= width;
        }
        return segments;
    }

    private static int GridFloor(double value) => (int)Math.Floor(value * 2 + 1e-7);
    private static int GridCeil(double value) => (int)Math.Ceiling(value * 2 - 1e-7);
    private static List<Point2> Rectangle(double x0, double y0, double x1, double y1) =>
    [
        new Point2(x0, y0),
        new Point2(x1, y0),
        new Point2(x1, y1),
        new Point2(x0, y1)
    ];
}
