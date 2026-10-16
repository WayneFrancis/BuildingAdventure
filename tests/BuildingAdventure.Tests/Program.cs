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

Check("Version 1 building files still deserialize with one floor", () =>
{
    var text = """{"schemaVersion":1,"name":"Old Tavern","perimeter":[{"x":0,"y":0},{"x":12,"y":0},{"x":12,"y":10},{"x":0,"y":10}]}""";
    var loaded = BuildingDocumentJson.Deserialize(text);
    Assert(BuildingStoreys.FloorCount(loaded) == 1);
    Assert(loaded.UpperFloors.Count == 0 && loaded.Staircases.Count == 0);
    Equal(PolygonGeometry.Area(loaded.Perimeter), 120);
});

Check("Adding upper floors preserves ground rooms and uses independent outlines", () =>
{
    var ground = new BuildingDocument
    {
        Name = "Shop and Home",
        Perimeter = rectangle.ToList(),
        Rooms = [new RoomDefinition { Name = "Shop", Type = "Shop",
            Outline = rectangle.ToList() }],
        Doors = [new DoorDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(two.SchemaVersion == 2 && BuildingStoreys.FloorCount(two) == 2);
    Assert(two.UpperFloors[0].Perimeter.SequenceEqual(ground.Perimeter));
    Assert(two.UpperFloors[0].Rooms.Count == 0 && two.UpperFloors[0].Doors.Count == 0);
    Assert(two.Rooms.Count == 1 && two.Doors.Count == 1);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Rooms.Add(new RoomDefinition { Name = "Bedroom", Type = "Bedroom",
        Outline = rectangle.ToList() });
    BuildingStoreys.SetFloor(two, 1, upper);
    Assert(two.Rooms.Count == 1 && two.Rooms[0].Name == "Shop");
    Assert(two.UpperFloors[0].Rooms.Single().Name == "Bedroom");
    Assert(BuildingStoreys.Validate(two, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(two));
    Assert(loaded.Rooms.Count == 1 && loaded.UpperFloors[0].Rooms.Count == 1);
    Assert(loaded.UpperFloors[0].Rooms[0].Type == "Bedroom");
});

Check("Staircase connects adjacent floors and round-trips JSON", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var flight = new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(5, 4),
        Width = 1.4, Length = 3.6, RotationDegrees = 30
    };
    two.Staircases.Add(flight);
    Assert(BuildingStoreys.Validate(two, out reason), reason);
    var footprint = BuildingStoreys.StairFootprint(flight);
    Equal(PolygonGeometry.Area(footprint), flight.Width * flight.Length);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(two));
    Assert(loaded.Staircases.Count == 1);
    Assert(loaded.Staircases[0].Id == flight.Id);
    Equal(loaded.Staircases[0].RotationDegrees, 30);
    Assert(loaded.Staircases[0].LowerFloorIndex == 0);
});

Check("Staircase must fit both floor perimeters", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var smallUpper = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)]
    };
    BuildingStoreys.SetFloor(two, 1, smallUpper);
    two.Staircases.Add(new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(8, 4)
    });
    Assert(!BuildingStoreys.Validate(two, out reason));
    Assert(reason.Contains("Floor"));
});

Check("Stairs may not intersect an interior partition wall", () =>
{
    var ground = new BuildingDocument
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
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    building.Staircases.Add(new StaircaseDefinition
    {
        Centre = new Point2(5, 4), Width = 2, Length = 4,
        LowerFloorIndex = 0
    });
    Assert(!BuildingStoreys.Validate(building, out reason));
    Assert(reason.Contains("interior wall"));
});

Check("Remove highest floor also removes its connecting staircase", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    three.Staircases.Add(new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(3, 3)
    });
    three.Staircases.Add(new StaircaseDefinition
    {
        LowerFloorIndex = 1, Centre = new Point2(7, 5)
    });
    Assert(BuildingStoreys.Validate(three, out reason), reason);
    Assert(BuildingStoreys.TryRemoveTopFloor(three, out var reduced, out reason), reason);
    Assert(BuildingStoreys.FloorCount(reduced) == 2);
    Assert(reduced.Staircases.Count == 1 &&
           reduced.Staircases[0].LowerFloorIndex == 0);
    Assert(three.Staircases.Count == 2);
    Assert(BuildingStoreys.Validate(reduced, out reason), reason);
});

Check("Floor limit is four stories", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    var current = ground;
    for (int i = 0; i < 3; i++)
    {
        Assert(BuildingStoreys.TryAddFloor(current, out var next, out var reason), reason);
        current = next;
    }
    Assert(BuildingStoreys.FloorCount(current) == 4);
    Assert(!BuildingStoreys.TryAddFloor(current, out _, out _));
});

Check("Reject malformed staircase with unknown floor index", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    two.Staircases.Add(new StaircaseDefinition
    {
        LowerFloorIndex = 1, Centre = new Point2(5, 4)
    });
    Assert(!BuildingStoreys.Validate(two, out _));
});

Check("Reject upper floors stored under schema version one", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    building.UpperFloors.Add(new StoreyDefinition
    {
        Name = "First Floor", Perimeter = rectangle.ToList()
    });
    Assert(!BuildingStoreys.Validate(building, out _));
});

Check("Cannot place stairs without a second floor", () =>
{
    var ground = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Staircases = [new StaircaseDefinition { Centre = new Point2(5, 4) }]
    };
    ground.SchemaVersion = 2;
    Assert(!BuildingStoreys.Validate(ground, out _));
});

Check("Repositioning stairs preserves identity and alignment across both floors", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var flight = new StaircaseDefinition { Centre = new Point2(3, 3), LowerFloorIndex = 0 };
    Assert(BuildingStoreys.TryAddStaircase(building, flight, out var placed, out reason), reason);
    var changed = flight.Copy();
    changed.Centre = new Point2(7, 5);
    Assert(BuildingStoreys.TryUpdateStaircase(placed, changed,
        out var moved, out reason), reason);
    Assert(moved.Staircases.Count == 1 && moved.Staircases[0].Id == flight.Id);
    Equal(moved.Staircases[0].Centre.X, 7);
    Equal(moved.Staircases[0].Centre.Y, 5);
    Equal(placed.Staircases[0].Centre.X, 3);
    Equal(placed.Staircases[0].Centre.Y, 3);
    Assert(BuildingStoreys.Validate(moved, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(moved));
    Assert(loaded.Staircases.Count == 1 && loaded.Staircases[0].Id == flight.Id);
    Equal(loaded.Staircases[0].Centre.X, 7);
});

Check("Resize and arbitrary-angle rotation of placed stairs", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var flight = new StaircaseDefinition { Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(building, flight, out var placed, out reason), reason);
    var changed = flight.Copy();
    changed.Length = 4.5;
    changed.Width = 1.8;
    changed.RotationDegrees = 37;
    Assert(BuildingStoreys.TryUpdateStaircase(placed, changed,
        out var updated, out reason), reason);
    Equal(PolygonGeometry.Area(BuildingStoreys.StairFootprint(updated.Staircases[0])), 8.1);
    Equal(updated.Staircases[0].RotationDegrees, 37);
    Equal(placed.Staircases[0].RotationDegrees, 0);
});

Check("Reject moved stairs crossing outside the upper floor, preserving originals", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var smallUpper = BuildingStoreys.GetFloor(building, 1);
    smallUpper.Perimeter = [new(0, 0), new(6, 0), new(6, 8), new(0, 8)];
    BuildingStoreys.SetFloor(building, 1, smallUpper);
    var flight = new StaircaseDefinition { Centre = new Point2(3, 4) };
    Assert(BuildingStoreys.TryAddStaircase(building, flight,
        out var placed, out reason), reason);
    var changed = flight.Copy();
    changed.Centre = new Point2(8, 4);
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, changed,
        out var rejected, out reason));
    Assert(reason.Contains("First Floor"));
    Equal(placed.Staircases[0].Centre.X, 3);
    Equal(rejected.Staircases[0].Centre.X, 8); // Candidate is separate from source.
});

Check("Reject staircase rotation that collides with an interior wall", () =>
{
    var ground = new BuildingDocument
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
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var flight = new StaircaseDefinition
    {
        Centre = new Point2(3.5, 4), Width = 1.2, Length = 3.6,
        RotationDegrees = 0
    };
    Assert(BuildingStoreys.TryAddStaircase(building, flight,
        out var placed, out reason), reason);
    var rotated = flight.Copy();
    rotated.RotationDegrees = 90;
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, rotated, out _, out reason));
    Assert(reason.Contains("interior wall"));
    Equal(placed.Staircases[0].RotationDegrees, 0);
});

Check("Stair updates preserve other staircases and floors", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(building, out var withUpper, out var reason), reason);
    building = withUpper;
    var first = new StaircaseDefinition { Centre = new Point2(3, 3) };
    var second = new StaircaseDefinition { Centre = new Point2(7, 5) };
    Assert(BuildingStoreys.TryAddStaircase(building, first, out var withFirst, out reason), reason);
    building = withFirst;
    Assert(BuildingStoreys.TryAddStaircase(building, second, out var withSecond, out reason), reason);
    building = withSecond;
    var moved = first.Copy();
    moved.Centre = new Point2(4, 3);
    Assert(BuildingStoreys.TryUpdateStaircase(building, moved,
        out var result, out reason), reason);
    Assert(result.Staircases.Count == 2);
    Equal(result.Staircases.Single(s => s.Id == second.Id).Centre.X, 7);
    Equal(result.UpperFloors[0].Perimeter[1].X, 10);
});

Check("Stair update rejects unknown identifiers and a changed floor pair", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var existing = new StaircaseDefinition { Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(building, existing,
        out var placed, out reason), reason);
    var absent = existing.Copy();
    absent.Id = Guid.NewGuid();
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, absent, out _, out reason));
    Assert(reason.Contains("not found"));
    var changedFloor = existing.Copy();
    changedFloor.LowerFloorIndex = 1;
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, changedFloor, out _, out reason));
    Assert(reason.Contains("pair of floors"));
});

Check("Stair creation rejects duplicate identities", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(building, out var withUpper, out var reason), reason);
    building = withUpper;
    var stair = new StaircaseDefinition { Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(building, stair, out var placed, out reason), reason);
    building = placed;
    Assert(!BuildingStoreys.TryAddStaircase(building, stair, out _, out reason));
    Assert(reason.Contains("unique"));
    Assert(building.Staircases.Count == 1);
});

Check("Rejected staircase rotation preserves last valid angle for the next edit", () =>
{
    var ground = new BuildingDocument
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
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var flight = new StaircaseDefinition
    {
        Centre = new Point2(3.5, 4), Width = 1.2, Length = 3.6,
        RotationDegrees = 0
    };
    Assert(BuildingStoreys.TryAddStaircase(building, flight,
        out var placed, out reason), reason);

    var invalidRotation = flight.Copy();
    invalidRotation.RotationDegrees = 90; // Would cross the room partition.
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, invalidRotation,
        out _, out reason));
    Assert(reason.Contains("interior wall"));
    Equal(placed.Staircases[0].RotationDegrees, 0);

    var nextRotation = placed.Staircases[0].Copy();
    nextRotation.RotationDegrees = 30;
    Assert(BuildingStoreys.TryUpdateStaircase(placed, nextRotation,
        out var updated, out reason), reason);
    Equal(updated.Staircases[0].RotationDegrees, 30);
    Equal(placed.Staircases[0].RotationDegrees, 0);
});

Check("Stair rotation handle remains outside the long edge at any zoom", () =>
{
    var stair = new StaircaseDefinition
    {
        Centre = new Point2(5, 4), Length = 3.6, Width = 1.4,
        RotationDegrees = 0
    };
    Point2 at40 = StairRotation.HandleCentre(stair, 40);
    Point2 at80 = StairRotation.HandleCentre(stair, 80);
    Equal(at40.X, 5);
    Equal(at40.Y, 4 + 1.8 + 24d / 40);
    Equal(at80.Y, 4 + 1.8 + 24d / 80);

    stair.RotationDegrees = 90;
    Point2 rotated = StairRotation.HandleCentre(stair, 40);
    Equal(rotated.X, 5 - 1.8 - 24d / 40);
    Equal(rotated.Y, 4);
});

Check("Stair rotation handle derives the correct angle in every quadrant", () =>
{
    var centre = new Point2(5, 4);
    Equal(StairRotation.PointerAngle(centre, new Point2(5, 8)), 0);
    Equal(StairRotation.PointerAngle(centre, new Point2(1, 4)), 90);
    Equal(StairRotation.PointerAngle(centre, new Point2(5, 0)), 180);
    Equal(StairRotation.PointerAngle(centre, new Point2(9, 4)), 270);
    Equal(StairRotation.PointerAngle(centre, new Point2(4, 5)), 45);
});

Check("Rotation drag preserves the starting angle and grab offset", () =>
{
    Equal(StairRotation.FromDrag(42, 47, 47, false), 42);
    Equal(StairRotation.FromDrag(42, 47, 62, false), 57);
    Equal(StairRotation.FromDrag(42, 47, 62, true), 60);
    Equal(StairRotation.FromDrag(42, 47, 62, false), 57); // Shift off.
});

Check("Rotation drag follows cursor correctly across 0 and 360 degrees", () =>
{
    Equal(StairRotation.FromDrag(355, 350, 5, false), 10);
    Equal(StairRotation.FromDrag(355, 350, 5, true), 15);
    Equal(StairRotation.FromDrag(5, 10, 350, false), 345);
    Equal(StairRotation.Normalize(-15), 345);
    Equal(StairRotation.Normalize(375), 15);
});

Check("Rotation handle can propose invalid geometry without mutating valid stairs", () =>
{
    var ground = new BuildingDocument
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
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var flight = new StaircaseDefinition
    {
        Centre = new Point2(3.5, 4), Length = 3.6, Width = 1.2,
        RotationDegrees = 0
    };
    Assert(BuildingStoreys.TryAddStaircase(building, flight, out var placed, out reason), reason);
    var candidate = flight.Copy();
    candidate.RotationDegrees = StairRotation.FromDrag(0, 0, 90, false);
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, candidate,
        out _, out reason), "A rotation into the shared wall should be rejected.");
    Assert(reason.Contains("interior wall"));
    Equal(placed.Staircases[0].RotationDegrees, 0);
});

Check("Different floors can have independent differently shaped perimeters", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(building, 1);
    upper.Perimeter =
    [
        new(0, 0), new(8, 0), new(8, 6), new(5, 6), new(5, 8), new(0, 8)
    ];
    BuildingStoreys.SetFloor(building, 1, upper);

    Equal(PolygonGeometry.Area(building.Perimeter), 80);
    Equal(PolygonGeometry.Area(building.UpperFloors[0].Perimeter), 58);
    Assert(building.Perimeter.Count == 4 && building.UpperFloors[0].Perimeter.Count == 6);
    Assert(BuildingStoreys.Validate(building, out reason), reason);

    var staircase = new StaircaseDefinition { Centre = new Point2(3, 3) };
    Assert(BuildingStoreys.TryAddStaircase(building, staircase,
        out var withStairs, out reason), reason);
    Assert(BuildingStoreys.Validate(withStairs, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(withStairs));
    Assert(loaded.Perimeter.Count == 4 && loaded.UpperFloors[0].Perimeter.Count == 6);
    Equal(PolygonGeometry.Area(loaded.UpperFloors[0].Perimeter), 58);
});

Check("Stairwell cutout exists only in upper slab and reduces its usable area", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var stairs = new StaircaseDefinition
    {
        Centre = new Point2(5, 4), Length = 3.6, Width = 1.4,
        OpeningExtraLength = 0.4, OpeningExtraWidth = 0.6
    };
    Assert(BuildingStoreys.TryAddStaircase(building, stairs,
        out var linked, out reason), reason);
    Assert(BuildingStoreys.GetOpenings(linked, 0).Count == 0);
    var openings = BuildingStoreys.GetOpenings(linked, 1);
    Assert(openings.Count == 1);
    Assert(openings[0].StaircaseId == stairs.Id && openings[0].FloorIndex == 1);
    Equal(PolygonGeometry.Area(openings[0].Outline), 8);
    Equal(BuildingStoreys.SlabArea(linked, 0), 80);
    Equal(BuildingStoreys.SlabArea(linked, 1), 72);
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(linked));
    Equal(loaded.Staircases[0].OpeningExtraWidth, 0.6);
    Equal(loaded.Staircases[0].OpeningExtraLength, 0.4);
});

Check("Stairwell opening rotates and moves with its staircase", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var building, out var reason), reason);
    var original = new StaircaseDefinition
    {
        Centre = new Point2(4, 3), OpeningExtraWidth = 0.4
    };
    Assert(BuildingStoreys.TryAddStaircase(building, original,
        out var placed, out reason), reason);
    var changed = original.Copy();
    changed.Centre = new Point2(6, 5);
    changed.RotationDegrees = 30;
    Assert(BuildingStoreys.TryUpdateStaircase(placed, changed,
        out var moved, out reason), reason);
    var opening = BuildingStoreys.GetOpenings(moved, 1).Single();
    var centre = new Point2(opening.Outline.Average(p => p.X),
                            opening.Outline.Average(p => p.Y));
    Equal(centre.X, 6);
    Equal(centre.Y, 5);
    Equal(PolygonGeometry.Area(opening.Outline), 1.8 * 3.6);
    Equal(placed.Staircases[0].Centre.X, 4);
    Equal(placed.Staircases[0].RotationDegrees, 0);
});

Check("Stairwell extra clearance must fit the upper floor even when stairs fit", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var stair = new StaircaseDefinition { Centre = new Point2(1.5, 4), Width = 1.4 };
    Assert(BuildingStoreys.TryAddStaircase(building, stair,
        out var placed, out reason), reason);
    var enlarged = stair.Copy();
    enlarged.OpeningExtraWidth = 2; // Reaches outside x=0.
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, enlarged,
        out _, out reason));
    Assert(reason.Contains("stairwell opening"));
    Equal(placed.Staircases[0].OpeningExtraWidth, 0);
});

Check("Stairwell opening cannot cut through partition wall above", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(building, 1);
    upper.Rooms =
    [
        new RoomDefinition { Name = "West",
            Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
        new RoomDefinition { Name = "East",
            Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
    ];
    BuildingStoreys.SetFloor(building, 1, upper);
    var stairs = new StaircaseDefinition { Centre = new Point2(3.5, 4), Width = 1.2 };
    Assert(BuildingStoreys.TryAddStaircase(building, stairs,
        out var placed, out reason), reason);
    var expanded = stairs.Copy();
    expanded.OpeningExtraWidth = 2; // The stairs fit, but opening crosses x=5.
    Assert(!BuildingStoreys.TryUpdateStaircase(placed, expanded,
        out _, out reason));
    Assert(reason.Contains("stairwell opening crosses an interior wall"));
});

Check("Overlapping stairwell openings are rejected even when flights do not overlap", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var left = new StaircaseDefinition { Centre = new Point2(3, 4), OpeningExtraWidth = 0.8 };
    var right = new StaircaseDefinition { Centre = new Point2(5, 4), OpeningExtraWidth = 0.8 };
    Assert(BuildingStoreys.TryAddStaircase(building, left,
        out var once, out reason), reason);
    Assert(!BuildingStoreys.TryAddStaircase(once, right, out _, out reason));
    Assert(reason.Contains("overlap"));
    Assert(once.Staircases.Count == 1);
});

Check("Older version 2 staircase files gain zero-clearance aligned openings", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var building, out var reason), reason);
    var stairs = new StaircaseDefinition { Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(building, stairs,
        out var placed, out reason), reason);
    string serialized = BuildingDocumentJson.Serialize(placed);
    // Simulate a pre-opening v2 file without either new JSON field.
    serialized = System.Text.RegularExpressions.Regex.Replace(
        serialized,
        @",\s*""openingExtraWidth""\s*:\s*0\s*,\s*""openingExtraLength""\s*:\s*0",
        "");
    var legacy = BuildingDocumentJson.Deserialize(serialized);
    Assert(legacy.Staircases.Count == 1);
    Equal(legacy.Staircases[0].OpeningExtraWidth, 0);
    Equal(legacy.Staircases[0].OpeningExtraLength, 0);
    Equal(BuildingStoreys.SlabArea(legacy, 1), 80 - 3.6 * 1.4);
});

Check("Adjacent floor references return detached matching floor plans", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    var first = BuildingStoreys.GetFloor(three, 1);
    first.Perimeter = [new(0, 0), new(7, 0), new(7, 8), new(0, 8)];
    BuildingStoreys.SetFloor(three, 1, first);

    var overlays = BuildingStoreys.AdjacentFloors(three, 1, true, true);
    Assert(overlays.Count == 2);
    Assert(overlays[0].FloorIndex == 0 && !overlays[0].IsAbove);
    Assert(overlays[1].FloorIndex == 2 && overlays[1].IsAbove);
    Equal(PolygonGeometry.Area(overlays[0].Floor.Perimeter), 80);
    Equal(PolygonGeometry.Area(overlays[1].Floor.Perimeter), 80);
    overlays[0].Floor.Perimeter[0] = new Point2(-10, 0);
    Assert(three.Perimeter[0] == new Point2(0, 0));
    Assert(BuildingStoreys.AdjacentFloors(three, 1, false, true).Count == 1);
    Assert(BuildingStoreys.AdjacentFloors(three, 0, true, false).Count == 0);
    Assert(BuildingStoreys.Validate(three, out reason), reason);
});

Check("Stair openings on each floor come from flights arriving there", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    var upToFirst = new StaircaseDefinition { LowerFloorIndex = 0, Centre = new Point2(3, 4) };
    var upToSecond = new StaircaseDefinition { LowerFloorIndex = 1, Centre = new Point2(7, 4) };
    Assert(BuildingStoreys.TryAddStaircase(three, upToFirst, out var withFirst, out reason), reason);
    Assert(BuildingStoreys.TryAddStaircase(withFirst, upToSecond, out var full, out reason), reason);
    Assert(BuildingStoreys.GetOpenings(full, 0).Count == 0);
    Assert(BuildingStoreys.GetOpenings(full, 1).Single().StaircaseId == upToFirst.Id);
    Assert(BuildingStoreys.GetOpenings(full, 2).Single().StaircaseId == upToSecond.Id);
});

Check("Zero-clearance legacy stairs with intersecting reservations remain loadable", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var left = new StaircaseDefinition { Centre = new Point2(4, 4) };
    var right = new StaircaseDefinition { Centre = new Point2(4.5, 4) };
    two.Staircases.Add(left);
    two.Staircases.Add(right);
    Assert(BuildingStoreys.Validate(two, out reason), reason);
    var reopened = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(two));
    Assert(reopened.Staircases.Count == 2);
    var altered = right.Copy();
    altered.OpeningExtraWidth = 0.5;
    Assert(!BuildingStoreys.TryUpdateStaircase(two, altered, out _, out reason));
    Assert(reason.Contains("overlap"));
});

Check("Invalid negative stairwell clearance cannot be saved", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var stairs = new StaircaseDefinition
    {
        Centre = new Point2(5, 4), OpeningExtraLength = -0.1
    };
    Assert(!BuildingStoreys.TryAddStaircase(two, stairs, out _, out reason));
    Assert(reason.Contains("Invalid staircase"));
});

Check("Global undo and redo restore complete room edits and their floor", () =>
{
    var history = new BuildingEditHistory();
    var original = new BuildingDocument { Perimeter = rectangle.ToList() };
    var building = BuildingStoreys.Copy(original);
    var room = new RoomDefinition { Name = "Office", Type = "Office",
        Outline = rectangle.ToList() };
    history.RecordBefore(building, 0);
    building.Rooms.Add(room);
    Assert(history.CanUndo && !history.CanRedo && history.UndoCount == 1);

    Assert(history.TryUndo(building, 0, out var before));
    Assert(before.FloorIndex == 0 && before.Building.Rooms.Count == 0);
    Assert(history.CanRedo && history.RedoCount == 1);
    Assert(history.TryRedo(before.Building, before.FloorIndex, out var restored));
    Assert(restored.FloorIndex == 0 && restored.Building.Rooms.Count == 1);
    Assert(restored.Building.Rooms[0].Id == room.Id);
    Assert(history.CanUndo && !history.CanRedo);
});

Check("Global history can undo an upstairs edit after user switches downstairs", () =>
{
    var history = new BuildingEditHistory();
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var before = BuildingStoreys.Copy(two);
    var newRoom = new RoomDefinition { Name = "Bedroom",
        Outline = rectangle.ToList() };
    var edited = BuildingStoreys.GetFloor(two, 1);
    edited.Rooms.Add(newRoom);
    history.RecordBefore(before, 1);
    BuildingStoreys.SetFloor(two, 1, edited);

    // Changing the selected floor is just a view action; no history entry.
    Assert(history.UndoCount == 1);
    Assert(history.TryUndo(two, 0, out var undone));
    Assert(undone.FloorIndex == 1);
    Assert(undone.Building.UpperFloors[0].Rooms.Count == 0);
    Assert(undone.Building.Perimeter.SequenceEqual(rectangle));
    Assert(history.TryRedo(undone.Building, undone.FloorIndex, out var redone));
    Assert(redone.FloorIndex == 0);
    Assert(redone.Building.UpperFloors[0].Rooms.Single().Name == "Bedroom");
});

Check("Global history includes staircase movement and opening size", () =>
{
    var history = new BuildingEditHistory();
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var building, out var reason), reason);
    var stairs = new StaircaseDefinition { Centre = new Point2(3, 4) };
    history.RecordBefore(building, 0);
    Assert(BuildingStoreys.TryAddStaircase(building, stairs,
        out var added, out reason), reason);
    var moved = stairs.Copy();
    moved.Centre = new Point2(7, 4);
    moved.OpeningExtraLength = 0.4;
    history.RecordBefore(added, 0);
    Assert(BuildingStoreys.TryUpdateStaircase(added, moved,
        out var changed, out reason), reason);

    Assert(history.TryUndo(changed, 1, out var beforeMove));
    Equal(beforeMove.Building.Staircases.Single().Centre.X, 3);
    Equal(beforeMove.Building.Staircases.Single().OpeningExtraLength, 0);
    Assert(beforeMove.FloorIndex == 0);
    Assert(history.TryUndo(beforeMove.Building, beforeMove.FloorIndex, out var beforeAdding));
    Assert(beforeAdding.Building.Staircases.Count == 0);
    Assert(history.TryRedo(beforeAdding.Building, beforeAdding.FloorIndex, out var redone));
    Assert(redone.Building.Staircases.Count == 1);
    Assert(history.TryRedo(redone.Building, redone.FloorIndex, out var movedAgain));
    Equal(movedAgain.Building.Staircases.Single().Centre.X, 7);
    Equal(movedAgain.Building.Staircases.Single().OpeningExtraLength, 0.4);
    Assert(BuildingStoreys.Validate(movedAgain.Building, out reason), reason);
});

Check("Adding then removing a floor is undoable across floor-count changes", () =>
{
    var history = new BuildingEditHistory();
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    history.RecordBefore(ground, 0);
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(BuildingStoreys.FloorCount(two) == 2);

    history.RecordBefore(two, 1);
    Assert(BuildingStoreys.TryRemoveTopFloor(two, out var one, out reason), reason);
    Assert(BuildingStoreys.FloorCount(one) == 1);

    Assert(history.TryUndo(one, 0, out var undoRemoval));
    Assert(BuildingStoreys.FloorCount(undoRemoval.Building) == 2);
    Assert(undoRemoval.FloorIndex == 1);
    Assert(history.TryUndo(undoRemoval.Building, undoRemoval.FloorIndex,
        out var undoAddition));
    Assert(BuildingStoreys.FloorCount(undoAddition.Building) == 1);
    Assert(undoAddition.FloorIndex == 0);
    Assert(history.TryRedo(undoAddition.Building, 0, out var redoAddition));
    Assert(BuildingStoreys.FloorCount(redoAddition.Building) == 2);
});

Check("A new committed edit clears the redo branch", () =>
{
    var history = new BuildingEditHistory();
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    history.RecordBefore(building, 0);
    var changed = BuildingStoreys.Copy(building);
    changed.Name = "First name";
    Assert(history.TryUndo(changed, 0, out var restored));
    Assert(history.CanRedo);
    history.RecordBefore(restored.Building, 0);
    Assert(!history.CanRedo && history.RedoCount == 0);
    var other = BuildingStoreys.Copy(restored.Building);
    other.Name = "Different edit";
    Assert(history.TryUndo(other, 0, out var undoNewEdit));
    Assert(undoNewEdit.Building.Name == building.Name);
});

Check("History snapshots do not share mutable room or stair instances", () =>
{
    var history = new BuildingEditHistory();
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(building, out var two, out var reason), reason);
    two.Staircases.Add(new StaircaseDefinition { Centre = new Point2(5, 4) });
    history.RecordBefore(two, 1);
    two.Perimeter[0] = new Point2(-2, 0);
    two.Staircases[0].Centre = new Point2(7, 5);
    Assert(history.TryUndo(two, 0, out var copy));
    Equal(copy.Building.Perimeter[0].X, 0);
    Equal(copy.Building.Staircases[0].Centre.X, 5);

    // Even the returned snapshot must not mutate a subsequent redo target.
    copy.Building.Staircases[0].Centre = new Point2(8, 6);
    Assert(history.TryRedo(copy.Building, 1, out var redo));
    Equal(redo.Building.Staircases[0].Centre.X, 7);
});

Check("History records only successful commits and exposes bounded counts", () =>
{
    var history = new BuildingEditHistory();
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    for (int i = 0; i < 225; i++)
    {
        history.RecordBefore(building, 0);
        building = BuildingStoreys.Copy(building);
        building.Name = $"Version {i}";
    }
    Assert(history.UndoCount == 200);
    Assert(history.CanUndo);
    for (int i = 0; i < 200; i++)
    {
        Assert(history.TryUndo(building, 0, out var previous));
        building = previous.Building;
    }
    Assert(!history.CanUndo && history.RedoCount == 200);
    Assert(!history.TryUndo(building, 0, out _));
});

Check("New or opened document resets both global history directions", () =>
{
    var history = new BuildingEditHistory();
    var original = new BuildingDocument { Perimeter = rectangle.ToList() };
    history.RecordBefore(original, 0);
    var changed = BuildingStoreys.Copy(original);
    changed.Name = "Edited";
    Assert(history.TryUndo(changed, 0, out _));
    Assert(history.CanRedo);
    history.Clear();
    Assert(!history.CanUndo && !history.CanRedo);
    Assert(history.UndoCount == 0 && history.RedoCount == 0);
});

Check("Global history preserves JSON round-trip after cross-floor undo", () =>
{
    var history = new BuildingEditHistory();
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var room = new RoomDefinition { Name = "Upper study", Outline = rectangle.ToList() };
    history.RecordBefore(two, 1);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Rooms.Add(room);
    BuildingStoreys.SetFloor(two, 1, upper);
    Assert(history.TryUndo(two, 0, out var previous));
    var roundTripped = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(previous.Building));
    Assert(roundTripped.UpperFloors[0].Rooms.Count == 0);
    Assert(BuildingStoreys.FloorCount(roundTripped) == 2);
});

Check("Unassigned interior means usable floor without a named room", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    var floor = new RoomDefinition
    {
        Name = "Bedroom",
        Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)]
    };
    doc.Rooms.Add(floor);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(2, 4)) ==
        FloorSpaceAnalysis.SpaceKind.DefinedRoom);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(6, 4)) ==
        FloorSpaceAnalysis.SpaceKind.UnassignedInterior);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(12, 4)) ==
        FloorSpaceAnalysis.SpaceKind.Outside);
});

Check("Stairwell void is not classified as unassigned usable space", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    var stair = new StaircaseDefinition { Centre = new Point2(5, 4) };
    var voids = BuildingStoreys.GetOpenings([stair], 1);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(5, 4), voids) ==
        FloorSpaceAnalysis.SpaceKind.FloorOpening);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(9, 4), voids) ==
        FloorSpaceAnalysis.SpaceKind.UnassignedInterior);
});

Check("Window added to exterior wall survives save and load", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(WindowPlacement.TryAddWindow(doc, new Point2(5, 0), 1.2,
        out var changed, out var reason), reason);
    Assert(changed.Windows.Count == 1 && doc.Windows.Count == 0);
    var reopened = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(changed));
    Assert(reopened.Windows.Count == 1);
    Equal(reopened.Windows[0].Centre.X, 5);
    Equal(reopened.Windows[0].Width, 1.2);
});

Check("Window on an exterior diagonal remains valid", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(8, 4), new(8, 10), new(0, 10)]
    };
    Assert(WindowPlacement.TryAddWindow(doc, new Point2(4, 2), 1.6,
        out var changed, out var reason), reason);
    Assert(FloorGeometry.Validate(changed, out reason), reason);
});

Check("Interior partitions cannot contain exterior windows", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Left",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Right",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ]
    };
    Assert(!WindowPlacement.TryAddWindow(doc, new Point2(5, 4), 1.2,
        out _, out var reason));
    Assert(reason.Contains("exterior"));
});

Check("Windows must avoid corners and overlapping doors", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(!WindowPlacement.TryAddWindow(doc, new Point2(0.4, 0), 1.2,
        out _, out _));
    doc.Doors.Add(new DoorDefinition { Centre = new Point2(5, 0) });
    Assert(!WindowPlacement.TryAddWindow(doc, new Point2(6, 0), 1.2,
        out _, out var reason));
    Assert(reason.Contains("door"));
});

Check("Exterior windows cannot overlap one another", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(WindowPlacement.TryAddWindow(doc, new Point2(3, 0), 1.2,
        out var first, out var reason), reason);
    Assert(!WindowPlacement.TryAddWindow(first, new Point2(4, 0), 1.2,
        out _, out reason));
    Assert(reason.Contains("windows"));
});

Check("Window follows a rotated perimeter wall when a vertex moves", () =>
{
    var win = new WindowDefinition { Centre = new Point2(5, 0), Width = 1.2 };
    var moved = WindowPlacement.ForMovedVertex(rectangle, [], [win],
        new Point2(10, 0), new Point2(10, 2));
    Assert(moved.Count == 1 && moved[0].Id == win.Id);
    Equal(moved[0].Centre.X, 5);
    Equal(moved[0].Centre.Y, 1);
    Equal(win.Centre.Y, 0);
    var adjusted = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(10, 2), new(10, 8), new(0, 8)],
        Windows = moved
    };
    Assert(FloorGeometry.Validate(adjusted, out var reason), reason);
});

Check("Splitting a continuous exterior wall does not invalidate its window", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(PerimeterEditing.TrySplitWall(doc, true, null, 0,
        new Point2(5, 0), out var split, out var reason), reason);
    Assert(split.Windows.Count == 1);
    Assert(FloorGeometry.Validate(split, out reason), reason);
});

Check("Windows follow wall movement without changing their IDs", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(5, 0) }]
    };
    var wall = FloorGeometry.BuildWalls(doc.Perimeter, doc.Rooms)
        .Single(w => w.IsExterior && w.A.Y == 0 && w.B.Y == 0);
    Assert(InteriorEditing.TryMoveWall(doc, wall, 1, out var moved, out var reason), reason);
    Assert(moved.Windows.Count == 1 && moved.Windows[0].Id == doc.Windows[0].Id);
    Assert(moved.Windows[0].Centre.DistanceTo(doc.Windows[0].Centre) > 0.01);
    Assert(FloorGeometry.Validate(moved, out reason), reason);
});

Check("Window width is validated before changing stored geometry", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(1.1, 0) }]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    var proposed = BuildingStoreys.Copy(doc);
    proposed.Windows[0].Width = 3;
    Assert(!FloorGeometry.Validate(proposed, out reason));
    Equal(doc.Windows[0].Width, 1.2);
});

Check("Windows survive independent upper-floor save and global history snapshots", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Windows.Add(new WindowDefinition
    {
        Centre = new Point2(5, 0), Width = 1.5
    });
    var history = new BuildingEditHistory();
    history.RecordBefore(two, 1);
    BuildingStoreys.SetFloor(two, 1, upper);
    var opened = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(two));
    Assert(opened.Windows.Count == 0);
    Assert(opened.UpperFloors[0].Windows.Count == 1);
    Assert(history.TryUndo(two, 0, out var undone));
    Assert(undone.Building.UpperFloors[0].Windows.Count == 0);
    Assert(history.TryRedo(undone.Building, undone.FloorIndex, out var redone));
    Assert(redone.Building.UpperFloors[0].Windows.Count == 1);
});

Check("Older building files with no window property remain valid", () =>
{
    var oldJson = """{"schemaVersion":1,"name":"Original","perimeter":[{"x":0,"y":0},{"x":10,"y":0},{"x":10,"y":8},{"x":0,"y":8}],"rooms":[],"doors":[]}""";
    var loaded = BuildingDocumentJson.Deserialize(oldJson);
    Assert(loaded.Windows.Count == 0);
    Assert(FloorGeometry.Validate(loaded, out var reason), reason);
});

Check("Removing a perimeter corner repositions an exterior window", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(5, 0), new(10, 2), new(10, 8), new(0, 8)],
        Windows = [new WindowDefinition { Centre = new Point2(2.5, 0) }]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    Assert(PerimeterEditing.TryRemoveCorner(doc, true, null, 1,
        out var changed, out reason), reason);
    Assert(changed.Perimeter.Count == 4 && changed.Windows.Count == 1);
    Assert(changed.Windows[0].Id == doc.Windows[0].Id);
    Assert(changed.Windows[0].Centre.Y > 0.01);
    Assert(FloorGeometry.Validate(changed, out reason), reason);
});

Check("Completely assigned floor has no unassigned interior points", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [new RoomDefinition { Name = "Great Hall", Outline = rectangle.ToList() }]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(5, 4)) ==
        FloorSpaceAnalysis.SpaceKind.DefinedRoom);
    Assert(FloorSpaceAnalysis.Classify(doc, new Point2(12, 4)) ==
        FloorSpaceAnalysis.SpaceKind.Outside);
});

Check("Shared exterior wall move carries partial room walls and its window", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Shop",
                Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] },
            new RoomDefinition { Name = "Home",
                Outline = [new(4, 0), new(10, 0), new(10, 8), new(4, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(7, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(original, out var reason), reason);
    var wall = FloorGeometry.BuildWalls(original.Perimeter, original.Rooms)
        .First(w => w.IsExterior && w.A.Y == 0 && w.B.Y == 0);
    Assert(InteriorEditing.TryMoveWall(original, wall, 1,
        out var moved, out reason), reason);
    Equal(moved.Perimeter[0].Y, moved.Perimeter[1].Y);
    Equal(moved.Rooms[0].Outline[0].Y, moved.Perimeter[0].Y);
    Equal(moved.Rooms[0].Outline[1].Y, moved.Perimeter[0].Y);
    Equal(moved.Rooms[1].Outline[0].Y, moved.Perimeter[0].Y);
    Equal(moved.Rooms[1].Outline[1].Y, moved.Perimeter[0].Y);
    Equal(moved.Windows[0].Centre.Y, moved.Perimeter[0].Y);
    Equal(original.Windows[0].Centre.Y, 0);
    Assert(FloorGeometry.Validate(moved, out reason), reason);
});

Check("Moving perimeter corner interpolates every room's shared wall endpoints", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "A",
                Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] },
            new RoomDefinition { Name = "B",
                Outline = [new(4, 0), new(7, 0), new(7, 8), new(4, 8)] },
            new RoomDefinition { Name = "C",
                Outline = [new(7, 0), new(10, 0), new(10, 8), new(7, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(6, 0) }]
    };
    Assert(FloorGeometry.Validate(original, out var reason), reason);
    var moved = SharedBoundaryEditing.MoveVertex(original,
        new Point2(0, 0), new Point2(0, 2));
    Equal(moved.Rooms[0].Outline[1].Y, 1.2);
    Equal(moved.Rooms[1].Outline[0].Y, 1.2);
    Equal(moved.Rooms[1].Outline[1].Y, 0.6);
    Equal(moved.Rooms[2].Outline[0].Y, 0.6);
    Equal(moved.Rooms[2].Outline[1].Y, 0);
    Equal(moved.Windows[0].Centre.Y, 0.8);
    Equal(original.Rooms[1].Outline[0].Y, 0);
    Assert(FloorGeometry.Validate(moved, out reason), reason);
});

Check("Room corner on exterior is treated as an exterior corner for attached points", () =>
{
    var original = new BuildingDocument
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
    Assert(FloorGeometry.Validate(original, out var reason), reason);
    // Even when initiated by selecting a room vertex, a coincident perimeter
    // endpoint requires both adjacent rooms to adjust as one boundary.
    var changed = SharedBoundaryEditing.MoveVertex(original,
        new Point2(0, 0), new Point2(0, 1));
    Equal(changed.Perimeter[0].Y, 1);
    Equal(changed.Rooms[0].Outline[1].Y, 0.5);
    Equal(changed.Rooms[1].Outline[0].Y, 0.5);
    Assert(FloorGeometry.Validate(changed, out reason), reason);
});

Check("Interior vertex editing does not move the unrelated exterior outline", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [new RoomDefinition { Name = "Hall",
            Outline = [new(2, 2), new(8, 2), new(8, 6), new(2, 6)] }]
    };
    var moved = SharedBoundaryEditing.MoveVertex(
        doc, new Point2(2, 2), new Point2(3, 2));
    Assert(moved.Perimeter.SequenceEqual(rectangle));
    Equal(moved.Rooms[0].Outline[0].X, 3);
    Assert(FloorGeometry.Validate(moved, out var reason), reason);
});

Check("Moving an exterior wall moves all collinear perimeter split vertices", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter =
        [
            new(0, 0), new(5, 0), new(10, 0), new(10, 8), new(0, 8)
        ],
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    var fragment = FloorGeometry.BuildWalls(doc.Perimeter, doc.Rooms)
        .First(w => w.IsExterior && w.A.Y == 0 && w.B.Y == 0);
    Assert(InteriorEditing.TryMoveWall(doc, fragment, 1, out var moved, out reason), reason);
    Equal(moved.Perimeter[0].Y, moved.Perimeter[1].Y);
    Equal(moved.Perimeter[1].Y, moved.Perimeter[2].Y);
    Equal(moved.Rooms[0].Outline[1].Y, moved.Rooms[1].Outline[0].Y);
    Assert(FloorGeometry.Validate(moved, out reason), reason);
});

Check("Window relocation along exterior wall can be validated without changing size", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(3, 0), Width = 1.2 }]
    };
    var changed = BuildingStoreys.Copy(doc);
    changed.Windows[0].Centre = new Point2(6, 0);
    Assert(FloorGeometry.Validate(changed, out var reason), reason);
    Equal(changed.Windows[0].Width, 1.2);
    Equal(doc.Windows[0].Centre.X, 3);
    changed.Windows[0].Centre = new Point2(6, 3);
    Assert(!FloorGeometry.Validate(changed, out _));
});

Check("Window endpoint resizing keeps the opposite endpoint fixed", () =>
{
    var original = new WindowDefinition
    {
        Centre = new Point2(5, 0), Width = 1.2
    };
    // Original endpoints are 4.4 and 5.6. Keep 4.4 and drag 5.6 to 6.4.
    var resized = WindowPlacement.ResizeFromEndpoint(
        original, new Point2(4.4, 0), new Point2(6.4, 0));
    Equal(resized.Width, 2);
    Equal(resized.Centre.X, 5.4);
    var changed = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [resized]
    };
    Assert(FloorGeometry.Validate(changed, out var reason), reason);
    Equal(original.Width, 1.2);
});

Check("Window endpoint editing preserves identity and rejects invalid results", () =>
{
    var original = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(5, 0), Width = 1.2 }]
    };
    var oldWindow = original.Windows[0];
    var resized = WindowPlacement.ResizeFromEndpoint(oldWindow,
        new Point2(4.4, 0), new Point2(6.4, 0));
    Assert(resized.Id == oldWindow.Id);
    Assert(WindowPlacement.TryUpdateWindow(original, resized,
        out var updated, out var reason), reason);
    Equal(updated.Windows[0].Width, 2);
    Equal(updated.Windows[0].Centre.X, 5.4);
    Equal(original.Windows[0].Width, 1.2);

    var invalid = WindowPlacement.ResizeFromEndpoint(oldWindow,
        new Point2(4.4, 0), new Point2(10.2, 0));
    Assert(!WindowPlacement.TryUpdateWindow(original, invalid, out _, out reason));
    Assert(!string.IsNullOrWhiteSpace(reason));
    Equal(original.Windows[0].Centre.X, 5);
});

Check("Windows move cleanly along multiple exterior segments", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(2.5, 0) }]
    };
    var moved = doc.Windows[0].Copy();
    moved.Centre = new Point2(7.5, 0);
    Assert(WindowPlacement.TryUpdateWindow(doc, moved,
        out var updated, out var reason), reason);
    Assert(updated.Windows[0].Id == doc.Windows[0].Id);
    Equal(updated.Windows[0].Centre.X, 7.5);
    Assert(FloorGeometry.Validate(updated, out reason), reason);
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
