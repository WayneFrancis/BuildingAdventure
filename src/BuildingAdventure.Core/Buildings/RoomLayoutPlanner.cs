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
