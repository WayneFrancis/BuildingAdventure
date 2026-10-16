using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Core.Buildings;

/// <summary>A room occupies a closed polygon in the current ground-floor plan.</summary>
public sealed class RoomDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Room";
    public string Type { get; set; } = "Other";
    public List<Point2> Outline { get; set; } = new();

    public RoomDefinition Copy() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        Outline = Outline.ToList()
    };
}

/// <summary>Door centre and opening width, in metres; attachment is derived from the wall geometry.</summary>
public sealed class DoorDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Point2 Centre { get; set; }
    public double Width { get; set; } = 0.9;

    public DoorDefinition Copy() => new() { Id = Id, Centre = Centre, Width = Width };
}


/// <summary>A glazed opening on an exterior wall. Width is along the wall in metres.</summary>
public sealed class WindowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Point2 Centre { get; set; }
    public double Width { get; set; } = 1.2;

    public WindowDefinition Copy() => new() { Id = Id, Centre = Centre, Width = Width };
}
