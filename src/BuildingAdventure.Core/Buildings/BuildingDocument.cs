using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>The versioned, renderer-independent building file format.</summary>
public sealed class BuildingDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled Building";
    public List<Point2> Perimeter { get; set; } = new();
    public List<RoomDefinition> Rooms { get; set; } = new();
    public List<DoorDefinition> Doors { get; set; } = new();

    // Version 2. Ground floor retains the original root properties so
    // existing v0.1-v0.4 JSON files load without conversion.
    public List<StoreyDefinition> UpperFloors { get; set; } = new();
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
        if (document.Rooms is null || document.Doors is null)
            throw new InvalidDataException("Rooms or doors collection is missing.");
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
