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

Check("Split an exterior wall into two segments without changing the footprint", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(4, 0), out var result, out var reason), reason);
    Assert(result.Perimeter.Count == 5, "Expected five perimeter vertices.");
    Equal(PolygonGeometry.Area(result.Perimeter), 80);
    Equal(PolygonGeometry.Perimeter(result.Perimeter), 36);
    Assert(doc.Perimeter.Count == 4, "Original document was unexpectedly modified.");
});

Check("Split a wall directly through an existing doorway", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(5, 0), out var split, out var reason), reason);
    Assert(split.Perimeter.Count == 5);
    Assert(split.Doors.Count == 1);
    Assert(FloorGeometry.Validate(split, out reason), reason);
    var walls = FloorGeometry.BuildWalls(split.Perimeter, split.Rooms);
    Assert(FloorGeometry.TryFindDoorWall(split.Doors[0], walls, out var supportingWall));
    Assert(supportingWall is not null);
    Equal(supportingWall!.Length, 10);
    var reopened = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(split));
    Assert(reopened.Perimeter.Count == 5 && reopened.Doors.Count == 1);
});

Check("Split a diagonal perimeter wall with a doorway", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(8, 4), new(8, 10), new(0, 10)],
        Doors = [new DoorDefinition { Centre = new Point2(4, 2) }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(3, 1.5), out var split, out var reason), reason);
    Assert(FloorGeometry.Validate(split, out reason), reason);
    Equal(PolygonGeometry.Area(split.Perimeter), 64);
    Assert(split.Perimeter.Contains(new Point2(3, 1.5)));
});

Check("Exterior wall split also inserts shared corners into room boundary", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Shop",
                Outline = [new(0, 0), new(10, 0), new(10, 4), new(0, 4)]
            }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(5, 0), out var split, out var reason), reason);
    Assert(split.Perimeter.Contains(new Point2(5, 0)));
    Assert(split.Rooms[0].Outline.Contains(new Point2(5, 0)));
    Assert(split.Rooms[0].Outline.Count == 5);
    Assert(FloorGeometry.Validate(split, out reason), reason);
});

Check("A newly split corner can be dragged into a new angled exterior", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(2, 0) }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(6, 0), out var split, out var reason), reason);

    var oldVertex = new Point2(6, 0);
    var newVertex = new Point2(6, 2);
    var doors = DoorPlacement.ForMovedVertex(
        split.Perimeter, split.Rooms, split.Doors, oldVertex, newVertex);
    var reshaped = new BuildingDocument
    {
        Perimeter = split.Perimeter.Select(p => p == oldVertex ? newVertex : p).ToList(),
        Doors = doors
    };
    Assert(FloorGeometry.Validate(reshaped, out reason), reason);
    Equal(doors[0].Centre.X, 2);
    Equal(doors[0].Centre.Y, 2d / 3d);
});

Check("Split near an existing corner is rejected", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(!PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(0.01, 0), out _, out _));
});

Check("Splitting room wall propagates to another room sharing that wall", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Shop",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Residence",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4) }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, false, doc.Rooms[0].Id, 1,
        new Point2(5, 4), out var split, out var reason), reason);
    Assert(split.Rooms[0].Outline.Count == 5);
    Assert(split.Rooms[1].Outline.Count == 5);
    Assert(FloorGeometry.Validate(split, out reason), reason);
});

Check("Remove a split perimeter corner to restore one straight wall", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(PerimeterEditing.TrySplitWall(original, true, null, 0,
        new Point2(5, 0), out var split, out var splitReason), splitReason);
    Assert(split.Perimeter.Count == 5);

    Assert(PerimeterEditing.TryRemoveCorner(split, true, null, 1,
        out var joined, out var joinReason), joinReason);
    Assert(joined.Perimeter.SequenceEqual(rectangle));
    Equal(PolygonGeometry.Area(joined.Perimeter), 80);
    Equal(joined.Doors[0].Centre.X, 5);
    Equal(joined.Doors[0].Centre.Y, 0);
    Assert(split.Perimeter.Count == 5, "The input document must be untouched.");
    Assert(FloorGeometry.Validate(joined, out joinReason), joinReason);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(joined));
    Assert(loaded.Perimeter.Count == 4 && loaded.Doors.Count == 1);
});

Check("Remove angled corner joins two walls and repositions doors", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(5, 1), new(10, 0), new(10, 8), new(0, 8)],
        Doors =
        [
            new DoorDefinition { Centre = new Point2(2.5, 0.5) },
            new DoorDefinition { Centre = new Point2(7.5, 0.5) }
        ]
    };
    Assert(FloorGeometry.Validate(original, out var reason), reason);

    Assert(PerimeterEditing.TryRemoveCorner(original, true, null, 1,
        out var joined, out reason), reason);
    Assert(joined.Perimeter.SequenceEqual(rectangle));
    Equal(joined.Doors[0].Centre.X, 2.5);
    Equal(joined.Doors[0].Centre.Y, 0);
    Equal(joined.Doors[1].Centre.X, 7.5);
    Equal(joined.Doors[1].Centre.Y, 0);
    Assert(FloorGeometry.Validate(joined, out reason), reason);
});

Check("Removing perimeter corner removes matching shared room vertex", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Shop",
                Outline = [new(0, 0), new(10, 0), new(10, 4), new(0, 4)]
            }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(PerimeterEditing.TrySplitWall(original, true, null, 0,
        new Point2(5, 0), out var split, out var reason), reason);
    Assert(split.Rooms[0].Outline.Count == 5);

    Assert(PerimeterEditing.TryRemoveCorner(split, true, null, 1,
        out var joined, out reason), reason);
    Assert(joined.Perimeter.Count == 4);
    Assert(joined.Rooms[0].Outline.Count == 4);
    Assert(FloorGeometry.Validate(joined, out reason), reason);
});

Check("Removing interior shared corner joins adjoining room walls", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Shop",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Residence",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4) }]
    };
    Assert(PerimeterEditing.TrySplitWall(original, false, original.Rooms[0].Id, 1,
        new Point2(5, 4), out var split, out var reason), reason);
    Assert(PerimeterEditing.TryRemoveCorner(split, false, split.Rooms[0].Id, 2,
        out var joined, out reason), reason);
    Assert(joined.Rooms[0].Outline.Count == 4);
    Assert(joined.Rooms[1].Outline.Count == 4);
    Assert(FloorGeometry.Validate(joined, out reason), reason);
});

Check("Do not remove a corner from a triangle", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(5, 0), new(2, 4)]
    };
    Assert(!PerimeterEditing.TryRemoveCorner(original, true, null, 1,
        out _, out var reason));
    Assert(reason.Contains("three corners"));
});

Check("Reject removing a corner if it would cut across an existing room", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Shop",
                Outline = [new(0, 0), new(5, 0), new(5, 2), new(0, 2)]
            }
        ]
    };
    Assert(FloorGeometry.Validate(original, out var reason), reason);
    Assert(!PerimeterEditing.TryRemoveCorner(original, true, null, 1,
        out _, out reason));
    Assert(!string.IsNullOrWhiteSpace(reason));
    Assert(original.Perimeter.SequenceEqual(rectangle));
});

Check("Split a rectangular room into two adjacent rooms", () =>
{
    var room = new RoomDefinition { Name = "Workshop", Type = "Workshop",
        Outline = rectangle.ToList() };
    var source = new BuildingDocument
    {
        Name = "Merchant",
        Perimeter = rectangle.ToList(),
        Rooms = [room]
    };
    Assert(InteriorEditing.TrySplitRoom(source, room.Id, new(5, 0), new(5, 8),
        out var result, out var reason), reason);
    Assert(source.Rooms.Count == 1, "Original floor should remain unchanged.");
    Assert(result.Rooms.Count == 2);
    Assert(result.Rooms[0].Id == room.Id);
    Assert(result.Rooms[1].Id != room.Id);
    Assert(result.Rooms[0].Name == "Workshop A");
    Assert(result.Rooms[1].Name == "Workshop B");
    Equal(PolygonGeometry.Area(result.Rooms[0].Outline), 40);
    Equal(PolygonGeometry.Area(result.Rooms[1].Outline), 40);
    Assert(result.Perimeter.Count == 6);
    Assert(FloorGeometry.Validate(result, out reason), reason);
    var roundTrip = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(result));
    Assert(roundTrip.Rooms.Count == 2 && roundTrip.Perimeter.Count == 6);
});

Check("Split along an arbitrary diagonal, preserving an exterior door", () =>
{
    var room = new RoomDefinition { Name = "Hall", Outline = rectangle.ToList() };
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [room],
        Doors = [new DoorDefinition { Centre = new Point2(2, 0) }]
    };
    Assert(InteriorEditing.TrySplitRoom(source, room.Id, new(0, 0), new(10, 8),
        out var result, out var reason), reason);
    Assert(result.Rooms.Count == 2 && result.Doors.Count == 1);
    Equal(PolygonGeometry.Area(result.Rooms[0].Outline), 40);
    Equal(PolygonGeometry.Area(result.Rooms[1].Outline), 40);
    Assert(FloorGeometry.Validate(result, out reason), reason);
});

Check("Reject a partition which crosses outside a concave room", () =>
{
    var room = new RoomDefinition { Name = "L-shaped room", Outline = concave.ToList() };
    var source = new BuildingDocument { Perimeter = concave.ToList(), Rooms = [room] };
    Assert(FloorGeometry.Validate(source, out var reason), reason);
    Assert(!InteriorEditing.TrySplitRoom(source, room.Id, new(1, 5), new(6, 1),
        out _, out reason), "Concave room split should not cross open space.");
    Assert(source.Rooms.Count == 1);
});

Check("Reject a partition with an endpoint away from the room wall", () =>
{
    var room = new RoomDefinition { Outline = rectangle.ToList() };
    var source = new BuildingDocument { Perimeter = rectangle.ToList(), Rooms = [room] };
    Assert(!InteriorEditing.TrySplitRoom(source, room.Id, new(5, 3), new(10, 4),
        out _, out _));
});

Check("Move shared room wall while preserving door and adjacency", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Shop",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Home",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4) }]
    };
    var wall = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)
        .Single(w => !w.IsExterior);
    // Direction of the first room's shared edge is upwards, so negative
    // normal displacement moves it towards the right side of the building.
    Assert(InteriorEditing.TryMoveWall(source, wall, -1,
        out var result, out var reason), reason);
    Assert(FloorGeometry.Validate(result, out reason), reason);
    Equal(PolygonGeometry.Area(result.Rooms[0].Outline), 48);
    Equal(PolygonGeometry.Area(result.Rooms[1].Outline), 32);
    Equal(result.Doors[0].Centre.X, 6);
    Equal(result.Doors[0].Centre.Y, 4);
    Equal(source.Doors[0].Centre.X, 5);
    Assert(result.Perimeter.SequenceEqual(rectangle));
});

Check("Move a diagonal exterior wall and its door", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(8, 4), new(8, 10), new(0, 10)],
        Doors = [new DoorDefinition { Centre = new Point2(4, 2) }]
    };
    var wall = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)[0];
    Assert(InteriorEditing.TryMoveWall(source, wall, -1,
        out var result, out var reason), reason);
    Assert(FloorGeometry.Validate(result, out reason), reason);
    double dx = 4 / Math.Sqrt(80);
    double dy = -8 / Math.Sqrt(80);
    Equal(result.Doors[0].Centre.X, 4 + dx);
    Equal(result.Doors[0].Centre.Y, 2 + dy);
});

Check("Door on adjoining wall follows its moved endpoint", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(10, 4) }]
    };
    var topWall = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)[0];
    Assert(InteriorEditing.TryMoveWall(source, topWall, 1,
        out var result, out var reason), reason);
    Equal(result.Doors[0].Centre.X, 10);
    Equal(result.Doors[0].Centre.Y, 4.5);
    Assert(FloorGeometry.Validate(result, out reason), reason);
});

Check("Reject moving a shared wall beyond exterior and preserve original", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ]
    };
    var wall = FloorGeometry.BuildWalls(source.Perimeter, source.Rooms)
        .Single(w => !w.IsExterior);
    Assert(!InteriorEditing.TryMoveWall(source, wall, -6,
        out _, out var reason));
    Assert(!string.IsNullOrWhiteSpace(reason));
    Equal(source.Rooms[0].Outline[1].X, 5);
});

Check("Reject moves of stale or nonexistent wall segments", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(!InteriorEditing.TryMoveWall(source,
        new WallSegment(new(3, 3), new(3, 7), false), 1,
        out _, out var reason));
    Assert(reason.Contains("no longer exists"));
});

Check("Merge two rectangular rooms and remove the partition door", () =>
{
    var shop = new RoomDefinition { Name = "Shop", Type = "Shop",
        Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] };
    var storeroom = new RoomDefinition { Name = "Storeroom", Type = "Storeroom",
        Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] };
    var outsideDoor = new DoorDefinition { Centre = new Point2(2, 0) };
    var insideDoor = new DoorDefinition { Centre = new Point2(5, 4) };
    var source = new BuildingDocument
    {
        Name = "Merchant Hall",
        Perimeter = rectangle.ToList(),
        Rooms = [shop, storeroom],
        Doors = [outsideDoor, insideDoor]
    };
    Assert(RoomMerging.TryMergeRooms(source, shop.Id, storeroom.Id,
        out var merged, out var reason), reason);
    Assert(merged.Rooms.Count == 1);
    Assert(merged.Rooms[0].Id == shop.Id);
    Assert(merged.Rooms[0].Name == "Shop");
    Assert(merged.Rooms[0].Type == "Shop");
    Equal(PolygonGeometry.Area(merged.Rooms[0].Outline), 80);
    Assert(merged.Doors.Count == 1 &&
        merged.Doors[0].Id == outsideDoor.Id);
    Assert(source.Rooms.Count == 2 && source.Doors.Count == 2);
    Assert(FloorGeometry.Validate(merged, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(merged));
    Assert(loaded.Rooms.Count == 1 && loaded.Doors.Count == 1);
});

Check("Merge rooms with an angled shared partition", () =>
{
    var a = new RoomDefinition { Name = "Diagonal A",
        Outline = [new(0, 0), new(3, 0), new(5, 8), new(0, 8)] };
    var b = new RoomDefinition { Name = "Diagonal B",
        Outline = [new(3, 0), new(10, 0), new(10, 8), new(5, 8)] };
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [a, b],
        Doors = [new DoorDefinition { Centre = new Point2(4, 4) }]
    };
    Assert(FloorGeometry.Validate(source, out var reason), reason);
    Assert(RoomMerging.TryMergeRooms(source, a.Id, b.Id,
        out var merged, out reason), reason);
    Assert(merged.Rooms.Count == 1 && merged.Doors.Count == 0);
    Equal(PolygonGeometry.Area(merged.Rooms[0].Outline), 80);
    Assert(FloorGeometry.Validate(merged, out reason), reason);
});

Check("Merge rooms with only a partial shared wall", () =>
{
    var exterior = new List<Point2> { new(0, 0), new(12, 0), new(12, 10), new(0, 10) };
    var a = new RoomDefinition { Name = "Large room",
        Outline = [new(0, 0), new(6, 0), new(6, 10), new(0, 10)] };
    var b = new RoomDefinition { Name = "Side room",
        Outline = [new(6, 3), new(12, 3), new(12, 8), new(6, 8)] };
    var source = new BuildingDocument
    {
        Perimeter = exterior, Rooms = [a, b],
        Doors = [new DoorDefinition { Centre = new Point2(6, 5) }]
    };
    Assert(FloorGeometry.Validate(source, out var reason), reason);
    Assert(RoomMerging.TryMergeRooms(source, a.Id, b.Id,
        out var merged, out reason), reason);
    Assert(merged.Rooms.Count == 1 && merged.Doors.Count == 0);
    Equal(PolygonGeometry.Area(merged.Rooms[0].Outline), 90);
    Assert(FloorGeometry.Validate(merged, out reason), reason);
});

Check("Merge keeps doors bordering a third room", () =>
{
    var perimeter = new List<Point2> { new(0, 0), new(12, 0), new(12, 8), new(0, 8) };
    var a = new RoomDefinition { Name = "A",
        Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] };
    var b = new RoomDefinition { Name = "B",
        Outline = [new(4, 0), new(8, 0), new(8, 8), new(4, 8)] };
    var c = new RoomDefinition { Name = "C",
        Outline = [new(8, 0), new(12, 0), new(12, 8), new(8, 8)] };
    var partitionDoor = new DoorDefinition { Centre = new Point2(4, 4) };
    var remainingDoor = new DoorDefinition { Centre = new Point2(8, 4) };
    var doc = new BuildingDocument
    {
        Perimeter = perimeter, Rooms = [a, b, c],
        Doors = [partitionDoor, remainingDoor]
    };
    Assert(RoomMerging.TryMergeRooms(doc, a.Id, b.Id,
        out var merged, out var reason), reason);
    Assert(merged.Rooms.Count == 2);
    Assert(merged.Doors.Count == 1);
    Assert(merged.Doors[0].Id == remainingDoor.Id);
    Equal(PolygonGeometry.Area(merged.Rooms[0].Outline), 64);
    Assert(FloorGeometry.Validate(merged, out reason), reason);
});

Check("Merging reversed-winding room contours is supported", () =>
{
    var left = new RoomDefinition { Name = "Left",
        Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] };
    var right = new RoomDefinition { Name = "Right",
        Outline = [new(5, 8), new(10, 8), new(10, 0), new(5, 0)] };
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(), Rooms = [left, right]
    };
    Assert(RoomMerging.TryMergeRooms(source, left.Id, right.Id,
        out var result, out var reason), reason);
    Equal(PolygonGeometry.Area(result.Rooms[0].Outline), 80);
});

Check("Rooms touching only at a corner cannot merge", () =>
{
    var exterior = new List<Point2> { new(0, 0), new(8, 0), new(8, 8), new(0, 8) };
    var a = new RoomDefinition { Outline = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)] };
    var b = new RoomDefinition { Outline = [new(4, 4), new(8, 4), new(8, 8), new(4, 8)] };
    var source = new BuildingDocument { Perimeter = exterior, Rooms = [a, b] };
    Assert(!RoomMerging.TryMergeRooms(source, a.Id, b.Id, out _, out var reason));
    Assert(reason.Contains("share a wall"));
});

Check("Merge rejects non-adjacent and identical room selections", () =>
{
    var a = new RoomDefinition { Outline = [new(0, 0), new(2, 0), new(2, 2), new(0, 2)] };
    var b = new RoomDefinition { Outline = [new(7, 5), new(9, 5), new(9, 7), new(7, 7)] };
    var source = new BuildingDocument { Perimeter = rectangle.ToList(), Rooms = [a, b] };
    Assert(!RoomMerging.TryMergeRooms(source, a.Id, b.Id, out _, out _));
    Assert(!RoomMerging.TryMergeRooms(source, a.Id, a.Id, out _, out _));
});

Check("Moving corner snaps to existing room corner instead of rounded grid", () =>
{
    var polygon = new List<Point2> { new(1.1, 1.2), new(4.4, 3.1), new(7, 3.9) };
    var result = GeometrySnap.ForMovedVertex(new(4.45, 3.12), new(1.1, 1.2),
        [polygon], 0.2, true, 0.5);
    Equal(result.X, 4.4);
    Equal(result.Y, 3.1);
});

Check("Moving corner snaps exactly onto diagonal wall", () =>
{
    var polygon = new List<Point2>
    {
        new(2, 2), new(6, 6), new(7, 2)
    };
    var result = GeometrySnap.ForMovedVertex(new(4.1, 3.99),
        new Point2(-1, -1), [polygon], 0.2, true);
    Equal(result.X, 4.045);
    Equal(result.Y, 4.045);
});

Check("Moving corner does not snap to its own incident wall", () =>
{
    var polygon = new List<Point2> { new(0, 0), new(10, 0), new(10, 8), new(0, 8) };
    var result = GeometrySnap.ForMovedVertex(new(1.13, 0.03),
        new Point2(0, 0), [polygon], 0.2, false);
    Equal(result.X, 1.13);
    Equal(result.Y, 0.03);
});

Check("Shift bypasses snapping for vertex movement", () =>
{
    var polygon = new List<Point2> { new(1, 1), new(5, 5), new(5, 1) };
    var query = new Point2(5.01, 5.02);
    var result = GeometrySnap.ForMovedVertex(query, new Point2(0, 0),
        [polygon], 0.3, true, 0.5, bypass: true);
    Equal(result.X, query.X);
    Equal(result.Y, query.Y);
});

Check("A room can be split and immediately merged back together", () =>
{
    var original = new RoomDefinition
    {
        Name = "Inn Common Room", Type = "Common room",
        Outline = rectangle.ToList()
    };
    var document = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [original],
        Doors = [new DoorDefinition { Centre = new Point2(3, 0) }]
    };
    Assert(InteriorEditing.TrySplitRoom(document, original.Id,
        new Point2(5, 0), new Point2(5, 8), out var split, out var reason), reason);
    Assert(split.Rooms.Count == 2);
    Assert(RoomMerging.TryMergeRooms(split, split.Rooms[0].Id, split.Rooms[1].Id,
        out var merged, out reason), reason);
    Assert(merged.Rooms.Count == 1);
    Assert(merged.Rooms[0].Id == original.Id);
    Equal(PolygonGeometry.Area(merged.Rooms[0].Outline), 80);
    Assert(merged.Doors.Count == 1);
    Assert(FloorGeometry.Validate(merged, out reason), reason);
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
