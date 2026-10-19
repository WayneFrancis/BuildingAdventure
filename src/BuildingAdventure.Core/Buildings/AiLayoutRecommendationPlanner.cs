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
        BuildingDocument source, int floorIndex, int maxResults = 20)
    {
        if (!BuildingStoreys.HasFloor(source,floorIndex) || maxResults<1)
            return [];
        var options=new List<AiLayoutOption>();
        // Preserve hallway dead-end recovery from the existing prototype.
        foreach (var corner in AiLayoutCandidatePlanner.FindCandidates(
                     source,floorIndex,Math.Min(8,maxResults)))
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
                     source,floorIndex,Math.Min(12,maxResults)))
        {
            options.Add(new AiLayoutOption(options.Count+1,
                "Resize adjacent rooms",
                "Enlarge "+resize.GainingRoom+" at the expense of "+
                resize.LosingRoom+" ("+
                $"{resize.GainingAreaBefore:0.#} → "+
                $"{resize.GainingAreaAfter:0.#} m²)",
                resize.AreaSquareMetres,resize.Suggestion));
        }
        return options.Take(maxResults).ToArray();
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
            "others RESIZE adjacent rooms by MOVING a shared internal wall. "+
            "Consider practical room sizes, accessibility and adjacency. "+
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
        if (BuildingGenerator.SameRoomLayout(source,
            option.Suggestion.Building,[floorIndex]))
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
