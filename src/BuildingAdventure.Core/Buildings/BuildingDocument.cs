using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>The versioned, renderer-independent building file format.</summary>
public sealed class BuildingDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled Building";
    // 0 = metric (1m visual grid, 0.5m editing snap);
    // 5/10 = imperial tabletop squares (metres remain canonical geometry).
    public int GridFeet { get; set; }

    public List<Point2> Perimeter { get; set; } = new();
    public List<RoomDefinition> Rooms { get; set; } = new();
    public List<DoorDefinition> Doors { get; set; } = new();
    public List<WindowDefinition> Windows { get; set; } = new();
    public WallThicknessSettings WallThickness { get; set; } = new();

    // Version 2. Ground floor retains the original root properties so
    // existing v0.1-v0.4 JSON files load without conversion.
    public List<StoreyDefinition> UpperFloors { get; set; } = new();
    // BasementFloors[0] is one level below ground (-1), [1] is -2, etc.
    public List<StoreyDefinition> BasementFloors { get; set; } = new();
    // When false, the building consists solely of underground levels.
    // Root-level geometry remains for legacy document compatibility.
    public bool HasGroundFloor { get; set; } = true;
    public List<StaircaseDefinition> Staircases { get; set; } = new();
}

public static class BuildingDocumentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Serialize(BuildingDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public static BuildingDocument Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<BuildingDocument>(json, Options)
                       ?? throw new InvalidDataException("The file does not contain a building.");
        Validate(document);
        return document;
    }

    private static void Validate(BuildingDocument document)
    {
        if (document.Rooms is null || document.Doors is null || document.Windows is null)
            throw new InvalidDataException("Rooms, doors or windows collection is missing.");
        if (document.SchemaVersion is not (1 or 2))
            throw new InvalidDataException($"Unsupported document schema version: {document.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 200)
            throw new InvalidDataException("Building name must contain between 1 and 200 characters.");
        if (document.Perimeter is null)
            throw new InvalidDataException("The perimeter is missing.");
        if (!BuildingStoreys.Validate(document, out string reason))
            throw new InvalidDataException($"Invalid building: {reason}");
    }
}
