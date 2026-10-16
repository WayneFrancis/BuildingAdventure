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

Check("Door tracks wall as horizontal edge rotates to diagonal", () =>
{
    var originalDoors = new List<DoorDefinition>
    {
        new() { Centre = new Point2(5, 0), Width = 0.9 }
    };
    Point2 oldCorner = new(10, 0);
    Point2 newCorner = new(10, 2);
    var doors = DoorPlacement.ForMovedVertex(
        rectangle, [], originalDoors, oldCorner, newCorner);

    Equal(doors[0].Centre.X, 5);
    Equal(doors[0].Centre.Y, 1);
    Equal(originalDoors[0].Centre.Y, 0); // No mutation of source snapshot.

    var changedOutline = new List<Point2> { new(0, 0), newCorner, new(10, 8), new(0, 8) };
    var document = new BuildingDocument { Perimeter = changedOutline, Doors = doors };
    Assert(FloorGeometry.Validate(document, out var reason), reason);

    var reloaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(document));
    Equal(reloaded.Doors[0].Centre.Y, 1);
});

Check("Door tracks an existing diagonal wall rotated further", () =>
{
    var diagonal = new List<Point2>
    {
        new(0, 0), new(8, 4), new(8, 10), new(0, 10)
    };
    var originalDoors = new List<DoorDefinition>
    {
        new() { Centre = new Point2(4, 2) }
    };
    var newCorner = new Point2(8, 6);
    var doors = DoorPlacement.ForMovedVertex(
        diagonal, [], originalDoors, new Point2(8, 4), newCorner);

    Equal(doors[0].Centre.X, 4);
    Equal(doors[0].Centre.Y, 3);
    var document = new BuildingDocument
    {
        Perimeter = [new(0, 0), newCorner, new(8, 10), new(0, 10)],
        Doors = doors
    };
    Assert(FloorGeometry.Validate(document, out var reason), reason);
});

Check("Door on unrelated wall does not move", () =>
{
    var originalDoor = new DoorDefinition { Centre = new Point2(5, 8) };
    var doors = DoorPlacement.ForMovedVertex(
        rectangle, [], [originalDoor], new Point2(10, 0), new Point2(10, 2));
    Assert(doors.Count == 1);
    Equal(doors[0].Centre.X, 5);
    Equal(doors[0].Centre.Y, 8);
});

Check("Door follows angled shared wall between two rooms", () =>
{
    var first = new RoomDefinition
    {
        Name = "Shop",
        Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)]
    };
    var second = new RoomDefinition
    {
        Name = "Residence",
        Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)]
    };
    var oldRooms = new List<RoomDefinition> { first, second };
    var newRooms = oldRooms.Select(r => r.Copy()).ToList();
    var oldPoint = new Point2(5, 0);
    var newPoint = new Point2(4, 2);
    foreach (var room in newRooms)
        room.Outline = room.Outline
            .Select(p => p == oldPoint ? newPoint : p).ToList();

    var doors = DoorPlacement.ForMovedVertex(
        rectangle, oldRooms,
        [new DoorDefinition { Centre = new Point2(5, 4) }],
        oldPoint, newPoint);

    Equal(doors[0].Centre.X, 4.5);
    Equal(doors[0].Centre.Y, 5);
    var document = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = newRooms,
        Doors = doors
    };
    Assert(FloorGeometry.Validate(document, out var reason), reason);
});

Check("Invalid door clearance still rejects extreme wall shortening", () =>
{
    var oldDoor = new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 };
    var newCorner = new Point2(0.8, 0);
    var doors = DoorPlacement.ForMovedVertex(
        rectangle, [], [oldDoor], new Point2(10, 0), newCorner);
    var document = new BuildingDocument
    {
        Perimeter = [new(0, 0), newCorner, new(10, 8), new(0, 8)],
        Doors = doors
    };
    Assert(!FloorGeometry.Validate(document, out _),
        "Shortened walls must not accept a door with insufficient clearance.");
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
