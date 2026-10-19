using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// A model-safe catalog of C#-validated architectural actions. Model output
/// is only a candidate number; it is never parsed as a wall or room edit.
/// </summary>
public sealed record AiLayoutOption(
    int Id, string Kind, string Summary, double AreaSquareMetres,
    BuildingSuggestion Suggestion);

public static class AiLayoutRecommendationPlanner
{
    public static IReadOnlyList<AiLayoutOption> FindOptions(
        BuildingDocument source, int floorIndex, int maxResults = 20,
        string? goal = null)
    {
        if (!BuildingStoreys.HasFloor(source,floorIndex) || maxResults<1)
            return [];
        // Doorway intents are separate from resizing: a request to connect
        // two rooms must never silently change their dimensions instead.
        if (AiDoorPlacementPlanner.IsDoorwayGoal(goal))
            return AiDoorPlacementPlanner.FindOptions(
                source, floorIndex, goal!, maxResults);

        var options=new List<AiLayoutOption>();
        // Preserve hallway dead-end recovery from the existing prototype.
        foreach (var corner in AiLayoutCandidatePlanner.FindCandidates(
                     source,floorIndex,Math.Min(6,maxResults)))
        {
            if (!AiLayoutImprover.TryApply(source,floorIndex,
                JsonSerializer.Serialize(corner.Transfer),out var plan,out _))
                continue;
            options.Add(new AiLayoutOption(options.Count+1,
                "Reclaim unused hallway",corner.FromName+" → "+
                corner.ToName,corner.AreaSquareMetres,plan));
        }

        // Additional option type: a partition separating two rectangular
        // rooms can be nudged without relocating any existing doorway.
        foreach (var resize in AiRoomResizePlanner.FindCandidates(
                     source,floorIndex,Math.Min(7,maxResults)))
        {
            options.Add(new AiLayoutOption(options.Count+1,
                "Resize adjacent rooms",
                "Enlarge "+resize.GainingRoom+" at the expense of "+
                resize.LosingRoom+" ("+
                $"{resize.GainingAreaBefore:0.#} → "+
                $"{resize.GainingAreaAfter:0.#} m²)",
                resize.AreaSquareMetres,resize.Suggestion));
        }
        // Partial shared walls and orthogonal L-shaped rooms are validated
        // by the same shape-transfer engine, rather than trusting the LLM
        // to invent disconnected or overlapping room polygons.
        // For a specific goal, retain all safe directions and full-band
        // depths until AFTER spatial constraints are applied. Earlier
        // alphabetical/small-area truncation hid the upper bedroom strip.
        int partialLimit = string.IsNullOrWhiteSpace(goal)
            ? Math.Min(7, maxResults)
            : Math.Min(48, Math.Max(24, maxResults * 2));
        foreach (var partial in AiPartialRoomResizePlanner.FindCandidates(
                     source, floorIndex, partialLimit))
        {
            options.Add(new AiLayoutOption(options.Count + 1,
                "Resize partial / L-shaped rooms",
                "Enlarge " + partial.GainingRoom + " at the expense of " +
                partial.LosingRoom + " (" +
                $"{partial.GainingAreaBefore:0.#} → " +
                $"{partial.GainingAreaAfter:0.#} m²)",
                partial.AreaSquareMetres, partial.Suggestion));
        }
        var relevant = string.IsNullOrWhiteSpace(goal)
            ? options
            : AiLayoutSpatialIntent.Filter(source, floorIndex, goal, options)
                .ToList();
        // Renumber AFTER narrowing the shortlist. The IDs offered to the
        // model must match the final geometrically valid selection list.
        return relevant.Take(maxResults).Select((o, i) =>
            o with { Id = i + 1 }).ToArray();
    }

    public static string BuildSelectionPrompt(
        BuildingDocument source,int floorIndex,string goal,
        IReadOnlyList<AiLayoutOption> options)
    {
        if (!BuildingStoreys.HasFloor(source,floorIndex) ||
            options.Count==0)
            throw new ArgumentException("At least one validated edit is required.");
        var floor=BuildingStoreys.GetFloor(source,floorIndex);
        var prompt = new
        {
            goal,
            units="metres",
            floorIndex,
            rooms=floor.Rooms.Select(r=>new
            {
                name=r.Name,type=r.Type,
                areaSquareMetres=Math.Round(PolygonGeometry.Area(r.Outline),2)
            }),
            candidates=options.Select(o=>new
            {
                candidateId=o.Id,kind=o.Kind,
                summary=o.Summary,
                affectedAreaSquareMetres=o.AreaSquareMetres
            })
        };
        return "You are an architect reviewing an RPG building layout. "+
            "All proposed candidate improvements were ALREADY VERIFIED by "+
            "the C# geometry engine. Choose the ONE candidate that best "+
            "matches the user's goal. Some reclaim dead-end hallway corners; "+
            "others RESIZE adjacent rooms by MOVING a shared internal wall, "+
            "or reclaim a rectangular patch from a partial shared wall "+
            "between two rooms (including L-shaped rooms). "+
            "For a doorway request, candidates add a safe new doorway on "+
            "an existing shared partition without moving any room walls. "+
            "A named room, relative direction (ABOVE/BELOW/LEFT/RIGHT), "+
            "and words such as ALL or ENTIRE are mandatory spatial "+
            "constraints, not optional suggestions. ABOVE means smaller "+
            "Y coordinates in this floor plan. Only listed candidates "+
            "already match the requested location; never substitute "+
            "a different side. Consider practical room sizes. "+
            "Do not invent wall coordinates, new room IDs or extra edits. "+
            "Return ONLY JSON with candidateId (integer, one of the supplied "+
            "IDs) and reason (short string). No Markdown.\n\n"+
            JsonSerializer.Serialize(prompt,new JsonSerializerOptions
            {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase
            });
    }

    public static bool TryApplySelection(
        BuildingDocument source,int floorIndex,string modelJson,
        IReadOnlyList<AiLayoutOption> options,
        out BuildingSuggestion suggestion,out string reason)
    {
        suggestion = new BuildingSuggestion(BuildingStoreys.Copy(source),0,[],"");
        reason = "";
        if (modelJson.Length>4096)
        {
            reason="The AI response was unexpectedly large.";
            return false;
        }
        AiChoice? choice;
        try
        {
            choice=JsonSerializer.Deserialize<AiChoice>(modelJson,
                new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        }
        catch(JsonException)
        {
            reason="The AI did not return a valid numbered choice.";
            return false;
        }
        var option=options.FirstOrDefault(o=>o.Id==choice?.CandidateId);
        if(option is null ||
           !option.Suggestion.FloorIndexes.Contains(floorIndex) ||
           !BuildingStoreys.HasFloor(option.Suggestion.Building,floorIndex))
        {
            reason="AI selected an unknown candidate ID. Available IDs: "+
                string.Join(", ",options.Select(o=>o.Id));
            return false;
        }
        if (!BuildingStoreys.Validate(option.Suggestion.Building,out reason))
        {
            reason="Candidate failed revalidation: "+reason;
            return false;
        }
        // SameRoomLayout intentionally ignores doors and windows (it is
        // used by room regeneration). A doorway-only AI edit must not be
        // discarded simply because its room outlines remain unchanged.
        bool roomChanged = !BuildingGenerator.SameRoomLayout(source,
            option.Suggestion.Building, [floorIndex]);
        bool doorChanged = BuildingStoreys.GetFloor(source, floorIndex)
            .Doors.Count != BuildingStoreys.GetFloor(
                option.Suggestion.Building, floorIndex).Doors.Count;
        if (!roomChanged && !doorChanged)
        {
            reason="The selected candidate doesn't change the floor.";
            return false;
        }
        string explanation=choice?.Reason ?? "";
        explanation=explanation.Length>200 ? explanation[..200] : explanation;
        suggestion=new BuildingSuggestion(
            BuildingStoreys.Copy(option.Suggestion.Building),
            0,[floorIndex],"AI IMPROVEMENT DRAFT — "+option.Kind+
            ": "+option.Summary+ $"; {option.AreaSquareMetres:0.##} m² affected. "+
            explanation);
        reason="";
        return true;
    }

    private sealed class AiChoice
    {
        public int CandidateId {get;set;}
        public string Reason {get;set;}="";
    }
}
