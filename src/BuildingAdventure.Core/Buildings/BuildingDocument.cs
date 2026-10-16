using System.Text.Json;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>The versioned, renderer-independent building file format.</summary>
public sealed class BuildingDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled Building";
    public List<Point2> Perimeter { get; set; } = new();
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
        if (document.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported document schema version: {document.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 200)
            throw new InvalidDataException("Building name must contain between 1 and 200 characters.");
        if (document.Perimeter is null)
            throw new InvalidDataException("The perimeter is missing.");
        if (document.Perimeter.Count > 0 &&
            !PolygonGeometry.IsValidOutline(document.Perimeter, out string reason))
            throw new InvalidDataException($"Invalid perimeter: {reason}");
    }
}
