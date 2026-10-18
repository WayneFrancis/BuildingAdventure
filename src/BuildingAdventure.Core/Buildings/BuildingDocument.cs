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
    public List<ChimneyDefinition> Chimneys { get; set; } = new();
}

/// <summary>
/// A continuous masonry chimney serving a fireplace on every level it
/// intersects. Placement may be against an exterior or interior wall;
/// the world-space stack location does not move between storeys.
/// </summary>
public sealed class ChimneyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Point2 Centre { get; set; }
    public double Width { get; set; } = 0.8;
    public double Depth { get; set; } = 0.8;
    public bool Exterior { get; set; } = true;

    // Fireplaces belong to the inhabited level. Other intersecting storeys
    // show the continuing flue only, even where it crosses a bedroom.
    public int HearthFloorIndex { get; set; }
    public List<Guid> HearthRoomIds { get; set; } = new();

    // A kitchen and living room on opposite sides of a shared partition
    // can use two hearth faces drawing from the SAME continuous flue.
    public Point2? OtherHearthCentre { get; set; }

    public ChimneyDefinition Copy() => new()
    {
        Id = Id, Centre = Centre, Width = Width,
        Depth = Depth, Exterior = Exterior,
        HearthFloorIndex = HearthFloorIndex,
        HearthRoomIds = HearthRoomIds.ToList(),
        OtherHearthCentre = OtherHearthCentre
    };
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
