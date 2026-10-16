using System.IO;
using BuildingAdventure.Core.Buildings;
using BuildingAdventure.Core.Geometry;

int passed = 0;
int failed = 0;

void Check(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        failed++;
    }
}

void Equal(double actual, double expected)
{
    if (Math.Abs(actual - expected) > 1e-7)
        throw new Exception($"Expected {expected}, got {actual}.");
}

void Assert(bool condition, string explanation = "Assertion failed")
{
    if (!condition) throw new Exception(explanation);
}

var rectangle = new List<Point2>
{
    new(0, 0), new(10, 0), new(10, 8), new(0, 8)
};
var concave = new List<Point2>
{
    new(0, 0), new(6, 0), new(6, 2),
    new(2, 2), new(2, 5), new(0, 5)
};

Check("Rectangle area, perimeter and validity", () =>
{
    Assert(PolygonGeometry.IsValidOutline(rectangle, out _));
    Equal(PolygonGeometry.Area(rectangle), 80);
    Equal(PolygonGeometry.Perimeter(rectangle), 36);
});

Check("Clockwise and counterclockwise areas agree", () =>
{
    Equal(PolygonGeometry.Area(rectangle.AsEnumerable().Reverse().ToList()), 80);
});

Check("Diagonal walls and triangular area", () =>
{
    var triangle = new List<Point2> { new(0, 0), new(4, 0), new(2, 3) };
    Assert(PolygonGeometry.IsValidOutline(triangle, out _));
    Equal(PolygonGeometry.Area(triangle), 6);
    Equal(PolygonGeometry.Perimeter(triangle), 4 + 2 * Math.Sqrt(13));
});

Check("Concave building and point containment", () =>
{
    Assert(PolygonGeometry.IsValidOutline(concave, out _));
    Equal(PolygonGeometry.Area(concave), 18);
    Assert(PolygonGeometry.ContainsPoint(concave, new(1, 4)));
    Assert(!PolygonGeometry.ContainsPoint(concave, new(5, 4)));
    Assert(PolygonGeometry.ContainsPoint(concave, new(6, 1)));
});

Check("Reject crossing walls", () =>
{
    var bowTie = new List<Point2>
    {
        new(0, 0), new(4, 4), new(0, 4), new(4, 0)
    };
    Assert(!PolygonGeometry.IsValidOutline(bowTie, out _));
});

Check("Reject reversed, overlapping walls", () =>
{
    var doubledBack = new List<Point2>
    {
        new(0, 0), new(4, 0), new(2, 0), new(2, 4), new(0, 4)
    };
    Assert(!PolygonGeometry.IsValidOutline(doubledBack, out _));
});

Check("Reject fewer than three corners", () =>
{
    Assert(!PolygonGeometry.IsValidOutline([new(0, 0), new(1, 1)], out _));
});

Check("Project point onto angled wall", () =>
{
    var p = PolygonGeometry.ClosestPointOnSegment(
        new Point2(3, 0), new Point2(0, 0), new Point2(4, 4));
    Equal(p.X, 1.5);
    Equal(p.Y, 1.5);
});

Check("JSON round trip preserves the coordinates", () =>
{
    var document = new BuildingDocument { Name = "Merchant House", Perimeter = concave };
    string json = BuildingDocumentJson.Serialize(document);
    var loaded = BuildingDocumentJson.Deserialize(json);
    Assert(loaded.Name == document.Name);
    Assert(loaded.Perimeter.SequenceEqual(concave));
    Assert(loaded.SchemaVersion == 1);
});

Check("JSON rejects incompatible schema versions", () =>
{
    try
    {
        BuildingDocumentJson.Deserialize("""{"schemaVersion":99,"name":"Test","perimeter":[]}""");
        throw new Exception("Expected InvalidDataException.");
    }
    catch (InvalidDataException) { }
});

Check("JSON rejects intersecting perimeter", () =>
{
    try
    {
        BuildingDocumentJson.Deserialize(
            """{"schemaVersion":1,"name":"Test","perimeter":[{"x":0,"y":0},{"x":4,"y":4},{"x":0,"y":4},{"x":4,"y":0}]}""");
        throw new Exception("Expected InvalidDataException.");
    }
    catch (InvalidDataException) { }
});

Check("Older v0.1 JSON still loads with no rooms", () =>
{
    var oldFile = """{"schemaVersion":1,"name":"Old Building","perimeter":[{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":8},{"x":0,"y":8}]}""";
    var document = BuildingDocumentJson.Deserialize(oldFile);
    Assert(document.Rooms.Count == 0 && document.Doors.Count == 0);
});

Check("Adjacent rooms share one interior wall", () =>
{
    var left = new RoomDefinition
    {
        Name = "Shop", Type = "Shop",
        Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)]
    };
    var right = new RoomDefinition
    {
        Name = "Home", Type = "Living room",
        Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)]
    };
    var building = new BuildingDocument
    {
        Name = "Merchant House",
        Perimeter = rectangle.ToList(),
        Rooms = [left, right],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4) }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    var walls = FloorGeometry.BuildWalls(building.Perimeter, building.Rooms);
    Assert(walls.Count == 7, $"Expected 7 unique wall fragments; got {walls.Count}.");
    Assert(walls.Count(w => !w.IsExterior) == 1);
    Assert(walls.Single(w => !w.IsExterior).Length == 8);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(building));
    Assert(loaded.Rooms.Count == 2 && loaded.Doors.Count == 1);
    Assert(loaded.Rooms[0].Name == "Shop");
});

Check("Reject partly overlapping rooms", () =>
{
    var document = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "A", Outline = [new(0, 0), new(6, 0), new(6, 8), new(0, 8)] },
            new RoomDefinition { Name = "B", Outline = [new(4, 0), new(10, 0), new(10, 8), new(4, 8)] }
        ]
    };
    Assert(!FloorGeometry.Validate(document, out _));
});

Check("Reject rooms that cross a concave exterior notch", () =>
{
    var document = new BuildingDocument
    {
        Perimeter = concave.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Bad Room",
                Outline = [new(1, 4), new(5, 1), new(1, 1)] }
        ]
    };
    Assert(!FloorGeometry.Validate(document, out _));
});

Check("Reject doors detached from walls", () =>
{
    var document = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(4, 4) }]
    };
    Assert(!FloorGeometry.Validate(document, out _));
});

Check("Reject a door too close to a wall corner", () =>
{
    var document = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(0.2, 0) }]
    };
    Assert(!FloorGeometry.Validate(document, out _));
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
