using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A constrained AI-generated architectural edit. The model describes one
/// rectangular transfer; C# owns all polygon construction and validation.
/// </summary>
public sealed class AiRoomTransfer
{
    public int FloorIndex { get; set; }
    public Guid FromRoomId { get; set; }
    public Guid ToRoomId { get; set; }
    public double XMin { get; set; }
    public double YMin { get; set; }
    public double XMax { get; set; }
    public double YMax { get; set; }
    public string Reason { get; set; } = "";
}

public static class AiLayoutImprover
{
    private const double Eps = 1e-6;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The prompt is a design request, not an instruction to overwrite the
    /// project. The full room/door geometry gives the model exact units and
    /// valid IDs. Only a constrained edit is accepted from its response.
    /// </summary>
    public static string BuildPrompt(BuildingDocument source, int floorIndex,
        string goal)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex))
            throw new ArgumentOutOfRangeException(nameof(floorIndex));
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var payload = new
        {
            floorIndex,
            units = "metres",
            goal,
            perimeter = floor.Perimeter,
            rooms = floor.Rooms.Select(r => new
            {
                id = r.Id, name = r.Name, type = r.Type,
                outline = r.Outline
            }),
            doors = floor.Doors.Select(d => new { centre = d.Centre, d.Width }),
            staircases = source.Staircases.Where(s =>
                s.LowerFloorIndex == floorIndex ||
                s.LowerFloorIndex + 1 == floorIndex)
                .Select(s => new
                {
                    s.Centre, s.Length, s.Width, s.RotationDegrees,
                    s.LandingLength, s.OpeningExtraWidth, s.OpeningExtraLength
                }),
            chimneys = source.Chimneys.Where(c => c.HearthFloorIndex == floorIndex)
                .Select(c => new { c.Centre, c.Width, c.Depth })
        };
        return "You are planning a practical tabletop-RPG building, not " +
            "editing software. Choose ONE useful, safe improvement that " +
            "transfers an AXIS-ALIGNED RECTANGLE of corridor/hallway into " +
            "a room touching it. The source must have type Hallway or " +
            "Corridor; the destination must be a non-hallway room. " +
            "Give exact room UUIDs already present in the data. " +
            "All rectangle coordinates must be in metres (xMin,yMin,xMax,yMax), " +
            "with xMin < xMax and yMin < yMax. The rectangle must lie " +
            "entirely INSIDE the source hallway and touch a shared wall " +
            "with its destination room. The source hallway must remain " +
            "connected and all existing doorways, stair openings and " +
            "landings must be preserved; do not cut away any doorway. " +
            "Prefer redundant hallway dead ends and small useful alcoves. " +
            "Do not invent room IDs, move existing stairs, or propose new " +
            "rooms/doors. If no safe transfer exists, return an invalid " +
            "zero-area rectangle and explain why. " +
            "Return ONLY JSON with these properties: floorIndex (integer), " +
            "fromRoomId (UUID string), toRoomId (UUID string), xMin, yMin, " +
            "xMax, yMax (numbers), reason (short string). " +
            "Do not include Markdown or commentary outside JSON.\n\n" +
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }

    public static bool TryApply(BuildingDocument source, int activeFloor,
        string modelJson, out BuildingSuggestion suggestion, out string reason) =>
        TryApplyCore(source, activeFloor, modelJson, true,
            out suggestion, out reason);

    // Only call this with C#-generated, prevalidated candidate rectangles.
    // Unlike the public legacy hallway transfer, both rooms are ordinary
    // rooms; an L-shaped outline is permitted when its edges are orthogonal.
    internal static bool TryApplyRoomTransfer(BuildingDocument source,
        int activeFloor, AiRoomTransfer edit,
        out BuildingSuggestion suggestion, out string reason) =>
        TryApplyCore(source, activeFloor, JsonSerializer.Serialize(edit),
            false, out suggestion, out reason);

    private static bool TryApplyCore(BuildingDocument source, int activeFloor,
        string modelJson, bool hallwaySource,
        out BuildingSuggestion suggestion, out string reason)
    {
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(source),
            0, [], "");
        reason = "";
        if (!BuildingStoreys.HasFloor(source, activeFloor))
        {
            reason = "The requested floor does not exist.";
            return false;
        }
        AiRoomTransfer? edit;
        try
        {
            if (modelJson.Length > 16384)
            {
                reason = "The AI response was unexpectedly large.";
                return false;
            }
            edit = JsonSerializer.Deserialize<AiRoomTransfer>(modelJson, Json);
        }
        catch (JsonException)
        {
            reason = "The model did not return valid JSON. Try a different model or goal.";
            return false;
        }
        if (edit is null || edit.FloorIndex != activeFloor ||
            edit.FromRoomId == Guid.Empty || edit.ToRoomId == Guid.Empty ||
            edit.FromRoomId == edit.ToRoomId ||
            !new[] { edit.XMin, edit.YMin, edit.XMax, edit.YMax }
                .All(double.IsFinite) ||
            edit.XMax - edit.XMin < 0.2 || edit.YMax - edit.YMin < 0.2)
        {
            reason = "The proposal has invalid floor, room IDs or rectangle dimensions.";
            return false;
        }
        double area = (edit.XMax - edit.XMin) * (edit.YMax - edit.YMin);
        if (area < 0.25 || area > 12)
        {
            reason = "AI transfers must be between 0.25 and 12 square metres.";
            return false;
        }
        var originalFloor = BuildingStoreys.GetFloor(source, activeFloor);
        var hall = originalFloor.Rooms.FirstOrDefault(r => r.Id == edit.FromRoomId);
        var target = originalFloor.Rooms.FirstOrDefault(r => r.Id == edit.ToRoomId);
        if (hall is null || target is null ||
            (hallwaySource
                ? hall.Type is not ("Hallway" or "Corridor") ||
                  target.Type is "Hallway" or "Corridor"
                : hall.Type is "Hallway" or "Corridor" ||
                  target.Type is "Hallway" or "Corridor"))
        {
            reason = hallwaySource
                ? "AI must move area from an existing hallway to a different room."
                : "Both rooms must exist and must not be hallways or corridors.";
            return false;
        }
        var rectangle = new List<Point2>
        {
            new(edit.XMin,edit.YMin),new(edit.XMax,edit.YMin),
            new(edit.XMax,edit.YMax),new(edit.XMin,edit.YMax)
        };
        if (!FloorGeometry.IsWithinPolygon(rectangle, hall.Outline))
        {
            reason = "The proposed rectangle is not completely inside the hallway.";
            return false;
        }
        if (!BuildingConnectivity.ShareWall(target.Outline, rectangle, 0.2))
        {
            reason = "The selected patch does not share a wall with the destination room.";
            return false;
        }
        if (!TryTransferShapes(hall.Outline, target.Outline, edit,
                out var smallerHall, out var largerRoom, out reason))
            return false;

        var candidate = BuildingStoreys.Copy(source);
        var floor = BuildingStoreys.GetFloor(candidate, activeFloor);
        floor.Rooms.Single(r => r.Id == edit.FromRoomId).Outline = smallerHall;
        floor.Rooms.Single(r => r.Id == edit.ToRoomId).Outline = largerRoom;
        BuildingStoreys.SetFloor(candidate, activeFloor, floor);
        if (!BuildingStoreys.Validate(candidate, out var validation))
        {
            reason = "The proposed change breaks the building: " + validation;
            return false;
        }
        if (!AiOpeningProtection.PreservesExistingDoorAccess(
                source, candidate, activeFloor))
        {
            reason = "The wall edit would block an existing doorway or " +
                "change which rooms it connects.";
            return false;
        }
        // Geometry must remain completely allocated, not create a sliver or
        // secretly discard a piece of the old floor plan.
        double before = RoomLayoutPlanner.UnassignedArea(originalFloor);
        double after = RoomLayoutPlanner.UnassignedArea(floor);
        if (Math.Abs(before - after) > 0.01 ||
            Math.Abs(PolygonGeometry.Area(smallerHall) -
                (PolygonGeometry.Area(hall.Outline) - area)) > 0.01 ||
            Math.Abs(PolygonGeometry.Area(largerRoom) -
                (PolygonGeometry.Area(target.Outline) + area)) > 0.01)
        {
            reason = "The edit failed its floor-area conservation check.";
            return false;
        }
        suggestion = new BuildingSuggestion(candidate, 0, [activeFloor],
            "AI IMPROVEMENT DRAFT — " + hall.Name + " → " + target.Name +
            $" ({area:0.##} m²). " + (edit.Reason.Length > 240
                ? edit.Reason[..240] : edit.Reason));
        reason = "";
        return true;
    }

    private readonly record struct Vertex(int X, int Y);
    private readonly record struct Edge(Vertex Start, Vertex End);

    // Boolean union/difference for a rectangle and existing orthogonal
    // rooms. Build occupied grid cells at EVERY source corner, trace exposed
    // boundary edges, and insist on one simple polygon without holes.
    // This also allows useful L-shaped destination rooms.
    private static bool TryTransferShapes(
        IReadOnlyList<Point2> hall, IReadOnlyList<Point2> target,
        AiRoomTransfer edit, out List<Point2> newHall,
        out List<Point2> newTarget, out string reason)
    {
        newHall = []; newTarget = [];
        reason = "";
        var xs = hall.Concat(target).Select(p => p.X)
            .Append(edit.XMin).Append(edit.XMax).Distinct().Order().ToArray();
        var ys = hall.Concat(target).Select(p => p.Y)
            .Append(edit.YMin).Append(edit.YMax).Distinct().Order().ToArray();
        if (xs.Length > 80 || ys.Length > 80 ||
            !IsOrthogonal(hall) || !IsOrthogonal(target))
        {
            reason = "This first AI prototype supports orthogonal rooms only.";
            return false;
        }
        bool[,] hallCells = new bool[xs.Length - 1, ys.Length - 1];
        bool[,] targetCells = new bool[xs.Length - 1, ys.Length - 1];
        for (int x = 0; x < xs.Length - 1; x++)
        for (int y = 0; y < ys.Length - 1; y++)
        {
            var point = new Point2((xs[x] + xs[x+1]) / 2,
                                   (ys[y] + ys[y+1]) / 2);
            bool patch = point.X > edit.XMin && point.X < edit.XMax &&
                         point.Y > edit.YMin && point.Y < edit.YMax;
            bool inHall = PolygonGeometry.ContainsPoint(hall, point);
            bool inTarget = PolygonGeometry.ContainsPoint(target, point);
            if (patch && (!inHall || inTarget))
            {
                reason = "The transfer overlaps space outside its source hallway.";
                return false;
            }
            hallCells[x,y] = inHall && !patch;
            targetCells[x,y] = inTarget || patch;
        }
        if (!Trace(hallCells, xs, ys, out newHall) ||
            !Trace(targetCells, xs, ys, out newTarget))
        {
            reason = "That patch would split a room, make a hole or create an invalid wall.";
            return false;
        }
        return true;
    }

    private static bool IsOrthogonal(IReadOnlyList<Point2> shape) =>
        Enumerable.Range(0, shape.Count).All(i =>
            Math.Abs(shape[i].X-shape[(i+1)%shape.Count].X)<Eps ||
            Math.Abs(shape[i].Y-shape[(i+1)%shape.Count].Y)<Eps);

    private static bool Trace(bool[,] filled, double[] xs, double[] ys,
        out List<Point2> outline)
    {
        outline = [];
        int width = filled.GetLength(0), height = filled.GetLength(1);
        var edges = new List<Edge>();
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            if (!filled[x,y]) continue;
            if (y == 0 || !filled[x,y-1])
                edges.Add(new(new(x,y),new(x+1,y)));
            if (x == width-1 || !filled[x+1,y])
                edges.Add(new(new(x+1,y),new(x+1,y+1)));
            if (y == height-1 || !filled[x,y+1])
                edges.Add(new(new(x+1,y+1),new(x,y+1)));
            if (x == 0 || !filled[x-1,y])
                edges.Add(new(new(x,y+1),new(x,y)));
        }
        if (edges.Count < 4) return false;
        var next = new Dictionary<Vertex, Vertex>();
        foreach (var edge in edges)
            if (!next.TryAdd(edge.Start, edge.End)) return false;
        if (next.Count != edges.Count) return false;
        var vertices = new List<Vertex>();
        var visited = new HashSet<Vertex>();
        Vertex cursor = edges[0].Start;
        while (visited.Add(cursor))
        {
            vertices.Add(cursor);
            if (!next.TryGetValue(cursor, out cursor)) return false;
        }
        if (cursor != edges[0].Start || vertices.Count != edges.Count)
            return false; // disjoint room/hole
        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[(i+vertices.Count-1)%vertices.Count];
            var b = vertices[i];
            var c = vertices[(i+1)%vertices.Count];
            if ((a.X == b.X && b.X == c.X) ||
                (a.Y == b.Y && b.Y == c.Y))
                continue;
            outline.Add(new Point2(xs[b.X], ys[b.Y]));
        }
        return outline.Count >= 4 &&
            PolygonGeometry.IsValidOutline(outline, out _);
    }
}
