using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A geometry-first shortlist for AI improvement. The language model ranks
/// exact, fully validated edits; it never gets to invent room IDs or vertices.
/// Only corridor corner/notch transfers are considered in this prototype.
/// </summary>
public sealed record AiLayoutCandidate(
    int Id, AiRoomTransfer Transfer, string FromName, string ToName,
    double AreaSquareMetres);

public static class AiLayoutCandidatePlanner
{
    private const double Eps = 1e-5;
    private static readonly double[] ExtraDepths =
        [0.4, 0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3];

    public static IReadOnlyList<AiLayoutCandidate> FindCandidates(
        BuildingDocument source, int floorIndex, int maxResults = 16)
    {
        if (!BuildingStoreys.HasFloor(source, floorIndex) ||
            maxResults < 1) return [];
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var halls = floor.Rooms.Where(r =>
            r.Type is "Hallway" or "Corridor").ToArray();
        var targets = floor.Rooms.Where(r =>
            r.Type is not ("Hallway" or "Corridor")).ToArray();
        var proposed = new List<(AiRoomTransfer Edit, string Hall,
            string Target, double Area, int ExposedSides)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hall in halls)
        foreach (var target in targets)
        {
            if (!Orthogonal(hall.Outline) || !Orthogonal(target.Outline) ||
                !BuildingConnectivity.ShareWall(hall.Outline,
                    target.Outline, 0.4))
                continue;
            foreach (var h in Edges(hall.Outline))
            foreach (var t in Edges(target.Outline))
            {
                bool horizontal = Math.Abs(h.A.Y - h.B.Y) < Eps &&
                                  Math.Abs(t.A.Y - t.B.Y) < Eps &&
                                  Math.Abs(h.A.Y - t.A.Y) < Eps;
                bool vertical = Math.Abs(h.A.X - h.B.X) < Eps &&
                                Math.Abs(t.A.X - t.B.X) < Eps &&
                                Math.Abs(h.A.X - t.A.X) < Eps;
                if (!horizontal && !vertical) continue;
                double edgeCoord = horizontal ? h.A.Y : h.A.X;
                double low = Math.Max(
                    Math.Min(horizontal ? h.A.X : h.A.Y,
                             horizontal ? h.B.X : h.B.Y),
                    Math.Min(horizontal ? t.A.X : t.A.Y,
                             horizontal ? t.B.X : t.B.Y));
                double high = Math.Min(
                    Math.Max(horizontal ? h.A.X : h.A.Y,
                             horizontal ? h.B.X : h.B.Y),
                    Math.Max(horizontal ? t.A.X : t.A.Y,
                             horizontal ? t.B.X : t.B.Y));
                if (high - low < 0.4) continue;

                // Split the common wall at every actual room/hall corner,
                // so a model cannot inadvertently absorb a bedroom door.
                var cuts = hall.Outline.Concat(target.Outline)
                    .Select(p => horizontal ? p.X : p.Y)
                    .Append(low).Append(high)
                    .Where(v => v >= low - Eps && v <= high + Eps)
                    .Distinct().Order().ToArray();
                var depths = hall.Outline.Select(p =>
                        Math.Abs((horizontal ? p.Y : p.X) - edgeCoord))
                    .Concat(ExtraDepths).Distinct()
                    .Where(x => x >= 0.2 && x <= 4).Order().ToArray();

                foreach (int sign in new[] { -1, 1 })
                {
                    Point2 probe = horizontal
                        ? new((low + high) / 2, edgeCoord + sign * 0.02)
                        : new(edgeCoord + sign * 0.02, (low + high) / 2);
                    if (!PolygonGeometry.ContainsPoint(hall.Outline, probe) ||
                        PolygonGeometry.ContainsPoint(target.Outline, probe))
                        continue;

                    for (int i = 0; i < cuts.Length - 1; i++)
                    for (int j = i + 1; j < cuts.Length; j++)
                    foreach (double depth in depths)
                    {
                        double x0 = horizontal ? cuts[i] :
                            Math.Min(edgeCoord, edgeCoord + sign * depth);
                        double x1 = horizontal ? cuts[j] :
                            Math.Max(edgeCoord, edgeCoord + sign * depth);
                        double y0 = horizontal ?
                            Math.Min(edgeCoord, edgeCoord + sign * depth) : cuts[i];
                        double y1 = horizontal ?
                            Math.Max(edgeCoord, edgeCoord + sign * depth) : cuts[j];
                        double area = (x1 - x0) * (y1 - y0);
                        if (area < 0.25 || area > 12) continue;
                        string key = FormattableString.Invariant(
                            $"{hall.Id}|{target.Id}|{x0:R}|{y0:R}|{x1:R}|{y1:R}");
                        if (!keys.Add(key)) continue;
                        var rectangle = Rect(x0, y0, x1, y1);
                        if (!FloorGeometry.IsWithinPolygon(rectangle, hall.Outline))
                            continue;

                        // Transfer only a redundant hallway corner, not an
                        // arbitrary slab cut that narrows the main corridor.
                        // One rectangle side borders the destination room;
                        // two other sides must already be outer hall edges.
                        int exposed = Edges(rectangle).Count(edge =>
                            OnSameBoundary(edge.A, edge.B, hall.Outline));
                        if (exposed < 3) continue;

                        proposed.Add((new AiRoomTransfer
                        {
                            FloorIndex = floorIndex,
                            FromRoomId = hall.Id, ToRoomId = target.Id,
                            XMin = x0, YMin = y0, XMax = x1, YMax = y1,
                            Reason = "Reclaim a redundant hall corner"
                        }, hall.Name, target.Name, area, exposed));
                    }
                }
            }
        }

        // Prioritize patches whose three sides are already existing hall
        // boundary (true corners), then smaller, more practical alcoves.
        var ordered = proposed.OrderByDescending(p => p.ExposedSides)
            .ThenBy(p => p.Area).ThenBy(p => p.Hall, StringComparer.Ordinal)
            .ThenBy(p => p.Target, StringComparer.Ordinal).ToArray();
        var valid = new List<AiLayoutCandidate>();
        int attempts = 0;
        foreach (var p in ordered)
        {
            if (attempts++ >= 400 || valid.Count >= maxResults) break;
            string editJson = JsonSerializer.Serialize(p.Edit);
            if (!AiLayoutImprover.TryApply(source, floorIndex, editJson,
                    out _, out _))
                continue;
            valid.Add(new AiLayoutCandidate(valid.Count + 1, p.Edit,
                p.Hall, p.Target, p.Area));
        }
        return valid;
    }

    public static string BuildSelectionPrompt(
        BuildingDocument source, int floorIndex, string goal,
        IReadOnlyList<AiLayoutCandidate> candidates)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("At least one validated candidate is required.");
        var floor = BuildingStoreys.GetFloor(source, floorIndex);
        var payload = new
        {
            units = "metres",
            floor = BuildingStoreys.FloorLabel(floorIndex),
            goal,
            rooms = floor.Rooms.Select(r =>
                new { r.Name, r.Type, area = PolygonGeometry.Area(r.Outline) }),
            candidates = candidates.Select(c => new
            {
                candidateId = c.Id,
                from = c.FromName,
                to = c.ToName,
                areaGained = c.AreaSquareMetres,
                rectangle = new
                {
                    c.Transfer.XMin, c.Transfer.YMin,
                    c.Transfer.XMax, c.Transfer.YMax
                }
            })
        };
        return "Choose the BEST improvement from the supplied ALREADY-VALIDATED " +
            "architectural changes for this RPG building. Do not invent " +
            "coordinates, room IDs, floors, or new changes. Consider the " +
            "user's goal, practical circulation, useful alcoves and sensible " +
            "room proportions. Candidates are numbered from 1. " +
            "Return ONLY JSON with properties candidateId (integer) and " +
            "reason (short string) explaining your selection. Use an ID " +
            "that exists in the candidates array. No Markdown.\n\n" +
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }

    public static bool TryApplySelection(
        BuildingDocument source, int floorIndex, string modelJson,
        IReadOnlyList<AiLayoutCandidate> candidates,
        out BuildingSuggestion suggestion, out string reason)
    {
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(source),
            0, [], "");
        reason = "";
        if (modelJson.Length > 4096)
        {
            reason = "AI response was unexpectedly large.";
            return false;
        }
        AiCandidateChoice? choice;
        try
        {
            choice = JsonSerializer.Deserialize<AiCandidateChoice>(modelJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            reason = "AI did not return a valid candidate selection in JSON.";
            return false;
        }
        var candidate = candidates.FirstOrDefault(c =>
            c.Id == choice?.CandidateId);
        if (candidate is null || candidate.Transfer.FloorIndex != floorIndex)
        {
            reason = "AI selected an unknown candidate ID. Available IDs: " +
                string.Join(", ", candidates.Select(c => c.Id));
            return false;
        }
        var edit = candidate.Transfer;
        // Keep an untrusted model explanation out of the geometry and bound
        // the string length before including it in the displayed proposal.
        edit = new AiRoomTransfer
        {
            FloorIndex = edit.FloorIndex,
            FromRoomId = edit.FromRoomId, ToRoomId = edit.ToRoomId,
            XMin = edit.XMin, XMax = edit.XMax,
            YMin = edit.YMin, YMax = edit.YMax,
            Reason = (choice?.Reason ?? "").Length > 200
                ? choice!.Reason[..200] : choice?.Reason ?? ""
        };
        return AiLayoutImprover.TryApply(source, floorIndex,
            JsonSerializer.Serialize(edit), out suggestion, out reason);
    }

    private sealed class AiCandidateChoice
    {
        public int CandidateId { get; set; }
        public string Reason { get; set; } = "";
    }

    private static IEnumerable<(Point2 A, Point2 B)> Edges(
        IReadOnlyList<Point2> ring) =>
        Enumerable.Range(0, ring.Count).Select(i =>
            (ring[i], ring[(i + 1) % ring.Count]));

    private static bool Orthogonal(IReadOnlyList<Point2> ring) =>
        Edges(ring).All(e => Math.Abs(e.A.X - e.B.X) < Eps ||
                             Math.Abs(e.A.Y - e.B.Y) < Eps);

    private static List<Point2> Rect(double x0, double y0,
        double x1, double y1) =>
        [new(x0,y0), new(x1,y0), new(x1,y1), new(x0,y1)];

    private static bool OnSameBoundary(Point2 start, Point2 end,
        IReadOnlyList<Point2> outline) =>
        Edges(outline).Any(e =>
        {
            var a = PolygonGeometry.ClosestPointOnSegment(start, e.A, e.B);
            var b = PolygonGeometry.ClosestPointOnSegment(end, e.A, e.B);
            return a.DistanceTo(start) < Eps && b.DistanceTo(end) < Eps;
        });
}
