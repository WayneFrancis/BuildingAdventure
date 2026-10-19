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

Check("Wall thickness defaults and physically meaningful limits", () =>
{
    var defaults = new WallThicknessSettings();
    Equal(defaults.ExteriorMetres, 0.24);
    Equal(defaults.InteriorMetres, 0.12);
    Assert(defaults.Validate(out var reason), reason);
    var invalid = defaults.Copy();
    invalid.InteriorMetres = 0;
    Assert(!invalid.Validate(out reason) && reason.Contains("Interior"));
    invalid.InteriorMetres = 0.12;
    invalid.ExteriorMetres = 1.1;
    Assert(!invalid.Validate(out reason) && reason.Contains("Exterior"));
    invalid.ExteriorMetres = double.NaN;
    Assert(!invalid.Validate(out _));
    Assert(defaults.Validate(out reason), reason);
});

Check("A full perimeter without openings renders four solid wall pieces", () =>
{
    var floor = new BuildingDocument { Perimeter = rectangle.ToList() };
    var pieces = WallConstruction.SolidPieces(floor);
    Assert(pieces.Count == 4 && pieces.All(p => p.IsExterior));
    Equal(pieces.Sum(p => p.Length), PolygonGeometry.Perimeter(rectangle));
    Assert(pieces.All(p => p.Length > 0));
});

Check("Exterior doorway subtracts an actual opening from the solid wall", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    Equal(pieces.Sum(p => p.Length), 36 - 0.9);
    var top = pieces.Where(p => p.IsExterior &&
        Math.Abs(p.A.Y) < 1e-7 && Math.Abs(p.B.Y) < 1e-7).ToList();
    Assert(top.Count == 2);
    Assert(top.All(p => p.A.X <= 4.55 + 1e-6 ||
                        p.A.X >= 5.45 - 1e-6));
});

Check("Windows cut through actual exterior wall rather than just overpainting it", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(2, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    Equal(pieces.Sum(p => p.Length), 36 - 1.2);
    Assert(!pieces.Where(p => p.IsExterior && p.A.Y == 0 && p.B.Y == 0)
        .Any(p => p.A.X < 2 && p.B.X > 2));
});

Check("Multiple exterior wall openings subtract each width independently", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(7, 0), Width = 0.9 }],
        Windows = [new WindowDefinition { Centre = new Point2(3, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    Equal(pieces.Sum(p => p.Length), 36 - 0.9 - 1.2);
    Assert(pieces.Count == 6);
});

Check("Window across a room-split exterior wall cuts both wall fragments", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Left",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Right",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(5, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    // Perimeter 36 m and the 8 m partition, less the 1.2 m window.
    Equal(pieces.Sum(p => p.Length), 36 + 8 - 1.2);
    var top = pieces.Where(p => p.IsExterior &&
        Math.Abs(p.A.Y) < 1e-7 && Math.Abs(p.B.Y) < 1e-7).ToList();
    Assert(top.Count == 2);
    Assert(top.All(p => Math.Max(p.A.X, p.B.X) <= 4.4 + 1e-6 ||
                        Math.Min(p.A.X, p.B.X) >= 5.6 - 1e-6));
});

Check("Interior door only cuts the internal partition and preserves the exterior", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Left",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "Right",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4), Width = 0.9 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    Equal(pieces.Where(p => p.IsExterior).Sum(p => p.Length), 36);
    Equal(pieces.Where(p => !p.IsExterior).Sum(p => p.Length), 8 - 0.9);
});

Check("Door and window openings on diagonal walls cut correct physical length", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(8, 4), new(8, 10), new(0, 10)],
        Windows = [new WindowDefinition { Centre = new Point2(4, 2), Width = 1.6 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var pieces = WallConstruction.SolidPieces(floor);
    Equal(pieces.Sum(p => p.Length),
        PolygonGeometry.Perimeter(floor.Perimeter) - 1.6);
});

Check("Upper floors inherit thickness initially but edit independently", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        WallThickness = new WallThicknessSettings
        {
            ExteriorMetres = 0.35, InteriorMetres = 0.18
        }
    };
    Assert(BuildingStoreys.TryAddFloor(building, out var two, out var reason), reason);
    Equal(two.UpperFloors[0].WallThickness.ExteriorMetres, 0.35);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.WallThickness.ExteriorMetres = 0.5;
    upper.WallThickness.InteriorMetres = 0.08;
    BuildingStoreys.SetFloor(two, 1, upper);
    Equal(two.WallThickness.ExteriorMetres, 0.35);
    Equal(two.UpperFloors[0].WallThickness.ExteriorMetres, 0.5);
    var reopened = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(two));
    Equal(reopened.WallThickness.ExteriorMetres, 0.35);
    Equal(reopened.UpperFloors[0].WallThickness.InteriorMetres, 0.08);
    Assert(BuildingStoreys.Validate(reopened, out reason), reason);
});

Check("Wall thickness changes participate in whole-building undo and redo", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    var history = new BuildingEditHistory();
    history.RecordBefore(building, 0);
    var updated = BuildingStoreys.Copy(building);
    updated.WallThickness.ExteriorMetres = 0.45;
    Assert(history.TryUndo(updated, 0, out var undone));
    Equal(undone.Building.WallThickness.ExteriorMetres, 0.24);
    Assert(history.TryRedo(undone.Building, 0, out var redone));
    Equal(redone.Building.WallThickness.ExteriorMetres, 0.45);
});

Check("Older JSON documents get wall thickness defaults without schema migration", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    string json = BuildingDocumentJson.Serialize(building);
    json = System.Text.RegularExpressions.Regex.Replace(
        json, @",\s*""wallThickness""\s*:\s*\{[^}]*\}", "");
    var reopened = BuildingDocumentJson.Deserialize(json);
    Equal(reopened.WallThickness.ExteriorMetres, 0.24);
    Equal(reopened.WallThickness.InteriorMetres, 0.12);
    Assert(FloorGeometry.Validate(reopened, out var reason), reason);
});

Check("Invalid thickness is rejected without modifying the stored building", () =>
{
    var floor = new BuildingDocument { Perimeter = rectangle.ToList() };
    var invalid = BuildingStoreys.Copy(floor);
    invalid.WallThickness.InteriorMetres = double.NaN;
    Assert(!BuildingStoreys.Validate(invalid, out var reason));
    Assert(reason.Contains("thickness"));
    Equal(floor.WallThickness.InteriorMetres, 0.12);
});

Check("Right-angle exterior corners have solid mitred junctions", () =>
{
    var floor = new BuildingDocument { Perimeter = rectangle.ToList() };
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(joins.Count == 4);
    var topLeft = joins.Single(j => j.Centre == new Point2(0, 0));
    Assert(topLeft.IsExterior);
    Assert(topLeft.Outline.Count >= 4);
    Assert(PolygonGeometry.ContainsPoint(topLeft.Outline, new Point2(-0.1, -0.1)),
        "The outside-corner notch must be filled.");
    Assert(PolygonGeometry.ContainsPoint(topLeft.Outline, new Point2(0, 0)));
    Assert(!PolygonGeometry.ContainsPoint(topLeft.Outline, new Point2(-0.25, -0.25)),
        "A junction must not extend indefinitely beyond the wall.");
});

Check("Angled exterior corners receive filled miters without enormous spikes", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(8, 2), new(10, 8), new(2, 9)]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(joins.Count == 4);
    foreach (var join in joins)
    {
        Assert(join.Outline.Count >= 3);
        Assert(PolygonGeometry.ContainsPoint(join.Outline, join.Centre));
        Assert(PolygonGeometry.Area(join.Outline) > 0);
        Assert(join.Outline.All(p => p.DistanceTo(join.Centre) < 1),
            "The miter should stay close to the true structural corner.");
    }
});

Check("A partition T-junction with an exterior wall is sealed", () =>
{
    var floor = new BuildingDocument
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
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    var topT = joins.Single(j => j.Centre == new Point2(5, 0));
    Assert(topT.IsExterior);
    Assert(topT.Outline.Count >= 4);
    Assert(PolygonGeometry.ContainsPoint(topT.Outline, new Point2(5, -0.09)));
    Assert(PolygonGeometry.ContainsPoint(topT.Outline, new Point2(5.05, 0.02)));
    Assert(joins.Any(j => j.Centre == new Point2(5, 8)));
});

Check("Collinear split vertices do not create thick square bulges", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter =
        [
            new(0, 0), new(5, 0), new(10, 0), new(10, 8), new(0, 8)
        ]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(joins.Count == 4);
    Assert(!joins.Any(j => j.Centre == new Point2(5, 0)));
});

Check("Window opening endpoints are not closed by junction patches", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(5, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(!joins.Any(j =>
        j.Centre.DistanceTo(new Point2(4.4, 0)) < 0.01 ||
        j.Centre.DistanceTo(new Point2(5.6, 0)) < 0.01));
    Assert(joins.Count == 4);
    Assert(WallConstruction.SolidPieces(floor).Count == 5);
});

Check("Interior door jambs stay open when walls meet elsewhere", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4), Width = 0.9 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(!joins.Any(j => Math.Abs(j.Centre.X - 5) < 1e-6 &&
        (Math.Abs(j.Centre.Y - 3.55) < 0.01 ||
         Math.Abs(j.Centre.Y - 4.45) < 0.01)));
    Assert(joins.Any(j => j.Centre == new Point2(5, 0)));
    Assert(joins.Any(j => j.Centre == new Point2(5, 8)));
    Equal(WallConstruction.SolidPieces(floor)
        .Where(p => !p.IsExterior).Sum(p => p.Length), 8 - 0.9);
});

Check("Junction border padding expands fill without modifying wall openings", () =>
{
    var floor = new BuildingDocument { Perimeter = rectangle.ToList() };
    var inner = WallConstruction.JunctionPatches(floor);
    var outer = WallConstruction.JunctionPatches(floor, 0.02);
    Assert(inner.Count == outer.Count);
    var centre = new Point2(0, 0);
    var i = inner.Single(j => j.Centre == centre);
    var o = outer.Single(j => j.Centre == centre);
    Assert(PolygonGeometry.Area(o.Outline) > PolygonGeometry.Area(i.Outline));
    var invalid = false;
    try { WallConstruction.JunctionPatches(floor, -0.01); }
    catch (ArgumentOutOfRangeException) { invalid = true; }
    Assert(invalid);
});

Check("Narrow interior partition receives a smaller miter than exterior corners", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        WallThickness = new WallThicknessSettings
        {
            ExteriorMetres = 0.4,
            InteriorMetres = 0.1
        },
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var joins = WallConstruction.JunctionPatches(floor);
    Assert(joins.Count >= 6);
    Assert(joins.Single(j => j.Centre == new Point2(5, 0)).IsExterior);
    Assert(joins.Single(j => j.Centre == new Point2(5, 0)).Outline.Count >= 4);
});

Check("Opening grid snap uses half-metre increments on an axis-aligned wall", () =>
{
    var wall = new WallSegment(new(0, 0), new(10, 0), true);
    var snapped = OpeningEditing.OnWall(new Point2(3.26, 0.19), wall, true);
    Equal(snapped.X, 3.5);
    Equal(snapped.Y, 0);
    var free = OpeningEditing.OnWall(new Point2(3.26, 0.19), wall, false);
    Equal(free.X, 3.26);
    Equal(free.Y, 0);
});

Check("Snapped window endpoints remain attached to diagonal walls", () =>
{
    var wall = new WallSegment(new(0, 0), new(8, 4), true);
    var snapped = OpeningEditing.OnWall(new Point2(3.21, 1.61), wall, true);
    Equal(snapped.X, 3);
    Equal(snapped.Y, 1.5);
    var free = OpeningEditing.OnWall(new Point2(3.21, 1.61), wall, false);
    Assert(free.DistanceTo(snapped) > 0.1);
    Equal(free.Y, free.X / 2);
});

Check("Snapped pointer is clipped to finite wall endpoints", () =>
{
    var wall = new WallSegment(new(0, 0), new(2, 0), true);
    var result = OpeningEditing.OnWall(new Point2(9, -1), wall, true);
    Equal(result.X, 2);
    Equal(result.Y, 0);
});

Check("Door endpoint resize fixes the opposite end and preserves door identity", () =>
{
    var door = new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 };
    var edited = OpeningEditing.ResizeDoor(door,
        new Point2(4.55, 0), new Point2(6.05, 0));
    Equal(edited.Width, 1.5);
    Equal(edited.Centre.X, 5.3);
    Assert(edited.Id == door.Id);
    Equal(door.Width, 0.9);
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [door]
    };
    Assert(OpeningEditing.TryUpdateDoor(floor, edited, out var saved, out var reason),
        reason);
    Equal(saved.Doors[0].Width, 1.5);
    Equal(floor.Doors[0].Width, 0.9);
});

Check("Door can move onto a different wall including interior partitions", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 8), new(5, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(2, 0) }]
    };
    var changed = floor.Doors[0].Copy();
    changed.Centre = new Point2(5, 4);
    Assert(OpeningEditing.TryUpdateDoor(floor, changed, out var moved, out var reason),
        reason);
    Equal(moved.Doors[0].Centre.X, 5);
    Equal(moved.Doors[0].Centre.Y, 4);
    Equal(floor.Doors[0].Centre.X, 2);
    Assert(moved.Doors[0].Id == floor.Doors[0].Id);
});

Check("Door resize rejects impossible width while preserving the source", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(1, 0), Width = 0.9 }]
    };
    var proposed = floor.Doors[0].Copy();
    proposed.Width = 3;
    Assert(!OpeningEditing.TryUpdateDoor(floor, proposed, out _, out var reason));
    Assert(reason.Contains("wall"));
    Equal(floor.Doors[0].Width, 0.9);
});

Check("Door resize must reject overlapping doorways and windows", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [
            new DoorDefinition { Centre = new Point2(2, 0), Width = 0.9 },
            new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(8, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(floor, out var reason), reason);
    var tooWide = floor.Doors[1].Copy();
    tooWide.Centre = new Point2(4, 0);
    tooWide.Width = 3;
    Assert(!OpeningEditing.TryUpdateDoor(floor, tooWide, out _, out reason));
    Assert(reason.Contains("close"));
    var moved = floor.Doors[1].Copy();
    moved.Centre = new Point2(7, 0);
    Assert(!OpeningEditing.TryUpdateDoor(floor, moved, out _, out reason));
    Assert(reason.Contains("window"));
});

Check("Door editing participates in document history and JSON persistence", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(4, 0), Width = 0.9 }]
    };
    var journal = new BuildingEditHistory();
    journal.RecordBefore(source, 0);
    var edited = OpeningEditing.ResizeDoor(source.Doors[0],
        new Point2(3.55, 0), new Point2(5.05, 0));
    Assert(OpeningEditing.TryUpdateDoor(source, edited, out var current, out var reason),
        reason);
    var saved = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(current));
    Equal(saved.Doors[0].Width, 1.5);
    Assert(journal.TryUndo(saved, 0, out var old));
    Equal(old.Building.Doors[0].Width, 0.9);
    Assert(journal.TryRedo(old.Building, 0, out var again));
    Equal(again.Building.Doors[0].Width, 1.5);
});

Check("Window centres on its room wall segment, not full exterior perimeter", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Small shop",
                Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] },
            new RoomDefinition { Name = "Large shop",
                Outline = [new(4, 0), new(10, 0), new(10, 8), new(4, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(1, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    var windowId = building.Windows.Single().Id;
    Assert(OpeningEditing.TryCentreWindow(building, windowId,
        out var centred, out reason), reason);
    Equal(centred.Windows.Single().Centre.X, 2);
    Equal(centred.Windows.Single().Centre.Y, 0);
    Equal(building.Windows.Single().Centre.X, 1);
    Assert(centred.Windows.Single().Id == windowId);
    Assert(reason.Contains("room wall"));
});

Check("Window centres on second room segment of a shared long perimeter", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Small shop",
                Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] },
            new RoomDefinition { Name = "Large shop",
                Outline = [new(4, 0), new(10, 0), new(10, 8), new(4, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(8, 0), Width = 1.2 }]
    };
    Assert(OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var centred, out var reason), reason);
    Equal(centred.Windows[0].Centre.X, 7);
    Assert(FloorGeometry.Validate(centred, out reason), reason);
});

Check("Window in unassigned interior uses exterior segment as fallback", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(2, 0), Width = 1.2 }]
    };
    Assert(OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var centred, out var reason), reason);
    Equal(centred.Windows[0].Centre.X, 5);
    Assert(reason.Contains("no room wall"));
});

Check("Window centring on an angled room wall stays on its diagonal edge", () =>
{
    var outline = new List<Point2>
    {
        new(0, 0), new(8, 4), new(8, 10), new(0, 10)
    };
    var building = new BuildingDocument
    {
        Perimeter = outline,
        Rooms = [new RoomDefinition { Name = "Angled room",
            Outline = outline.ToList() }],
        Windows = [new WindowDefinition { Centre = new Point2(2, 1), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    Assert(OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var centred, out reason), reason);
    Equal(centred.Windows[0].Centre.X, 4);
    Equal(centred.Windows[0].Centre.Y, 2);
    Assert(FloorGeometry.Validate(centred, out reason), reason);
});

Check("Interior door centres on its physical partition segment", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(5, 0), new(5, 3), new(0, 3)] },
            new RoomDefinition { Name = "East",
                Outline = [new(5, 0), new(10, 0), new(10, 3), new(5, 3)] },
            new RoomDefinition { Name = "Rear",
                Outline = [new(0, 3), new(10, 3), new(10, 8), new(0, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 1), Width = 0.9 }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    Assert(OpeningEditing.TryCentreDoor(building, building.Doors[0].Id,
        out var centred, out reason), reason);
    Equal(centred.Doors[0].Centre.X, 5);
    Equal(centred.Doors[0].Centre.Y, 1.5);
    Equal(building.Doors[0].Centre.Y, 1);
});

Check("Exterior door centres on room-divided physical wall segment", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "West",
                Outline = [new(0, 0), new(4, 0), new(4, 8), new(0, 8)] },
            new RoomDefinition { Name = "East",
                Outline = [new(4, 0), new(10, 0), new(10, 8), new(4, 8)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(1, 0), Width = 0.9 }]
    };
    Assert(OpeningEditing.TryCentreDoor(building, building.Doors[0].Id,
        out var centred, out var reason), reason);
    Equal(centred.Doors[0].Centre.X, 2);
    Assert(FloorGeometry.Validate(centred, out reason), reason);
});

Check("Window will not silently centre on the perimeter if room segment is too short", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Tiny room",
                Outline = [new(0, 0), new(1, 0), new(1, 8), new(0, 8)] },
            new RoomDefinition { Name = "Hall",
                Outline = [new(1, 0), new(10, 0), new(10, 8), new(1, 8)] }
        ],
        Windows = [new WindowDefinition { Centre = new Point2(1, 0), Width = 1.2 }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    // Window currently sits at the shared corner and straddles both segments.
    // For the action, the short segment cannot contain this opening.
    // The other adjacent room segment is eligible; it must not select the
    // entire 10m perimeter midpoint.
    Assert(OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var centred, out reason), reason);
    Equal(centred.Windows[0].Centre.X, 5.5);
    Assert(centred.Windows[0].Centre.X != 5);
});

Check("Centring a window is rejected if the room segment midpoint conflicts with a door", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [new RoomDefinition { Name = "Front",
            Outline = [new(0, 0), new(6, 0), new(6, 8), new(0, 8)] }],
        Windows = [new WindowDefinition { Centre = new Point2(1.5, 0), Width = 1.2 }],
        Doors = [new DoorDefinition { Centre = new Point2(3, 0), Width = 0.9 }]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    Assert(!OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var ignored, out reason));
    Assert(reason.Contains("Cannot centre"));
    Equal(building.Windows[0].Centre.X, 1.5);
    Equal(ignored.Windows[0].Centre.X, 1.5);
});

Check("Centring a door rejects overlapping openings and does not mutate it", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [
            new DoorDefinition { Centre = new Point2(2, 0), Width = 0.9 },
            new DoorDefinition { Centre = new Point2(5, 0), Width = 0.9 }
        ]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    // The first door would be moved to the midpoint of the entire front wall,
    // colliding with the second door.
    Assert(!OpeningEditing.TryCentreDoor(building, building.Doors[0].Id,
        out var unchanged, out reason));
    Equal(unchanged.Doors[0].Centre.X, 2);
    Equal(building.Doors[0].Centre.X, 2);
});

Check("Centring door and window are undoable and preserve saved JSON", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Windows = [new WindowDefinition { Centre = new Point2(2, 0) }],
        Doors = [new DoorDefinition { Centre = new Point2(8, 0) }]
    };
    var journal = new BuildingEditHistory();
    journal.RecordBefore(building, 0);
    Assert(OpeningEditing.TryCentreWindow(building, building.Windows[0].Id,
        out var withWindow, out var reason), reason);
    Equal(withWindow.Windows[0].Centre.X, 5);
    // Door midpoint conflicts with centred window: move the door first to
    // another wall so both centring changes are valid.
    withWindow.Doors[0].Centre = new Point2(10, 4);
    Assert(FloorGeometry.Validate(withWindow, out reason), reason);
    Assert(OpeningEditing.TryCentreDoor(withWindow, withWindow.Doors[0].Id,
        out var centred, out reason), reason);
    var reopened = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(centred));
    Equal(reopened.Doors[0].Centre.Y, 4);
    Assert(journal.TryUndo(reopened, 0, out var restored));
    Equal(restored.Building.Windows[0].Centre.X, 2);
    Equal(restored.Building.Doors[0].Centre.X, 8);
});

Check("Whole-building generator previews three valid independently populated floors", () =>
{
    Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 1234,
        out var suggestion, out var reason), reason);
    Assert(BuildingStoreys.FloorCount(suggestion.Building) == 3);
    Assert(suggestion.FloorIndexes.SequenceEqual([0, 1, 2]));
    Assert(suggestion.Building.SchemaVersion == 2);
    Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
    for (int i = 0; i < 3; i++)
    {
        var floor = BuildingStoreys.GetFloor(suggestion.Building, i);
        Assert(floor.Perimeter.Count == 4);
        Assert(floor.Rooms.Count > 0);
    }
});

Check("Whole-building generation supports one and four floors", () =>
{
    foreach (int count in new[] { 1, 4 })
    {
        Assert(BuildingGenerator.TrySuggestWholeBuilding(count, 321,
            out var result, out var reason), reason);
        Assert(BuildingStoreys.FloorCount(result.Building) == count);
        Assert(BuildingStoreys.Validate(result.Building, out reason), reason);
    }
    Assert(!BuildingGenerator.TrySuggestWholeBuilding(5, 42,
        out _, out _));
});

Check("Generator creates an initial perimeter without modifying the input", () =>
{
    var blank = new BuildingDocument();
    Assert(BuildingGenerator.TrySuggest(blank, [0], 867,
        out var suggestion, out var reason), reason);
    Assert(blank.Perimeter.Count == 0 && blank.Rooms.Count == 0);
    Assert(suggestion.Building.Perimeter.Count == 4);
    Assert(suggestion.Building.Rooms.Count > 0);
    Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
});

Check("Generator only edits checked floors, including upper floors", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(two, [1], 99,
        out var suggestion, out reason), reason);
    Assert(suggestion.Building.Rooms.Count == 0);
    Assert(suggestion.Building.UpperFloors[0].Rooms.Count > 0);
    Assert(two.UpperFloors[0].Rooms.Count == 0);
    Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
});

Check("Generator leaves existing room geometry and door locations intact", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms = [new RoomDefinition { Name = "Existing",
            Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)] }],
        Doors = [new DoorDefinition { Centre = new Point2(5, 0) }]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(doc, [0], 1938,
        out var proposal, out reason), reason);
    Assert(proposal.Building.Rooms.Count > 1);
    Assert(proposal.Building.Rooms[0].Id == doc.Rooms[0].Id);
    Assert(proposal.Building.Rooms[0].Outline.SequenceEqual(doc.Rooms[0].Outline));
    Equal(proposal.Building.Doors[0].Centre.X, 5);
    Assert(doc.Rooms.Count == 1);
});

Check("Regenerating from the same base retains originals without accumulating edits", () =>
{
    var original = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingGenerator.TrySuggest(original, [0], 101,
        out var first, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(original, [0], 202,
        out var second, out reason), reason);
    Assert(original.Rooms.Count == 0);
    Assert(first.Building.Rooms.Count > 0);
    Assert(second.Building.Rooms.Count > 0);
    Assert(first.Seed != second.Seed);
    Assert(BuildingStoreys.Validate(first.Building, out reason), reason);
    Assert(BuildingStoreys.Validate(second.Building, out reason), reason);
});

Check("Generator copies candidates so remembered variants are independent", () =>
{
    var original = new BuildingDocument();
    Assert(BuildingGenerator.TrySuggest(original, [0], 811,
        out var suggestion, out var reason), reason);
    var remembered = suggestion.Copy();
    remembered.Building.Rooms[0].Name = "Remembered candidate";
    Assert(suggestion.Building.Rooms[0].Name != "Remembered candidate");
    remembered.Building.Perimeter[0] = new Point2(500, 500);
    Assert(suggestion.Building.Perimeter[0] != new Point2(500, 500));
});

Check("Generator rejects empty or invalid floor selections", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(!BuildingGenerator.TrySuggest(doc, [], 101,
        out _, out var reason));
    Assert(reason.Contains("at least one"));
    Assert(!BuildingGenerator.TrySuggest(doc, [5], 101,
        out _, out reason));
    Assert(reason.Contains("no longer exists"));
});

Check("Generator acceptance can be undone as a single document change", () =>
{
    var before = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingGenerator.TrySuggest(before, [0], 654,
        out var preview, out var reason), reason);
    var journal = new BuildingEditHistory();
    journal.RecordBefore(before, 0);
    Assert(journal.TryUndo(preview.Building, 0, out var previous));
    Assert(previous.Building.Rooms.Count == 0);
    Assert(journal.TryRedo(previous.Building, 0, out var restored));
    Assert(restored.Building.Rooms.Count == preview.Building.Rooms.Count);
});

Check("Ordinary doors are visible to players, secret doors begin hidden", () =>
{
    var ordinary = new DoorDefinition { Centre = new Point2(2, 0) };
    var secret = new DoorDefinition
    {
        Centre = new Point2(7, 0), IsSecret = true, IsRevealed = false
    };
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [ordinary, secret]
    };
    Assert(FloorGeometry.Validate(building, out var reason), reason);
    var playerPlan = SecretDoorVisibility.PhysicalPlanForPlayer(building);
    Assert(playerPlan.Doors.Count == 1);
    Assert(playerPlan.Doors.Single().Id == ordinary.Id);
    Assert(building.Doors.Count == 2);
    Assert(SecretDoorVisibility.IsVisibleToPlayer(ordinary));
    Assert(!SecretDoorVisibility.IsVisibleToPlayer(secret));
    Equal(WallConstruction.SolidPieces(playerPlan).Sum(p => p.Length),
        WallConstruction.SolidPieces(building).Sum(p => p.Length) + secret.Width);
});

Check("Revealed secret door becomes visible without altering the source", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(5, 0),
            IsSecret = true, IsRevealed = true }]
    };
    var plan = SecretDoorVisibility.PhysicalPlanForPlayer(building);
    Assert(plan.Doors.Count == 1);
    Assert(SecretDoorVisibility.IsVisibleToPlayer(building.Doors[0]));
});

Check("Secret door flags persist in JSON and detached history snapshots", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Doors = [new DoorDefinition { Centre = new Point2(5, 0),
            IsSecret = true }]
    };
    var loaded = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(doc));
    Assert(loaded.Doors[0].IsSecret);
    Assert(!loaded.Doors[0].IsRevealed);
    var history = new BuildingEditHistory();
    history.RecordBefore(doc, 0);
    var revealed = BuildingStoreys.Copy(doc);
    revealed.Doors[0].IsRevealed = true;
    Assert(history.TryUndo(revealed, 0, out var earlier));
    Assert(!earlier.Building.Doors[0].IsRevealed);
    Assert(history.TryRedo(earlier.Building, 0, out var current));
    Assert(current.Building.Doors[0].IsRevealed);
});

Check("Generator avoids placing named rooms across existing stair footprints", () =>
{
    var ground = new BuildingDocument
    {
        Perimeter = [new(0, 0), new(16, 0), new(16, 12), new(0, 12)]
    };
    Assert(BuildingStoreys.TryAddFloor(ground, out var withUpper, out var reason), reason);
    var stairs = new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(4, 4),
        Length = 3.6, Width = 1.4
    };
    Assert(BuildingStoreys.TryAddStaircase(withUpper, stairs,
        out var source, out reason), reason);
    Assert(BuildingGenerator.TrySuggest(source, [0, 1], 1642,
        out var result, out reason), reason);
    foreach (int index in new[] { 0, 1 })
    foreach (var room in BuildingStoreys.GetFloor(result.Building, index).Rooms)
        Assert(!PolygonGeometry.ContainsPoint(room.Outline, stairs.Centre));
    Assert(BuildingStoreys.Validate(result.Building, out reason), reason);
});

Check("Basement levels use stable negative indices with ground remaining zero", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var one, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(one, out var two, out reason), reason);
    Assert(BuildingStoreys.FloorCount(two) == 3);
    Assert(BuildingStoreys.LowestFloorIndex(two) == -2);
    Assert(BuildingStoreys.HighestFloorIndex(two) == 0);
    Assert(BuildingStoreys.FloorIndexes(two).SequenceEqual([-2, -1, 0]));
    Assert(BuildingStoreys.FloorLabel(-1) == "Basement 1");
    Assert(BuildingStoreys.FloorLabel(-2) == "Basement 2");
    Assert(two.BasementFloors[0].Name == "Basement 1");
    Assert(two.BasementFloors[1].Name == "Basement 2");
    Assert(BuildingStoreys.Validate(two, out reason), reason);
    Assert(ground.BasementFloors.Count == 0);
});

Check("Basement and upper floor indices coexist without renumbering ground", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var withBasement, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(withBasement, out var withUpper, out reason), reason);
    Assert(BuildingStoreys.FloorIndexes(withUpper).SequenceEqual([-1, 0, 1]));
    Assert(BuildingStoreys.FloorCount(withUpper) == 3);
    var basement = BuildingStoreys.GetFloor(withUpper, -1);
    basement.Rooms.Add(new RoomDefinition { Name = "Cellar", Type = "Cellar",
        Outline = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)] });
    BuildingStoreys.SetFloor(withUpper, -1, basement);
    Assert(withUpper.Rooms.Count == 0);
    Assert(withUpper.UpperFloors[0].Rooms.Count == 0);
    Assert(withUpper.BasementFloors[0].Rooms.Count == 1);
    Assert(BuildingStoreys.Validate(withUpper, out reason), reason);
});

Check("Basement staircase creates slab opening at ground floor", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var withBasement, out var reason), reason);
    var stairs = new StaircaseDefinition
    {
        LowerFloorIndex = -1, Centre = new Point2(5, 4),
        Length = 3.6, Width = 1.4
    };
    Assert(BuildingStoreys.TryAddStaircase(withBasement, stairs,
        out var placed, out reason), reason);
    Assert(BuildingStoreys.GetOpenings(placed, 0).Count == 1);
    Assert(BuildingStoreys.GetOpenings(placed, -1).Count == 0);
    Assert(BuildingStoreys.GetOpenings(placed, 0)[0].StaircaseId == stairs.Id);
    Equal(BuildingStoreys.SlabArea(placed, 0), 80 - 3.6 * 1.4);
    Assert(BuildingStoreys.Validate(placed, out reason), reason);
});

Check("Second basement staircase opens first basement floor slab", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var one, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(one, out var two, out reason), reason);
    var flight = new StaircaseDefinition { LowerFloorIndex = -2,
        Centre = new Point2(5, 4), Length = 3.6, Width = 1.4 };
    Assert(BuildingStoreys.TryAddStaircase(two, flight, out var placed, out reason), reason);
    Assert(BuildingStoreys.GetOpenings(placed, -1).Count == 1);
    Assert(BuildingStoreys.GetOpenings(placed, 0).Count == 0);
    Assert(BuildingStoreys.Validate(placed, out reason), reason);
});

Check("Removing deepest basement cleans only its connecting stairs", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var one, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(one, out var two, out reason), reason);
    var deep = new StaircaseDefinition { LowerFloorIndex = -2,
        Centre = new Point2(3, 4) };
    var groundAccess = new StaircaseDefinition { LowerFloorIndex = -1,
        Centre = new Point2(7, 4) };
    Assert(BuildingStoreys.TryAddStaircase(two, deep, out var withDeep, out reason), reason);
    Assert(BuildingStoreys.TryAddStaircase(withDeep, groundAccess,
        out var withBoth, out reason), reason);
    Assert(BuildingStoreys.TryRemoveBottomBasement(withBoth, out var remaining,
        out reason), reason);
    Assert(remaining.BasementFloors.Count == 1);
    Assert(remaining.Staircases.Count == 1);
    Assert(remaining.Staircases[0].LowerFloorIndex == -1);
    Assert(BuildingStoreys.Validate(remaining, out reason), reason);
});

Check("Basements round-trip JSON and remain independent in history", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    var journal = new BuildingEditHistory();
    journal.RecordBefore(ground, 0);
    Assert(BuildingStoreys.TryAddBasement(ground, out var added, out var reason), reason);
    var restored = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(added));
    Assert(restored.BasementFloors.Count == 1);
    Assert(BuildingStoreys.FloorIndexes(restored).SequenceEqual([-1, 0]));
    Assert(journal.TryUndo(restored, -1, out var back));
    Assert(back.Building.BasementFloors.Count == 0);
    Assert(journal.TryRedo(back.Building, 0, out var again));
    Assert(again.Building.BasementFloors.Count == 1);
});

Check("Generator targets basement without changing untouched upper floors", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var one, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(one, out var building, out reason), reason);
    Assert(BuildingGenerator.TrySuggest(building, [-1], 785, out var proposal,
        out reason), reason);
    Assert(BuildingStoreys.GetFloor(proposal.Building, -1).Rooms.Count > 0);
    Assert(BuildingStoreys.GetFloor(proposal.Building, 0).Rooms.Count == 0);
    Assert(BuildingStoreys.GetFloor(proposal.Building, 1).Rooms.Count == 0);
});

Check("Generator selected basement and upper floor generate on both", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var one, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(one, out var building, out reason), reason);
    Assert(BuildingGenerator.TrySuggest(building, [-1, 1], 141,
        out var proposal, out reason), reason);
    Assert(BuildingStoreys.GetFloor(proposal.Building, -1).Rooms.Count > 0);
    Assert(BuildingStoreys.GetFloor(proposal.Building, 1).Rooms.Count > 0);
    Assert(BuildingStoreys.GetFloor(proposal.Building, 0).Rooms.Count == 0);
    Assert(proposal.FloorIndexes.SequenceEqual([-1, 1]));
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Four-level combined limit includes basements", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var b1, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(b1, out var b2, out reason), reason);
    Assert(BuildingStoreys.TryAddFloor(b2, out var four, out reason), reason);
    Assert(BuildingStoreys.FloorCount(four) == 4);
    Assert(!BuildingStoreys.TryAddBasement(four, out _, out _));
    Assert(!BuildingStoreys.TryAddFloor(four, out _, out _));
});

Check("Accepting one proposed room does not accept other proposed rooms", () =>
{
    var building = new BuildingDocument { Perimeter = rectangle.ToList() };
    var suggestions = new List<RoomDefinition>
    {
        new() { Name = "First proposal",
            Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)] },
        new() { Name = "Second proposal",
            Outline = [new(4, 0), new(8, 0), new(8, 3), new(4, 3)] }
    };
    var candidate = BuildingStoreys.Copy(building);
    candidate.Rooms.AddRange(suggestions);
    var proposal = new BuildingSuggestion(candidate, 17, [0], "Two rooms");
    Assert(BuildingGenerator.TryAcceptRoom(building, proposal, 0,
        suggestions[0].Id, out var updated, out var reason), reason);
    Assert(updated.Rooms.Count == 1);
    Assert(updated.Rooms[0].Id == suggestions[0].Id);
    Assert(building.Rooms.Count == 0);
    Assert(candidate.Rooms.Count == 2);
    Assert(BuildingGenerator.TryAcceptRoom(updated, proposal, 0,
        suggestions[1].Id, out var withBoth, out reason), reason);
    Assert(withBoth.Rooms.Count == 2);
    Assert(BuildingStoreys.Validate(withBoth, out reason), reason);
});

Check("Accepting a suggested room on a blank floor copies its initial perimeter", () =>
{
    var blank = new BuildingDocument();
    Assert(BuildingGenerator.TrySuggest(blank, [0], 817,
        out var suggestion, out var reason), reason);
    var chosen = suggestion.Building.Rooms[0];
    Assert(BuildingGenerator.TryAcceptRoom(blank, suggestion, 0,
        chosen.Id, out var committed, out reason), reason);
    Assert(committed.Perimeter.SequenceEqual(suggestion.Building.Perimeter));
    Assert(committed.Rooms.Count == 1);
    Assert(blank.Perimeter.Count == 0);
});

Check("Accepting one proposed basement room affects no other floors", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(ground, out var withBasement, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(withBasement, out var three, out reason), reason);
    Assert(BuildingGenerator.TrySuggest(three, [-1, 1], 211,
        out var proposal, out reason), reason);
    Guid roomId = BuildingStoreys.GetFloor(proposal.Building, -1).Rooms[0].Id;
    Assert(BuildingGenerator.TryAcceptRoom(three, proposal, -1, roomId,
        out var committed, out reason), reason);
    Assert(BuildingStoreys.GetFloor(committed, -1).Rooms.Count == 1);
    Assert(BuildingStoreys.GetFloor(committed, 1).Rooms.Count == 0);
    Assert(BuildingStoreys.GetFloor(committed, 0).Rooms.Count == 0);
    Assert(!BuildingGenerator.TryAcceptRoom(committed, proposal, -1, roomId,
        out _, out reason));
    Assert(reason.Contains("already been accepted"));
});

Check("Partial room acceptance rejects wrong floor or absent room", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    var proposalBuilding = BuildingStoreys.Copy(doc);
    var candidateRoom = new RoomDefinition
    {
        Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)]
    };
    proposalBuilding.Rooms.Add(candidateRoom);
    var suggestion = new BuildingSuggestion(proposalBuilding, 3, [0], "One room");
    Assert(!BuildingGenerator.TryAcceptRoom(doc, suggestion, -1,
        candidateRoom.Id, out _, out var message));
    Assert(message.Contains("not part"));
    Assert(!BuildingGenerator.TryAcceptRoom(doc, suggestion, 0,
        Guid.NewGuid(), out _, out message));
    Assert(message.Contains("not found"));
});

Check("Delete arbitrary middle floor shifts upper levels and preserves their rooms", () =>
{
    var baseFloor = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(baseFloor, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    Assert(BuildingStoreys.TryAddFloor(three, out var four, out reason), reason);
    var highest = BuildingStoreys.GetFloor(four, 3);
    highest.Rooms.Add(new RoomDefinition
    {
        Name = "Attic",
        Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)]
    });
    BuildingStoreys.SetFloor(four, 3, highest);
    Assert(BuildingStoreys.TryRemoveFloor(four, 1,
        out var shorter, out var lost, out reason), reason);
    Assert(lost == 0);
    Assert(BuildingStoreys.FloorIndexes(shorter).SequenceEqual([0, 1, 2]));
    Assert(BuildingStoreys.GetFloor(shorter, 2).Rooms[0].Name == "Attic");
    Assert(BuildingStoreys.GetFloor(shorter, 1).Rooms.Count == 0);
    Assert(BuildingStoreys.GetFloor(four, 3).Rooms.Count == 1);
    Assert(BuildingStoreys.Validate(shorter, out reason), reason);
});

Check("Deleting ground promotes an upper floor with all its contents", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Rooms.Add(new RoomDefinition
    {
        Name = "Former first floor",
        Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)]
    });
    BuildingStoreys.SetFloor(two, 1, upper);
    Assert(BuildingStoreys.TryRemoveFloor(two, 0, out var remaining,
        out int removedStairs, out reason), reason);
    Assert(removedStairs == 0);
    Assert(remaining.HasGroundFloor && remaining.UpperFloors.Count == 0);
    Assert(remaining.Rooms.Count == 1 && remaining.Rooms[0].Name == "Former first floor");
    Assert(BuildingStoreys.FloorIndexes(remaining).SequenceEqual([0]));
    Assert(BuildingStoreys.Validate(remaining, out reason), reason);
});

Check("Deleting ground from a cellar building can leave basement but no ground", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(doc, out var withBasement, out var reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(withBasement, 0, out var remaining,
        out _, out reason), reason);
    Assert(!remaining.HasGroundFloor);
    Assert(BuildingStoreys.FloorIndexes(remaining).SequenceEqual([-1]));
    Assert(!BuildingStoreys.HasFloor(remaining, 0));
    Assert(remaining.Perimeter.Count == 0);
    Assert(BuildingStoreys.GetFloor(remaining, -1).Perimeter.Count == 4);
    Assert(BuildingStoreys.Validate(remaining, out reason), reason);
    Assert(BuildingStoreys.TryAddFloor(remaining, out var newGround, out reason), reason);
    Assert(newGround.HasGroundFloor && BuildingStoreys.HasFloor(newGround, 0));
    Assert(BuildingStoreys.Validate(newGround, out reason), reason);
});

Check("Deleting first basement shifts ground into a basement and upper into ground", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(doc, out var basement, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(basement, out var three, out reason), reason);
    var first = BuildingStoreys.GetFloor(three, 1);
    first.Rooms.Add(new RoomDefinition
    {
        Name = "Promoted ground room",
        Outline = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)]
    });
    BuildingStoreys.SetFloor(three, 1, first);
    Assert(BuildingStoreys.TryRemoveFloor(three, -1,
        out var result, out _, out reason), reason);
    Assert(BuildingStoreys.FloorIndexes(result).SequenceEqual([-1, 0]));
    Assert(result.Rooms.Single().Name == "Promoted ground room");
    Assert(result.BasementFloors.Count == 1);
    Assert(BuildingStoreys.Validate(result, out reason), reason);
});

Check("Deleting topmost basement in basement-only structure closes index gaps", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(doc, out var first, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(first, out var second, out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(second, 0, out var cellarsOnly,
        out _, out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(cellarsOnly, -1, out var remainder,
        out _, out reason), reason);
    Assert(BuildingStoreys.FloorIndexes(remainder).SequenceEqual([-1]));
    Assert(BuildingStoreys.GetFloor(remainder, -1).Perimeter.Count == 4);
    Assert(BuildingStoreys.Validate(remainder, out reason), reason);
});

Check("Deleting a floor removes attached stair flights and renumbers surviving flights", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(doc, out var b1, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(b1, out var first, out reason), reason);
    Assert(BuildingStoreys.TryAddFloor(first, out var second, out reason), reason);
    var low = new StaircaseDefinition { LowerFloorIndex = -1, Centre = new Point2(3, 4) };
    var above = new StaircaseDefinition { LowerFloorIndex = 1, Centre = new Point2(7, 4) };
    Assert(BuildingStoreys.TryAddStaircase(second, low, out var hasLow, out reason), reason);
    Assert(BuildingStoreys.TryAddStaircase(hasLow, above,
        out var hasBoth, out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(hasBoth, 1, out var result,
        out var lost, out reason), reason);
    Assert(lost == 1);
    Assert(result.Staircases.Count == 1 && result.Staircases[0].Id == low.Id);
    Assert(BuildingStoreys.FloorIndexes(result).SequenceEqual([-1, 0, 1]));
    Assert(BuildingStoreys.Validate(result, out reason), reason);
});

Check("Delete floor never leaves invalid stair links or changes input", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    var link = new StaircaseDefinition { LowerFloorIndex = 0, Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(two, link, out var withStairs, out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(withStairs, 0, out var result,
        out var lost, out reason), reason);
    Assert(lost == 1 && result.Staircases.Count == 0);
    Assert(withStairs.Staircases.Count == 1);
    Assert(!BuildingStoreys.TryRemoveFloor(new BuildingDocument(), 0,
        out _, out _, out reason));
    Assert(reason.Contains("at least one floor"));
});

Check("Stacked staircase flights allow exactly matching up and down footprints", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    var lower = new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(5, 4),
        Length = 3.6, Width = 1.4
    };
    var upper = new StaircaseDefinition
    {
        LowerFloorIndex = 1, Centre = new Point2(5, 4),
        Length = 3.6, Width = 1.4
    };
    Assert(BuildingStoreys.TryAddStaircase(three, lower, out var first, out reason), reason);
    Assert(BuildingStoreys.TryAddStaircase(first, upper, out var both, out reason), reason);
    Assert(both.Staircases.Count == 2);
    Assert(BuildingStoreys.GetOpenings(both, 1).Count == 1);
    Assert(BuildingStoreys.GetOpenings(both, 2).Count == 1);
    Assert(BuildingStoreys.Validate(both, out reason), reason);
    var roundTrip = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(both));
    Assert(roundTrip.Staircases.Count == 2);
});

Check("Deleting one intermediate level deletes both connecting stacked flights", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    var low = new StaircaseDefinition { LowerFloorIndex = 0, Centre = new Point2(5, 4) };
    var high = new StaircaseDefinition { LowerFloorIndex = 1, Centre = new Point2(5, 4) };
    Assert(BuildingStoreys.TryAddStaircase(three, low, out var one, out reason), reason);
    Assert(BuildingStoreys.TryAddStaircase(one, high, out var both, out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(both, 1, out var remaining,
        out var lost, out reason), reason);
    Assert(lost == 2);
    Assert(remaining.Staircases.Count == 0);
    Assert(BuildingStoreys.FloorIndexes(remaining).SequenceEqual([0, 1]));
    Assert(BuildingStoreys.Validate(remaining, out reason), reason);
});

Check("Whole-building generator creates four real floors before acceptance", () =>
{
    Assert(BuildingGenerator.TrySuggestWholeBuilding(4, 1783,
        out var proposal, out var reason), reason);
    Assert(BuildingStoreys.FloorCount(proposal.Building) == 4);
    Assert(BuildingStoreys.FloorIndexes(proposal.Building).SequenceEqual([0, 1, 2, 3]));
    Assert(proposal.FloorIndexes.SequenceEqual([0, 1, 2, 3]));
    for (int floor = 0; floor < 4; floor++)
        Assert(BuildingStoreys.GetFloor(proposal.Building, floor).Rooms.Count > 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Generator fills a narrow unused strip when large random rectangles do not fit", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Existing large room",
                Outline = [new(0, 0), new(8, 0), new(8, 8), new(0, 8)]
            }
        ]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(doc, [0], 2026,
        out var proposal, out reason), reason);
    Assert(proposal.Building.Rooms.Count > doc.Rooms.Count);
    Assert(proposal.Building.Rooms[0].Id == doc.Rooms[0].Id);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Selecting all four existing floors generates rooms on all four", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    Assert(BuildingStoreys.TryAddFloor(three, out var four, out reason), reason);
    Assert(BuildingGenerator.TrySuggest(four, [0, 1, 2, 3], 601,
        out var suggestion, out reason), reason);
    foreach (int floor in new[] { 0, 1, 2, 3 })
        Assert(BuildingStoreys.GetFloor(suggestion.Building, floor).Rooms.Count > 0);
    Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
});

Check("Basement-only buildings save and reopen without introducing ground", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddBasement(doc, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(two, 0, out var cellar,
        out _, out reason), reason);
    var reopened = BuildingDocumentJson.Deserialize(BuildingDocumentJson.Serialize(cellar));
    Assert(!reopened.HasGroundFloor);
    Assert(BuildingStoreys.FloorIndexes(reopened).SequenceEqual([-1]));
    Assert(BuildingStoreys.Validate(reopened, out reason), reason);
});

Check("Floor deletion reindexes stairs connecting surviving upper levels", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(two, out var three, out reason), reason);
    var upstairs = new StaircaseDefinition
    {
        LowerFloorIndex = 1, Centre = new Point2(5, 4)
    };
    Assert(BuildingStoreys.TryAddStaircase(three, upstairs, out var building,
        out reason), reason);
    Assert(BuildingStoreys.TryRemoveFloor(building, 0, out var shifted,
        out var count, out reason), reason);
    Assert(count == 0);
    Assert(shifted.Staircases.Count == 1);
    Assert(shifted.Staircases[0].Id == upstairs.Id);
    Assert(shifted.Staircases[0].LowerFloorIndex == 0);
    Assert(BuildingStoreys.FloorIndexes(shifted).SequenceEqual([0, 1]));
    Assert(BuildingStoreys.Validate(shifted, out reason), reason);
});

Check("Generator explains when no 1 metre room can fit", () =>
{
    var doc = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Nearly full floor",
                Outline = [new(0, 0), new(9.5, 0), new(9.5, 8), new(0, 8)]
            }
        ]
    };
    Assert(FloorGeometry.Validate(doc, out var reason), reason);
    Assert(!BuildingGenerator.TrySuggest(doc, [0], 55, out _, out reason));
    Assert(reason.Contains("No valid room"));
    Assert(doc.Rooms.Count == 1);
});

Check("Generation options can disable every feature explicitly", () =>
{
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = false,
        Windows = false, Stairs = false
    };
    Assert(!options.Any);
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(!BuildingGenerator.TrySuggest(source, [0], 91, options,
        out _, out var reason));
    Assert(reason.Contains("Select at least one feature"));
});

Check("Connected generation creates a hallway, adjoining rooms and doors", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 412, options,
        out var suggestion, out var reason), reason);
    var floor = suggestion.Building;
    var corridor = floor.Rooms.Single(r => r.Type == "Hallway");
    var generated = floor.Rooms.Where(r => r.Id != corridor.Id).ToList();
    Assert(generated.Count > 0);
    Assert(generated.Any(room =>
        BuildingConnectivity.ShareWall(room.Outline, corridor.Outline)));
    Assert(floor.Doors.Count >= 2);
    Assert(floor.Doors.Any(d => FloorGeometry.BuildWalls(floor.Perimeter, floor.Rooms)
        .Any(w => w.IsExterior &&
            PolygonGeometry.ClosestPointOnSegment(d.Centre, w.A, w.B)
                .DistanceTo(d.Centre) < 0.01)));
    Assert(BuildingStoreys.Validate(floor, out reason), reason);
    Assert(source.Rooms.Count == 0 && source.Doors.Count == 0);
});

Check("Corridor-only mode creates a hallway without generating ordinary rooms", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = true, Doors = false
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 314, options,
        out var proposal, out var reason), reason);
    Assert(proposal.Building.Rooms.Count == 1);
    Assert(proposal.Building.Rooms[0].Type == "Hallway");
    Assert(proposal.Building.Doors.Count == 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Door-only generation connects existing rooms without adding new rooms", () =>
{
    var source = new BuildingDocument
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
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 120, options,
        out var proposal, out var reason), reason);
    Assert(proposal.Building.Rooms.Count == 2);
    Assert(proposal.Building.Doors.Count >= 2);
    Assert(BuildingConnectivity.CountRoomsWithoutDoors(proposal.Building, 0) == 0);
    Assert(source.Doors.Count == 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Window-only mode creates glazed openings on defined exterior room walls", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "Shop",
                Outline = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)] }
        ]
    };
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = false, Windows = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 561, options,
        out var proposal, out var reason), reason);
    Assert(proposal.Building.Rooms.Count == 1);
    Assert(proposal.Building.Windows.Count > 0);
    Assert(proposal.Building.Windows.All(w => w.Width == 1.2));
    Assert(source.Windows.Count == 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Stair-only mode joins existing floors without generating rooms", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var two, out var reason), reason);
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = false, Stairs = true
    };
    Assert(BuildingGenerator.TrySuggest(two, [0, 1], 20, options,
        out var proposal, out reason), reason);
    Assert(proposal.Building.Staircases.Count == 1);
    Assert(proposal.Building.Staircases[0].LowerFloorIndex == 0);
    Assert(proposal.Building.Rooms.Count == 0);
    Assert(proposal.Building.UpperFloors[0].Rooms.Count == 0);
    Assert(two.Staircases.Count == 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Stair generation never connects an unchecked floor", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var two, out var reason), reason);
    var stairsOnly = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = false, Stairs = true
    };
    Assert(!BuildingGenerator.TrySuggest(two, [0], 100, stairsOnly,
        out _, out reason));
    Assert(reason.Contains("No valid new features"));
});

Check("Generate only on the selected upper floor with corridors and doors", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(source, out var two, out var reason), reason);
    var options = new GenerationOptions { Rooms = true, Corridors = true, Doors = true };
    Assert(BuildingGenerator.TrySuggest(two, [1], 552, options,
        out var proposal, out reason), reason);
    Assert(proposal.Building.Rooms.Count == 0);
    Assert(proposal.Building.Doors.Count == 0);
    Assert(proposal.Building.UpperFloors[0].Rooms.Any(r => r.Type == "Hallway"));
    Assert(proposal.Building.UpperFloors[0].Doors.Count > 0);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Whole-building options are applied across all proposed storeys", () =>
{
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true, Stairs = true
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 812, options,
        out var proposal, out var reason), reason);
    Assert(BuildingStoreys.FloorCount(proposal.Building) == 3);
    Assert(proposal.FloorIndexes.SequenceEqual([0, 1, 2]));
    foreach (var index in proposal.FloorIndexes)
    {
        var floor = BuildingStoreys.GetFloor(proposal.Building, index);
        Assert(floor.Rooms.Any(r => r.Type == "Hallway"));
        Assert(floor.Rooms.Count >= 2);
        Assert(floor.Doors.Count >= 2);
    }
    Assert(proposal.Building.Staircases.Count >= 1);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Generation options are detached and copy without leaking changes", () =>
{
    var source = new GenerationOptions
    {
        Rooms = true, Corridors = false, Doors = true,
        Windows = true, Stairs = false
    };
    var copy = source.Copy();
    copy.Doors = false;
    copy.Stairs = true;
    Assert(source.Doors && !source.Stairs && source.Windows);
    Assert(!copy.Doors && copy.Stairs);
});

Check("Adjacent room geometry requires a wall, not merely a shared corner", () =>
{
    var room1 = new List<Point2>
    {
        new(0, 0), new(3, 0), new(3, 3), new(0, 3)
    };
    var room2 = new List<Point2>
    {
        new(3, 3), new(5, 3), new(5, 5), new(3, 5)
    };
    Assert(!BuildingConnectivity.ShareWall(room1, room2));
    var hall = new List<Point2>
    {
        new(3, 0), new(5, 0), new(5, 3), new(3, 3)
    };
    Assert(BuildingConnectivity.ShareWall(room1, hall));
});

Check("Door generation adds exterior entry when all existing doors are internal", () =>
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
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5, 4), Width = 0.9 }]
    };
    var onlyDoors = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 109, onlyDoors,
        out var proposal, out var reason), reason);
    Assert(proposal.Building.Doors.Count >= 2);
    var walls = FloorGeometry.BuildWalls(proposal.Building.Perimeter,
        proposal.Building.Rooms);
    Assert(proposal.Building.Doors.Any(door =>
        walls.Any(w => w.IsExterior &&
            PolygonGeometry.ClosestPointOnSegment(
                door.Centre, w.A, w.B).DistanceTo(door.Centre) < 0.01)));
    Assert(source.Doors.Count == 1);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
});

Check("Frontage planning fills both sides of a hall without wasting room area", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true,
        Purpose = BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 2245, options,
        out var plan, out var reason), reason);
    var floor = plan.Building;
    Assert(floor.Rooms.Any(r => r.Type == "Hallway"));
    Assert(floor.Rooms.Count >= 5, "Both sides should be subdivided.");
    Assert(RoomLayoutPlanner.UnassignedArea(floor) < 0.01,
        "Both hallway frontages should be filled, not scattered.");
    Assert(floor.Rooms.All(r => PolygonGeometry.Area(r.Outline) >= 4));
    Assert(BuildingStoreys.Validate(plan.Building, out reason), reason);
    Assert(plan.Description.Contains("unused"));
    Assert(source.Rooms.Count == 0);
});

Check("Frontage partitioning creates different valid layouts for different seeds", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true,
        Purpose = BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggest(doc, [0], 199, options,
        out var a, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(doc, [0], 517, options,
        out var b, out reason), reason);
    Assert(RoomLayoutPlanner.UnassignedArea(a.Building) < 0.01);
    Assert(RoomLayoutPlanner.UnassignedArea(b.Building) < 0.01);
    Assert(BuildingStoreys.Validate(a.Building, out reason), reason);
    Assert(BuildingStoreys.Validate(b.Building, out reason), reason);
});

Check("Residence generation avoids implausibly huge bathrooms or tiny living rooms", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions { Purpose = BuildingUse.Residence };
    Assert(BuildingGenerator.TrySuggest(doc, [0], 456, options,
        out var suggested, out var reason), reason);
    foreach (var room in suggested.Building.Rooms.Where(r => r.Type != "Hallway"))
    {
        double area = PolygonGeometry.Area(room.Outline);
        if (room.Type is "Bathroom" or "Pantry" or "Laundry")
            Assert(area < 9);
        if (room.Type == "Living room")
            Assert(area >= 23);
    }
    Assert(BuildingStoreys.Validate(suggested.Building, out reason), reason);
});

Check("Building-use choice assigns coherent room roles on different elevations", () =>
{
    var existing = new List<RoomDefinition>();
    var innUpper = Enumerable.Range(0, 20).Select(i =>
        RoomLayoutPlanner.ChooseRoomType(BuildingUse.Inn, 1, 16, existing,
            new Random(i))).ToList();
    Assert(innUpper.All(t => t is "Guest room" or "Suite" or "Linen store"));

    for (int i = 0; i < 20; i++)
    {
        string small = RoomLayoutPlanner.ChooseRoomType(
            BuildingUse.Residence, 0, 6, existing, new Random(i));
        Assert(small is "Bathroom" or "Pantry" or "Laundry");
        string upper = RoomLayoutPlanner.ChooseRoomType(
            BuildingUse.Residence, 1, 15, existing, new Random(i));
        Assert(upper is "Bedroom" or "Study");
        string dungeon = RoomLayoutPlanner.ChooseRoomType(
            BuildingUse.Dungeon, -1, 16, existing, new Random(i));
        Assert(dungeon is "Chamber" or "Barracks" or "Guardroom" or "Vault");
    }
});

Check("Generator preserves manually defined rooms and refuses overlapping partitions", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = rectangle.ToList(),
        Rooms =
        [
            new RoomDefinition { Name = "My handcrafted room", Type = "Office",
                Outline = [new(0, 0), new(3, 0), new(3, 2), new(0, 2)] }
        ]
    };
    var original = source.Rooms[0].Copy();
    Assert(FloorGeometry.Validate(source, out var reason), reason);
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 500, options,
        out var plan, out reason), reason);
    Assert(plan.Building.Rooms.Any(r => r.Id == original.Id &&
        r.Outline.SequenceEqual(original.Outline)));
    Assert(source.Rooms.Count == 1);
    Assert(BuildingStoreys.Validate(plan.Building, out reason), reason);
});

Check("Purpose setting survives options copy independently", () =>
{
    var a = new GenerationOptions { Purpose = BuildingUse.Shop };
    var b = a.Copy();
    b.Purpose = BuildingUse.Dungeon;
    Assert(a.Purpose == BuildingUse.Shop);
    Assert(b.Purpose == BuildingUse.Dungeon);
});

Check("Floor coverage metric returns remaining area and is never negative", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Equal(RoomLayoutPlanner.UnassignedArea(doc), 80);
    doc.Rooms.Add(new RoomDefinition
    {
        Outline = [new(0, 0), new(5, 0), new(5, 8), new(0, 8)]
    });
    Equal(RoomLayoutPlanner.UnassignedArea(doc), 40);
});

Check("Residential frontage reserves a proper living room rather than tiny equal rooms", () =>
{
    var source = new BuildingDocument { Perimeter = rectangle.ToList() };
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true,
        Purpose = BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggest(source, [0], 9917, options,
        out var proposal, out var reason), reason);
    var floor = proposal.Building;
    var living = floor.Rooms.Where(r => r.Type == "Living room").ToList();
    Assert(living.Count == 1,
        "An ordinary 10 x 8 metre residential ground floor needs one living room.");
    Assert(PolygonGeometry.Area(living[0].Outline) >= 23,
        "The principal room must be genuinely usable as a living room.");
    Assert(floor.Rooms.Any(r => r.Type == "Kitchen"));
    Assert(floor.Rooms.Any(r => r.Type == "Dining room"));
    Assert(floor.Rooms.Any(r => r.Type == "Bathroom"));
    Assert(floor.Rooms.Select(r => r.Name).Distinct().Count() == floor.Rooms.Count,
        "Generated room names should be unique.");
    Assert(RoomLayoutPlanner.UnassignedArea(floor) < 0.01);
    Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
    Assert(source.Rooms.Count == 0, "Suggestions must not modify the source.");
});

Check("Residential room roles are allocated before repeat bedrooms", () =>
{
    var existing = new List<RoomDefinition>();
    string[] expected = ["Kitchen", "Dining room", "Bedroom", "Study", "Bedroom"];
    foreach (var role in expected)
    {
        string picked = RoomLayoutPlanner.ChooseRoomType(
            BuildingUse.Residence, 0, 15, existing, new Random(72));
        Assert(picked == role, $"Expected {role}, got {picked}.");
        existing.Add(new RoomDefinition { Type = picked });
    }
    Assert(RoomLayoutPlanner.NextRoomName("Kitchen", existing) == "Kitchen");
    existing.Add(new RoomDefinition { Name = "Kitchen", Type = "Kitchen" });
    Assert(RoomLayoutPlanner.NextRoomName("Kitchen", existing) == "Kitchen 2");
});

Check("Three-floor whole-building preview remains three floors through acceptance copy and JSON", () =>
{
    var existing = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.FloorCount(existing) == 1);
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true,
        Purpose = BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 27017, options,
        out var draft, out var reason), reason);
    Assert(draft.FloorIndexes.SequenceEqual([0, 1, 2]));
    Assert(BuildingStoreys.FloorCount(draft.Building) == 3);
    Assert(draft.Building.UpperFloors.Count == 2);
    foreach (int index in draft.FloorIndexes)
        Assert(BuildingStoreys.GetFloor(draft.Building, index).Rooms.Count > 0);
    var accepted = BuildingStoreys.Copy(draft.Building);
    Assert(BuildingStoreys.FloorCount(accepted) == 3);
    var reloaded = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(accepted));
    Assert(BuildingStoreys.FloorCount(reloaded) == 3);
    Assert(BuildingStoreys.FloorIndexes(reloaded).SequenceEqual([0, 1, 2]));
    Assert(BuildingStoreys.Validate(reloaded, out reason), reason);
    Assert(BuildingStoreys.FloorCount(existing) == 1);
});
Check("Three-floor house reserves aligned open staircase alcoves beside the hall", () =>
{
    var settings = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true, Stairs = true,
        Purpose = BuildingUse.Residence
    };
    foreach (int seed in new[] { 812, 1783, 42024 })
    {
        Assert(BuildingGenerator.TrySuggestWholeBuilding(3, seed, settings,
            out var draft, out var reason), reason);
        var building = draft.Building;
        Assert(BuildingStoreys.FloorCount(building) == 3);
        Assert(building.Staircases.Count == 2);
        var first = building.Staircases.Single(s => s.LowerFloorIndex == 0);
        var second = building.Staircases.Single(s => s.LowerFloorIndex == 1);
        Assert(first.Centre.DistanceTo(second.Centre) < 1e-6);
        foreach (int index in BuildingStoreys.FloorIndexes(building))
        {
            var floor = BuildingStoreys.GetFloor(building, index);
            var hall = floor.Rooms.Single(r => r.Type == "Hallway");
            Assert(!floor.Rooms.Any(r => r.Type == "Stairwell"),
                "A small home shouldn't give stairs a separate closed room.");
            Assert(PolygonGeometry.ContainsPoint(hall.Outline, first.Centre),
                "The open side alcove must contain the stairs in the hallway footprint.");
            Assert(hall.Outline.Count > 4,
                "A stair alcove should expand the hallway, not leave an unassigned pocket.");
            Assert(floor.Doors.Any(d => Enumerable.Range(0, hall.Outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(d.Centre, hall.Outline[i],
                    hall.Outline[(i + 1) % hall.Outline.Count])
                    .DistanceTo(d.Centre) < 0.01)),
                "The hall needs at least one doorway.");
        }
        Assert(BuildingStoreys.Validate(building, out reason), reason);
    }
});

Check("Hall-only multi-floor stair suggestions use a wall-side flight without blocking circulation", () =>
{
    var doc = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(doc, out var two, out var reason), reason);
    Assert(BuildingGenerator.TrySuggest(two, [0, 1], 1223,
        new GenerationOptions { Rooms = false, Corridors = true, Doors = false },
        out var halls, out reason), reason);
    var source = halls.Building;
    var options = new GenerationOptions
    {
        Rooms = false, Corridors = false, Doors = false, Stairs = true
    };
    Assert(BuildingGenerator.TrySuggest(source, [0, 1], 1223, options,
        out var suggestion, out reason), reason);
    Assert(suggestion.Building.Staircases.Count == 1,
        "Furnished hallways now admit a wall-side flight and both landings.");
    Assert(source.Staircases.Count == 0,
        "A suggestion must not modify the accepted building.");
    Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
});

Check("Compact domestic stairs stay against a wall with landing clearance", () =>
{
    var settings = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true, Stairs = true,
        Purpose = BuildingUse.Residence
    };
    foreach (int seed in new[] { 812, 1783, 42024 })
    {
        Assert(BuildingGenerator.TrySuggestWholeBuilding(3, seed, settings,
            out var draft, out var reason), reason);
        foreach (var stair in draft.Building.Staircases)
        {
            Assert(stair.Width <= 1.0 + 1e-6);
            Assert(stair.Length <= 2.8 + 1e-6);
            Assert(stair.LandingLength >= 1.0);
            var floor = BuildingStoreys.GetFloor(draft.Building, stair.LowerFloorIndex);
            Assert(PolygonGeometry.ContainsPoint(
                floor.Rooms.Single(r => r.Type == "Hallway").Outline, stair.Centre));
            Assert(BuildingStoreys.Validate(draft.Building, out reason), reason);
        }
    }
});

Check("Automatic stairs without corridors no longer float in the middle of the building", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    int added = BuildingConnectivity.AddStairs(two, [0, 1], out var proposal);
    Assert(added == 1);
    var stair = proposal.Staircases.Single();
    var flight = BuildingStoreys.StairFootprint(stair);
    double gap = new[]
    {
        flight.Min(p => p.X), 10 - flight.Max(p => p.X),
        flight.Min(p => p.Y), 8 - flight.Max(p => p.Y)
    }.Min();
    Assert(gap >= 0.19 && gap <= 0.21, "A staircase must hug a perimeter wall.");
    Assert(BuildingStoreys.Validate(proposal, out reason), reason);
});

Check("Five and ten-foot map grids persist exactly through building JSON", () =>
{
    foreach (int grid in new[] { 0, 5, 10 })
    {
        var doc = new BuildingDocument
        {
            GridFeet = grid, Perimeter = rectangle.ToList()
        };
        Assert(BuildingStoreys.Validate(doc, out var reason), reason);
        Assert(BuildingDocumentJson.Deserialize(
            BuildingDocumentJson.Serialize(doc)).GridFeet == grid);
        Assert(BuildingStoreys.Copy(doc).GridFeet == grid);
    }
    var invalid = new BuildingDocument { GridFeet = 7, Perimeter = rectangle.ToList() };
    Assert(!BuildingStoreys.Validate(invalid, out _));
});

Check("A generated three-floor staircase reserves two one-metre landings per flight", () =>
{
    var options = new GenerationOptions
    {
        Corridors = true, Rooms = true, Doors = true,
        Stairs = true, Purpose = BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 812, options,
        out var draft, out var reason), reason);
    Assert(draft.Building.Staircases.Count == 2);
    foreach (var stair in draft.Building.Staircases)
    {
        Assert(stair.LandingLength >= 1);
        var landings = BuildingStoreys.StairLandings(stair);
        Assert(landings.Count == 2);
        foreach (var landing in landings)
        {
            Assert(Math.Abs(PolygonGeometry.Area(landing) -
                stair.Width * stair.LandingLength) < 1e-6);
            for (int index = stair.LowerFloorIndex;
                 index <= stair.LowerFloorIndex + 1; index++)
                Assert(FloorGeometry.IsWithinPolygon(landing,
                    BuildingStoreys.GetFloor(draft.Building, index).Perimeter));
        }
    }
    Assert(BuildingStoreys.Validate(draft.Building, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(draft.Building));
    Assert(loaded.Staircases.All(s => s.LandingLength == 1));
});

Check("Windows go to exterior bedrooms and living rooms, not to storerooms", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = [new(0,0), new(12,0), new(12,8), new(0,8)],
        Rooms =
        [
            new RoomDefinition { Name = "Bedroom", Type = "Bedroom",
                Outline = [new(0,0), new(6,0), new(6,4), new(0,4)] },
            new RoomDefinition { Name = "Living room", Type = "Living room",
                Outline = [new(6,0), new(12,0), new(12,4), new(6,4)] },
            new RoomDefinition { Name = "Storeroom", Type = "Storeroom",
                Outline = [new(0,4), new(6,4), new(6,8), new(0,8)] },
            new RoomDefinition { Name = "Dining room", Type = "Dining room",
                Outline = [new(6,4), new(12,4), new(12,8), new(6,8)] }
        ]
    };
    Assert(BuildingStoreys.Validate(building, out var reason), reason);
    Assert(BuildingConnectivity.AddWindows(building, 0, out var glazed) == 3);
    Assert(building.Windows.Count == 0);
    foreach (var room in glazed.Rooms)
    {
        bool hasWindow = glazed.Windows.Any(w =>
            Enumerable.Range(0, room.Outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(w.Centre, room.Outline[i],
                    room.Outline[(i + 1) % room.Outline.Count])
                    .DistanceTo(w.Centre) < 1e-5));
        Assert(hasWindow == (room.Type != "Storeroom"));
    }
    Assert(BuildingStoreys.Validate(glazed, out reason), reason);
});

Check("One fireplace chimney stacks through all covering floors including basement", () =>
{
    var ground = new BuildingDocument { Perimeter = rectangle.ToList() };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    Assert(BuildingStoreys.TryAddBasement(two, out var three, out reason), reason);
    Assert(BuildingConnectivity.TryAddChimney(three, out var withChimney),
        "A chimney should fit against a wall on three empty rectangular levels.");
    Assert(withChimney.Chimneys.Count == 1,
        "Expected exactly one shared chimney definition.");
    var chimney = withChimney.Chimneys.Single();
    Assert(BuildingStoreys.ChimneyFloors(withChimney, chimney)
        .SequenceEqual([-1, 0, 1]), "Chimney must serve basement, ground and upper.");
    Assert(BuildingStoreys.Validate(withChimney, out reason), reason);
    var loaded = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(withChimney));
    Assert(loaded.Chimneys.Single().Id == chimney.Id,
        "Chimney must survive JSON with the same ID.");
    Assert(BuildingStoreys.ChimneyFloors(loaded, loaded.Chimneys.Single()).Count == 3,
        "JSON must preserve all chimney storeys.");

    // Basement doesn't share the stack's footprint, so it doesn't get a
    // phantom fireplace even though the other levels retain theirs.
    var basement = BuildingStoreys.GetFloor(loaded, -1);
    basement.Perimeter =
        [new(0,0), new(1,0), new(1,1), new(0,1)];
    BuildingStoreys.SetFloor(loaded, -1, basement);
    Assert(BuildingStoreys.ChimneyFloors(loaded, loaded.Chimneys.Single())
        .SequenceEqual([0, 1]), "A small partial basement must not show the distant fireplace.");
    Assert(BuildingStoreys.Validate(loaded, out reason), reason);
});

Check("Generated 5-foot and 10-foot maps have exact tabletop-aligned outer footprints", () =>
{
    foreach (int feet in new[] { 5, 10 })
    {
        var settings = new GenerationOptions
        {
            Corridors = true, Rooms = true, Doors = true, Windows = true
        };
        Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 25843, settings,
            out var draft, out var reason, feet), reason);
        Assert(draft.Building.GridFeet == feet);
        Assert(BuildingStoreys.FloorCount(draft.Building) == 3);
        double step = feet * 0.3048;
        foreach (int index in BuildingStoreys.FloorIndexes(draft.Building))
        {
            var perimeter = BuildingStoreys.GetFloor(draft.Building, index).Perimeter;
            foreach (var corner in perimeter)
            {
                Assert(Math.Abs(corner.X / step - Math.Round(corner.X / step)) < 1e-7,
                    $"X perimeter coordinate must lie on the {feet}-foot grid.");
                Assert(Math.Abs(corner.Y / step - Math.Round(corner.Y / step)) < 1e-7,
                    $"Y perimeter coordinate must lie on the {feet}-foot grid.");
            }
        }
    }
});

Check("Three-floor plans keep windows away from staircase flights", () =>
{
    var settings = new GenerationOptions
    {
        Corridors = true, Rooms = true, Doors = true, Windows = true, Stairs = true
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(3, 812, settings,
        out var draft, out var reason), reason);
    foreach (int index in BuildingStoreys.FloorIndexes(draft.Building))
    {
        var floor = BuildingStoreys.GetFloor(draft.Building, index);
        foreach (var window in floor.Windows)
        foreach (var stair in draft.Building.Staircases.Where(s =>
            s.LowerFloorIndex == index || s.LowerFloorIndex + 1 == index))
            Assert(window.Centre.DistanceTo(stair.Centre) >= 2.3 - 1e-6,
                "A window should not be immediately beside the staircase.");
    }
    Assert(BuildingStoreys.Validate(draft.Building, out reason), reason);
});

Check("Imperial-room frontage ends exactly at exterior walls, without half-metre gaps", () =>
{
    foreach (double w in new[] { 10.668, 12.192 })
    {
        double h = 8.636;
        var doc = new BuildingDocument
        {
            GridFeet = 5,
            Perimeter = [new(0,0), new(w,0), new(w,h), new(0,h)]
        };
        Assert(BuildingConnectivity.TryAddCorridor(doc, 0,
            out var withHall, out var hallId));
        int count = RoomLayoutPlanner.FillHallFrontage(withHall, 0, hallId,
            BuildingUse.Residence, new Random(73), out var divided);
        Assert(count > 0);
        Assert(divided.Rooms.Any(r => r.Type != "Hallway" &&
            r.Outline.Any(p => Math.Abs(p.Y - h) < 1e-6)),
            "A room must reach the exact lower exterior wall.");
        Assert(divided.Rooms.Any(r => r.Type != "Hallway" &&
            r.Outline.Any(p => Math.Abs(p.Y) < 1e-6)),
            "A room must reach the exact upper exterior wall.");
        Assert(BuildingStoreys.Validate(divided, out var reason), reason);
    }
});

Check("Medieval footprint profiles distinguish cottages from inns and can create L-wings", () =>
{
    double meanResidence = Enumerable.Range(1, 20).Average(seed =>
        PolygonGeometry.Area(BuildingGenerator.MedievalFootprint(
            BuildingUse.Residence, 1, 0, new Random(seed))));
    double meanInn = Enumerable.Range(1, 20).Average(seed =>
        PolygonGeometry.Area(BuildingGenerator.MedievalFootprint(
            BuildingUse.Inn, 2, 0, new Random(seed))));
    Assert(meanResidence < meanInn,
        "A medieval cottage shouldn't have the same footprint as an inn.");
    Assert(meanResidence >= 40 && meanResidence < 110);
    Assert(meanInn >= 90);
    Assert(Enumerable.Range(1, 150).Any(seed =>
        BuildingGenerator.MedievalFootprint(BuildingUse.Inn, 2, 5,
            new Random(seed)).Count == 6),
        "Nonrectangular L-shaped wings should be produced sometimes.");
    foreach (int feet in new[] { 5, 10 })
    foreach (int seed in new[] { 7, 18, 53 })
    {
        double step = feet * 0.3048;
        var shape = BuildingGenerator.MedievalFootprint(
            BuildingUse.Mixed, 2, feet, new Random(seed));
        Assert(PolygonGeometry.IsValidOutline(shape, out _));
        Assert(shape.All(p =>
            Math.Abs(p.X / step - Math.Round(p.X / step)) < 1e-7 &&
            Math.Abs(p.Y / step - Math.Round(p.Y / step)) < 1e-7));
    }
});

Check("Generated hearth in a furnished residence is not placed inside its hallway", () =>
{
    var options = new GenerationOptions
    {
        Rooms = true, Corridors = true, Doors = true, Fireplaces = true
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(1, 1834, options,
        out var draft, out var reason), reason);
    foreach (var chimney in draft.Building.Chimneys)
    {
        var floor = BuildingStoreys.GetFloor(draft.Building, 0);
        Assert(!floor.Rooms.Any(r =>
            r.Type == "Hallway" &&
            PolygonGeometry.ContainsPoint(r.Outline, chimney.Centre)));
        Assert(floor.Rooms.Any(r => r.Type != "Hallway" &&
            PolygonGeometry.ContainsPoint(r.Outline, chimney.Centre)),
            "The hearth should be positioned within a living or cooking room.");
    }
    Assert(BuildingStoreys.Validate(draft.Building, out reason), reason);
});

Check("Inn hearths are broader than cottage hearths without heating empty hallways", () =>
{
    var floor = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,9),new(0,9)]
    };
    Assert(BuildingConnectivity.TryAddChimney(floor, out var cottage));
    Assert(BuildingConnectivity.TryAddChimney(floor, out var inn, BuildingUse.Inn));
    var cottageHearth = cottage.Chimneys.Single();
    var innHearth = inn.Chimneys.Single();
    Assert(Math.Max(innHearth.Width, innHearth.Depth) >= 2.4 - 1e-6);
    Assert(Math.Max(cottageHearth.Width, cottageHearth.Depth) <= 1.4 + 1e-6);
    Assert(BuildingStoreys.Validate(inn, out var reason), reason);

    var hallwayOnly = new BuildingDocument
    {
        Perimeter = floor.Perimeter.ToList(),
        Rooms = [new RoomDefinition
        {
            Name = "Hallway", Type = "Hallway",
            Outline = floor.Perimeter.ToList()
        }]
    };
    Assert(!BuildingConnectivity.TryAddChimney(hallwayOnly, out _),
        "Do not add a decorative fireplace to a corridor when no living room exists.");
});

Check("Generated upper-storey doors never open directly into empty air", () =>
{
    var ground = new BuildingDocument
    {
        Perimeter = [new(0,0), new(12,0), new(12,10), new(0,10)],
        Rooms =
        [
            new RoomDefinition { Name = "Hall", Type = "Hallway",
                Outline = [new(0,0),new(2,0),new(2,10),new(0,10)] },
            new RoomDefinition { Name = "Living room", Type = "Living room",
                Outline = [new(2,0),new(7,0),new(7,4),new(2,4)] },
            new RoomDefinition { Name = "Kitchen", Type = "Kitchen",
                Outline = [new(7,0),new(12,0),new(12,6),new(7,6)] },
            new RoomDefinition { Name = "Dining room", Type = "Dining room",
                Outline = [new(2,4),new(7,4),new(7,10),new(2,10)] },
            new RoomDefinition { Name = "Storeroom", Type = "Storeroom",
                Outline = [new(7,6),new(12,6),new(12,10),new(7,10)] }
        ]
    };
    Assert(BuildingStoreys.Validate(ground, out var reason), reason);
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out reason), reason);
    var upstairs = BuildingStoreys.GetFloor(two, 1);
    upstairs.Rooms =
    [
        new RoomDefinition { Name = "Upper hallway", Type = "Hallway",
            Outline = [new(0,0),new(2,0),new(2,10),new(0,10)] },
        new RoomDefinition { Name = "Bedroom", Type = "Bedroom",
            Outline = [new(2,0),new(7,0),new(7,10),new(2,10)] },
        new RoomDefinition { Name = "Bedroom 2", Type = "Bedroom",
            Outline = [new(7,0),new(12,0),new(12,10),new(7,10)] }
    ];
    BuildingStoreys.SetFloor(two, 1, upstairs);
    Assert(BuildingStoreys.Validate(two, out reason), reason);
    Assert(BuildingConnectivity.AddDoors(two, 0, out var withGround) >= 4);
    Assert(BuildingConnectivity.AddDoors(withGround, 1, out var complete) >= 2);
    var gf = BuildingStoreys.GetFloor(complete, 0);
    var upper = BuildingStoreys.GetFloor(complete, 1);
    bool OnWall(Point2 point, IReadOnlyList<Point2> points) =>
        Enumerable.Range(0, points.Count).Any(i =>
            PolygonGeometry.ClosestPointOnSegment(
                point, points[i], points[(i + 1) % points.Count])
                .DistanceTo(point) < 1e-5);
    var outer = FloorGeometry.BuildWalls(upper.Perimeter, upper.Rooms)
        .Where(w => w.IsExterior).ToList();
    Assert(!upper.Doors.Any(d => outer.Any(w =>
        PolygonGeometry.ClosestPointOnSegment(d.Centre, w.A, w.B)
            .DistanceTo(d.Centre) < 1e-5)),
        "Upper-level rooms must not get a door opening off the exterior perimeter.");
    foreach (var room in upper.Rooms.Where(r => r.Type != "Hallway"))
        Assert(upper.Doors.Any(d => OnWall(d.Centre, room.Outline) &&
            upper.Rooms.Any(other => other.Id != room.Id &&
                OnWall(d.Centre, other.Outline))),
            "Every generated bedroom must have an internal route.");
    var hall = gf.Rooms.Single(r => r.Type == "Hallway");
    var living = gf.Rooms.Single(r => r.Type == "Living room");
    Assert(gf.Doors.Any(d => OnWall(d.Centre, hall.Outline) &&
        OnWall(d.Centre, living.Outline)),
        "The living room needs hall access even with an outside entrance.");
    var kitchen = gf.Rooms.Single(r => r.Type == "Kitchen");
    foreach (var type in new[] { "Living room", "Dining room", "Storeroom" })
    {
        var partner = gf.Rooms.Single(r => r.Type == type);
        Assert(gf.Doors.Any(d => OnWall(d.Centre, kitchen.Outline) &&
            OnWall(d.Centre, partner.Outline)),
            "A kitchen should connect to its adjoining " + type);
    }
    Assert(BuildingStoreys.Validate(complete, out reason), reason);
});

Check("Regenerate checked floors changes a detached draft without erasing a drawn perimeter", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,8),new(0,8)],
        Rooms = [new RoomDefinition { Name = "Manual room", Type = "Workshop",
            Outline = [new(0,0),new(6,0),new(6,4),new(0,4)] }]
    };
    Assert(BuildingStoreys.TryAddFloor(source, out var two, out var reason), reason);
    var original = BuildingStoreys.Copy(two);
    var options = new GenerationOptions
    { Rooms = true, Corridors = true, Doors = true, Windows = true };
    foreach (int seed in new[] { 73, 145 })
    {
        Assert(BuildingGenerator.TryRegenerate(two, [0], seed, options,
            out var proposal, out reason), reason);
        Assert(proposal.Building.Rooms.Count > 1);
        Assert(proposal.Building.Rooms.All(r => r.Name != "Manual room"));
        Assert(proposal.Building.Perimeter.SequenceEqual(two.Perimeter));
        Assert(BuildingStoreys.GetFloor(proposal.Building, 1).Perimeter
            .SequenceEqual(BuildingStoreys.GetFloor(two, 1).Perimeter));
        Assert(BuildingStoreys.Validate(proposal.Building, out reason), reason);
    }
    Assert(two.Rooms.Single().Name == "Manual room");
    Assert(BuildingStoreys.GetFloor(two, 0).Perimeter.SequenceEqual(original.Perimeter));
});

Check("Orthogonal L-wing and precise perimeter cells are filled without room seams", () =>
{
    var shape = new List<Point2> {new(0,0),new(10,0),new(10,6),
        new(14,6),new(14,10),new(0,10)};
    var building = new BuildingDocument
    {
        Perimeter = shape,
        Rooms = [new RoomDefinition { Name = "Hall", Type = "Hallway",
            Outline = [new(0,0),new(2,0),new(2,10),new(0,10)] }]
    };
    int added = RoomLayoutPlanner.FillUnassignedRegions(building, 0,
        BuildingUse.Residence, new Random(12), out var filled);
    Assert(added >= 2, "The main room strip and L-wing should both be usable.");
    Assert(RoomLayoutPlanner.UnassignedArea(filled) < 0.01,
        "Full-size orthogonal remainder must not remain empty.");
    Assert(BuildingStoreys.Validate(filled, out var reason), reason);

    var footPlan = new BuildingDocument
    {
        GridFeet = 5,
        Perimeter = [new(0,0),new(12.192,0),
            new(12.192,9.144),new(0,9.144)],
        Rooms = [new RoomDefinition { Name = "Hall", Type = "Hallway",
            Outline = [new(0,0),new(2,0),new(2,9.144),new(0,9.144)] }]
    };
    Assert(RoomLayoutPlanner.FillUnassignedRegions(footPlan, 0,
        BuildingUse.Residence, new Random(12), out var filledFeet) >= 1);
    Assert(RoomLayoutPlanner.UnassignedArea(filledFeet) < 0.01);
    Assert(BuildingStoreys.Validate(filledFeet, out reason), reason);
});

Check("A shared kitchen-living hearth serves two rooms but not the upstairs bedroom", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,10),new(0,10)],
        Rooms =
        [
            new RoomDefinition { Name = "Living room", Type = "Living room",
                Outline = [new(0,0),new(7,0),new(7,10),new(0,10)] },
            new RoomDefinition { Name = "Kitchen", Type = "Kitchen",
                Outline = [new(7,0),new(12,0),new(12,10),new(7,10)] }
        ]
    };
    Assert(BuildingStoreys.TryAddFloor(building, out var two, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Rooms =
    [
        new RoomDefinition { Name = "Bedroom", Type = "Bedroom",
            Outline = [new(0,0),new(7,0),new(7,10),new(0,10)] },
        new RoomDefinition { Name = "Bedroom 2", Type = "Bedroom",
            Outline = [new(7,0),new(12,0),new(12,10),new(7,10)] }
    ];
    BuildingStoreys.SetFloor(two, 1, upper);
    Assert(BuildingConnectivity.TryAddChimney(two, out var heated),
        "Shared hearth should fit where bedroom partitions follow ground floor walls.");
    var stack = heated.Chimneys.Single();
    Assert(stack.HearthRoomIds.Count == 2, "Two ground-floor hearth faces need one flue.");
    Assert(stack.OtherHearthCentre.HasValue);
    Assert(stack.HearthFloorIndex == 0);
    Assert(heated.UpperFloors[0].Rooms.All(r => !stack.HearthRoomIds.Contains(r.Id)));
    Assert(Math.Abs(stack.Centre.X - 7) < 0.9,
        "The flue must follow the upstairs bedroom partition, not its centre.");
    var restored = BuildingDocumentJson.Deserialize(
        BuildingDocumentJson.Serialize(heated));
    Assert(restored.Chimneys.Single().HearthRoomIds.Count == 2);
    Assert(BuildingStoreys.Validate(restored, out reason), reason);
});

Check("Upper bedrooms follow compatible kitchen-living partition locations", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,8),new(0,8)],
        Rooms =
        [
            new RoomDefinition { Type = "Hallway", Name = "Hallway",
                Outline = [new(0,3),new(12,3),new(12,5),new(0,5)] },
            new RoomDefinition { Type = "Living room", Name = "Living room",
                Outline = [new(0,0),new(7,0),new(7,3),new(0,3)] },
            new RoomDefinition { Type = "Kitchen", Name = "Kitchen",
                Outline = [new(7,0),new(12,0),new(12,3),new(7,3)] }
        ]
    };
    Assert(BuildingStoreys.TryAddFloor(building, out var two, out var reason), reason);
    var upstairs = BuildingStoreys.GetFloor(two, 1);
    var hallway = building.Rooms.First(r => r.Type == "Hallway").Copy();
    upstairs.Rooms.Add(hallway);
    BuildingStoreys.SetFloor(two, 1, upstairs);
    Assert(RoomLayoutPlanner.FillHallFrontage(two, 1, hallway.Id,
        BuildingUse.Residence, new Random(44), out var result) > 0);
    var rooms = BuildingStoreys.GetFloor(result, 1).Rooms;
    Assert(rooms.Any(r => r.Type != "Hallway" &&
        r.Outline.Any(p => Math.Abs(p.X - 7) < 1e-6)),
        "An upper partition should align with the 7m kitchen/living wall below.");
    Assert(BuildingStoreys.Validate(result, out reason), reason);
});

Check("Dining and pantry room roles are favoured beside a kitchen wall", () =>
{
    var kitchen = new RoomDefinition { Type = "Kitchen", Name = "Kitchen",
        Outline = [new(0,0),new(4,0),new(4,4),new(0,4)] };
    var candidate = new List<Point2> { new(4,0),new(7,0),new(7,4),new(4,4) };
    var dining = RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence, 0, 12, [kitchen], candidate, new Random(23));
    Assert(dining == "Dining room", "Dining should be a preferred kitchen neighbour.");
    var withDining = new List<RoomDefinition>
    {
        kitchen,
        new RoomDefinition { Type = "Dining room", Name = "Dining room",
            Outline = [new(0,4),new(4,4),new(4,7),new(0,7)] }
    };
    var pantry = RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence, 0, 8, withDining, candidate, new Random(23));
    Assert(pantry == "Pantry", "A compact storage space can adjoin the kitchen.");
});

Check("Narrow exterior slivers extend new rooms but never alter hand-drawn rooms", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = [new(0,0),new(10,0),new(10,8),new(0,8)]
    };
    var proposal = BuildingStoreys.Copy(source);
    proposal.Rooms.Add(new RoomDefinition
    {
        Type = "Bedroom", Name = "Bedroom",
        Outline = [new(0,0),new(9.6,0),new(9.6,8),new(0,8)]
    });
    Assert(BuildingStoreys.Validate(proposal, out var reason), reason);
    Assert(RoomLayoutPlanner.ExtendSuggestedRoomsToExterior(
        source, proposal, 0, out var extended) > 0);
    Assert(RoomLayoutPlanner.UnassignedArea(extended) < 0.001);
    Assert(proposal.Rooms[0].Outline.Any(p => Math.Abs(p.X - 9.6) < 1e-6));

    var existing = BuildingStoreys.Copy(proposal);
    var retained = BuildingStoreys.Copy(existing);
    retained.Rooms.Add(new RoomDefinition
    {
        Type = "Closet", Name = "New closet",
        Outline = [new(9.6,0),new(10,0),new(10,8),new(9.6,8)]
    });
    Assert(RoomLayoutPlanner.ExtendSuggestedRoomsToExterior(
        existing, retained, 0, out var kept) == 0);
    Assert(kept.Rooms.First().Outline.SequenceEqual(existing.Rooms[0].Outline));
    Assert(BuildingStoreys.Validate(kept, out reason), reason);
});

Check("Living room with a street entrance still gets an internal hallway door", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = [new(0,0), new(10,0), new(10,8), new(0,8)],
        Rooms =
        [
            new RoomDefinition { Type = "Hallway", Name = "Hallway",
                Outline = [new(0,3),new(10,3),new(10,5),new(0,5)] },
            new RoomDefinition { Type = "Living room", Name = "Living room",
                Outline = [new(0,0),new(10,0),new(10,3),new(0,3)] }
        ],
        Doors = [new DoorDefinition { Centre = new Point2(5,0), Width = 0.9 }]
    };
    Assert(BuildingStoreys.Validate(building, out var reason), reason);
    Assert(BuildingConnectivity.AddDoors(building, 0, out var updated) > 0);
    Assert(updated.Doors.Any(d => Math.Abs(d.Centre.Y - 3) < 1e-6 &&
        d.Centre.X > 0.5 && d.Centre.X < 9.5),
        "An exterior-only doorway must not count as a hall connection.");
    Assert(updated.Doors.Any(d => d.Centre.DistanceTo(new Point2(5,0)) < 1e-6),
        "The original street entrance must remain.");
    Assert(BuildingStoreys.Validate(updated, out reason), reason);
});

Check("New two-storey homes contain a connected flight and a single open stair hall", () =>
{
    foreach (int gridFeet in new[] { 0, 5, 10 })
    foreach (int seed in new[] { 812, 1783 })
    {
        var options = new GenerationOptions
        {
            Rooms = true, Corridors = true, Doors = true,
            Stairs = true, Windows = true, Purpose = BuildingUse.Residence
        };
        Assert(BuildingGenerator.TrySuggestWholeBuilding(2, seed, options,
            out var draft, out var reason, gridFeet), reason);
        Assert(BuildingStoreys.FloorCount(draft.Building) == 2);
        Assert(draft.Building.Staircases.Count == 1,
            "One flight must connect the two storeys.");
        foreach (int index in new[] { 0, 1 })
        {
            var floor = BuildingStoreys.GetFloor(draft.Building, index);
            var hall = floor.Rooms.Single(r => r.Type == "Hallway");
            Assert(PolygonGeometry.ContainsPoint(
                hall.Outline, draft.Building.Staircases.Single().Centre),
                "The stair alcove must form part of the open hallway.");
            Assert(!floor.Rooms.Any(r => r.Type == "Stairwell"));
        }
        Assert(BuildingStoreys.Validate(draft.Building, out reason), reason);
    }
});

Check("Exact hallway ends meet custom imperial perimeter walls without narrow gaps", () =>
{
    foreach (double width in new[] { 10.668, 12.192, 13.716 })
    {
        var doc = new BuildingDocument
        {
            GridFeet = 5,
            Perimeter = [new(0,0),new(width,0),
                new(width,9.144),new(0,9.144)]
        };
        Assert(BuildingConnectivity.TryAddCorridor(doc, 0,
            out var placed, out var hallId));
        var hall = placed.Rooms.Single(r => r.Id == hallId);
        Assert(Math.Abs(hall.Outline.Min(p => p.X)) < 1e-8);
        Assert(Math.Abs(hall.Outline.Max(p => p.X) - width) < 1e-8);
        Assert(BuildingStoreys.Validate(placed, out var reason), reason);
    }
});

Check("Complete opening span, not just centre, must clear kitchen fireplace", () =>
{
    var building = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,8),new(0,8)],
        Windows = [new WindowDefinition
        {
            Centre = new Point2(5.4, 0), Width = 1.2
        }],
        Chimneys = [new ChimneyDefinition
        {
            Centre = new Point2(4, 0.37),
            Width = 1.4, Depth = 0.7
        }]
    };
    Assert(BuildingStoreys.WindowConflictsWithChimney(building, 0,
        building.Windows.Single(), building.Chimneys.Single()));
    Assert(!BuildingStoreys.Validate(building, out var reason));
    Assert(reason.Contains("window", StringComparison.OrdinalIgnoreCase));
    building.Windows[0].Centre = new Point2(8, 0);
    Assert(!BuildingStoreys.WindowConflictsWithChimney(building, 0,
        building.Windows.Single(), building.Chimneys.Single()));
    Assert(BuildingStoreys.Validate(building, out reason), reason);
});

Check("LShape2 uploaded two-floor 5ft plan has no unassigned 0.762m corridor strip", () =>
{
    // Reconstructed directly from the supplied LShape2.json wall geometry.
    // The exterior steps inward at (13.716,4.572) before meeting the hall
    // at y=5.334, leaving 7.62 x 0.762 metres unassigned on EACH floor.
    List<Point2> Rect(double l, double t, double r, double b) =>
        [new(l,t),new(r,t),new(r,b),new(l,b)];
    RoomDefinition Room(string name, string type, double l, double t,
        double r, double b) => new()
        { Name = name, Type = type, Outline = Rect(l,t,r,b) };
    var outline = new List<Point2> {new(7.62,0),new(13.716,0),
        new(13.716,4.572),new(21.336,4.572),
        new(21.336,10.668),new(7.62,10.668)};
    var ground = new BuildingDocument
    {
        GridFeet = 5, Perimeter = outline.ToList(),
        Rooms =
        [
            Room("Hallway 1","Hallway",7.62,5.334,21.336,7.334),
            Room("Living room","Living room",7.62,7.334,16.12,10.668),
            Room("Kitchen","Kitchen",16.12,7.334,21.336,10.668),
            Room("Dining room","Dining room",7.62,0,13.716,5.334)
        ],
        Doors =
        [
            new DoorDefinition { Centre = new Point2(21.336,6.334), Width=0.9 },
            new DoorDefinition { Centre = new Point2(11.87,7.334), Width=0.9 },
            new DoorDefinition { Centre = new Point2(18.728,7.334), Width=0.9 },
            new DoorDefinition { Centre = new Point2(10.668,5.334), Width=0.9 },
            new DoorDefinition { Centre = new Point2(16.12,9.001), Width=0.8 }
        ],
        Windows =
        [
            new WindowDefinition { Centre=new Point2(11.87,10.668),Width=1.2 },
            new WindowDefinition { Centre=new Point2(10.668,0),Width=1.2 }
        ]
    };
    ground.Chimneys.Add(new ChimneyDefinition
    {
        Centre=new Point2(18.728,10.298),Width=1.4,Depth=0.7,
        HearthFloorIndex=0,HearthRoomIds=[ground.Rooms[2].Id]
    });
    Assert(BuildingStoreys.TryAddFloor(ground, out var original, out var reason),reason);
    var upper = BuildingStoreys.GetFloor(original,1);
    upper.Rooms =
    [
        Room("Hallway 1","Hallway",7.62,5.334,21.336,7.334),
        Room("Bedroom","Bedroom",7.62,0,13.716,5.334),
        Room("Study","Study",7.62,7.334,16.12,10.668),
        Room("Bedroom 2","Bedroom",16.12,7.334,21.336,10.668)
    ];
    upper.Doors =
    [
        new DoorDefinition {Centre=new Point2(10.668,5.334), Width=0.9},
        new DoorDefinition {Centre=new Point2(11.87,7.334), Width=0.9},
        new DoorDefinition {Centre=new Point2(18.728,7.334), Width=0.9}
    ];
    upper.Windows =
    [
        new WindowDefinition {Centre=new Point2(10.668,0),Width=1.2},
        new WindowDefinition {Centre=new Point2(11.87,10.668),Width=1.2},
        new WindowDefinition {Centre=new Point2(21.336,9.001),Width=1.2}
    ];
    BuildingStoreys.SetFloor(original,1,upper);
    Assert(BuildingStoreys.Validate(original,out reason),reason);
    double gap = RoomLayoutPlanner.UnassignedArea(BuildingStoreys.GetFloor(original,0));
    Assert(Math.Abs(gap-5.80644)<0.001, $"Expected uploaded 5.806m² gap, got {gap}.");
    Assert(RoomLayoutPlanner.AbsorbNarrowPerimeterGapsIntoHall(original,0,
        out var fixedGround)==1,"The narrow wing strip should join ground-floor circulation.");
    Assert(RoomLayoutPlanner.AbsorbNarrowPerimeterGapsIntoHall(fixedGround,1,
        out var fixedBoth)==1,"The upstairs should recover the same 0.762m strip.");
    foreach(int level in new[]{0,1})
    {
        var floor=BuildingStoreys.GetFloor(fixedBoth,level);
        Assert(RoomLayoutPlanner.UnassignedArea(floor)<0.001,
            "Every metre of the uploaded L-shaped floor is now accounted for.");
        Assert(floor.Rooms.Single(r=>r.Type=="Hallway").Outline.Count==6,
            "The hallway should acquire a stepped L-wing edge.");
    }
    Assert(BuildingConnectivity.AddStairs(fixedBoth,[0,1],out var withStairs)==1,
        "Furnished L-shaped hallways must allow a slim wall-side staircase.");
    Assert(withStairs.Staircases.Single().LandingLength>=1.0);
    Assert(BuildingStoreys.Validate(withStairs,out reason),reason);
    var options = new GenerationOptions
    {
        Rooms=true,Corridors=true,Stairs=true,Doors=false,
        Windows=false,Fireplaces=false,Purpose=BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggest(original,[0,1],1762,options,
        out var suggested,out reason),reason);
    Assert(suggested.Building.Staircases.Count==1);
    Assert(RoomLayoutPlanner.UnassignedArea(
        BuildingStoreys.GetFloor(suggested.Building,0))<0.001);
    Assert(RoomLayoutPlanner.UnassignedArea(
        BuildingStoreys.GetFloor(suggested.Building,1))<0.001);
    Assert(BuildingStoreys.Validate(suggested.Building,out reason),reason);
});
Check("Upper-floor dragged perimeter corners honor 5-foot and 10-foot grid intervals", () =>
{
    var polygon = new List<Point2>
    {
        new(0,0),new(15.24,0),new(15.24,12.192),new(0,12.192)
    };
    foreach (int feet in new[] {5,10})
    {
        double step = feet * 0.3048;
        var moved = GeometrySnap.ForMovedVertex(
            new Point2(8.12,6.35), new Point2(0,0),
            [polygon], 0.01, true, step);
        Equal(moved.X, Math.Round(8.12 / step,
            MidpointRounding.AwayFromZero) * step);
        Equal(moved.Y, Math.Round(6.35 / step,
            MidpointRounding.AwayFromZero) * step);
        var free = GeometrySnap.ForMovedVertex(
            new Point2(8.12,6.35), new Point2(0,0),
            [polygon], 0.01, true, step, bypass:true);
        Equal(free.X,8.12);
        Equal(free.Y,6.35);
    }
});

Check("Edited first-floor perimeter receives room and stair suggestions before subdivision", () =>
{
    var source = new BuildingDocument
    {
        GridFeet = 5,
        Perimeter = [new(0,0),new(12.192,0),new(12.192,9.144),new(0,9.144)]
    };
    Assert(BuildingStoreys.TryAddFloor(source, out var resized, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(resized, 1);
    upper.Perimeter = [new(0,0),new(10.668,0),new(10.668,9.144),new(0,9.144)];
    BuildingStoreys.SetFloor(resized, 1, upper);
    Assert(BuildingStoreys.Validate(resized, out reason), reason);
    var options = new GenerationOptions
    {
        Rooms=true,Corridors=true,Doors=true,Windows=false,
        Stairs=true,Purpose=BuildingUse.Residence
    };
    Assert(BuildingGenerator.TrySuggest(resized,[0,1],1881,options,
        out var proposal,out reason),reason);
    Assert(proposal.Building.Staircases.Count==1,
        "Stairs should be reserved before filling rooms.");
    foreach(int index in new[]{0,1})
    {
        var floor=BuildingStoreys.GetFloor(proposal.Building,index);
        Assert(floor.Rooms.Any(r=>r.Type=="Hallway"));
        Assert(floor.Rooms.Any(r=>r.Type!="Hallway"));
        Assert(RoomLayoutPlanner.UnassignedArea(floor) <
            PolygonGeometry.Area(floor.Perimeter)*0.25,
            "A resized upper floor should not remain mostly unassigned.");
    }
    Assert(BuildingStoreys.Validate(proposal.Building,out reason),reason);
    Assert(BuildingGenerator.TryRegenerate(resized,[0,1],1881,options,
        out var replacement,out reason),reason);
    Assert(replacement.Building.Staircases.Count==1);
    Assert(BuildingStoreys.GetFloor(replacement.Building,1).Rooms.Count>=2);
    Assert(BuildingStoreys.Validate(replacement.Building,out reason),reason);
});

Check("Stepped upper-floor footprint can use a shorter interior hallway", () =>
{
    var source = new BuildingDocument
    {
        Perimeter=[new(0,0),new(14,0),new(14,10),new(0,10)]
    };
    Assert(BuildingStoreys.TryAddFloor(source,out var two,out var reason),reason);
    var upper=BuildingStoreys.GetFloor(two,1);
    upper.Perimeter=[new(0,0),new(6,0),new(6,8.5),
        new(14,8.5),new(14,10),new(0,10)];
    BuildingStoreys.SetFloor(two,1,upper);
    Assert(BuildingStoreys.Validate(two,out reason),reason);
    Assert(BuildingConnectivity.TryAddCorridor(two,1,
        out var withHall,out var hallId),
        "A rectangular full-width hall is not the only valid hallway in an L.");
    var hall=BuildingStoreys.GetFloor(withHall,1).Rooms.Single(r=>r.Id==hallId);
    Assert(FloorGeometry.IsWithinPolygon(hall.Outline,upper.Perimeter));
    Assert(BuildingGenerator.TrySuggest(two,[1],1777,
        new GenerationOptions
        {Rooms=true,Corridors=true,Doors=true,Stairs=false},
        out var draft,out reason),reason);
    Assert(BuildingStoreys.GetFloor(draft.Building,1).Rooms.Count>1);
    Assert(BuildingStoreys.Validate(draft.Building,out reason),reason);
});
Check("Two blank floors with smaller inset upstairs generate aligned hall and working stairs", () =>
{
    foreach (int grid in new[] { 0, 5, 10 })
    foreach (int variant in new[] { 0, 1, 2 })
    {
        double step = grid == 0 ? 0.5 : grid * 0.3048;
        double width = 12 * step, height = 8 * step;
        // Use realistically-sized plans in metric mode as well; the
        // failure is relative hallway alignment, not absolute size.
        if (grid == 0) { width = 12.0; height = 9.0; step = 0.5; }
        var original = new BuildingDocument
        {
            GridFeet = grid,
            Perimeter =
            [
                new(0,0),new(width,0),new(width,height),new(0,height)
            ]
        };
        Assert(BuildingStoreys.TryAddFloor(original, out var two,
            out var reason), reason);
        var upper = BuildingStoreys.GetFloor(two, 1);
        // Exactly one grid cell inset; for variant 2 the first floor
        // is also shifted, so both perpendicular axes change.
        double left = step * (variant == 1 ? 0 : 1);
        double right = width - step * (variant == 2 ? 0 : 1);
        double top = step * (variant == 2 ? 0 : 1);
        double bottom = height - step * (variant == 1 ? 0 : 1);
        upper.Perimeter =
        [
            new(left, top),new(right, top),
            new(right, bottom),new(left, bottom)
        ];
        BuildingStoreys.SetFloor(two, 1, upper);
        Assert(BuildingStoreys.Validate(two, out reason), reason);
        var opts = new GenerationOptions
        {
            Rooms = true, Corridors = true, Doors = true,
            Stairs = true, Windows = false,
            Purpose = BuildingUse.Residence
        };
        Assert(BuildingGenerator.TrySuggest(two, [0, 1], 610 + variant,
            opts, out var suggestion, out reason),
            $"Grid={grid} variant={variant}: {reason}");
        Assert(suggestion.Building.Staircases.Count == 1,
            "A shared stair connection must be planned on blank floors.");
        var staircase = suggestion.Building.Staircases.Single();
        Assert(staircase.LandingLength >= 1.0);
        foreach (int index in new[] { 0, 1 })
        {
            var floor = BuildingStoreys.GetFloor(suggestion.Building, index);
            var hallway = floor.Rooms.FirstOrDefault(r => r.Type == "Hallway");
            Assert(hallway is not null);
            Assert(PolygonGeometry.ContainsPoint(hallway.Outline, staircase.Centre),
                "The flights must be inside the common circulation area.");
            Assert(floor.Rooms.Any(r => r.Type != "Hallway"),
                "Room generation must still proceed after reserving stairs.");
        }
        Assert(BuildingStoreys.Validate(suggestion.Building, out reason), reason);
        Assert(two.Staircases.Count == 0,
            "The original unallocated drawing must remain unchanged.");
    }
});

Check("Two blank offset storeys fit an aligned stair corridor before other rooms", () =>
{
    var ground = new BuildingDocument
    {
        Perimeter = [new(0,0),new(13,0),new(13,10),new(0,10)]
    };
    Assert(BuildingStoreys.TryAddFloor(ground, out var two, out var reason), reason);
    var upper = BuildingStoreys.GetFloor(two, 1);
    upper.Perimeter = [new(0.75,0.5),new(11.75,0.5),
        new(11.75,8.5),new(0.75,8.5)];
    BuildingStoreys.SetFloor(two, 1, upper);
    Assert(BuildingConnectivity.TryAddAlignedHallwaysAndStairs(
        two, 0, out var staged, out reason), reason);
    var stair = staged.Staircases.Single();
    foreach (int level in new[] { 0, 1 })
    {
        var floor = BuildingStoreys.GetFloor(staged, level);
        Assert(floor.Rooms.Count == 1);
        Assert(PolygonGeometry.ContainsPoint(floor.Rooms[0].Outline, stair.Centre));
    }
    Assert(BuildingStoreys.Validate(staged, out reason), reason);
});

Check("L.json ground-floor shallow 25-foot wing is circulation, never a bedroom", () =>
{
    // Exact 5-foot-grid geometry of the uploaded L.json, excluding the
    // implausible room the old residual pass added at y=3.048..4.834.
    List<Point2> Box(double l, double t, double r, double b) =>
        [new(l,t),new(r,t),new(r,b),new(l,b)];
    var perimeter = new List<Point2>
    {
        new(6.096,0), new(10.668,0), new(10.668,3.048),
        new(18.288,3.048), new(18.288,10.668), new(6.096,10.668)
    };
    var hall = new RoomDefinition
    {
        Name="Hallway 1", Type="Hallway",
        Outline=Box(6.096,4.834,18.288,6.834)
    };
    var source = new BuildingDocument
    {
        GridFeet=5, Perimeter=perimeter,
        Rooms=
        [
            hall,
            new RoomDefinition {Name="Living room",Type="Living room",
                Outline=Box(6.096,6.834,13.596,10.668)},
            new RoomDefinition {Name="Kitchen",Type="Kitchen",
                Outline=Box(13.596,6.834,18.288,10.668)},
            new RoomDefinition {Name="Dining room",Type="Dining room",
                Outline=Box(6.096,0,10.668,4.834)}
        ]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    var stripe = Box(10.668,3.048,18.288,4.834);
    Equal(PolygonGeometry.Area(stripe), 7.62 * 1.786);
    string label = RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence,0,PolygonGeometry.Area(stripe),
        source.Rooms,stripe,new Random(12));
    Assert(label=="Passage",$"Expected circulation, not {label}.");

    int count=RoomLayoutPlanner.AbsorbNarrowPerimeterGapsIntoHall(
        source,0,out var connected);
    Assert(count==1,"A long shallow L-wing should merge into its hallway.");
    Assert(connected.Rooms.Count==4,
        "The slim wing is circulation, not a fifth bedroom.");
    Assert(PolygonGeometry.ContainsPoint(
        connected.Rooms.Single(r=>r.Type=="Hallway").Outline,
        new Point2(15,4.0)));
    Assert(RoomLayoutPlanner.UnassignedArea(connected)<0.001,
        "The hallway must now account for the entire narrow strip.");
    Assert(BuildingStoreys.Validate(connected,out reason),reason);
    Equal(RoomLayoutPlanner.UnassignedArea(source),7.62*1.786);
});

Check("Bedroom names require room width, not just floor area", () =>
{
    List<Point2> Box(double w,double h) =>
        [new(0,0),new(w,0),new(w,h),new(0,h)];
    var rooms = new List<RoomDefinition>();
    string wide=RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence,1,12,rooms,Box(3,4),new Random(12));
    Assert(wide=="Bedroom",$"3x4m should qualify as a bedroom: {wide}");
    string shallow=RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence,1,12,rooms,Box(2.4,5),new Random(12));
    Assert(shallow!="Bedroom" && shallow!="Guest room",
        "A 2.4m-wide room should not become sleeping accommodation.");
    string passage=RoomLayoutPlanner.ChooseRoomTypeForOutline(
        BuildingUse.Residence,1,14,rooms,Box(1.8,7.7),new Random(12));
    Assert(passage=="Passage");
});

Check("L.json blank unequal storeys generate no 6-foot-wide bedrooms", () =>
{
    var ground = new BuildingDocument
    {
        GridFeet=5, Perimeter=[
            new(6.096,0),new(10.668,0),new(10.668,3.048),
            new(18.288,3.048),new(18.288,10.668),new(6.096,10.668)]
    };
    Assert(BuildingStoreys.TryAddFloor(ground,out var original,
        out var reason),reason);
    var upper=BuildingStoreys.GetFloor(original,1);
    upper.Perimeter=[
        new(6.096,0),new(10.668,0),new(10.668,4.572),
        new(18.288,4.572),new(18.288,10.668),new(6.096,10.668)];
    BuildingStoreys.SetFloor(original,1,upper);
    Assert(BuildingStoreys.Validate(original,out reason),reason);
    var options = new GenerationOptions
    {
        Rooms=true,Corridors=true,Doors=true,Stairs=true,
        Windows=false,Fireplaces=false,Purpose=BuildingUse.Residence
    };
    foreach(int seed in new[]{12,310,1211})
    {
        Assert(BuildingGenerator.TrySuggest(original,[0,1],seed,options,
            out var draft,out reason),reason);
        foreach(int floorIndex in new[]{0,1})
        {
            var floor=BuildingStoreys.GetFloor(draft.Building,floorIndex);
            foreach(var room in floor.Rooms.Where(r=>
                r.Type is "Bedroom" or "Guest room" or "Suite"))
            {
                double w=room.Outline.Max(p=>p.X)-room.Outline.Min(p=>p.X);
                double h=room.Outline.Max(p=>p.Y)-room.Outline.Min(p=>p.Y);
                Assert(Math.Min(w,h)>=2.5-1e-5,
                    $"Seed {seed} floor {floorIndex}: {room.Name} is {w} x {h} metres.");
            }
        }
        var gf=BuildingStoreys.GetFloor(draft.Building,0);
        Assert(!gf.Rooms.Any(r=>r.Type=="Bedroom" &&
            PolygonGeometry.ContainsPoint(r.Outline,new Point2(15,4))));
        Assert(gf.Rooms.Any(r=>r.Type=="Hallway" &&
            PolygonGeometry.ContainsPoint(r.Outline,new Point2(15,4))),
            "The old 1.786m-deep bedroom strip should belong to hallway circulation.");
        Assert(BuildingStoreys.Validate(draft.Building,out reason),reason);
    }
});
Check("Consolidate two generated passages into a single hallway", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var hall=new RoomDefinition
    {
        Type="Hallway",Name="Hallway 1",Outline=R(0,6,12,8)
    };
    var source=new BuildingDocument
    {
        Perimeter=R(0,0,12,10),
        Rooms=[
            hall,
            new RoomDefinition
            {
                Type="Passage",Name="Passage",Outline=R(0,4,6,6)
            },
            new RoomDefinition
            {
                Type="Passage",Name="Passage 2",Outline=R(6,4,12,6)
            }
        ]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    Assert(RoomLayoutPlanner.ConsolidateGeneratedRooms(
        source,0,[hall.Id],out var repaired)==2);
    Assert(repaired.Rooms.Count==1);
    Assert(repaired.Rooms.Single().Type=="Hallway");
    Assert(Math.Abs(PolygonGeometry.Area(repaired.Rooms[0].Outline)-48)<1e-6);
    Assert(BuildingStoreys.Validate(repaired,out reason),reason);
    Assert(source.Rooms.Count==3);
});

Check("Generated small side room merges into a realistic six-corner L-shaped bedroom", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var hall=new RoomDefinition {Type="Hallway",Name="Hallway 1",
        Outline=R(0,7,12,9)};
    var bedroom=new RoomDefinition {Type="Bedroom",Name="Bedroom",
        Outline=R(0,0,6,4)};
    var fragment=new RoomDefinition {Type="Storeroom",Name="Storeroom",
        Outline=R(6,2,9,4)};
    var source=new BuildingDocument
    {
        Perimeter=R(0,0,12,10),Rooms=[hall,bedroom,fragment]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    Assert(RoomLayoutPlanner.ConsolidateGeneratedRooms(
        source,0,[hall.Id],out var connected)==1);
    Assert(connected.Rooms.Count==2);
    var l=connected.Rooms.Single(r=>r.Id==bedroom.Id);
    Assert(l.Type=="Bedroom");
    Assert(l.Outline.Count>=6,
        "The combined room should retain the L-shaped re-entrant corner.");
    Assert(Math.Abs(PolygonGeometry.Area(l.Outline)-30)<1e-6);
    Assert(PolygonGeometry.ContainsPoint(l.Outline,new Point2(8,3)));
    Assert(!PolygonGeometry.ContainsPoint(l.Outline,new Point2(8,1)));
    Assert(BuildingStoreys.Validate(connected,out reason),reason);
});

Check("Generated consolidation never changes existing hand-drawn L rooms", () =>
{
    var hall=new RoomDefinition {Type="Hallway",Name="Hallway 1",
        Outline=[new(0,7),new(12,7),new(12,9),new(0,9)]};
    var handmade=new RoomDefinition {Type="Other",Name="Room A",
        Outline=[new(0,0),new(6,0),new(6,2),new(9,2),new(9,4),new(0,4)]};
    var disposable=new RoomDefinition {Type="Passage",Name="Passage",
        Outline=[new(9,2),new(11,2),new(11,4),new(9,4)]};
    var source=new BuildingDocument {Perimeter=[
        new(0,0),new(12,0),new(12,10),new(0,10)],
        Rooms=[hall,handmade,disposable]};
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    RoomLayoutPlanner.ConsolidateGeneratedRooms(source,0,
        [hall.Id,handmade.Id],out var draft);
    var kept=draft.Rooms.Single(r=>r.Id==handmade.Id);
    Assert(kept.Outline.SequenceEqual(handmade.Outline));
    Assert(kept.Name=="Room A");
    Assert(BuildingStoreys.Validate(draft,out reason),reason);
});

Check("Delete chimney removes the entire shared fireplace stack across floors", () =>
{
    var ground=new BuildingDocument
    {
        Perimeter=[new(0,0),new(12,0),new(12,10),new(0,10)]
    };
    Assert(BuildingStoreys.TryAddFloor(ground,out var two,out var reason),reason);
    var flue=new ChimneyDefinition
    {
        Centre=new Point2(6,0.5),Width=1.4,Depth=0.7
    };
    two.Chimneys.Add(flue);
    Assert(BuildingStoreys.Validate(two,out reason),reason);
    Assert(BuildingStoreys.TryRemoveChimney(two,flue.Id,
        out var removed,out reason),reason);
    Assert(removed.Chimneys.Count==0);
    Assert(two.Chimneys.Count==1,
        "Deletion must not modify original document.");
    Assert(!BuildingStoreys.TryRemoveChimney(removed,flue.Id,
        out _,out reason));
    Assert(reason.Contains("does not exist"));
    Assert(BuildingStoreys.Validate(removed,out reason),reason);
});
Check("example.json keeps six-corner Room A and its angled wall adjoining Room B", () =>
{
    // Reproduces the perimeter and two user-drawn rooms from example.json.
    // The internal separation is slightly angled, and should never be
    // forced back onto a rectangular room grid.
    var floor=new BuildingDocument
    {
        GridFeet=5,
        Perimeter=[
            new(6.096,1.524),new(10.668,1.524),
            new(10.668,3.048),new(12.362624398073836,3.048),
            new(16.764,3.048),new(16.764,10.668),
            new(6.096,10.668)]
    };
    var a=new RoomDefinition
    {
        Name="Room A",Type="Other",Outline=[
            new(12.385557784911716,5.096),new(6.096,5.096),
            new(6.096,1.524),new(10.668,1.524),
            new(10.668,3.048),new(12.362624398073836,3.048)]
    };
    var b=new RoomDefinition
    {
        Name="Room B",Type="Other",Outline=[
            new(12.362624398073836,3.048),new(16.764,3.048),
            new(16.764,5.096),new(12.385557784911716,5.096)]
    };
    floor.Rooms.AddRange([a,b]);
    Assert(BuildingStoreys.Validate(floor,out var reason),reason);
    Assert(a.Outline.Count==6 && b.Outline.Count==4);
    Assert(RoomMerging.TryMergeRooms(floor,a.Id,b.Id,out var joined,out reason),
        reason);
    Assert(joined.Rooms.Count==1);
    Assert(joined.Rooms[0].Id==a.Id);
    Assert(joined.Rooms[0].Outline.Count>=6);
    Assert(BuildingStoreys.Validate(joined,out reason),reason);
    Assert(floor.Rooms.Count==2,
        "Automatic or manual unions must be detached from the source.");
});
Check("Reclaim oversized hallway from uploaded stepped two-storey footprint", () =>
{
    // Regression geometry from example(1).json: the ground-floor hallway
    // grew to 50.15m² (56.8%) after the narrow passage merging pass.
    var oversized = new BuildingDocument
    {
        SchemaVersion = 2, GridFeet = 5,
        Perimeter =
        [
            new(6.096, 1.524), new(10.668, 1.524),
            new(10.668, 3.048), new(16.764, 3.048),
            new(16.764, 10.668), new(6.096, 10.668)
        ]
    };
    var hall = new RoomDefinition
    {
        Name = "Hallway 1", Type = "Hallway",
        Outline =
        [
            new(16.764, 7.096), new(6.096, 7.096),
            new(6.096, 1.524), new(10.668, 1.524),
            new(10.668, 3.048), new(16.764, 3.048)
        ]
    };
    oversized.Rooms.AddRange(
    [
        hall,
        new RoomDefinition
        {
            Name = "Living room", Type = "Living room",
            Outline = [new(6.096, 7.096), new(13.096, 7.096),
                new(13.096, 10.668), new(6.096, 10.668)]
        },
        new RoomDefinition
        {
            Name = "Kitchen", Type = "Kitchen",
            Outline = [new(13.096, 7.096), new(16.764, 7.096),
                new(16.764, 10.668), new(13.096, 10.668)]
        }
    ]);
    oversized.UpperFloors.Add(new StoreyDefinition
    {
        Name = "First Floor",
        Perimeter =
        [
            new(6.096, 1.524), new(10.668, 1.524),
            new(10.668, 4.572), new(16.764, 4.572),
            new(16.764, 10.668), new(6.096, 10.668)
        ],
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Hallway 1", Type = "Hallway",
                Outline = [new(6.096, 5.096), new(16.764, 5.096),
                    new(16.764, 7.096), new(6.096, 7.096)]
            }
        ]
    });
    oversized.Staircases.Add(new StaircaseDefinition
    {
        LowerFloorIndex = 0, Centre = new Point2(11.43, 5.696),
        Length = 2.8, Width = 0.9, RotationDegrees = 90,
        LandingLength = 1
    });
    Assert(BuildingStoreys.Validate(oversized, out var reason), reason);
    int changed = RoomLayoutPlanner.ReclaimOversizedGeneratedHallway(
        oversized, 0, Array.Empty<Guid>(), BuildingUse.Residence,
        new Random(123), out var repaired);
    Assert(changed == 3, $"Expected three reclaimed rooms, got {changed}.");
    Assert(repaired.Rooms.Count == 6);
    Assert(PolygonGeometry.Area(repaired.Rooms.Single(r => r.Type == "Hallway")
        .Outline) < 22, "The original 50m² hall was not reduced to circulation.");
    Assert(oversized.Rooms.Count == 3, "The original proposal was modified.");
    Assert(BuildingStoreys.Validate(repaired, out reason), reason);
    Assert(repaired.Staircases.Count == 1);
    Assert(repaired.UpperFloors[0].Rooms.Count == 1,
        "The nonselected upper floor must remain untouched.");
    int blocked = RoomLayoutPlanner.ReclaimOversizedGeneratedHallway(
        oversized, 0, [hall.Id], BuildingUse.Residence,
        new Random(123), out var untouched);
    Assert(blocked == 0 && untouched.Rooms.Count == 3,
        "Hand-drawn hallways must never be partitioned.");
});

Check("Blank stepped floors put the staircase against a common corridor wall", () =>
{
    var blank = new BuildingDocument
    {
        SchemaVersion = 2,
        Perimeter =
        [
            new(6.096, 1.524), new(10.668, 1.524),
            new(10.668, 3.048), new(16.764, 3.048),
            new(16.764, 10.668), new(6.096, 10.668)
        ],
        UpperFloors =
        [
            new StoreyDefinition
            {
                Perimeter =
                [
                    new(6.096, 1.524), new(10.668, 1.524),
                    new(10.668, 4.572), new(16.764, 4.572),
                    new(16.764, 10.668), new(6.096, 10.668)
                ]
            }
        ]
    };
    Assert(BuildingConnectivity.TryAddAlignedHallwaysAndStairs(
        blank, 0, out var aligned, out var reason), reason);
    Assert(aligned.Staircases.Count == 1);
    var stair = aligned.Staircases.Single();
    var lowerHall = aligned.Rooms.Single(r => r.Type == "Hallway");
    var upperHall = aligned.UpperFloors[0].Rooms.Single(r => r.Type == "Hallway");
    // Along the room-facing south wall on both levels, not 15cm adrift.
    double wallY = lowerHall.Outline.Max(p => p.Y);
    Assert(Math.Abs(wallY - upperHall.Outline.Max(p => p.Y)) < 1e-6);
    Assert(Math.Abs(stair.Centre.Y + stair.Width / 2 - wallY) < 1e-6,
        "Stair flight must actually hug the shared hallway wall.");
    Assert(BuildingStoreys.Validate(aligned, out reason), reason);
});

Check("example2.json entrance creates two storage rooms beside door", () =>
{
    // Uploaded stepped ground plan: door at (16.764,3.048), 1.786m-deep
    // excess hallway strip, 2m corridor and a shared upper-floor staircase.
    var oversized = new BuildingDocument
    {
        SchemaVersion = 2, GridFeet = 5,
        Perimeter = [
            new(7.62,0),new(12.192,0),new(12.192,3.048),
            new(21.336,3.048),new(21.336,10.668),new(7.62,10.668)
        ],
        Rooms =
        [
            new RoomDefinition
            {
                Name="Hallway 1",Type="Hallway",Outline=[
                    new(7.62,4.834),new(12.192,4.834),
                    new(12.192,3.048),new(21.336,3.048),
                    new(21.336,6.834),new(7.62,6.834)]
            },
            new RoomDefinition
            {
                Name="Living room",Type="Living room",Outline=[
                    new(7.62,6.834),new(16.12,6.834),
                    new(16.12,10.668),new(7.62,10.668)]
            },
            new RoomDefinition
            {
                Name="Kitchen",Type="Kitchen",Outline=[
                    new(16.12,6.834),new(21.336,6.834),
                    new(21.336,10.668),new(16.12,10.668)]
            },
            new RoomDefinition
            {
                Name="Dining room",Type="Dining room",Outline=[
                    new(7.62,0),new(12.192,0),
                    new(12.192,4.834),new(7.62,4.834)]
            }
        ],
        Doors =
        [
            new DoorDefinition { Centre=new(16.764,3.048),Width=0.9 },
            new DoorDefinition { Centre=new(10.34,6.834),Width=0.9 },
            new DoorDefinition { Centre=new(18.728,6.834),Width=0.9 },
            new DoorDefinition { Centre=new(9.906,4.834),Width=0.9 },
            new DoorDefinition { Centre=new(16.12,8.751),Width=0.8 }
        ],
        UpperFloors =
        [
            new StoreyDefinition
            {
                Name="First Floor",
                Perimeter=[
                    new(7.62,0),new(12.192,0),new(12.192,4.572),
                    new(21.336,4.572),new(21.336,10.668),
                    new(7.62,10.668)],
                Rooms=[
                    new RoomDefinition
                    {
                        Name="Hallway 1",Type="Hallway",Outline=[
                            new(7.62,4.834),new(12.192,4.834),
                            new(12.192,4.572),new(21.336,4.572),
                            new(21.336,6.834),new(7.62,6.834)]
                    }
                ]
            }
        ],
        Staircases =
        [
            new StaircaseDefinition
            {
                LowerFloorIndex=0, Centre=new(14.478,6.384),
                Length=2.8,Width=0.9,RotationDegrees=90,LandingLength=1
            }
        ]
    };
    Assert(BuildingStoreys.Validate(oversized,out var reason),reason);
    var originalDoor = oversized.Doors[0];
    var originalStair = oversized.Staircases.Single().Copy();
    var originalHall = oversized.Rooms.Single(r=>r.Type=="Hallway");
    int count = RoomLayoutPlanner.PartitionGeneratedEntranceWing(
        oversized,0,Array.Empty<Guid>(),out var improved);
    Assert(count==2,$"Expected two entrance storerooms; got {count}.");
    var storage=improved.Rooms.Where(r=>r.Type=="Storage").ToArray();
    Assert(storage.Length==2);
    foreach(var room in storage)
        Assert(Math.Abs(PolygonGeometry.Area(room.Outline)-6.379592)<0.001,
            "Each flanking room should reclaim the shallow entrance wing.");
    var newHall=improved.Rooms.Single(r=>r.Type=="Hallway");
    Assert(Math.Abs(PolygonGeometry.Area(newHall.Outline)-31.004)<0.01);
    Assert(improved.Doors.Any(d=>d.Id==originalDoor.Id &&
        d.Centre.Equals(originalDoor.Centre)));
    Assert(improved.Staircases.Single().Centre.Equals(originalStair.Centre));
    Assert(improved.UpperFloors[0].Rooms.Count==1);
    Assert(BuildingStoreys.Validate(improved,out reason),reason);
    Assert(oversized.Rooms.Count==4 &&
        oversized.Rooms.Single(r=>r.Type=="Hallway").Outline
            .SequenceEqual(originalHall.Outline));
    int addedDoors=BuildingConnectivity.AddDoors(improved,0,out var accessed);
    Assert(addedDoors==2,$"Expected access doors for two stores; got {addedDoors}.");
    foreach(var room in accessed.Rooms.Where(r=>r.Type=="Storage"))
        Assert(accessed.Doors.Any(d=>room.Outline.Zip(
            room.Outline.Skip(1).Append(room.Outline[0]),
            (a,b)=>PolygonGeometry.ClosestPointOnSegment(d.Centre,a,b)
                .DistanceTo(d.Centre)).Any(distance=>distance<0.001)),
            "Each storage room must have a door.");
    Assert(BuildingStoreys.Validate(accessed,out reason),reason);
    Assert(RoomLayoutPlanner.PartitionGeneratedEntranceWing(
        accessed,0,Array.Empty<Guid>(),out _)==0,
        "An already split entrance must not be partitioned twice.");
    Assert(RoomLayoutPlanner.PartitionGeneratedEntranceWing(
        oversized,0,[originalHall.Id],out var protectedPlan)==0 &&
        protectedPlan.Rooms.Count==4,
        "An existing user's hallway must not be changed by generation.");

    // Rotation regression: the same entrance may be on an east/west wall.
    // Flip both coordinates (and the stair axis) without changing lengths.
    var rotated = BuildingStoreys.Copy(oversized);
    Point2 Swap(Point2 p) => new(p.Y, p.X);
    rotated.Perimeter = rotated.Perimeter.Select(Swap).ToList();
    foreach (var room in rotated.Rooms)
        room.Outline = room.Outline.Select(Swap).ToList();
    foreach (var door in rotated.Doors) door.Centre = Swap(door.Centre);
    foreach (var upper in rotated.UpperFloors)
    {
        upper.Perimeter = upper.Perimeter.Select(Swap).ToList();
        foreach (var room in upper.Rooms)
            room.Outline = room.Outline.Select(Swap).ToList();
    }
    foreach (var stair in rotated.Staircases)
    {
        stair.Centre = Swap(stair.Centre);
        stair.RotationDegrees = 0;
    }
    Assert(BuildingStoreys.Validate(rotated,out reason),reason);
    Assert(RoomLayoutPlanner.PartitionGeneratedEntranceWing(
        rotated,0,Array.Empty<Guid>(),out var turned)==2,
        "Storage allocation must work for entrances on vertical exterior walls.");
    Assert(BuildingStoreys.Validate(turned,out reason),reason);
    Assert(BuildingConnectivity.AddDoors(turned,0,out var turnedAccess)==2);
    Assert(BuildingStoreys.Validate(turnedAccess,out reason),reason);
});

Check("example2(1) dining room and bedroom reach hallway without crossing storerooms", () =>
{
    // The uploaded v0.31 layout placed two 6.858 x 2.048 m storerooms
    // between the north rooms and the central hall. Neither had a hall
    // door; each was the compulsory route to a bedroom/dining room.
    var document = new BuildingDocument
    {
        SchemaVersion = 2, GridFeet = 5,
        Perimeter =
        [
            new(7.62,0), new(12.192,0), new(12.192,3.048),
            new(16.764,3.048), new(16.764,0), new(21.336,0),
            new(21.336,3.048), new(21.336,10.668),
            new(7.62,10.668)
        ],
        Rooms =
        [
            new RoomDefinition
            {
                Name = "Hallway 1", Type = "Hallway",
                Outline = [new(7.62,5.096),new(21.336,5.096),
                    new(21.336,7.096),new(7.62,7.096)]
            },
            new RoomDefinition
            {
                Name = "Living room", Type = "Living room",
                Outline = [new(7.62,7.096),new(16.12,7.096),
                    new(16.12,10.668),new(7.62,10.668)]
            },
            new RoomDefinition
            {
                Name = "Kitchen", Type = "Kitchen",
                Outline = [new(16.12,7.096),new(21.336,7.096),
                    new(21.336,10.668),new(16.12,10.668)]
            },
            new RoomDefinition
            {
                Name = "Dining room", Type = "Dining room",
                Outline = [new(7.62,0),new(12.192,0),
                    new(12.192,3.048),new(7.62,3.048)]
            },
            new RoomDefinition
            {
                Name = "Bedroom", Type = "Bedroom",
                Outline = [new(16.764,0),new(21.336,0),
                    new(21.336,3.048),new(16.764,3.048)]
            },
            new RoomDefinition
            {
                Name = "Storeroom", Type = "Storeroom",
                Outline = [new(7.62,3.048),new(14.478,3.048),
                    new(14.478,5.096),new(7.62,5.096)]
            },
            new RoomDefinition
            {
                Name = "Storeroom 2", Type = "Storeroom",
                Outline = [new(14.478,3.048),new(21.336,3.048),
                    new(21.336,5.096),new(14.478,5.096)]
            }
        ],
        UpperFloors =
        [
            new StoreyDefinition
            {
                Name = "First Floor",
                Perimeter =
                [
                    new(7.62,1.524),new(12.192,1.524),new(12.192,3.048),
                    new(16.764,3.048),new(16.764,1.524),new(21.336,1.524),
                    new(21.336,3.048),new(21.336,10.668),
                    new(7.62,10.668)
                ]
            }
        ],
        Staircases =
        [
            new StaircaseDefinition
            {
                LowerFloorIndex = 0,
                Centre = new Point2(14.478,6.646),
                Length = 2.8, Width = 0.9,
                RotationDegrees = 90, LandingLength = 1
            }
        ]
    };
    Assert(BuildingStoreys.Validate(document, out var reason),reason);
    Guid hallId = document.Rooms[0].Id;
    Guid diningId = document.Rooms[3].Id;
    Guid bedroomId = document.Rooms[4].Id;
    int routes = RoomLayoutPlanner.OpenDirectAccessPastGeneratedStores(
        document,0,Array.Empty<Guid>(),out var routed);
    Assert(routes==2,$"Expected one passage beside each storage room; got {routes}.");
    Assert(routed.Rooms.Count==document.Rooms.Count,
        "No extra enclosed passage rooms should be created.");
    Assert(routed.Rooms.Where(r=>r.Type=="Storeroom").Count()==2);
    Assert(BuildingStoreys.Validate(routed,out reason),reason);
    Assert(document.Rooms[0].Outline.Count==4 &&
        document.Rooms[5].Outline.Max(p=>p.X)==14.478,
        "Planning must not mutate the input document.");
    Assert(BuildingConnectivity.ShareWall(
        routed.Rooms.Single(r=>r.Id==hallId).Outline,
        routed.Rooms.Single(r=>r.Id==diningId).Outline,1.0));
    Assert(BuildingConnectivity.ShareWall(
        routed.Rooms.Single(r=>r.Id==hallId).Outline,
        routed.Rooms.Single(r=>r.Id==bedroomId).Outline,1.0));
    Assert(BuildingConnectivity.AddDoors(routed,0,out var opened)>=7);
    bool HasSharedDoor(RoomDefinition a,RoomDefinition b) =>
        opened.Doors.Any(d=>
            Enumerable.Range(0,a.Outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(d.Centre,
                    a.Outline[i],a.Outline[(i+1)%a.Outline.Count])
                .DistanceTo(d.Centre)<1e-5) &&
            Enumerable.Range(0,b.Outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(d.Centre,
                    b.Outline[i],b.Outline[(i+1)%b.Outline.Count])
                .DistanceTo(d.Centre)<1e-5));
    var hallway = opened.Rooms.Single(r=>r.Id==hallId);
    Assert(HasSharedDoor(hallway,opened.Rooms.Single(r=>r.Id==diningId)),
        "Dining room should have its own hall entrance.");
    Assert(HasSharedDoor(hallway,opened.Rooms.Single(r=>r.Id==bedroomId)),
        "Bedroom should have its own hall entrance.");
    foreach(var store in opened.Rooms.Where(r=>r.Type=="Storeroom"))
    {
        Assert(HasSharedDoor(hallway,store),
            "Storeroom should open directly into circulation.");
        Assert(opened.Doors.Count(d=>
            Enumerable.Range(0,store.Outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(d.Centre,
                    store.Outline[i],store.Outline[(i+1)%store.Outline.Count])
                    .DistanceTo(d.Centre)<1e-5))==1,
            "A normal storeroom must not become a two-door passage.");
    }
    Assert(BuildingStoreys.Validate(opened,out reason),reason);
    Assert(RoomLayoutPlanner.OpenDirectAccessPastGeneratedStores(
        routed,0,Array.Empty<Guid>(),out _)==0,
        "Already accessible rooms should not acquire extra hall spurs.");
    Assert(RoomLayoutPlanner.OpenDirectAccessPastGeneratedStores(
        document,0,[hallId],out var protectedPlan)==0 &&
        protectedPlan.Rooms[0].Outline.SequenceEqual(document.Rooms[0].Outline),
        "The planner must preserve manually authored hallways.");
});

Check("Storeroom adjoining kitchen and hallway uses one kitchen door", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = [new(0,0),new(8,0),new(8,9),new(0,9)],
        Rooms =
        [
            new RoomDefinition {Name="Hallway",Type="Hallway",
                Outline=[new(0,0),new(2,0),new(2,9),new(0,9)]},
            new RoomDefinition {Name="Kitchen",Type="Kitchen",
                Outline=[new(2,0),new(8,0),new(8,5),new(2,5)]},
            new RoomDefinition {Name="Storeroom",Type="Storeroom",
                Outline=[new(2,5),new(8,5),new(8,9),new(2,9)]}
        ]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    BuildingConnectivity.AddDoors(source,0,out var withDoors);
    var kitchen=withDoors.Rooms.Single(r=>r.Type=="Kitchen");
    var store=withDoors.Rooms.Single(r=>r.Type=="Storeroom");
    Assert(withDoors.Doors.Count(d=>
        PolygonGeometry.ClosestPointOnSegment(d.Centre,
            new Point2(2,5),new Point2(8,5)).DistanceTo(d.Centre)<1e-5)==1,
        "Kitchen should provide the store's only entrance.");
    Assert(withDoors.Doors.Count(d=>
        Enumerable.Range(0,store.Outline.Count).Any(i=>
            PolygonGeometry.ClosestPointOnSegment(d.Centre,
                store.Outline[i],store.Outline[(i+1)%store.Outline.Count])
                .DistanceTo(d.Centre)<1e-5))==1,
        "Don't add a second service entrance.");
    Assert(BuildingStoreys.Validate(withDoors,out reason),reason);
});

Check("Final-generation repair fixes example2(2) even after doors were already added", () =>
{
    // This is the EXACT blocked-room structure saved by v0.32, with
    // both storeroom/bedroom and storeroom/dining doorways already in.
    // Earlier tests only checked blank rooms BEFORE their doors existed.
    List<Point2> R(double x0, double y0, double x1, double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var source = new BuildingDocument
    {
        SchemaVersion = 2, GridFeet = 5,
        Perimeter = [
            new(7.62,0),new(12.192,0),new(12.192,3.048),
            new(16.764,3.048),new(16.764,0),new(21.336,0),
            new(21.336,3.048),new(21.336,10.668),new(7.62,10.668)
        ],
        Rooms = [
            new RoomDefinition {Type="Hallway",Name="Hallway 1",
                Outline=R(7.62,5.096,21.336,7.096)},
            new RoomDefinition {Type="Living room",Name="Living room",
                Outline=R(7.62,7.096,16.12,10.668)},
            new RoomDefinition {Type="Kitchen",Name="Kitchen",
                Outline=R(16.12,7.096,21.336,10.668)},
            new RoomDefinition {Type="Dining room",Name="Dining room",
                Outline=R(7.62,0,12.192,3.048)},
            new RoomDefinition {Type="Bedroom",Name="Bedroom",
                Outline=R(16.764,0,21.336,3.048)},
            new RoomDefinition {Type="Storeroom",Name="Storeroom",
                Outline=R(7.62,3.048,14.478,5.096)},
            new RoomDefinition {Type="Storeroom",Name="Storeroom 2",
                Outline=R(14.478,3.048,21.336,5.096)}
        ],
        Doors = [
            new DoorDefinition {Centre=new(21.336,6.096),Width=0.9},
            new DoorDefinition {Centre=new(10.34,7.096),Width=0.9},
            new DoorDefinition {Centre=new(18.728,7.096),Width=0.9},
            new DoorDefinition {Centre=new(9.906,3.048),Width=0.9},
            new DoorDefinition {Centre=new(19.05,3.048),Width=0.9},
            new DoorDefinition {Centre=new(16.12,8.882),Width=0.8}
        ],
        UpperFloors = [
            new StoreyDefinition
            {
                Name="First Floor",
                Perimeter=[
                    new(7.62,1.524),new(12.192,1.524),new(12.192,3.048),
                    new(16.764,3.048),new(16.764,1.524),new(21.336,1.524),
                    new(21.336,3.048),new(21.336,10.668),new(7.62,10.668)
                ],
                Rooms = [
                    new RoomDefinition {Type="Hallway",Name="Hallway 1",
                        Outline=R(7.62,5.096,21.336,7.096)},
                    new RoomDefinition {Type="Bedroom",Name="Bedroom",
                        Outline=[new(7.62,1.524),new(12.192,1.524),
                            new(12.192,3.048),new(16.764,3.048),
                            new(16.764,5.096),new(7.62,5.096)]},
                    new RoomDefinition {Type="Study",Name="Study",
                        Outline=R(16.764,1.524,21.336,5.096)},
                    new RoomDefinition {Type="Bedroom",Name="Bedroom 2",
                        Outline=R(7.62,7.096,16.12,10.668)},
                    new RoomDefinition {Type="Bedroom",Name="Bedroom 3",
                        Outline=R(16.12,7.096,21.336,10.668)}
                ]
            }
        ],
        Staircases = [
            new StaircaseDefinition {LowerFloorIndex=0,
                Centre=new(14.478,6.646),Length=2.8,Width=0.9,
                RotationDegrees=90,LandingLength=1}
        ]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    var unreachableBefore = BuildingConnectivity.RoomsIsolatedByServiceSpaces(source,0);
    Assert(unreachableBefore.Count == 2 &&
        unreachableBefore.Any(r => r.Type == "Dining room") &&
        unreachableBefore.Any(r => r.Type == "Bedroom"),
        "The old result must be recognised as two inaccessible main rooms.");
    var idHall=source.Rooms[0].Id;
    var idDining=source.Rooms[3].Id;
    var idBedroom=source.Rooms[4].Id;
    var firstStore=source.Rooms[5].Id;
    var secondStore=source.Rooms[6].Id;
    int routes=RoomLayoutPlanner.RepairGeneratedStorageAccess(
        source,0,Array.Empty<Guid>(),out var corrected);
    Assert(routes==2,$"Expected two usable hall branches after removing generated store doors; got {routes}.");
    Assert(BuildingStoreys.Validate(corrected,out reason),reason);
    Assert(BuildingConnectivity.RoomsIsolatedByServiceSpaces(corrected,0).Count==0,
        "The generator must reject any final draft with blocked living rooms.");
    bool Touches(RoomDefinition room,Point2 centre) =>
        Enumerable.Range(0,room.Outline.Count).Any(i=>
            PolygonGeometry.ClosestPointOnSegment(centre,room.Outline[i],
                room.Outline[(i+1)%room.Outline.Count]).DistanceTo(centre)<1e-5);
    bool DoorConnects(RoomDefinition a,RoomDefinition b) =>
        corrected.Doors.Any(d=>Touches(a,d.Centre) && Touches(b,d.Centre));
    var hall=corrected.Rooms.Single(r=>r.Id==idHall);
    Assert(DoorConnects(hall,corrected.Rooms.Single(r=>r.Id==idDining)),
        "The dining room still has no own corridor entrance.");
    Assert(DoorConnects(hall,corrected.Rooms.Single(r=>r.Id==idBedroom)),
        "The bedroom still has no own corridor entrance.");
    foreach(var id in new[]{firstStore,secondStore})
    {
        var store=corrected.Rooms.Single(r=>r.Id==id);
        Assert(DoorConnects(hall,store),"Storeroom should open onto a shared hall.");
        Assert(corrected.Doors.Count(d=>Touches(store,d.Centre))==1,
            "Storeroom should have one door, not become a through passage.");
    }
    Assert(corrected.Staircases.Single().Centre.Equals(source.Staircases.Single().Centre));
    Assert(corrected.UpperFloors[0].Rooms.Count==source.UpperFloors[0].Rooms.Count);
    // These two extra-long stores were created in front of the dining
    // room and bedroom although no kitchen/workshop serves them. With
    // individual access already restored, turn that wasted storage band
    // into useful L-shaped accommodation instead of reinstating it.
    int expanded = RoomLayoutPlanner.AbsorbMisplacedGeneratedStores(
        corrected, 0, Array.Empty<Guid>(), out var functional);
    Assert(expanded == 2,
        $"Expected both misplaced 14m² store bands to be reallocated, got {expanded}.");
    Assert(!functional.Rooms.Any(r => r.Type == "Storeroom"), "Both obsolete stores should be removed.");
    Assert(functional.Rooms.Single(r => r.Id == idDining).Outline.Count >= 6, "Expanded dining room should retain a nonrectangular outline.");
    Assert(functional.Rooms.Single(r => r.Id == idBedroom).Outline.Count >= 6, "Expanded bedroom should retain a nonrectangular outline.");
    Assert(BuildingStoreys.Validate(functional, out reason), reason);
    Assert(BuildingConnectivity.RoomsIsolatedByServiceSpaces(functional,0).Count==0, "No room should be stranded behind storage after consolidating.");
    Assert(functional.Staircases.Single().Centre.Equals(source.Staircases.Single().Centre));
    Assert(corrected.Rooms.Count==7,
        "Room consolidation must not modify the earlier repaired proposal.");
    Assert(source.Doors.Count==6 && source.Rooms[0].Outline.Count==4,
        "The suggestion must not silently edit the already saved source.");
    Assert(RoomLayoutPlanner.RepairGeneratedStorageAccess(
        source,0,[idHall],out var protectedPlan)==0 &&
        protectedPlan.Doors.Count==source.Doors.Count,
        "Do not rewrite hand-authored existing hallways.");

    // The real user invokes Regenerate on an already accepted two-storey
    // JSON file, rather than starting from a blank footprint. Verify the
    // new architectural choice is effective in that workflow too.
    var opts = new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Windows=false,Stairs=false,HallHouseLayout=true
    };
    bool regenerated=false;
    var failures = new List<string>();
    foreach(int seed in new[]{7,12,23,37})
    {
        if (!BuildingGenerator.TryRegenerate(source,[0],seed,opts,
            out var suggestion,out var rejected))
        {
            failures.Add(seed+": "+rejected);
            continue;
        }
        if (!suggestion.Building.Rooms.Any(r=>r.Type=="Common room"))
        {
            failures.Add(seed+": "+suggestion.Description);
            continue;
        }
        regenerated=true;
        Assert(suggestion.Building.Staircases.Single().Centre.Equals(
            source.Staircases.Single().Centre));
        Assert(suggestion.Building.UpperFloors[0].Rooms.Count ==
            source.UpperFloors[0].Rooms.Count,
            "Regenerating the ground floor must not edit upstairs.");
        Assert(BuildingStoreys.Validate(suggestion.Building,out reason),reason);
        Assert(BuildingConnectivity.RoomsIsolatedByServiceSpaces(
            suggestion.Building,0).Count==0);
        break;
    }
    Assert(regenerated,
        "Regenerate must be able to replace the uploaded corridor plan: "+
        string.Join(" | ",failures));
});

Check("Modest single-storey hall house has common room bedrooms and screens passage", () =>
{
    var source = new BuildingDocument
    {
        Perimeter = [new(0,0),new(12,0),new(12,9),new(0,9)]
    };
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        source,out var planned,out var reason),reason);
    Assert(BuildingStoreys.Validate(planned,out reason),reason);
    Assert(planned.Rooms.Any(r=>r.Type=="Common room"));
    Assert(!planned.Rooms.Any(r=>r.Type=="Great hall"),
        "A small domestic residence should not have a manor's great hall.");
    Assert(planned.Rooms.Count(r=>r.Type=="Bedroom")>=1,
        "A one-storey residence must provide sleeping accommodation.");
    Assert(planned.Rooms.Any(r=>r.Type=="Kitchen"));
    Assert(planned.Rooms.Any(r=>r.Type=="Pantry"));
    Assert(!planned.Rooms.Any(r=>r.Type=="Parlour"),
        "A single-storey cottage needs bedrooms rather than an unnecessary parlour.");
    var passage=planned.Rooms.Single(r=>r.Type=="Hallway");
    Assert(passage.Name=="Screens passage");
    Assert(PolygonGeometry.Area(passage.Outline)<20,
        "A screens passage must not cover the centre of the building.");
    Assert(PolygonGeometry.Area(planned.Rooms.Single(r=>r.Type=="Common room")
        .Outline)>=10,"The communal room should be a usable living space.");
    Assert(Math.Abs(planned.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))-108)<0.001,
        "The hall house should account for every part of the footprint.");
    var pantry=planned.Rooms.Single(r=>r.Type=="Pantry");
    Assert(planned.Doors.Count(d=>
        Enumerable.Range(0,pantry.Outline.Count).Any(i=>
            PolygonGeometry.ClosestPointOnSegment(d.Centre,
                pantry.Outline[i],pantry.Outline[(i+1)%pantry.Outline.Count])
                .DistanceTo(d.Centre)<1e-5))==1,
        "A pantry should have one intentional entrance.");
    Assert(planned.Doors.Count>=5);
    Assert(source.Rooms.Count==0,
        "A proposal must never rewrite its caller's building.");
});

Check("Hall house supports transposed long-axis orientation", () =>
{
    var source = new BuildingDocument
    {
        Perimeter=[new(0,0),new(9,0),new(9,12),new(0,12)]
    };
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        source,out var planned,out var reason),reason);
    Assert(planned.Rooms.Single(r=>r.Type=="Common room").Outline.Count>=4);
    Assert(planned.Rooms.Any(r=>r.Type=="Bedroom"));
    Assert(planned.Rooms.Single(r=>r.Type=="Hallway").Name=="Screens passage");
    Assert(BuildingStoreys.Validate(planned,out reason),reason);
});

Check("Hall-house generator respects stepped uploaded shape and both stair landings", () =>
{
    var source = new BuildingDocument
    {
        SchemaVersion=2,GridFeet=5,
        Perimeter=[
            new(7.62,0),new(12.192,0),new(12.192,3.048),
            new(16.764,3.048),new(16.764,0),new(21.336,0),
            new(21.336,3.048),new(21.336,10.668),new(7.62,10.668)
        ],
        UpperFloors=[new StoreyDefinition
        {
            Perimeter=[
                new(7.62,1.524),new(12.192,1.524),new(12.192,3.048),
                new(16.764,3.048),new(16.764,1.524),
                new(21.336,1.524),new(21.336,3.048),
                new(21.336,10.668),new(7.62,10.668)
            ]
        }],
        Staircases=[new StaircaseDefinition
        {
            LowerFloorIndex=0,Centre=new(14.478,6.646),
            Length=2.8,Width=0.9,LandingLength=1,RotationDegrees=90
        }]
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        source,out var hallHouse,out reason),reason);
    Assert(BuildingStoreys.Validate(hallHouse,out reason),reason);
    Assert(hallHouse.Rooms.Any(r=>r.Type=="Common room"));
    Assert(!hallHouse.Rooms.Any(r=>r.Type=="Great hall"),
        "Modest two-floor residence should have a common room rather than a great hall.");
    Assert(!hallHouse.Rooms.Any(r=>r.Type=="Storeroom"),
        "Do not generate two storerooms barricading the dining and bedchamber.");
    Assert(hallHouse.Staircases.Count==1 &&
        hallHouse.Staircases[0].Centre.Equals(source.Staircases[0].Centre));
    Assert(hallHouse.UpperFloors[0].Perimeter.SequenceEqual(source.UpperFloors[0].Perimeter));
    Assert(source.Rooms.Count==0);
});

Check("Residence generation exposes historical style with safe legacy opt-out", () =>
{
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Windows=true,Stairs=false,HallHouseLayout=true
    };
    var footprint = BuildingGenerator.MedievalFootprint(
        BuildingUse.Residence,1,0,new Random(12));
    var blankHistoric = new BuildingDocument { Perimeter=footprint };
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        blankHistoric,out _,out var layoutReason),
        "Seed 12 hall-house template: " + layoutReason +
        "; footprint=" + string.Join(";",footprint.Select(p=>$"({p.X},{p.Y})")));
    Assert(BuildingGenerator.TrySuggestWholeBuilding(
        1,12,options,out var historic,out var reason),reason);
    Assert(historic.Building.Rooms.Any(r=>r.Type=="Common room") &&
        historic.Building.Rooms.Any(r=>r.Type=="Bedroom"),
        "The enabled historical generator should create a common room AND a bedroom.");
    Assert(historic.Building.Rooms.Any(r=>r.Name=="Screens passage"));
    Assert(historic.Description.Contains("Medieval residence layout."));
    Assert(BuildingStoreys.Validate(historic.Building,out reason),reason);

    options.HallHouseLayout=false;
    bool legacyOk=false;
    foreach (int oldSeed in new[]{12,13,14,15,16,17,18,19,20})
    {
        if (!BuildingGenerator.TrySuggestWholeBuilding(
            1,oldSeed,options,out var legacy,out reason))
            continue; // Some random corridor plans fail door-access quality checks.
        Assert(!legacy.Building.Rooms.Any(r=>r.Type=="Great hall"),
            "Unchecking the historical generator must retain the legacy plan.");
        legacyOk=true;
        break;
    }
    Assert(legacyOk,"Legacy layout mode should still work for a usable seed.");
});

Check("Two-storey residence offers historical hall-house layout with safe stair access", () =>
{
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Windows=true,Stairs=true,HallHouseLayout=true
    };
    bool historic=false;
    var attempted=new List<string>();
    foreach(int seed in new[]{12,17,23,37,42})
    {
        if (!BuildingGenerator.TrySuggestWholeBuilding(
            2,seed,options,out var proposed,out var reason,5))
        {
            attempted.Add(seed + ": " + reason);
            continue;
        }
        var generated=proposed.Building;
        if (!generated.Rooms.Any(r=>r.Type=="Common room"))
        {
            attempted.Add(seed+": "+proposed.Description);
            continue;
        }
        historic=true;
        Assert(generated.Rooms.Single(r=>r.Type=="Hallway").Name==
            "Screens passage","Do not retain a full-length central corridor on the ground floor.");
        Assert(generated.Rooms.Any(r=>r.Type is "Pantry" or "Buttery"),
            "The service end should contain an enclosed pantry or buttery.");
        Assert(generated.Staircases.Count==1);
        Assert(BuildingStoreys.Validate(generated,out reason),reason);
        Assert(BuildingConnectivity.RoomsIsolatedByServiceSpaces(generated,0).Count==0);
        Assert(generated.UpperFloors.Count==1);
        break;
    }
    Assert(historic, "No two-storey historic plan was returned: "+
        string.Join(" | ",attempted));
});

Check("Hall-house option carries through generation option cloning", () =>
{
    var setting=new GenerationOptions
    {HallHouseLayout=true,Purpose=BuildingUse.Residence,Stairs=true};
    var clone=setting.Copy();
    Assert(clone.HallHouseLayout && clone.Stairs &&
        clone.Purpose==BuildingUse.Residence);
    clone.HallHouseLayout=false;
    Assert(setting.HallHouseLayout);
});

Check("Regeneration seeds produce different historical room geometries and room sets", () =>
{
    var blank = new BuildingDocument
    {
        Perimeter=[new(0,0),new(12,0),new(12,9),new(0,9)]
    };
    var results = new List<BuildingDocument>();
    var errors = new List<string>();
    for(int seed=101;seed<=114;seed++)
    {
        if (!MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
            blank,seed,out var design,out var why))
        {
            errors.Add($"{seed}: {why}");
            continue;
        }
        Assert(BuildingStoreys.Validate(design,out why),why);
        if (!results.Any(previous=>
            BuildingGenerator.SameRoomLayout(previous,design,[0])))
            results.Add(design);
    }
    Assert(results.Count>=5,
        $"Expected at least five different room configurations, found {results.Count}. "+
        string.Join(" | ",errors));
    Assert(results.Select(plan=>string.Join(",",plan.Rooms
        .Select(r=>r.Type).OrderBy(t=>t))).Distinct().Count()>=2,
        "Different seeds should alter room roles or whether private rooms exist, "+
        "not just shift the exact same bedroom walls.");
    Assert(blank.Rooms.Count==0);
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        blank,101,out var reproducibleA,out var reproducibleReason),
        reproducibleReason);
    Assert(MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
        blank,101,out var reproducibleB,out reproducibleReason),
        reproducibleReason);
    Assert(BuildingGenerator.SameRoomLayout(reproducibleA,reproducibleB,[0]),
        "The same seed and perimeter must reproduce identical architecture.");
    var changed=BuildingStoreys.Copy(results[0]);
    changed.Rooms[0].Id=Guid.NewGuid();
    changed.Doors.Clear();
    Assert(BuildingGenerator.SameRoomLayout(results[0],changed,[0]),
        "New GUIDs and doors are not a meaningful regeneration.");
    changed.Rooms[0].Type="Workshop";
    Assert(!BuildingGenerator.SameRoomLayout(results[0],changed,[0]),
        "Different room roles must count as a real change.");
});

Check("Regenerate accepted medieval house rerolls room geometry across seeds", () =>
{
    var options = new GenerationOptions
    {
        Purpose=BuildingUse.Residence,HallHouseLayout=true,Rooms=true,
        Corridors=true,Doors=true,Windows=true
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(1,101,options,
        out var first,out var reason),reason);
    var accepted = first.Building;
    var distinct = new List<BuildingDocument>();
    var failures = new List<string>();
    foreach(int seed in Enumerable.Range(120,14))
    {
        if(!BuildingGenerator.TryRegenerate(accepted,[0],seed,options,
            out var replacement,out var failed))
        {
            failures.Add($"{seed}: {failed}");
            continue;
        }
        Assert(BuildingStoreys.Validate(replacement.Building,out failed),failed);
        Assert(replacement.Building.Perimeter.SequenceEqual(accepted.Perimeter));
        if(!BuildingGenerator.SameRoomLayout(accepted,replacement.Building,[0]) &&
            !distinct.Any(r=>BuildingGenerator.SameRoomLayout(
                r,replacement.Building,[0])))
            distinct.Add(replacement.Building);
    }
    Assert(distinct.Count>=3,
        $"A user should see at least three different rerolls; found {distinct.Count}. "+
        string.Join(" | ",failures));
    Assert(accepted.Rooms.Any(r=>r.Type=="Common room") &&
        accepted.Rooms.Any(r=>r.Type=="Bedroom"),
        "No accepted cottage rooms may be mutated during regeneration.");
});

Check("Small one-storey houses always include a usable private bedchamber", () =>
{
    var opts=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,HallHouseLayout=true,
        Rooms=true,Corridors=true,Doors=true,Windows=true,Stairs=false
    };
    var failures=new List<string>();
    foreach(var (width,depth) in new[] {(8.0,6.0),(9.0,7.0),(12.0,9.0)})
    {
        var blank=new BuildingDocument
        {
            Perimeter=[new(0,0),new(width,0),
                new(width,depth),new(0,depth)]
        };
        int successes=0;
        int layoutsWithTwoBedrooms=0;
        for(int seed=1;seed<=15;seed++)
        {
            if(!MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
                blank,seed,out var generated,out var reason))
            {
                failures.Add($"{width}x{depth} seed {seed}: {reason}");
                continue;
            }
            successes++;
            Assert(BuildingStoreys.Validate(generated,out reason),reason);
            var sleeping=generated.Rooms.Where(r=>r.Type=="Bedroom").ToArray();
            Assert(sleeping.Length>=1 && sleeping.Length<=2,
                $"No bedroom in {width}x{depth} residence seed {seed}.");
            if (sleeping.Length == 2) layoutsWithTwoBedrooms++;
            Assert(generated.Rooms.Any(r=>r.Type=="Common room") &&
                !generated.Rooms.Any(r=>r.Type=="Great hall"),
                "A modest house should contain a common living space, not a great hall.");
            Assert(!generated.Rooms.Any(r=>r.Type=="Parlour"),
                "Bedrooms take precedence over a private parlour on one storey.");
            Assert(sleeping.All(r=>PolygonGeometry.Area(r.Outline)>=8),
                "Bedrooms must have enough floor space.");
            var common=generated.Rooms.Single(r=>r.Type=="Common room");
            bool BordersDoor(RoomDefinition r)=>generated.Doors.Any(d=>
                Enumerable.Range(0,r.Outline.Count).Any(i=>
                    PolygonGeometry.ClosestPointOnSegment(d.Centre,
                        r.Outline[i],r.Outline[(i+1)%r.Outline.Count])
                        .DistanceTo(d.Centre)<1e-5) &&
                Enumerable.Range(0,common.Outline.Count).Any(i=>
                    PolygonGeometry.ClosestPointOnSegment(d.Centre,
                        common.Outline[i],common.Outline[(i+1)%common.Outline.Count])
                        .DistanceTo(d.Centre)<1e-5));
            Assert(sleeping.All(BordersDoor),
                "Every bedroom must be accessible without crossing a pantry or kitchen.");
        }
        Assert(successes>=3,
            $"Too few valid bedroom-containing plans for {width}x{depth}: "+
            string.Join(" | ",failures));
        if (width >= 12)
            Assert(layoutsWithTwoBedrooms > 0,
                "A sufficiently large one-storey residence should sometimes "+
                "contain two separately accessible bedrooms.");
    }
});

Check("Large manor footprint retains a real great hall distinct from common cottage room", () =>
{
    var source=new BuildingDocument
    {
        Perimeter=[new(0,0),new(19,0),new(19,12),new(0,12)]
    };
    bool found=false;
    for(int seed=1;seed<=20;seed++)
    {
        if(!MedievalHallHousePlanner.TryReplaceGeneratedGroundFloor(
            source,seed,out var proposal,out _))
            continue;
        found=true;
        Assert(proposal.Rooms.Any(r=>r.Type=="Great hall") &&
            !proposal.Rooms.Any(r=>r.Type=="Common room"),
            "Great hall should be reserved for large manor-style residences.");
        Assert(proposal.Rooms.Any(r=>r.Type=="Bedroom"),
            "Even a large one-storey residence requires sleeping space.");
        Assert(BuildingStoreys.Validate(proposal,out var reason),reason);
        break;
    }
    Assert(found,"A manor-scale building should have a valid large hall.");
});

Check("Single-storey regeneration retains bedrooms for accepted cottage", () =>
{
    var opts=new GenerationOptions
    {
        Rooms=true,Corridors=true,Doors=true,Windows=true,
        Purpose=BuildingUse.Residence,HallHouseLayout=true
    };
    Assert(BuildingGenerator.TrySuggestWholeBuilding(
        1,101,opts,out var original,out var reason),reason);
    Assert(original.Building.Rooms.Any(r=>r.Type=="Bedroom"));
    Assert(BuildingGenerator.TryRegenerate(
        original.Building,[0],120,opts,out var another,out reason),reason);
    Assert(another.Building.Rooms.Any(r=>r.Type=="Bedroom"));
    Assert(another.Building.Rooms.Any(r=>r.Type=="Common room"));
    Assert(!BuildingGenerator.SameRoomLayout(
        original.Building,another.Building,[0]));
    Assert(BuildingStoreys.Validate(another.Building,out reason),reason);
});

Check("Compact RPG house generates both floors, rear stairs and separate bedrooms", () =>
{
    var blank=new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=[new(0,0),new(9,0),new(9,10),new(0,10)],
        UpperFloors=[new StoreyDefinition
        {
            Perimeter=[new(0,0),new(9,0),new(9,10),new(0,10)]
        }]
    };
    Assert(CompactRpgHousePlanner.TryGenerate(blank,77,
        out var built,out var reason),reason);
    Assert(BuildingStoreys.Validate(built,out reason),reason);
    Assert(built.Staircases.Count==1,"Two levels must be linked by stairs.");
    var lower=BuildingStoreys.GetFloor(built,0);
    var upper=BuildingStoreys.GetFloor(built,1);
    Assert(lower.Rooms.Count(r=>r.Type=="Hallway")==1);
    Assert(lower.Rooms.Any(r=>r.Name=="Entrance lobby"));
    Assert(lower.Rooms.Any(r=>r.Type=="Living room"));
    Assert(lower.Rooms.Any(r=>r.Type=="Dining room"));
    Assert(lower.Rooms.Any(r=>r.Type=="Kitchen"));
    Assert(lower.Rooms.Any(r=>r.Type=="Guest room"));
    Assert(lower.Rooms.Any(r=>r.Type=="Study"));
    Assert(!lower.Rooms.Any(r=>r.Type=="Storeroom"),
        "Do not obstruct living space with badly positioned storerooms.");
    Assert(upper.Rooms.Count(r=>r.Type=="Bedroom")==3);
    Assert(upper.Rooms.Any(r=>r.Name=="Master Bedroom"));
    Assert(upper.Rooms.Any(r=>r.Name=="Upper landing"));
    Assert(upper.Rooms.Any(r=>r.Type=="Sitting room"));
    Assert(upper.Rooms.Any(r=>r.Type=="Bathroom"));
    double groundArea=lower.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline));
    double upperArea=upper.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline));
    Assert(Math.Abs(groundArea-90)<0.001);
    Assert(Math.Abs(upperArea-90)<0.001);
    Assert(PolygonGeometry.Area(lower.Rooms.Single(r=>r.Type=="Hallway").Outline)<7,
        "The ground floor should have only a small foyer, not a central corridor.");

    bool OnRoom(RoomDefinition room,Point2 p)=>Enumerable.Range(0,
        room.Outline.Count).Any(i=>
        PolygonGeometry.ClosestPointOnSegment(p,room.Outline[i],
            room.Outline[(i+1)%room.Outline.Count]).DistanceTo(p)<1e-5);
    bool HasDoor(BuildingDocument floor,RoomDefinition a,RoomDefinition b)=>
        floor.Doors.Any(d=>OnRoom(a,d.Centre)&&OnRoom(b,d.Centre));
    var entry=lower.Rooms.Single(r=>r.Name=="Entrance lobby");
    var living=lower.Rooms.Single(r=>r.Name=="Living room");
    Assert(HasDoor(lower,entry,living));
    Assert(lower.Rooms.Where(r=>r.Type is "Guest room" or "Study")
        .All(r=>HasDoor(lower,entry,r)));
    var landing=upper.Rooms.Single(r=>r.Name=="Upper landing");
    Assert(upper.Rooms.Where(r=>r.Type is "Bedroom" or "Bathroom")
        .All(r=>HasDoor(upper,landing,r)));
    Assert(HasDoor(upper,landing,
        upper.Rooms.Single(r=>r.Type=="Sitting room")));
    var stair=built.Staircases.Single();
    Assert(FloorGeometry.IsWithinPolygon(
        BuildingStoreys.StairFootprint(stair),living.Outline));
    Assert(FloorGeometry.IsWithinPolygon(
        BuildingStoreys.StairFootprint(stair),
        upper.Rooms.Single(r=>r.Type=="Sitting room").Outline));
    Assert(blank.Rooms.Count==0 && blank.Staircases.Count==0);
});

Check("Compact RPG whole-building option creates two-storey battle-map home", () =>
{
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Windows=true,Stairs=true,
        HallHouseLayout=true,CompactRpgHouseLayout=true
    };
    int compactCount=0;
    var details=new List<string>();
    var layouts=new List<BuildingDocument>();
    foreach(int seed in new[]{11,17,33,46,61,77,91})
    {
        if (!BuildingGenerator.TrySuggestWholeBuilding(2,seed,options,
            out var proposal,out var reason,5))
        {
            details.Add(seed+": "+reason);
            continue;
        }
        if (!proposal.Description.Contains("Compact two-storey RPG house"))
        {
            details.Add(seed+": "+proposal.Description);
            continue;
        }
        var b=proposal.Building;
        compactCount++;
        Assert(BuildingStoreys.Validate(b,out reason),reason);
        Assert(b.UpperFloors.Count==1 && b.Staircases.Count==1);
        Assert(b.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(!b.Rooms.Any(r=>r.Type=="Great hall" || r.Type=="Storeroom"));
        Assert(b.Rooms.Any(r=>r.Type=="Living room"));
        if(!layouts.Any(p=>BuildingGenerator.SameRoomLayout(p,b,[0,1])))
            layouts.Add(b);
    }
    Assert(compactCount>=3,
        "Expected at least three compact floorplans, got "+compactCount+
        ": "+string.Join(" | ",details));
    Assert(layouts.Count>=2,
        "Compact RPG generation should vary the room geometry across seeds.");
});

Check("Regeneration preserves compact RPG stairs and changes selected room geometry", () =>
{
    var source=new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=[new(0,0),new(9,0),new(9,10),new(0,10)],
        UpperFloors=[new StoreyDefinition
        { Perimeter=[new(0,0),new(9,0),new(9,10),new(0,10)] }]
    };
    Assert(CompactRpgHousePlanner.TryGenerate(source,77,
        out var accepted,out var reason),reason);
    var options=new GenerationOptions
    {
        Rooms=true,Corridors=true,Doors=true,Stairs=true,
        CompactRpgHouseLayout=true,Purpose=BuildingUse.Residence
    };
    int candidates=0;
    var details=new List<string>();
    foreach(int seed in Enumerable.Range(80,12))
    {
        if (!BuildingGenerator.TryRegenerate(accepted,[0,1],seed,options,
            out var next,out var failure))
        {
            details.Add(seed+": "+failure);
            continue;
        }
        if (!next.Description.Contains("Compact two-storey RPG house"))
        {
            details.Add(seed+": "+next.Description);
            continue;
        }
        candidates++;
        Assert(BuildingStoreys.Validate(next.Building,out failure),failure);
        Assert(next.Building.Staircases.Single().Id==accepted.Staircases.Single().Id,
            "Regeneration must preserve an accepted flight.");
        Assert(next.Building.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
    }
    Assert(candidates>0,"Could not regenerate a compact plan around " +
        "the existing staircase: "+string.Join(" | ",details));
    Assert(accepted.Rooms.Count==6);
});

Check("Selecting a door works on its visible swing leaf far from the centre", () =>
{
    var wall = new WallSegment(new(5,0), new(5,8), false);
    var door = new DoorDefinition { Centre = new(5,4), Width=0.9 };
    var walls = new List<WallSegment> { wall };
    // Drawn door: hinge=(5,3.55), open leaf swings left to (4.1,3.55).
    // This point lies inside the adjoining room and 29px from the
    // centre at 40px/m, beyond the old 11px centre-only hit area.
    var onLeaf = new Point2(4.28,3.55);
    Assert(onLeaf.DistanceTo(door.Centre)*40>12,
        "The regression cursor must miss the old centre hit target.");
    Assert(DoorHitTesting.FindDoorAt([door],walls,onLeaf,40)?.Id==door.Id,
        "Clicking the drawn door leaf should select the door, not the room.");
    Assert(DoorHitTesting.FindDoorAt([door],walls,new(5,4.2),40)?.Id==door.Id,
        "Clicking within the door gap should select the door.");
    Assert(DoorHitTesting.FindDoorAt([door],walls,new(5,4),40)?.Id==door.Id,
        "Clicking the centre marker should still select the door.");
    Assert(DoorHitTesting.FindDoorAt([door],walls,new(3,5),40) is null,
        "Clicking empty room space should not pick a distant door.");
    Assert(DoorHitTesting.FindDoorAt([door],walls,new(5.4,3.55),40) is null,
        "The door leaf only swings on one side; don't capture the opposite room.");
});

Check("Door leaf selection uses stable pixel tolerance at different zoom levels", () =>
{
    var wall = new WallSegment(new(5,0),new(5,8),false);
    var door = new DoorDefinition { Centre=new(5,4),Width=0.9 };
    foreach (double zoom in new[] { 20.0, 40.0, 80.0, 150.0 })
    {
        var nearLeaf = new Point2(4.28,3.55 + 8.0/zoom);
        Assert(DoorHitTesting.FindDoorAt([door],[wall],nearLeaf,zoom)?.Id==door.Id,
            $"Door should remain selectable 8px from its leaf at zoom {zoom}.");
        var beyond = new Point2(4.28,3.55 + 25.0/zoom);
        if (zoom >= 40)
            Assert(DoorHitTesting.FindDoorAt([door],[wall],beyond,zoom) is null,
                $"Door hit target should not swallow broad room space at zoom {zoom}.");
    }
});

Check("Door selection chooses nearest door and excludes stale geometry", () =>
{
    var wall = new WallSegment(new(5,0),new(5,8),false);
    var first = new DoorDefinition {Centre=new(5,2),Width=0.9};
    var second = new DoorDefinition {Centre=new(5,6),Width=0.9};
    var walls = new List<WallSegment>{wall};
    Assert(DoorHitTesting.FindDoorAt([first,second],walls,
        new Point2(4.3,5.55),40,first.Id)?.Id==second.Id,
        "Previously selected farther door must not steal the next door click.");
    var stale = new DoorDefinition {Centre=new(25,25),Width=0.9};
    Assert(DoorHitTesting.FindDoorAt([stale],walls,
        new Point2(25,25),40) is null,
        "Only doors supported by a real wall can be hit.");
    Assert(DoorHitTesting.FindDoorAt([first],walls,
        new Point2(5,2),0) is null,
        "Invalid zoom factor should be handled safely.");
});

Check("Door swing hit testing follows diagonal door-support walls", () =>
{
    var wall = new WallSegment(new(1,1),new(5,5),false);
    var door = new DoorDefinition {Centre=new(3,3),Width=1.0};
    double dx=1/Math.Sqrt(2.0), dy=dx;
    var hinge = new Point2(3-dx*0.5,3-dy*0.5);
    var end = new Point2(hinge.X-dy,hinge.Y+dx);
    var onLeaf = new Point2((hinge.X+end.X)/2,
        (hinge.Y+end.Y)/2);
    Assert(DoorHitTesting.FindDoorAt([door],[wall],onLeaf,90)?.Id==door.Id,
        "A diagonal door's open leaf should be selectable like a straight one.");
});

Check("example5 oversized L-shaped stair wing becomes compact circulation on both floors", () =>
{
    // Reproduces the room/stair/door geometry in example5.json. The
    // exterior is 11m x 10m rectangular, but each L-shaped hallway
    // occupies 42 of its 110 square metres. The stair flight lives in
    // the side wing and cannot fit the earlier straight-spine repair.
    List<Point2> Box(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    List<Point2> BadHall() =>
        [new(0,4),new(6,4),new(6,0),new(11,0),
         new(11,6),new(0,6)];
    var plan=new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=Box(0,0,11,10),
        Rooms=
        [
            new RoomDefinition {Name="Hallway 1",Type="Hallway",Outline=BadHall()},
            new RoomDefinition {Name="Kitchen",Type="Kitchen",Outline=Box(0,0,2.5,4)},
            new RoomDefinition {Name="Dining room",Type="Dining room",Outline=Box(2.5,0,6,4)},
            new RoomDefinition {Name="Living room",Type="Living room",Outline=Box(0,6,7,10)},
            new RoomDefinition {Name="Bedroom",Type="Bedroom",Outline=Box(7,6,11,10)}
        ],
        Doors=
        [
            new DoorDefinition {Centre=new(11,5),Width=0.9},
            new DoorDefinition {Centre=new(1.25,4),Width=0.9},
            new DoorDefinition {Centre=new(4.25,4),Width=0.9},
            new DoorDefinition {Centre=new(3.5,6),Width=0.9},
            new DoorDefinition {Centre=new(9,6),Width=0.9},
            new DoorDefinition {Centre=new(2.5,2),Width=0.8}
        ],
        Windows=
        [
            new WindowDefinition {Centre=new(4,0),Width=1.2},
            new WindowDefinition {Centre=new(3.5,10),Width=1.2},
            new WindowDefinition {Centre=new(11,8),Width=1.2}
        ],
        UpperFloors=
        [
            new StoreyDefinition
            {
                Name="First Floor",Perimeter=Box(0,0,11,10),
                Rooms=
                [
                    new RoomDefinition {Name="Hallway 1",Type="Hallway",Outline=BadHall()},
                    new RoomDefinition {Name="Bedroom",Type="Bedroom",Outline=Box(0,0,2.5,4)},
                    new RoomDefinition {Name="Study",Type="Study",Outline=Box(2.5,0,6,4)},
                    new RoomDefinition {Name="Bedroom 2",Type="Bedroom",Outline=Box(0,6,7,10)},
                    new RoomDefinition {Name="Bedroom 3",Type="Bedroom",Outline=Box(7,6,11,10)}
                ],
                Doors=
                [
                    new DoorDefinition {Centre=new(1.25,4),Width=0.9},
                    new DoorDefinition {Centre=new(4.25,4),Width=0.9},
                    new DoorDefinition {Centre=new(3.5,6),Width=0.9},
                    new DoorDefinition {Centre=new(9,6),Width=0.9}
                ],
                Windows=
                [
                    new WindowDefinition {Centre=new(1.25,0),Width=1.2},
                    new WindowDefinition {Centre=new(4.25,0),Width=1.2},
                    new WindowDefinition {Centre=new(3.5,10),Width=1.2},
                    new WindowDefinition {Centre=new(11,8),Width=1.2}
                ]
            }
        ],
        Staircases=
        [
            new StaircaseDefinition
            {
                LowerFloorIndex=0,Centre=new(8.5,0.7),Length=2.8,
                Width=1,RotationDegrees=90,LandingLength=1
            }
        ]
    };
    Assert(BuildingStoreys.Validate(plan,out var reason),reason);
    Assert(Math.Abs(PolygonGeometry.Area(plan.Rooms[0].Outline)-42)<1e-6);
    int fixedLower=RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
        plan,0,Array.Empty<Guid>(),out var lower);
    Assert(fixedLower==1,"Ground-floor side stair wing should be reclaimed.");
    int fixedUpper=RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
        lower,1,Array.Empty<Guid>(),out var result);
    Assert(fixedUpper==1,"Upper side stair wing should be reclaimed.");
    Assert(BuildingStoreys.Validate(result,out reason),reason);
    foreach(var index in new[]{0,1})
    {
        var floor=BuildingStoreys.GetFloor(result,index);
        var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
        Assert(PolygonGeometry.Area(hall.Outline)<34,
            "The 42m² L hallway must shrink to useful circulation.");
        Assert(floor.Rooms.Count==6,"Reclaim the spare space as a useful room.");
        var added=floor.Rooms.Single(r=>r.Type ==
            (index==0?"Utility":"Bathroom"));
        Assert(PolygonGeometry.Area(added.Outline)>=7.5);
        Assert(Math.Abs(floor.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))-110)<0.01,
            "Every square metre of the 11 x 10m footprint must be accounted for.");
        foreach(var stair in result.Staircases.Where(s=>
            s.LowerFloorIndex==index || s.LowerFloorIndex+1==index))
        {
            Assert(FloorGeometry.IsWithinPolygon(
                BuildingStoreys.StairFootprint(stair),hall.Outline));
            foreach(var landing in BuildingStoreys.StairLandings(stair))
                Assert(FloorGeometry.IsWithinPolygon(landing,hall.Outline),
                    "Both stair landings must remain clear.");
        }
    }
    BuildingConnectivity.AddDoors(result,0,out var groundDoor);
    BuildingConnectivity.AddDoors(groundDoor,1,out var accessible);
    Assert(BuildingStoreys.Validate(accessible,out reason),reason);
    for(int index=0;index<=1;index++)
    {
        var floor=BuildingStoreys.GetFloor(accessible,index);
        var added=floor.Rooms.Single(r=>r.Type==
            (index==0?"Utility":"Bathroom"));
        var hallway=floor.Rooms.Single(r=>r.Type=="Hallway");
        bool On(Point2 point,IReadOnlyList<Point2> poly)=>
            Enumerable.Range(0,poly.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(point,
                    poly[i],poly[(i+1)%poly.Count]).DistanceTo(point)<1e-5);
        Assert(floor.Doors.Any(d=>On(d.Centre,added.Outline) &&
            On(d.Centre,hallway.Outline)),
            "The reclaimed room needs its own hallway access.");
        Assert(floor.Windows.Count==(index==0?3:4),
            "Existing exterior windows must survive reclamation.");
    }
    Assert(accessible.Doors.Any(d=>d.Centre.Equals(new Point2(11,5))),
        "Original street entry must stay where it was.");
    Assert(plan.Rooms.Count==5 &&
        Math.Abs(PolygonGeometry.Area(plan.Rooms[0].Outline)-42)<1e-6,
        "Original example5 must not be edited in place.");
    Assert(plan.Staircases.Single().Id==accessible.Staircases.Single().Id,
        "The original flight ID and position must be preserved.");

    int protectedChange=RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
        plan,0,[plan.Rooms[0].Id],out var protectedResult);
    Assert(protectedChange==0 && protectedResult.Rooms.Count==5,
        "Never repartition a manually authored hallway.");
    int again=RoomLayoutPlanner.ReclaimOversizedGeneratedStairWing(
        result,0,Array.Empty<Guid>(),out var rerun);
    Assert(again==0 && rerun.Rooms.Count==6,
        "Once corrected, do not repeatedly subdivide the stair wing.");
});

Check("example6(1) stepped two-storey footprint reclaims 77-percent L-shaped hallways", () =>
{
    // Faithfully reconstructed geometry from the accepted example6(1).json.
    // Both floors had 68.125m² halls within an 88.75m² stepped perimeter;
    // the kitchen/bedroom was the only other room on each level.
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    List<Point2> Shape() =>
        [new(0,0),new(8.5,0),new(8.5,5.5),
         new(10.5,5.5),new(10.5,9.5),new(0,9.5)];
    List<Point2> Hall() =>
        [new(0,0),new(5,0),new(5,5.75),
         new(10.5,5.75),new(10.5,9.5),new(0,9.5)];
    var original = new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=Shape(),
        Rooms=
        [
            new RoomDefinition
                {Name="Hallway 1",Type="Hallway",Outline=Hall()},
            new RoomDefinition
                {Name="Kitchen",Type="Kitchen",Outline=R(5,0,8.5,5.75)}
        ],
        Doors=
        [
            new DoorDefinition{Centre=new(10.5,6.75),Width=0.9},
            new DoorDefinition{Centre=new(6.75,5.75),Width=0.9}
        ],
        UpperFloors=
        [
            new StoreyDefinition
            {
                Name="First Floor",Perimeter=Shape(),
                Rooms=
                [
                    new RoomDefinition
                        {Name="Hallway 1",Type="Hallway",Outline=Hall()},
                    new RoomDefinition
                        {Name="Bedroom",Type="Bedroom",Outline=R(5,0,8.5,5.75)}
                ],
                Doors=
                [
                    new DoorDefinition{Centre=new(6.75,5.75),Width=0.9}
                ],
                Windows=
                [
                    new WindowDefinition{Centre=new(6.75,0),Width=1.2}
                ]
            }
        ],
        Staircases=
        [
            new StaircaseDefinition
            {
                LowerFloorIndex=0,Centre=new(2.5,0.7),
                Length=2.8,Width=1,RotationDegrees=90,LandingLength=1
            }
        ]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    Assert(Math.Abs(PolygonGeometry.Area(original.Perimeter)-88.75)<0.001);
    Assert(Math.Abs(PolygonGeometry.Area(original.Rooms[0].Outline)-68.125)<0.001);
    var protectedHallId=original.Rooms[0].Id;
    var initialStairId=original.Staircases[0].Id;

    int groundAdded=IrregularHallRepair.Reclaim(
        original,0,Array.Empty<Guid>(),BuildingUse.Residence,
        out var groundRepaired);
    Assert(groundAdded==3,
        "Should recover THREE domestic rooms from the oversized ground hallway.");
    int firstAdded=IrregularHallRepair.Reclaim(
        groundRepaired,1,Array.Empty<Guid>(),BuildingUse.Residence,
        out var repaired);
    Assert(firstAdded==3,
        "Should recover upstairs sleeping/private rooms, not keep a 77% hallway.");
    Assert(BuildingStoreys.Validate(repaired,out reason),reason);
    Assert(repaired.Staircases.Single().Id==initialStairId);
    for(int index=0;index<=1;index++)
    {
        var floor=BuildingStoreys.GetFloor(repaired,index);
        Assert(floor.Rooms.Count==5,
            "Each floor should now have four functional rooms and circulation.");
        var h=floor.Rooms.Single(r=>r.Type=="Hallway");
        Assert(PolygonGeometry.Area(h.Outline)<43,
            "Circulation must be less than half the usable floor area.");
        Assert(floor.Rooms.Count(r=>r.Type=="Kitchen")== (index==0?1:0));
        Assert(floor.Rooms.Any(r=>r.Type=="Living room"||r.Type=="Bedroom"));
        foreach(var flight in repaired.Staircases)
        {
            Assert(FloorGeometry.IsWithinPolygon(
                BuildingStoreys.StairFootprint(flight),h.Outline));
            Assert(FloorGeometry.IsWithinPolygon(
                BuildingStoreys.StairOpeningFootprint(flight),h.Outline));
            Assert(BuildingStoreys.StairLandings(flight).All(l=>
                FloorGeometry.IsWithinPolygon(l,h.Outline)));
        }
        Assert(Math.Abs(floor.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))-88.25)<0.02,
            "The original narrow 0.5m² gap must not grow or lose covered area.");
    }
    BuildingConnectivity.AddDoors(repaired,0,out var doorsGround);
    BuildingConnectivity.AddDoors(doorsGround,1,out var allDoors);
    Assert(BuildingStoreys.Validate(allDoors,out reason),reason);
    for(int index=0;index<=1;index++)
    {
        var floor=BuildingStoreys.GetFloor(allDoors,index);
        var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
        bool On(Point2 point,IReadOnlyList<Point2> ring)=>
            Enumerable.Range(0,ring.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(point,
                    ring[i],ring[(i+1)%ring.Count]).DistanceTo(point)<1e-5);
        foreach(var room in floor.Rooms.Where(r=>r.Type!="Hallway"))
            Assert(floor.Doors.Any(d=>On(d.Centre,hall.Outline)&&
                On(d.Centre,room.Outline)),
                $"Every {room.Type} needs its own access from the hall.");
    }
    Assert(allDoors.Doors.Any(d=>d.Centre.Equals(new Point2(10.5,6.75))),
        "Keep the original entrance at the end of the L wing.");
    Assert(original.Rooms.Count==2 &&
        Math.Abs(PolygonGeometry.Area(original.Rooms[0].Outline)-68.125)<0.001,
        "The original saved plan must remain untouched.");
    Assert(IrregularHallRepair.Reclaim(original,0,[protectedHallId],
        BuildingUse.Residence,out _)==0,
        "Never change a manually drawn L-shaped hallway.");
    Assert(IrregularHallRepair.Reclaim(repaired,0,Array.Empty<Guid>(),
        BuildingUse.Residence,out _)==0,
        "Do not repartition a hallway more than once.");
});

Check("Regenerate stepped example6 house without another 77-percent hallway", () =>
{
    List<Point2> outer =
        [new(0,0),new(8.5,0),new(8.5,5.5),
         new(10.5,5.5),new(10.5,9.5),new(0,9.5)];
    var source = new BuildingDocument
    {
        SchemaVersion=2,Perimeter=outer.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=outer.ToList()}],
        Staircases=[new StaircaseDefinition
        {
            LowerFloorIndex=0,Centre=new(2.5,0.7),
            Length=2.8,Width=1,RotationDegrees=90,LandingLength=1
        }]
    };
    var options = new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Stairs=true,HallHouseLayout=true,
        CompactRpgHouseLayout=true,Windows=false,Fireplaces=false
    };
    Assert(BuildingStoreys.Validate(source,out var reason),reason);
    Assert(SteppedRpgHousePlanner.TryGenerate(source,7,
        out var architected,out reason),
        "Direct two-floor stepped RPG layout: "+reason);
    Assert(BuildingStoreys.Validate(architected,out reason),reason);
    Assert(architected.Rooms.Count==5 &&
        architected.UpperFloors[0].Rooms.Count==5);
    Assert(architected.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
    Assert(architected.Rooms.Any(r=>r.Type=="Living room") &&
        architected.Rooms.Any(r=>r.Type=="Kitchen"));
    Assert(architected.Staircases.Single().Id==source.Staircases.Single().Id);
    Assert(architected.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))>=88.74 &&
        architected.UpperFloors[0].Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))>=88.74,
        "Direct architecture must allocate the entire stepped perimeter.");
    var withoutStairs=BuildingStoreys.Copy(source);
    withoutStairs.Staircases.Clear();
    Assert(SteppedRpgHousePlanner.TryGenerate(withoutStairs,7,
        out var newlyStaired,out reason),
        "Unconstrained stepped house should generate its own stair: "+reason);
    Assert(newlyStaired.Staircases.Count==1);
    Assert(BuildingStoreys.Validate(newlyStaired,out reason),reason);
    Assert(source.Rooms.Count==0 && source.Staircases.Count==1,
        "The source blank building and existing staircase must remain unchanged.");
    var attempts=new List<string>();
    int valid=0;
    foreach(int seed in new[]{7,12,17,23,37,43,61,77,101})
    {
        if (!BuildingGenerator.TryRegenerate(source,[0,1],seed,options,
            out var generated,out reason))
        {
            attempts.Add(seed+": "+reason);
            continue;
        }
        valid++;
        Assert(BuildingStoreys.Validate(generated.Building,out reason),reason);
        for(int index=0;index<=1;index++)
        {
            var floor=BuildingStoreys.GetFloor(generated.Building,index);
            double ratio=floor.Rooms.Where(r=>r.Type=="Hallway")
                .Sum(r=>PolygonGeometry.Area(r.Outline))/
                PolygonGeometry.Area(floor.Perimeter);
            Assert(ratio<0.55 || floor.Rooms.Count>2,
                "The full regenerate pipeline may not present another "+
                "hall-plus-one-room floor as a finished house.");
        }
        Assert(generated.Building.Staircases.Single().Id==source.Staircases.Single().Id,
            "Regeneration preserves the original staircase.");
    }
    Assert(valid>0,"No valid regeneration could be found across seeds: "+
        string.Join(" | ",attempts));
});

Check("Stepped RPG house supports rotations and mirrored L-wing orientation", () =>
{
    List<Point2> canonical =
        [new(0,0),new(8.5,0),new(8.5,5.5),
         new(10.5,5.5),new(10.5,9.5),new(0,9.5)];
    foreach(int angle in new[]{0,1,2,3})
    foreach(bool reflected in new[]{false,true})
    {
        Point2 Transform(Point2 point)
        {
            double x=reflected?-point.X:point.X;
            double y=point.Y;
            return angle switch
            {
                1=>new Point2(-y,x),
                2=>new Point2(-x,-y),
                3=>new Point2(y,-x),
                _=>new Point2(x,y)
            };
        }
        var ring=canonical.Select(Transform).ToList();
        var blank=new BuildingDocument
        {
            SchemaVersion=2,Perimeter=ring.ToList(),
            UpperFloors=[new StoreyDefinition{Perimeter=ring.ToList()}]
        };
        Assert(SteppedRpgHousePlanner.TryGenerate(blank,42,
            out var planned,out var reason),
            $"Rotation {angle}, reflected={reflected}: {reason}");
        Assert(BuildingStoreys.Validate(planned,out reason),reason);
        Assert(planned.Staircases.Count==1);
        Assert(planned.Rooms.Count==5 &&
            planned.UpperFloors[0].Rooms.Count==5);
        Assert(planned.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(Math.Abs(planned.Rooms.Sum(r=>
            PolygonGeometry.Area(r.Outline))-88.75)<0.01);
        Assert(Math.Abs(planned.UpperFloors[0].Rooms.Sum(r=>
            PolygonGeometry.Area(r.Outline))-88.75)<0.01);
        Assert(blank.Rooms.Count==0 && blank.Staircases.Count==0);
    }
});

Check("Stair UP and DOWN arrows oppose each other on the same flight", () =>
{
    foreach(double rotation in new[] {0.0,90.0,180.0,270.0,45.0})
    {
        var stair=new StaircaseDefinition
        {
            LowerFloorIndex=0,Centre=new(2.5,0.7),
            Length=2.8,Width=1,RotationDegrees=rotation,LandingLength=1
        };
        var up=BuildingStoreys.StairTravelArrow(stair,0);
        var down=BuildingStoreys.StairTravelArrow(stair,1);
        Assert(up.Start.DistanceTo(down.Tip)<1e-8 &&
            up.Tip.DistanceTo(down.Start)<1e-8,
            $"Ascent/descent arrows must oppose, including at {rotation}°.");
        Assert(up.Start.DistanceTo(up.Tip)>0.1);
        Assert(Math.Abs(up.Start.DistanceTo(stair.Centre)-
            up.Tip.DistanceTo(stair.Centre))<1e-8);
        bool rejected=false;
        try { BuildingStoreys.StairTravelArrow(stair,2); }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Assert(rejected,"Do not draw a stair on an unrelated level.");
    }
});

Check("example6(3) old staircase bay traps approach behind the flight", () =>
{
    // Saved original staircase and shallow bay from example6(3).json.
    // The clear transverse route behind both landings was only
    // 1.6m - (0.7m + 0.5m) = 0.4m.
    var originalStair=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.5,0.7),
        Length=2.8,Width=1,LandingLength=1,RotationDegrees=90
    };
    double oldCapDepth=1.6;
    double originalRearClearance=oldCapDepth-
        new[] {BuildingStoreys.StairFootprint(originalStair)}
            .Concat(BuildingStoreys.StairLandings(originalStair))
            .SelectMany(p=>p).Max(p=>p.Y);
    Assert(Math.Abs(originalRearClearance-0.4)<0.001,
        "The uploaded stair really is squeezed into a 40cm bypass.");
});

Check("New L-house staircase ascends inward and leaves a walkable landing bypass", () =>
{
    var shape = new List<Point2>
    {
        new(0,0),new(9,0),new(9,6),
        new(11,6),new(11,10),new(0,10)
    };
    var empty = new BuildingDocument
    {
        SchemaVersion=2,Perimeter=shape.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=shape.ToList()}]
    };
    foreach(int seed in new[]{1,7,31,77,112})
    {
        Assert(SteppedRpgHousePlanner.TryGenerate(empty,seed,
            out var house,out var reason),$"Seed {seed}: {reason}");
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        var stair=house.Staircases.Single();
        Assert(stair.LowerFloorIndex==0);
        var up=BuildingStoreys.StairTravelArrow(stair,0);
        var down=BuildingStoreys.StairTravelArrow(stair,1);
        Assert(up.Tip.X>up.Start.X,
            "Ascending the stair should point toward the interior approach.");
        Assert(down.Tip.X<down.Start.X,
            "Descending must point back toward the lower landing.");
        var hall=house.Rooms.Single(r=>r.Type=="Hallway");
        var upperHall=house.UpperFloors[0].Rooms.Single(r=>r.Type=="Hallway");
        var entireStair=new[] {BuildingStoreys.StairFootprint(stair),
                BuildingStoreys.StairOpeningFootprint(stair)}
            .Concat(BuildingStoreys.StairLandings(stair)).ToList();
        Assert(entireStair.All(r=>
            FloorGeometry.IsWithinPolygon(r,hall.Outline) &&
            FloorGeometry.IsWithinPolygon(r,upperHall.Outline)));
        double furthestY=entireStair.SelectMany(r=>r).Max(p=>p.Y);
        var hallBoundaryBeforeApproach=hall.Outline
            .Where(p=>p.Y<6-0.0001 && p.Y>furthestY)
            .Select(p=>p.Y).DefaultIfEmpty(0).Min();
        Assert(hallBoundaryBeforeApproach-furthestY>=1.1-0.0001,
            "A full 1.1m path behind the flight must connect both end " +
            "landings to the approach, not the old 0.4m pinch point.");
        Assert(house.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(house.Rooms.Sum(r=>PolygonGeometry.Area(r.Outline))>97.99);
        Assert(house.UpperFloors[0].Rooms.Sum(r=>
            PolygonGeometry.Area(r.Outline))>97.99);
    }
});

Check("Accepted example6(3) old staircase retains its position with widened upper landing access", () =>
{
    var perimeter=new List<Point2>
    {
        new(0,0),new(9,0),new(9,6),new(11,6),new(11,10),new(0,10)
    };
    var oldStair=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.5,0.7),Length=2.8,
        Width=1,RotationDegrees=90,LandingLength=1
    };
    var cleared=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=perimeter.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=perimeter.ToList()}],
        Staircases=[oldStair.Copy()]
    };
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Stairs=true,CompactRpgHouseLayout=true
    };
    Assert(BuildingGenerator.TryRegenerate(cleared,[0,1],38,options,
        out var suggestion,out var reason),reason);
    var plan=suggestion.Building;
    Assert(BuildingStoreys.Validate(plan,out reason),reason);
    var same=plan.Staircases.Single();
    Assert(same.Id==oldStair.Id &&
        same.Centre.DistanceTo(oldStair.Centre)<1e-8 &&
        Math.Abs(same.RotationDegrees-oldStair.RotationDegrees)<1e-8,
        "Regeneration should respect a stair the user has left in place.");
    foreach(int level in new[]{0,1})
    {
        var floor=BuildingStoreys.GetFloor(plan,level);
        var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
        var highestReserved=new[] {BuildingStoreys.StairFootprint(same),
            BuildingStoreys.StairOpeningFootprint(same)}
            .Concat(BuildingStoreys.StairLandings(same))
            .SelectMany(r=>r).Max(p=>p.Y);
        var capEnd=hall.Outline
            .Select(p=>p.Y).Where(y=>y>highestReserved+0.01 &&
                y<6.0-0.01).DefaultIfEmpty(0).Min();
        Assert(capEnd-highestReserved>=1.1-1e-4,
            "Previously accepted staircase must get at least " +
            "1.1m of approach clearance around both landings.");
        Assert(floor.Rooms.Count(r=>r.Type=="Hallway")==1);
    }
    var up=BuildingStoreys.StairTravelArrow(same,0);
    var down=BuildingStoreys.StairTravelArrow(same,1);
    Assert(up.Start.DistanceTo(down.Tip)<1e-8,
        "The two arrows should be opposite even on saved old stairs.");
});

Check("example7 side-stair utility and upstairs bathroom move down like example8", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var perimeter=new List<Point2>
    {
        new(0,0),new(9,0),new(9,6.5),new(11,6.5),
        new(11,11),new(0,11)
    };
    List<Point2> Hall() =>
    [
        new(4.5,0),new(6.5,0),new(6.5,11),new(0,11),
        new(0,6),new(1.6,6),new(1.6,9.7),new(4.5,9.7)
    ];
    var kitchen=new RoomDefinition
    {
        Name="Kitchen",Type="Kitchen",Outline=
        [
            new(6.5,0),new(9,0),new(9,6.5),
            new(11,6.5),new(11,11),new(6.5,11)
        ]
    };
    var plan=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=perimeter.ToList(),
        Rooms=
        [
            new RoomDefinition{Name="Hallway 1",Type="Hallway",Outline=Hall()},
            new RoomDefinition{Name="Living room",Type="Living room",
                Outline=R(0,0,4.5,6)},
            kitchen,
            new RoomDefinition{Name="Utility",Type="Utility",
                Outline=R(1.6,6,4.5,9.7)}
        ],
        Doors=
        [
            new DoorDefinition{Centre=new(5.5,0),Width=0.9},
            new DoorDefinition{Centre=new(4.5,3),Width=0.9},
            new DoorDefinition{Centre=new(6.5,5.5),Width=0.9},
            new DoorDefinition{Centre=new(4.5,7.85),Width=0.9}
        ],
        Windows=[new WindowDefinition{Centre=new(0,3),Width=1.2}],
        UpperFloors=
        [
            new StoreyDefinition
            {
                Name="First Floor",Perimeter=perimeter.ToList(),
                Rooms=
                [
                    new RoomDefinition{Name="Hallway 1",Type="Hallway",
                        Outline=Hall()},
                    new RoomDefinition{Name="Bedroom",Type="Bedroom",
                        Outline=R(0,0,4.5,6)},
                    new RoomDefinition{Name="Study",Type="Study",
                        Outline=R(6.5,6.5,11,11)},
                    new RoomDefinition{Name="Bedroom 2",Type="Bedroom",
                        Outline=R(6.5,0,9,6.5)},
                    new RoomDefinition{Name="Bathroom",Type="Bathroom",
                        Outline=R(1.6,6,4.5,9.7)}
                ],
                Doors=
                [
                    new DoorDefinition{Centre=new(4.5,3),Width=0.9},
                    new DoorDefinition{Centre=new(6.5,8.75),Width=0.9},
                    new DoorDefinition{Centre=new(6.5,3.25),Width=0.9},
                    new DoorDefinition{Centre=new(4.5,7.85),Width=0.9}
                ],
                Windows=
                [
                    new WindowDefinition{Centre=new(0,3),Width=1.2},
                    new WindowDefinition{Centre=new(11,8.75),Width=1.2},
                    new WindowDefinition{Centre=new(7.75,0),Width=1.2}
                ]
            }
        ],
        Staircases=
        [
            new StaircaseDefinition
            {
                LowerFloorIndex=0,Centre=new(0.7,8.5),Length=2.8,
                Width=1,RotationDegrees=0,LandingLength=1
            }
        ],
        Chimneys=
        [
            new ChimneyDefinition
            {
                Centre=new Point2(8.63,3.25),Width=0.7,
                Depth=1.4,Exterior=true,HearthFloorIndex=0,
                HearthRoomIds=[kitchen.Id]
            }
        ]
    };
    Assert(BuildingStoreys.Validate(plan,out var reason),reason);
    var stairId=plan.Staircases[0].Id;
    var fireplaceId=plan.Chimneys[0].Id;
    var entranceId=plan.Doors[0].Id;
    var kitchenId=kitchen.Id;
    Assert(Math.Abs(PolygonGeometry.Area(plan.Rooms[0].Outline)-33.77)<0.01);
    Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(plan))<0.01,
        "Example7 ground floor is mathematically fully assigned.");

    Assert(SideStairApproachPlanner.TryRebalance(plan,0,
        Array.Empty<Guid>(),out var lower,out var groundStage),
        "Utility room must shift south as in example8: "+groundStage);
    Assert(SideStairApproachPlanner.TryRebalance(lower,1,
        Array.Empty<Guid>(),out var repaired,out var upstairsStage),
        "First-floor bathroom must shift south too: "+upstairsStage);
    Assert(BuildingStoreys.Validate(repaired,out reason),reason);
    Assert(repaired.Staircases.Single().Id==stairId &&
        repaired.Chimneys.Single().Id==fireplaceId,
        "Do not move the staircase, fireplace or chimney stack.");
    Assert(repaired.Rooms.Any(r=>r.Id==kitchenId &&
        r.Type=="Kitchen") &&
        repaired.Doors.Any(d=>d.Id==entranceId),
        "Kitchen and front door are protected from moving.");
    foreach(int index in new[]{0,1})
    {
        var floor=BuildingStoreys.GetFloor(repaired,index);
        var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
        var room=floor.Rooms.Single(r=>r.Type==
            (index==0?"Utility":"Bathroom"));
        Assert(PolygonGeometry.Area(hall.Outline)<29,
            "The overwide stair-side corridor should shrink by >4m².");
        Assert(PolygonGeometry.Area(room.Outline)>16,
            "The utility/bathroom gets the freed space at the bottom.");
        Assert(room.Outline.Min(p=>p.Y)>=7.79 &&
            room.Outline.Max(p=>p.Y)>=10.99,
            "Move service room below the 1.8m cross-passage.");
        Assert(Math.Abs(floor.Rooms.Sum(r=>
            PolygonGeometry.Area(r.Outline))-108)<0.01,
            "No remaining unassigned strip or stolen room area.");
        var foot=repaired.Staircases.Single();
        Assert(FloorGeometry.IsWithinPolygon(
            BuildingStoreys.StairFootprint(foot),hall.Outline));
        Assert(BuildingStoreys.StairLandings(foot).All(l=>
            FloorGeometry.IsWithinPolygon(l,hall.Outline)),
            "Both stair landings stay in the circulation polygon.");
        bool On(Point2 p,IReadOnlyList<Point2> outline)=>
            Enumerable.Range(0,outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(p,
                    outline[i],outline[(i+1)%outline.Count])
                    .DistanceTo(p)<1e-5);
        foreach(var r in floor.Rooms.Where(r=>r.Id!=hall.Id))
        {
            Assert(floor.Doors.Any(d=>On(d.Centre,r.Outline) &&
                On(d.Centre,hall.Outline)),
                r.Name+" requires direct entry from the new hall.");
        }
        var access = index==1
            ? floor.Rooms.Single(r=>r.Type=="Study")
            : floor.Rooms.Single(r=>r.Type=="Kitchen");
        Assert(access.Id!=room.Id);
    }
    Assert(plan.Rooms[0].Outline.Count==8 &&
        Math.Abs(PolygonGeometry.Area(plan.Rooms[0].Outline)-33.77)<0.01,
        "The source JSON layout cannot be mutated by suggestions.");
    Assert(!SideStairApproachPlanner.TryRebalance(plan,0,
        [plan.Rooms[0].Id],out _),
        "Never change an accepted/hand-drawn hallway without regeneration.");
    Assert(!SideStairApproachPlanner.TryRebalance(repaired,0,
        Array.Empty<Guid>(),out _),
        "Reapplying this pass must be idempotent.");
});

Check("example9 restores 1.1m route to upper stairhead and assigns right exterior step", () =>
{
    // Reproduces accepted example9.json: two identical stepped storeys,
    // one horizontal stair near the exterior, shared generated hallway
    // with a 0.3m squeeze, plus 3.375m² of ignored exterior slivers.
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var outline=new List<Point2> {
        new(0,0),new(9.5,0),new(9.5,5.5),
        new(11.5,5.5),new(11.5,9.5),new(0,9.5)};
    List<Point2> H() => [
        new(0,0),new(5,0),new(5,5.75),
        new(11.5,5.75),new(11.5,7.25),
        new(0,7.25),new(0,5.75),new(3.6,5.75),
        new(3.6,1.5),new(0,1.5)
    ];
    RoomDefinition A(string type,string name,List<Point2> shape)=>
        new(){Type=type,Name=name,Outline=shape};
    var hall=A("Hallway","Hallway 1",H());
    var living=A("Living room","Living room",R(5,0,9,5.75));
    var smaller=A("Living room","Living room 2",R(0,1.5,3.6,5.75));
    var dining=A("Dining room","Dining room",R(0,7.25,5,9.5));
    var study=A("Study","Study",R(5,7.25,11.5,9.5));
    var stair=new StaircaseDefinition {
        LowerFloorIndex=0,Centre=new(2.5,0.7),
        Width=1,Length=2.8,LandingLength=1,RotationDegrees=90
    };
    var original=new BuildingDocument {
        SchemaVersion=2,Perimeter=outline.ToList(),
        Rooms=[hall,living,smaller,dining,study],
        Doors=[
            new DoorDefinition{Centre=new(11.5,6.75),Width=0.9},
            new DoorDefinition{Centre=new(7,5.75),Width=0.9},
            new DoorDefinition{Centre=new(3.6,3.625),Width=0.9},
            new DoorDefinition{Centre=new(2.5,7.25),Width=0.9},
            new DoorDefinition{Centre=new(8.25,7.25),Width=0.9}
        ],
        Windows=[
            new WindowDefinition{Centre=new(0,3.625),Width=1.2},
            new WindowDefinition{Centre=new(2.5,9.5),Width=1.2},
            new WindowDefinition{Centre=new(8.25,9.5),Width=1.2}
        ],
        UpperFloors=[new StoreyDefinition {
            Name="First Floor",Perimeter=outline.ToList(),
            Rooms=[
                A("Hallway","Hallway 1",H()),
                A("Bedroom","Bedroom",R(5,0,9,5.75)),
                A("Bedroom","Bedroom 2",R(0,1.5,3.6,5.75)),
                A("Bedroom","Bedroom 3",R(0,7.25,5,9.5)),
                A("Bathroom","Bathroom",R(5,7.25,11.5,9.5))
            ],
            Doors=[
                new DoorDefinition{Centre=new(7,5.75),Width=0.9},
                new DoorDefinition{Centre=new(3.6,3.625),Width=0.9},
                new DoorDefinition{Centre=new(2.5,7.25),Width=0.9},
                new DoorDefinition{Centre=new(8.25,7.25),Width=0.9}
            ],
            Windows=[
                new WindowDefinition{Centre=new(0,3.625),Width=1.2},
                new WindowDefinition{Centre=new(2.5,9.5),Width=1.2},
                new WindowDefinition{Centre=new(8.25,9.5),Width=1.2}
            ]
        }],
        Staircases=[stair],
        Chimneys=[new ChimneyDefinition {
            Centre=new Point2(7,0.37),Width=1.4,Depth=0.7,
            Exterior=true,HearthFloorIndex=0,HearthRoomIds=[living.Id]
        }]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    Assert(Math.Abs(PolygonGeometry.Area(outline)-98.25)<0.001);
    Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(original)-3.375)<0.001,
        "Original saved layout has a 3.375m² missing exterior step.");
    var beforeHead=BuildingStoreys.StairTravelArrow(stair,0).Tip;
    Assert(beforeHead.X<stair.Centre.X,
        "Ascent points into the old dead-end upper landing.");
    double reservedBack=new[] {BuildingStoreys.StairFootprint(stair),
        BuildingStoreys.StairOpeningFootprint(stair)}
        .Concat(BuildingStoreys.StairLandings(stair))
        .SelectMany(p=>p).Max(p=>p.Y);
    Assert(Math.Abs(1.5-reservedBack-0.3)<0.001,
        "The pre-repair walkway beside the void is only 30cm.");

    var result=original;
    for(int level=0;level<=1;level++)
    {
        int count=SteppedLayoutFinisher.Refine(result,level,
            Array.Empty<Guid>(),out var improved);
        Assert(count==2,
            $"Ground/upper stepped finisher should fix both faults on level {level}; got {count}");
        result=improved;
        Assert(BuildingStoreys.Validate(result,out reason),reason);
    }
    Assert(result.Staircases.Single().Id==stair.Id &&
        result.Chimneys.Single().HearthRoomIds.Single()==living.Id,
        "The flight position and linked fireplace must be preserved.");
    for(int level=0;level<=1;level++)
    {
        var storey=BuildingStoreys.GetFloor(result,level);
        var newHall=storey.Rooms.Single(r=>r.Type=="Hallway");
        Assert(RoomLayoutPlanner.UnassignedArea(storey)<0.02,
            "Notched exterior slivers must belong to a real room or hall.");
        Assert(Math.Abs(storey.Rooms.Sum(r=>
            PolygonGeometry.Area(r.Outline))-98.25)<0.02);
        var newSideRoom=storey.Rooms.Single(r=>r.Name==
            (level==0?"Living room 2":"Bedroom 2"));
        Assert(Math.Abs(newSideRoom.Outline.Min(p=>p.Y)-2.3)<0.01,
            "Shift the room wall away from the stair for a 1.1m walking route.");
        Assert(newSideRoom.Outline.Max(p=>p.Y)==5.75);
        var upperHeadRoute=R(0,1.2,3.6,2.3);
        Assert(FloorGeometry.IsWithinPolygon(upperHeadRoute,newHall.Outline),
            "A full-width 1.1m bypass must lead from the top landing to the hall.");
        var enlarged=storey.Rooms.Single(r=>
            r.Name==(level==0?"Living room":"Bedroom"));
        Assert(enlarged.Outline.Count==6,
            "Exterior room must follow the step with an L-shaped wall.");
        Assert(Math.Abs(PolygonGeometry.Area(enlarged.Outline)-25.75)<0.01);
        Assert(storey.Doors.Count==(level==0?5:4));
        Assert(storey.Windows.Count==3);
    }
    Assert(original.Rooms[0].Outline.Count==10 &&
        Math.Abs(RoomLayoutPlanner.UnassignedArea(original)-3.375)<0.001,
        "The previously accepted source floor cannot be modified.");
    Assert(SteppedLayoutFinisher.Refine(result,0,
        Array.Empty<Guid>(),out _) == 0,
        "The stair-side/exterior repair should be idempotent.");
    Assert(SteppedLayoutFinisher.Refine(original,0,
        original.Rooms.Select(r=>r.Id).ToArray(),out _) == 0,
        "Hand-authored rooms must be protected.");

    // The stepped outer wall could equally be on the other side,
    // or rotated on the editor's canvas. The same gap should be
    // closed without assuming that the step faces right or down.
    foreach(int quarterTurn in new[]{0,1,2,3})
    foreach(bool mirror in new[]{false,true})
    {
        Point2 Rotate(Point2 p)
        {
            double x=mirror ? -p.X : p.X, y=p.Y;
            return quarterTurn switch
            {
                1=>new Point2(-y,x),
                2=>new Point2(-x,-y),
                3=>new Point2(y,-x),
                _=>new Point2(x,y)
            };
        }
        var variant=BuildingStoreys.Copy(original);
        variant.UpperFloors.Clear();
        variant.Staircases.Clear();
        variant.Chimneys.Clear();
        variant.Doors.Clear();
        variant.Windows.Clear();
        variant.Perimeter=variant.Perimeter.Select(Rotate).ToList();
        foreach(var room in variant.Rooms)
            room.Outline=room.Outline.Select(Rotate).ToList();
        Assert(BuildingStoreys.Validate(variant,out reason),reason);
        int recovered=SteppedLayoutFinisher.Refine(variant,0,
            Array.Empty<Guid>(),out var joined);
        Assert(recovered==1,
            $"Step sliver wasn't closed at rotation {quarterTurn}, mirror={mirror}");
        Assert(BuildingStoreys.Validate(joined,out reason),reason);
        Assert(RoomLayoutPlanner.UnassignedArea(joined)<0.02,
            "A mirrored or rotated exterior edge must not leave a 3.375m² gap.");
    }
});

Check("example10 broad L-house generates kitchen and properly sized dining and bedrooms", () =>
{
    List<Point2> perimeter =
    [
        new(0,0),new(11.5,0),new(11.5,5),
        new(13.5,5),new(13.5,8.5),new(0,8.5)
    ];
    var flight=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.5,0.7),
        Length=2.8,Width=1,RotationDegrees=90,LandingLength=1
    };
    var blank=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=perimeter.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=perimeter.ToList()}],
        Staircases=[flight.Copy()]
    };
    Assert(BuildingStoreys.Validate(blank,out var reason),reason);
    var distinctLayouts=new List<BuildingDocument>();
    foreach(int seed in new[]{7,11,26,41,71,101})
    {
        Assert(BalancedSteppedRpgHousePlanner.TryGenerate(blank,seed,
            out var house,out reason),
            "Balanced example10 generation seed "+seed+": "+reason);
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        Assert(house.Staircases.Single().Id==flight.Id &&
            house.Staircases.Single().Centre.DistanceTo(flight.Centre)<1e-8,
            "Never silently reposition the user's accepted staircase.");
        var lower=BuildingStoreys.GetFloor(house,0);
        var upper=BuildingStoreys.GetFloor(house,1);
        Assert(lower.Rooms.Count(r=>r.Type=="Living room")==1,
            "Do not generate two living rooms but no kitchen.");
        Assert(lower.Rooms.Count(r=>r.Type=="Kitchen")==1);
        Assert(lower.Rooms.Any(r=>r.Type=="Dining room"));
        Assert(lower.Rooms.Any(r=>r.Type=="Study"));
        Assert(upper.Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(upper.Rooms.Any(r=>r.Type=="Sitting room"));
        Assert(upper.Rooms.Count(r=>r.Type=="Bathroom")==1);
        var hall=lower.Rooms.Single(r=>r.Type=="Hallway");
        Assert(PolygonGeometry.Area(hall.Outline)<14,
            "Common room should contain the stairs instead of a 37m² hallway.");
        double ShortSide(RoomDefinition r)=>
            Math.Min(r.Outline.Max(p=>p.X)-r.Outline.Min(p=>p.X),
                r.Outline.Max(p=>p.Y)-r.Outline.Min(p=>p.Y));
        Assert(ShortSide(lower.Rooms.Single(r=>r.Type=="Dining room"))>=3.05,
            "RPG dining room must be at least 10 feet wide, not 5.7 feet.");
        Assert(ShortSide(lower.Rooms.Single(r=>r.Type=="Study"))>=3.05,
            "Study must be a practical room, not a 1.75m strip.");
        var bedrooms=upper.Rooms.Where(r=>r.Type=="Bedroom").ToArray();
        Assert(bedrooms.All(r=>ShortSide(r)>=3.05),
            "Every bedroom must have at least 10 feet usable width.");
        var bedroomAreas=bedrooms.Select(r=>
            PolygonGeometry.Area(r.Outline)).ToArray();
        Assert(bedroomAreas.Max()/bedroomAreas.Min()<1.75,
            "Bedrooms should be comparably sized, not 34m² versus 9m².");
        Assert(bedroomAreas.Max()<27);
        var downstairsLiving=lower.Rooms.Single(r=>r.Type=="Living room");
        var upstairsSitting=upper.Rooms.Single(r=>r.Type=="Sitting room");
        var stairReservations=new[] {BuildingStoreys.StairFootprint(flight),
            BuildingStoreys.StairOpeningFootprint(flight)}
            .Concat(BuildingStoreys.StairLandings(flight));
        Assert(stairReservations.All(part=>
            FloorGeometry.IsWithinPolygon(part,downstairsLiving.Outline) &&
            FloorGeometry.IsWithinPolygon(part,upstairsSitting.Outline)),
            "Staircase belongs in the downstairs common room and upstairs sitting room.");
        bool On(Point2 p,IReadOnlyList<Point2> ring)=>
            Enumerable.Range(0,ring.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(p,
                    ring[i],ring[(i+1)%ring.Count]).DistanceTo(p)<1e-5);
        foreach(var index in new[]{0,1})
        {
            var floor=BuildingStoreys.GetFloor(house,index);
            var corridor=floor.Rooms.Single(r=>r.Type=="Hallway");
            foreach(var room in floor.Rooms.Where(r=>r.Id!=corridor.Id))
            {
                bool direct=floor.Doors.Any(d=>
                    On(d.Centre,room.Outline) &&
                    On(d.Centre,corridor.Outline));
                bool pantryViaKitchen=room.Type=="Pantry" &&
                    floor.Doors.Any(d=>On(d.Centre,room.Outline) &&
                        floor.Rooms.Any(r=>r.Type=="Kitchen" &&
                            On(d.Centre,r.Outline)));
                Assert(direct || pantryViaKitchen,
                    room.Name+" must connect to hallway or to the kitchen.");
            }
            Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(floor))<0.01,
                "Every part of the L-shaped exterior must be allocated.");
        }
        Assert(!lower.Rooms.Where(r=>r.Type!="Hallway")
            .All(g=>upper.Rooms.Any(u=>g.Outline.SequenceEqual(u.Outline))),
            "Both storeys should not be copies of identical room divisions.");
        if (!distinctLayouts.Any(p=>
            BuildingGenerator.SameRoomLayout(p,house,[0,1])))
            distinctLayouts.Add(house);
    }
    Assert(distinctLayouts.Count>=2,"Several seeds should vary the balanced plan.");
    Assert(blank.Rooms.Count==0 &&
        blank.UpperFloors[0].Rooms.Count==0);
});

Check("Regenerate example10 accepted L-house through balanced room-first archetype", () =>
{
    List<Point2> perimeter =
    [
        new(0,0),new(11.5,0),new(11.5,5),
        new(13.5,5),new(13.5,8.5),new(0,8.5)
    ];
    var oldStair=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.5,0.7),
        Length=2.8,Width=1,RotationDegrees=90,LandingLength=1
    };
    List<Point2> R(double x0,double y0,double x1,double y1)=>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var original=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=perimeter.ToList(),
        Rooms=[
            new RoomDefinition{Name="Living room",Type="Living room",
                Outline=R(5,0,11.5,5)},
            new RoomDefinition{Name="Living room 2",Type="Living room",
                Outline=R(0,2.3,3.6,5.25)}
        ],
        UpperFloors=[new StoreyDefinition
        {
            Perimeter=perimeter.ToList(),
            Rooms=[
                new RoomDefinition{Name="Bedroom",Type="Bedroom",
                    Outline=R(5,0,11.5,5)}
            ]
        }],
        Staircases=[oldStair]
    };
    // The accepted map has a fireplace in the old Living room at
    // (8.25, 0.37). The balanced plan puts its Kitchen there.
    // Preserve the masonry and reconnect its hearth face to the
    // new Kitchen room rather than leaving an unassigned fireplace.
    var originalHearth=new ChimneyDefinition
    {
        Centre=new Point2(8.25,0.37),Width=1.4,Depth=0.7,
        Exterior=true,HearthFloorIndex=0,
        HearthRoomIds=[original.Rooms[0].Id]
    };
    original.Chimneys.Add(originalHearth);
    // Regeneration must delete the old draft's rooms, so it can plan
    // both levels independently around the SAME accepted stair.
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Stairs=true,CompactRpgHouseLayout=true
    };
    Assert(BuildingGenerator.TryRegenerate(original,[0,1],28,options,
        out var suggestion,out var reason),reason);
    Assert(suggestion.Description.Contains("Balanced L-shaped RPG house"),
        "Wide L-shaped houses must prefer the balanced planner.");
    var proposed=suggestion.Building;
    Assert(BuildingStoreys.Validate(proposed,out reason),reason);
    Assert(proposed.Staircases.Single().Id==oldStair.Id);
    Assert(proposed.Rooms.Count(r=>r.Type=="Kitchen")==1);
    Assert(proposed.Rooms.Count(r=>r.Type=="Living room")==1);
    Assert(proposed.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
    var newlyAssignedKitchen=proposed.Rooms.Single(r=>r.Type=="Kitchen");
    Assert(proposed.Chimneys.Single().Id==originalHearth.Id &&
        proposed.Chimneys.Single().HearthRoomIds.SequenceEqual(
            new[]{newlyAssignedKitchen.Id}),
        "Keep the fireplace and connect it to the newly positioned Kitchen.");
    Assert(original.Chimneys.Single().HearthRoomIds.Single()==original.Rooms[0].Id,
        "Regeneration must preserve the source fireplace's old room link.");
    Assert(original.Rooms.Count==2 &&
        !original.Rooms.Any(r=>r.Type=="Kitchen"),
        "Regeneration must never mutate the accepted document.");
});

Check("Balanced L-house generates its own stairs on mirrored and rotated footprints", () =>
{
    var ring=new List<Point2>
    {
        new(0,0),new(11.5,0),new(11.5,5),
        new(13.5,5),new(13.5,8.5),new(0,8.5)
    };
    foreach(int quarterTurn in new[]{0,1,2,3})
    foreach(bool mirror in new[]{false,true})
    {
        Point2 Transform(Point2 p)
        {
            double x=mirror ? -p.X : p.X, y=p.Y;
            return quarterTurn switch
            {
                1=>new Point2(-y,x),
                2=>new Point2(-x,-y),
                3=>new Point2(y,-x),
                _=>new Point2(x,y)
            };
        }
        var transformed=ring.Select(Transform).ToList();
        var blank=new BuildingDocument
        {
            SchemaVersion=2,Perimeter=transformed.ToList(),
            UpperFloors=[new StoreyDefinition{
                Perimeter=transformed.ToList()}]
        };
        Assert(BalancedSteppedRpgHousePlanner.TryGenerate(blank,42,
            out var house,out var reason),
            $"Rotation {quarterTurn}, mirror {mirror}: {reason}");
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        Assert(house.Staircases.Count==1);
        Assert(house.Rooms.Count(r=>r.Type=="Kitchen")==1);
        Assert(house.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(
            BuildingStoreys.GetFloor(house,0)))<0.01);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(
            BuildingStoreys.GetFloor(house,1)))<0.01);
        Assert(blank.Staircases.Count==0,
            "Layout generation should not alter the input.");
    }
});


Check("example11 regenerates without duplicated living rooms or thin rooms", () =>
{
    List<Point2> outer =
    [
        new(0,0),new(11,0),new(11,5),
        new(13,5),new(13,8.5),new(0,8.5)
    ];
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    List<Point2> hall =
    [
        new(0,0),new(5,0),new(5,5.25),new(10,5.25),
        new(10,5),new(13,5),new(13,5.25),new(13,6.75),
        new(0,6.75),new(0,5.25),new(3.6,5.25),
        new(3.6,2.3),new(0,2.3)
    ];
    List<Point2> bigFront =
    [
        new(5,0),new(11,0),new(11,5),
        new(10,5),new(10,5.25),new(5,5.25)
    ];
    RoomDefinition Room(string name,string type,List<Point2> shape) =>
        new(){Name=name,Type=type,Outline=shape.ToList()};
    DoorDefinition Door(double x,double y) =>
        new(){Centre=new Point2(x,y),Width=0.9};
    WindowDefinition Window(double x,double y) =>
        new(){Centre=new Point2(x,y),Width=1.2};

    var original=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=outer.ToList(),
        Rooms=[
            Room("Hallway 1","Hallway",hall),
            Room("Living room","Living room",bigFront),
            Room("Living room 2","Living room",R(0,2.3,3.6,5.25)),
            Room("Dining room","Dining room",R(0,6.75,5,8.5)),
            Room("Study","Study",R(5,6.75,13,8.5))
        ],
        Doors=[Door(13,6.25),Door(7.5,5.25),
            Door(3.6,3.375),Door(2.5,6.75),Door(9,6.75)],
        Windows=[Window(11,2.5),Window(0,3.775),
            Window(2.5,8.5),Window(9,8.5)],
        UpperFloors=[new StoreyDefinition
        {
            Perimeter=outer.ToList(),
            Rooms=[
                Room("Hallway 1","Hallway",hall),
                Room("Bedroom","Bedroom",bigFront),
                Room("Bedroom 2","Bedroom",R(0,2.3,3.6,5.25)),
                Room("Bedroom 3","Bedroom",R(0,6.75,5,8.5)),
                Room("Bathroom","Bathroom",R(5,6.75,13,8.5))
            ],
            Doors=[Door(7.5,5.25),Door(3.6,3.375),
                Door(2.5,6.75),Door(9,6.75)],
            Windows=[Window(11,2.5),Window(0,3.775),
                Window(2.5,8.5),Window(9,8.5)]
        }],
        Staircases=[new StaircaseDefinition
        {
            LowerFloorIndex=0,Centre=new Point2(2.5,0.7),
            Length=2.8,Width=1,RotationDegrees=90,LandingLength=1
        }]
    };
    var oldLiving=original.Rooms.Single(r=>r.Name=="Living room");
    var hearth=new ChimneyDefinition
    {
        Centre=new Point2(8,0.37),Width=1.4,Depth=0.7,
        Exterior=true,HearthFloorIndex=0,
        HearthRoomIds=[oldLiving.Id]
    };
    original.Chimneys.Add(hearth);
    Assert(BuildingStoreys.Validate(original,out var reason),
        "Uploaded example11 layout must be valid: "+reason);

    var settings=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,Rooms=true,Corridors=true,
        Doors=true,Stairs=true,CompactRpgHouseLayout=true
    };
    foreach(int seed in new[]{7,28,101})
    {
        Assert(BuildingGenerator.TryRegenerate(original,[0,1],seed,
            settings,out var suggestion,out reason),
            "example11 regeneration seed "+seed+": "+reason);
        Assert(suggestion.Description.Contains("Balanced L-shaped RPG house"),
            "The 13m x 8.5m L-shaped house should use room-first planning.");
        var house=suggestion.Building;
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        var ground=BuildingStoreys.GetFloor(house,0);
        var first=BuildingStoreys.GetFloor(house,1);
        Assert(ground.Rooms.Count(r=>r.Type=="Kitchen")==1,
            "Ground floor requires one actual kitchen.");
        Assert(ground.Rooms.Count(r=>r.Type=="Living room")==1,
            "Avoid duplicate living rooms.");
        Assert(ground.Rooms.Count(r=>r.Type=="Hallway")==1);
        Assert(ground.Rooms.Any(r=>r.Type=="Dining room") &&
            ground.Rooms.Any(r=>r.Type=="Study"));
        double ShortSide(RoomDefinition r)=>
            Math.Min(r.Outline.Max(p=>p.X)-r.Outline.Min(p=>p.X),
                r.Outline.Max(p=>p.Y)-r.Outline.Min(p=>p.Y));
        Assert(ground.Rooms.Where(r=>r.Type is "Dining room" or "Study")
            .All(r=>ShortSide(r)>=3.048),
            "Dining and Study require at least 10 feet of usable short-side width.");
        Assert(PolygonGeometry.Area(ground.Rooms.Single(r=>r.Type=="Hallway").Outline)<14,
            "Do not retain the 35.88m² corridor alongside the lower stairs.");
        var bedrooms=first.Rooms.Where(r=>r.Type=="Bedroom").ToArray();
        Assert(bedrooms.Length==3);
        Assert(bedrooms.All(r=>ShortSide(r)>=3.048));
        var areas=bedrooms.Select(r=>PolygonGeometry.Area(r.Outline)).ToArray();
        Assert(areas.Max()/areas.Min()<1.75,
            "Largest upstairs bedroom must not dwarf the others.");
        Assert(areas.Max()<27);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(ground))<0.01);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(first))<0.01);
        Assert(house.Staircases.Single().Id==original.Staircases.Single().Id &&
            house.Staircases.Single().Centre.DistanceTo(new Point2(2.5,0.7))<1e-8,
            "Keep the accepted staircase in exactly the same position.");
        Assert(house.Chimneys.Single().Id==hearth.Id &&
            house.Chimneys.Single().HearthRoomIds.SequenceEqual(
                new[]{ground.Rooms.Single(r=>r.Type=="Kitchen").Id}),
            "Existing fireplace must be attached to the new Kitchen.");
        Assert(!ground.Rooms.Where(r=>r.Type!="Hallway")
            .All(g=>first.Rooms.Any(u=>g.Outline.SequenceEqual(u.Outline))),
            "First floor partitions must not mirror ground floor partitions.");
    }
    Assert(original.Rooms.Count==5 &&
        !original.Rooms.Any(r=>r.Type=="Kitchen") &&
        original.Chimneys.Single().HearthRoomIds.SequenceEqual(new[]{oldLiving.Id}),
        "Regeneration must not mutate the source example11 document.");
});

Check("example12 side-landing floorplan is accessible and not mirrored upstairs", () =>
{
    List<Point2> outer =
    [
        new(0,0),new(10.5,0),new(10.5,6),
        new(12.5,6),new(12.5,10),new(0,10)
    ];
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var flight=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.8,1.1),
        Length=2.8,Width=0.9,RotationDegrees=270,LandingLength=1
    };
    var example = new BuildingDocument
    {
        SchemaVersion=2,Perimeter=outer.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=outer.ToList()}],
        Staircases=[flight.Copy()]
    };
    Assert(BuildingStoreys.Validate(example,out var reason),reason);
    var distinct = new List<BuildingDocument>();
    for(int seed=1;seed<=5;seed++)
    {
        Assert(SideLandingRpgHousePlanner.TryGenerate(example,seed,
            out var house,out reason),
            "Side-landing generation on example12 seed "+seed+": "+reason);
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        Assert(house.Staircases.Single().Id==flight.Id &&
            house.Staircases.Single().Centre.DistanceTo(new Point2(2.8,0.45))<1e-7,
            "Old auto-generated stairs should move flush against the exterior wall.");
        Assert(flight.Centre.DistanceTo(new Point2(2.8,1.1))<1e-7,
            "Regeneration must not alter the source staircase.");
        var lower=BuildingStoreys.GetFloor(house,0);
        var upper=BuildingStoreys.GetFloor(house,1);
        Assert(lower.Rooms.Count(r=>r.Type=="Hallway")==1);
        Assert(lower.Rooms.Count(r=>r.Type=="Living room")==1);
        Assert(lower.Rooms.Count(r=>r.Type=="Kitchen")==1);
        Assert(lower.Rooms.Any(r=>r.Type=="Study") &&
            lower.Rooms.Any(r=>r.Type=="Dining room"));
        Assert(upper.Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(upper.Rooms.Any(r=>r.Type=="Bathroom") &&
            upper.Rooms.Any(r=>r.Type=="Sitting room"));
        var lowerHall=lower.Rooms.Single(r=>r.Type=="Hallway");
        var upperHall=upper.Rooms.Single(r=>r.Type=="Hallway");
        Assert(PolygonGeometry.Area(lowerHall.Outline)<14,
            "Downstairs circulation should no longer occupy 37 square metres.");
        Assert(!lowerHall.Outline.SequenceEqual(upperHall.Outline),
            "The upstairs hall must be independent, not a clone of downstairs.");
        Assert(PolygonGeometry.Area(upperHall.Outline)<25);
        Assert(!lower.Rooms.Where(r=>r.Type!="Hallway")
            .Any(r=>upper.Rooms.Any(u=>r.Outline.SequenceEqual(u.Outline))),
            "Do not mirror ground-floor room boundaries upstairs.");
        var newStair=house.Staircases.Single();
        var stairReservations=new[]{BuildingStoreys.StairFootprint(newStair),
            BuildingStoreys.StairOpeningFootprint(newStair)}
            .Concat(BuildingStoreys.StairLandings(newStair)).ToArray();
        var living=lower.Rooms.Single(r=>r.Type=="Living room");
        var sitting=upper.Rooms.Single(r=>r.Type=="Sitting room");
        Assert(Math.Abs(BuildingStoreys.StairFootprint(newStair)
            .Min(p=>p.Y))<1e-7,
            "The long staircase edge should abut the north exterior wall.");
        var kitchen=lower.Rooms.Single(r=>r.Type=="Kitchen");
        var pantryRoom=lower.Rooms.Single(r=>r.Type=="Pantry");
        var dining=lower.Rooms.Single(r=>r.Type=="Dining room");
        Assert(pantryRoom.Outline.Max(p=>p.Y)<=
            kitchen.Outline.Min(p=>p.Y)+1e-7,
            "Pantry must be on the far side of kitchen, not between kitchen and dining.");
        Assert(Math.Abs(kitchen.Outline.Max(p=>p.Y)-6)<1e-7,
            "Kitchen must share the full serving wall with the dining room.");
        bool OnWall(Point2 p,IReadOnlyList<Point2> outline)=>
            Enumerable.Range(0,outline.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(p,
                    outline[i],outline[(i+1)%outline.Count]).DistanceTo(p)<1e-5);
        Assert(lower.Doors.Any(d=>OnWall(d.Centre,kitchen.Outline) &&
            OnWall(d.Centre,dining.Outline)),
            "Require a direct kitchen-to-dining doorway.");
        Assert(stairReservations.All(p=>
            FloorGeometry.IsWithinPolygon(p,living.Outline) &&
            FloorGeometry.IsWithinPolygon(p,sitting.Outline)),
            "Put the ground flight inside the living room and the upper flight inside sitting.");
        // example12(2): the northeast 2m-wide wing (x=10.5..12.5)
        // from y=6 to the back-hall line belongs to Bedroom 3, not
        // to a dead-end of the upstairs corridor. Doors must still
        // connect the bedroom directly to the remaining cross hall.
        var bedroom3=upper.Rooms.Single(r=>r.Name=="Bedroom 3");
        var formerDeadEnd=new Point2(11.5,6.2);
        Assert(PolygonGeometry.ContainsPoint(bedroom3.Outline,formerDeadEnd),
            "Bedroom 3 should absorb the upper-right dead-end hallway patch.");
        Assert(!PolygonGeometry.ContainsPoint(upperHall.Outline,formerDeadEnd),
            "The hallway must not extend into the unused upper-right wing.");
        Assert(bedroom3.Outline.Count>=6,
            "Bedroom 3 should have an L-shaped outline, not a rectangle.");
        Assert(Math.Abs(upperHall.Outline.Max(p=>p.X)-10.5)<1e-7,
            "The upstairs cross-hall should stop at the front-wing corner.");
        var bedroomAreas=upper.Rooms.Where(r=>r.Type=="Bedroom")
            .Select(r=>PolygonGeometry.Area(r.Outline)).ToArray();
        Assert(bedroomAreas.Max()/bedroomAreas.Min()<1.75);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(lower))<0.01 &&
            Math.Abs(RoomLayoutPlanner.UnassignedArea(upper))<0.01);
        static bool On(Point2 p,IReadOnlyList<Point2> ring)=>
            Enumerable.Range(0,ring.Count).Any(i=>
                PolygonGeometry.ClosestPointOnSegment(
                    p,ring[i],ring[(i+1)%ring.Count]).DistanceTo(p)<1e-5);
        foreach(int index in new[]{0,1})
        {
            var floor=BuildingStoreys.GetFloor(house,index);
            var hall=floor.Rooms.Single(r=>r.Type=="Hallway");
            foreach(var room in floor.Rooms.Where(r=>r.Id!=hall.Id))
            {
                bool access=floor.Doors.Any(d=>On(d.Centre,room.Outline) &&
                    On(d.Centre,hall.Outline));
                bool pantry=room.Type=="Pantry" &&
                    floor.Doors.Any(d=>On(d.Centre,room.Outline) &&
                        floor.Rooms.Any(r=>r.Type=="Kitchen" &&
                            On(d.Centre,r.Outline)));
                Assert(access||pantry,
                    room.Name+" must have independent access.");
            }
        }
        if(!distinct.Any(other=>BuildingGenerator.SameRoomLayout(other,house,[0,1])))
            distinct.Add(house);
    }
    Assert(distinct.Count>=2,"Seed should vary independent upstairs bedrooms.");

    // Regenerate the accepted example12-shaped hallway layout: the old
    // 37m² stair passage was erroneously duplicated on both storeys.
    var existing=BuildingStoreys.Copy(example);
    List<Point2> oldHall =
    [
        new(0,0),new(5.6,0),new(5.6,6),new(12.5,6),
        new(12.5,7.4),new(0,7.4),new(0,6),new(4.2,6),
        new(4.2,2.65),new(0,2.65)
    ];
    RoomDefinition Room(string name,string type,List<Point2> outline)=>
        new(){Name=name,Type=type,Outline=outline.ToList()};
    var oldKitchen=Room("Kitchen","Kitchen",R(5.6,0,10.5,6));
    existing.Rooms =
    [
        Room("Stair passage","Hallway",oldHall),
        Room("Living room","Living room",R(0,2.65,4.2,6)),
        oldKitchen,
        Room("Dining room","Dining room",R(0,7.4,5.6,10)),
        Room("Study","Study",R(5.6,7.4,12.5,10))
    ];
    existing.UpperFloors[0].Rooms =
    [
        Room("Upper landing","Hallway",oldHall),
        Room("Master Bedroom","Bedroom",R(0,2.65,4.2,6)),
        Room("Bedroom","Bedroom",R(5.6,0,10.5,6)),
        Room("Bedroom 2","Bedroom",R(0,7.4,5.6,10)),
        Room("Bathroom","Bathroom",R(5.6,7.4,12.5,10))
    ];
    var hearth=new ChimneyDefinition
    {
        Centre=new Point2(10.13,3),Width=0.7,Depth=1.4,
        HearthFloorIndex=0,Exterior=true,HearthRoomIds=[oldKitchen.Id]
    };
    existing.Chimneys.Add(hearth);
    Assert(BuildingStoreys.Validate(existing,out reason),reason);
    var options=new GenerationOptions
    {
        Purpose=BuildingUse.Residence,CompactRpgHouseLayout=true,
        Rooms=true,Corridors=true,Doors=true,Stairs=true
    };
    Assert(BuildingGenerator.TryRegenerate(existing,[0,1],43,options,
        out var suggestion,out reason),"example12 regeneration: "+reason);
    Assert(suggestion.Description.Contains("Side-landing"),
        "Regeneration must choose the new independent upper layout.");
    Assert(PolygonGeometry.ContainsPoint(
            suggestion.Building.UpperFloors[0].Rooms.Single(r=>r.Name=="Bedroom 3").Outline,
            new Point2(11.5,6.2)),
        "Regeneration must absorb the example12(2) upstairs dead-end nook.");
    Assert(suggestion.Building.Chimneys.Single().Id==hearth.Id &&
        suggestion.Building.Chimneys.Single().HearthRoomIds.SequenceEqual(
            new[]{suggestion.Building.Rooms.Single(r=>r.Type=="Kitchen").Id}),
        "Retain example12 kitchen chimney link.");
    Assert(suggestion.Building.Staircases.Single().Id==flight.Id &&
        suggestion.Building.Staircases.Single().Centre.DistanceTo(
            new Point2(2.8,0.45))<1e-7,
        "The recognisable previously generated stair may be snapped to the wall.");
    Assert(existing.Staircases.Single().Centre.DistanceTo(
        new Point2(2.8,1.1))<1e-7,
        "Regeneration must not move the original stair before acceptance.");
    Assert(existing.Rooms.Single(r=>r.Type=="Hallway").Name=="Stair passage",
        "Regeneration should leave the accepted source untouched.");
});

Check("Side-landing house rotates and mirrors with independent floor partitions", () =>
{
    var ring=new List<Point2>
    {
        new(0,0),new(10.5,0),new(10.5,6),
        new(12.5,6),new(12.5,10),new(0,10)
    };
    foreach(int quarterTurn in new[]{0,1,2,3})
    foreach(bool mirror in new[]{false,true})
    {
        Point2 Transform(Point2 p)
        {
            double x=mirror?-p.X:p.X,y=p.Y;
            return quarterTurn switch
            {
                1=>new Point2(-y,x),
                2=>new Point2(-x,-y),
                3=>new Point2(y,-x),
                _=>new Point2(x,y)
            };
        }
        var shape=ring.Select(Transform).ToList();
        var blank=new BuildingDocument
        {
            SchemaVersion=2,Perimeter=shape.ToList(),
            UpperFloors=[new StoreyDefinition{Perimeter=shape.ToList()}]
        };
        Assert(SideLandingRpgHousePlanner.TryGenerate(blank,91,
            out var house,out var reason),
            $"Side-landing orientation {quarterTurn}, mirror={mirror}: {reason}");
        Assert(BuildingStoreys.Validate(house,out reason),reason);
        Assert(house.Staircases.Count==1);
        // In every transformed orientation the original canonical y=0
        // boundary must touch the narrow edge of the generated stair.
        var flight=house.Staircases.Single();
        Assert(BuildingStoreys.StairFootprint(flight).Any(p=>
            house.Perimeter.SelectMany((a,i)=>new[]{
                PolygonGeometry.ClosestPointOnSegment(p,a,
                    house.Perimeter[(i+1)%house.Perimeter.Count])})
                .Any(q=>q.DistanceTo(p)<1e-6)),
            "New stair needs to be placed against the outer wall.");
        Assert(house.Rooms.Count(r=>r.Type=="Kitchen")==1);
        Assert(house.UpperFloors[0].Rooms.Count(r=>r.Type=="Bedroom")==3);
        Assert(house.UpperFloors[0].Rooms.Single(r=>r.Name=="Bedroom 3")
            .Outline.Count>=6,
            "Rotated or mirrored upstairs wing must become an L-shaped Bedroom 3.");
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(house))<0.01);
        Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(
            BuildingStoreys.GetFloor(house,1)))<0.01);
        Assert(!house.Rooms.Single(r=>r.Type=="Hallway").Outline.SequenceEqual(
            house.UpperFloors[0].Rooms.Single(r=>r.Type=="Hallway").Outline));
    }
});


Check("Side-landing preserves a genuinely custom staircase instead of snapping it", () =>
{
    var shape=new List<Point2>
    {
        new(0,0),new(10.5,0),new(10.5,6),
        new(12.5,6),new(12.5,10),new(0,10)
    };
    var customFlight=new StaircaseDefinition
    {
        LowerFloorIndex=0,Centre=new(2.8,0.8),
        Length=2.8,Width=0.9,LandingLength=1,RotationDegrees=270
    };
    var blank=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=shape.ToList(),
        UpperFloors=[new StoreyDefinition{Perimeter=shape.ToList()}],
        Staircases=[customFlight.Copy()]
    };
    Assert(BuildingStoreys.Validate(blank,out var reason),reason);
    Assert(SideLandingRpgHousePlanner.TryGenerate(blank,9,
        out var planned,out reason),reason);
    Assert(planned.Staircases.Single().Id==customFlight.Id &&
        planned.Staircases.Single().Centre.DistanceTo(customFlight.Centre)<1e-7,
        "Do not auto-move custom flights that were not at the old 1.1m default.");
});


Check("AI layout improvement transfers a hallway dead end into Bedroom 3", () =>
{
    List<Point2> rect(double x0,double y0,double x1,double y1)=>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var hall=new RoomDefinition
    {
        Name="Upper cross hall",Type="Hallway",
        Outline=[
            new(0,4.9),new(10.5,4.9),
            new(10.5,6),new(12.5,6),
            new(12.5,6.5),new(0,6.5)
        ]
    };
    var bedroom=new RoomDefinition
    {
        Name="Bedroom 3",Type="Bedroom",
        Outline=rect(6.25,6.5,12.5,10)
    };
    var door=new DoorDefinition
    {
        Centre=new Point2(9.375,6.5),Width=0.9
    };
    var original=new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=rect(0,0,12.5,10),
        Rooms=[hall,bedroom],
        Doors=[door]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    string goal="Give Bedroom 3 the unused northeast upper-hall patch.";
    string prompt=AiLayoutImprover.BuildPrompt(original,0,goal);
    Assert(prompt.Contains(hall.Id.ToString()) &&
        prompt.Contains(bedroom.Id.ToString()) &&
        prompt.Contains(goal) && prompt.Contains("metres") &&
        prompt.Contains("doorways"),
        "Prompt must describe true geometry, IDs, units and the goal.");
    string Proposal(double x0,double y0,double x1,double y1,
        Guid? from=null,Guid? to=null,int floor=0) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            floorIndex=floor,
            fromRoomId=(from??hall.Id).ToString(),
            toRoomId=(to??bedroom.Id).ToString(),
            xMin=x0,yMin=y0,xMax=x1,yMax=y1,
            reason="Recover redundant corridor nook"
        });
    Assert(AiLayoutImprover.TryApply(original,0,
        Proposal(10.5,6,12.5,6.5),out var suggestion,out reason),reason);
    var improved=suggestion.Building;
    Assert(suggestion.Description.StartsWith("AI IMPROVEMENT DRAFT"));
    Assert(BuildingStoreys.Validate(improved,out reason),reason);
    Assert(improved.Rooms.Count==2 &&
        improved.Rooms.All(r=>original.Rooms.Any(old=>old.Id==r.Id)),
        "Edit must retain all room IDs and room count.");
    Assert(improved.Doors.Single().Id==door.Id &&
        improved.Doors.Single().Centre.DistanceTo(door.Centre)<1e-8);
    Assert(improved.Rooms.Single(r=>r.Id==bedroom.Id).Outline.Count>=6);
    Assert(PolygonGeometry.ContainsPoint(improved.Rooms.Single(r=>r.Id==bedroom.Id)
        .Outline,new Point2(11.5,6.2)));
    Assert(!PolygonGeometry.ContainsPoint(improved.Rooms.Single(r=>r.Id==hall.Id)
        .Outline,new Point2(11.5,6.2)));
    Equal(PolygonGeometry.Area(improved.Rooms.Single(r=>r.Id==bedroom.Id).Outline) -
        PolygonGeometry.Area(bedroom.Outline),1);
    Equal(PolygonGeometry.Area(hall.Outline) -
        PolygonGeometry.Area(improved.Rooms.Single(r=>r.Id==hall.Id).Outline),1);
    Assert(PolygonGeometry.ContainsPoint(hall.Outline,new Point2(11.5,6.2)),
        "Source document must not be mutated before explicit acceptance.");
    Assert(!AiLayoutImprover.TryApply(original,0,
        Proposal(6.25,6.5,8,7),out _,out reason),
        "Reject a rectangle that is outside the hallway.");
    Assert(!AiLayoutImprover.TryApply(original,0,
        Proposal(7,4.9,8,6.5),out _,out reason),
        "Reject a patch splitting hallway into disconnected parts.");
    Assert(!AiLayoutImprover.TryApply(original,0,
        Proposal(10.5,6,12.5,6.5,from:Guid.NewGuid()),out _,out reason),
        "Reject hallucinated source room IDs.");
    Assert(!AiLayoutImprover.TryApply(original,0,
        Proposal(10.5,6,12.5,6.5,floor:1),out _,out reason),
        "Reject hallucinated floor index.");
    Assert(!AiLayoutImprover.TryApply(original,0,"not json",out _,out reason),
        "Reject non-JSON model output.");

    var blocked=BuildingStoreys.Copy(original);
    blocked.Doors.Add(new DoorDefinition
    {
        Centre=new Point2(11.5,6.5),Width=0.9
    });
    Assert(BuildingStoreys.Validate(blocked,out reason),reason);
    Assert(!AiLayoutImprover.TryApply(blocked,0,
        Proposal(10.5,6,12.5,6.5),out _,out reason),
        "Reject removal of an existing wall that contains a doorway.");
});


Check("AI candidate shortlist avoids invented wall geometry and IDs", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var hall=new RoomDefinition
    {
        Name="Upper cross hall",Type="Hallway",
        Outline=[
            new(0,4.9),new(10.5,4.9),
            new(10.5,6),new(12.5,6),
            new(12.5,6.5),new(0,6.5)
        ]
    };
    var bedroom=new RoomDefinition
    {
        Name="Bedroom 3",Type="Bedroom",Outline=R(6.25,6.5,12.5,10)
    };
    var entrance=new DoorDefinition
    {
        Centre=new Point2(9.375,6.5),Width=0.9
    };
    var original=new BuildingDocument
    {
        SchemaVersion=2,
        Perimeter=R(0,0,12.5,10),
        Rooms=[hall,bedroom],
        Doors=[entrance]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    var candidates=AiLayoutCandidatePlanner.FindCandidates(original,0);
    Assert(candidates.Count>0,
        "C# must find the legitimate 2m x 0.5m Bedroom 3 corner without AI coordinates.");
    var candidate=candidates.FirstOrDefault(c=>
        c.Transfer.FromRoomId==hall.Id && c.Transfer.ToRoomId==bedroom.Id &&
        Math.Abs(c.Transfer.XMin-10.5)<1e-5 &&
        Math.Abs(c.Transfer.XMax-12.5)<1e-5 &&
        Math.Abs(c.Transfer.YMin-6)<1e-5 &&
        Math.Abs(c.Transfer.YMax-6.5)<1e-5);
    Assert(candidate is not null,
        "The exact example12 upper hall dead-end should be an available candidate.");
    Assert(candidates.All(c=>
        AiLayoutImprover.TryApply(original,0,
            System.Text.Json.JsonSerializer.Serialize(c.Transfer),out _,out _)),
        "Every shortlisted transfer must pass the complete C# validator.");

    var prompt=AiLayoutCandidatePlanner.BuildSelectionPrompt(original,0,
        "Give Bedroom 3 the unused northeast corner.",candidates);
    Assert(prompt.Contains("candidateId") &&
        prompt.Contains("Bedroom 3") &&
        prompt.Contains("already-validated",StringComparison.OrdinalIgnoreCase) &&
        !prompt.Contains("fromRoomId"),
        "Language model should select a candidate, not compose room IDs.");

    var choice=System.Text.Json.JsonSerializer.Serialize(new
    {
        candidateId=candidate!.Id,
        reason="This is the unused northeast corridor tip."
    });
    Assert(AiLayoutCandidatePlanner.TryApplySelection(
        original,0,choice,candidates,out var proposed,out reason),reason);
    Assert(BuildingStoreys.Validate(proposed.Building,out reason),reason);
    Assert(PolygonGeometry.ContainsPoint(
        proposed.Building.Rooms.Single(r=>r.Name=="Bedroom 3").Outline,
        new Point2(11.5,6.2)));
    Assert(!PolygonGeometry.ContainsPoint(
        proposed.Building.Rooms.Single(r=>r.Type=="Hallway").Outline,
        new Point2(11.5,6.2)));
    Assert(original.Rooms.Single(r=>r.Type=="Hallway")
        .Outline.SequenceEqual(hall.Outline),
        "AI selection must not mutate the original design.");
    Assert(!AiLayoutCandidatePlanner.TryApplySelection(original,0,
        "{\"candidateId\":9999,\"reason\":\"Guess\"}",candidates,
        out _,out reason) && reason.Contains("unknown candidate"),
        "Reject hallucinated candidate IDs explicitly.");
    Assert(!AiLayoutCandidatePlanner.TryApplySelection(original,0,
        "not json",candidates,out _,out _),
        "Reject non-JSON model responses.");

    var alreadyFixed=BuildingStoreys.Copy(proposed.Building);
    var leftovers=AiLayoutCandidatePlanner.FindCandidates(alreadyFixed,0);
    Assert(!leftovers.Any(c=>c.Transfer.ToRoomId==bedroom.Id &&
        c.Transfer.XMin>=10.5),
        "Once recovered, the same hallway corner must not be offered again.");
    var blocked=BuildingStoreys.Copy(original);
    blocked.Doors.Add(new DoorDefinition
    {
        Centre=new Point2(11.5,6.5),Width=0.9
    });
    Assert(BuildingStoreys.Validate(blocked,out reason),reason);
    var blockedCandidates=AiLayoutCandidatePlanner.FindCandidates(blocked,0);
    Assert(!blockedCandidates.Any(c=>c.Transfer.ToRoomId==bedroom.Id &&
        c.Transfer.XMin>=10.5),
        "The validator must not shortlist a patch that destroys an existing door.");
});


Check("AI shared-wall resize offers useful validated room changes without Ollama", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1)=>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var study=new RoomDefinition
    {
        Name="Study",Type="Study",Outline=R(0,0,4,5)
    };
    var bedroom=new RoomDefinition
    {
        Name="Bedroom",Type="Bedroom",Outline=R(4,0,10,5)
    };
    var outsideDoor=new DoorDefinition
    {
        Centre=new Point2(0,2.5),Width=0.9
    };
    var original=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=R(0,0,10,5),
        Rooms=[study,bedroom],Doors=[outsideDoor]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    Assert(AiLayoutCandidatePlanner.FindCandidates(original,0).Count==0,
        "Old corner-only planner cannot improve a building with no hallways.");
    var changes=AiRoomResizePlanner.FindCandidates(original,0);
    Assert(changes.Count>=2,
        "New planner should find both directions for a 5m shared wall.");
    var studyGrowth=changes.FirstOrDefault(c=>
        c.GainingRoom=="Study" && c.LosingRoom=="Bedroom");
    Assert(studyGrowth is not null,"Study should gain space from the larger bedroom.");
    Assert(studyGrowth!.AreaSquareMetres>=1.5 &&
        studyGrowth.GainingAreaAfter>studyGrowth.GainingAreaBefore);
    var proposed=studyGrowth.Suggestion.Building;
    Assert(BuildingStoreys.Validate(proposed,out reason),reason);
    Assert(proposed.Rooms.Select(r=>r.Id)
        .OrderBy(id=>id).SequenceEqual(original.Rooms.Select(r=>r.Id).OrderBy(id=>id)));
    Assert(proposed.Doors.Single().Id==outsideDoor.Id &&
        proposed.Doors.Single().Centre.DistanceTo(outsideDoor.Centre)<1e-7,
        "Do not change existing openings when moving this internal partition.");
    Equal(PolygonGeometry.Area(proposed.Rooms.Single(r=>r.Id==study.Id).Outline)+
        PolygonGeometry.Area(proposed.Rooms.Single(r=>r.Id==bedroom.Id).Outline),50);
    Equal(PolygonGeometry.Area(study.Outline),20); // source unchanged
    var options=AiLayoutRecommendationPlanner.FindOptions(original,0);
    Assert(options.Count>0 && options.All(o=>o.Kind=="Resize adjacent rooms"),
        "No hallway needed: the combined planner exposes wall shifts.");
    string prompt=AiLayoutRecommendationPlanner.BuildSelectionPrompt(original,0,
        "Enlarge the study by reducing the oversized bedroom.",options);
    Assert(prompt.Contains("Study") && prompt.Contains("Bedroom") &&
        prompt.Contains("shared internal wall") &&
        prompt.Contains("candidateId") &&
        !prompt.Contains(study.Id.ToString()),
        "The LLM must choose meaningful numbered changes, not room IDs.");
    var correct=options.First(o=>o.Summary.StartsWith("Enlarge Study"));
    var answer=System.Text.Json.JsonSerializer.Serialize(new
    {
        candidateId=correct.Id,reason="Makes the smaller study more usable"
    });
    Assert(AiLayoutRecommendationPlanner.TryApplySelection(
        original,0,answer,options,out var choice,out reason),reason);
    Assert(choice.Description.StartsWith("AI IMPROVEMENT DRAFT") &&
        choice.Description.Contains("Resize adjacent rooms") &&
        choice.Building.Rooms.Single(r=>r.Id==study.Id).Outline
            .Max(p=>p.X)>4);
    Assert(!AiLayoutRecommendationPlanner.TryApplySelection(
        original,0,"{\"candidateId\":9999,\"reason\":\"Fake\"}",
        options,out _,out reason) && reason.Contains("unknown candidate"),
        "Invalid model IDs must never execute arbitrary geometry.");
    Assert(!AiLayoutRecommendationPlanner.TryApplySelection(
        original,0,"not json",options,out _,out _));
});

Check("AI room resizing preserves door-bearing shared walls and floor validity", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1)=>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var first=new RoomDefinition
    {
        Name="Kitchen",Type="Kitchen",Outline=R(0,0,5,4)
    };
    var second=new RoomDefinition
    {
        Name="Dining room",Type="Dining room",Outline=R(0,4,5,10)
    };
    var original=new BuildingDocument
    {
        SchemaVersion=2,Perimeter=R(0,0,5,10),
        Rooms=[first,second]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    var options=AiRoomResizePlanner.FindCandidates(original,0);
    Assert(options.Count>0 && options.Any(o=>o.GainingRoom=="Kitchen"),
        "Horizontal full-wall adjacency must support the same validated shifts.");
    Assert(options.All(o=>BuildingStoreys.Validate(o.Suggestion.Building,out _)));
    var blocked=BuildingStoreys.Copy(original);
    blocked.Doors.Add(new DoorDefinition
    {
        Centre=new Point2(2.5,4),Width=0.9
    });
    Assert(BuildingStoreys.Validate(blocked,out reason),reason);
    Assert(AiRoomResizePlanner.FindCandidates(blocked,0).Count==0,
        "AI must not move a shared partition that contains a doorway.");
    Assert(AiLayoutRecommendationPlanner.FindOptions(blocked,0).Count==0,
        "No-model preview should be told no safe edit exists.");
    Assert(PolygonGeometry.Area(original.Rooms[0].Outline)==20 &&
        PolygonGeometry.Area(original.Rooms[1].Outline)==30,
        "Rejected changes must leave source room areas untouched.");
});


Check("AI resize recognises the slightly slanted two-room example13 layout", () =>
{
    // Exact room coordinates from example13.json. Neither room was a
    // perfect rectangle; Room 1 starts only 1.52m wide at its narrow end.
    var room1=new RoomDefinition
    {
        Id=Guid.Parse("c3fa9ec5-26b6-410a-b79c-43aaadf279e4"),
        Name="Room 1",Type="Other",
        Outline=[
            new(9.2,0),new(9.144,7.62),
            new(7.62,7.62),new(7.62,0)
        ]
    };
    var room2=new RoomDefinition
    {
        Id=Guid.Parse("07e7b5c8-5da1-47e6-a5c8-90b64dfb1723"),
        Name="Room 2",Type="Other",
        Outline=[
            new(9.2,0),new(9.144,7.62),
            new(15.24,7.62),new(15.24,0)
        ]
    };
    var original=new BuildingDocument
    {
        SchemaVersion=1,GridFeet=5,
        Perimeter=[new(7.62,0),new(7.62,7.62),
                   new(15.24,7.62),new(15.24,0)],
        Rooms=[room1,room2]
    };
    Assert(BuildingStoreys.Validate(original,out var reason),reason);
    Assert(room1.Outline.Max(p=>p.X)-room1.Outline.Min(p=>p.X)<2.4,
        "The test fixture must reproduce the undersized first room.");

    var changes=AiRoomResizePlanner.FindCandidates(original,0);
    Assert(changes.Any(c=>c.GainingRoom=="Room 1" &&
        c.LosingRoom=="Room 2"),
        "Small rooms with a slightly slanted full shared wall should be eligible.");

    var enlarge=changes.First(c=>c.GainingRoom=="Room 1");
    var suggested=enlarge.Suggestion.Building;
    Assert(BuildingStoreys.Validate(suggested,out reason),reason);
    var grown=suggested.Rooms.Single(r=>r.Id==room1.Id);
    var shrunk=suggested.Rooms.Single(r=>r.Id==room2.Id);
    Assert(PolygonGeometry.Area(grown.Outline)>PolygonGeometry.Area(room1.Outline),
        "Room 1 must become more usable.");
    Assert(grown.Outline.Min(p=>p.X)==7.62 &&
        grown.Outline.Max(p=>p.X)>=9.744-1e-5,
        "Existing exterior wall remains fixed and shared wall moves at least 0.6m.");
    Assert(Math.Abs((grown.Outline[0].X-grown.Outline[1].X)-0.056)<1e-5,
        "Moving the partition must retain its original slight lean.");
    Assert(Math.Abs(PolygonGeometry.Area(grown.Outline)+
        PolygonGeometry.Area(shrunk.Outline)-
        PolygonGeometry.Area(room1.Outline)-
        PolygonGeometry.Area(room2.Outline))<0.001,
        "Transferring area cannot lose or invent usable floor space.");
    Assert(suggested.Perimeter.SequenceEqual(original.Perimeter) &&
        original.Rooms.Single(r=>r.Id==room1.Id).Outline[0].X==9.2,
        "The source and exterior perimeter are unchanged before acceptance.");

    var choices=AiLayoutRecommendationPlanner.FindOptions(original,0);
    Assert(choices.Any(c=>c.Kind=="Resize adjacent rooms" &&
        c.Summary.StartsWith("Enlarge Room 1")),
        "The AI should receive a numbered valid option for this exact file.");
    var selected=choices.First(c=>c.Summary.StartsWith("Enlarge Room 1"));
    var response=System.Text.Json.JsonSerializer.Serialize(new
    {
        candidateId=selected.Id,reason="Give the narrow room more space"
    });
    Assert(AiLayoutRecommendationPlanner.TryApplySelection(
        original,0,response,choices,out var proposal,out reason),reason);
    Assert(proposal.Description.Contains("Room 1") &&
        BuildingStoreys.Validate(proposal.Building,out reason),reason);

    var protectedWall=BuildingStoreys.Copy(original);
    protectedWall.Doors.Add(new DoorDefinition
    {
        Centre=new Point2(9.172,3.81),Width=0.9
    });
    Assert(BuildingStoreys.Validate(protectedWall,out reason),reason);
    Assert(AiRoomResizePlanner.FindCandidates(protectedWall,0).Count==0,
        "Existing door on the slanted partition must prevent silent relocation.");
});


Check("AI can improve rooms with only a partial shared internal wall", () =>
{
    List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var main = new RoomDefinition
    {
        Name = "Study", Type = "Study", Outline = Rect(0,0,5,6)
    };
    var annex = new RoomDefinition
    {
        Name = "Bedroom", Type = "Bedroom", Outline = Rect(5,2,9,6)
    };
    var building = new BuildingDocument
    {
        Perimeter = Rect(0,0,9,6), Rooms = [main,annex]
    };
    Assert(BuildingStoreys.Validate(building,out var reason),reason);
    Assert(AiRoomResizePlanner.FindCandidates(building,0).Count == 0,
        "The original shared-full-wall planner cannot resize a partial partition.");
    var choices = AiPartialRoomResizePlanner.FindCandidates(building,0);
    Assert(choices.Count >= 2 && choices.Any(c=>c.GainingRoom=="Study") &&
        choices.Any(c=>c.GainingRoom=="Bedroom"),
        "Both directions of a partial shared wall must offer safe transfers.");
    var growStudy = choices.First(c=>c.GainingRoom=="Study");
    var changed = growStudy.Suggestion.Building;
    Assert(BuildingStoreys.Validate(changed,out reason),reason);
    Assert(changed.Rooms.Single(r=>r.Id==main.Id).Outline.Count>=6,
        "The study should become L-shaped when it acquires only the upper strip.");
    Assert(changed.Rooms.Single(r=>r.Id==main.Id).Outline
        .Any(p=>p.X>5+0.4), "Study must gain width along the partial wall.");
    Assert(changed.Rooms.Single(r=>r.Id==annex.Id).Outline.Count==4,
        "The donor bedroom should remain rectangular.");
    Assert(PolygonGeometry.Area(main.Outline)==30 &&
        PolygonGeometry.Area(annex.Outline)==16,
        "Unaccepted proposals must not modify source outlines.");
    Assert(changed.Perimeter.SequenceEqual(building.Perimeter) &&
        Math.Abs(PolygonGeometry.Area(changed.Rooms[0].Outline)+
          PolygonGeometry.Area(changed.Rooms[1].Outline)-46)<0.001,
        "Combined room area and exterior outline must be preserved.");

    var combined = AiLayoutRecommendationPlanner.FindOptions(building,0);
    Assert(combined.Any(o => o.Kind=="Resize partial / L-shaped rooms"),
        "Partially shared edges must reach the Ollama candidate shortlist.");
    var chosen = combined.First(o => o.Kind=="Resize partial / L-shaped rooms"
        && o.Summary.StartsWith("Enlarge Study"));
    string answer = System.Text.Json.JsonSerializer.Serialize(
        new { candidateId=chosen.Id,reason="Make the study larger" });
    Assert(AiLayoutRecommendationPlanner.TryApplySelection(
        building,0,answer,combined,out var proposal,out reason),reason);
    Assert(BuildingStoreys.Validate(proposal.Building,out reason),reason);
});

Check("AI can reshape orthogonal L-shaped rooms without affecting existing doors", () =>
{
    List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var irregular = new RoomDefinition
    {
        Name="L-shaped living room",Type="Living room",
        Outline=[
            new(0,0),new(6,0),new(6,2),
            new(4,2),new(4,8),new(0,8)
        ]
    };
    var bedroom = new RoomDefinition
    {
        Name="Bedroom",Type="Bedroom",Outline=Rect(4,2,10,8)
    };
    var building = new BuildingDocument
    {
        Perimeter=Rect(0,0,10,8),Rooms=[irregular,bedroom],
        Doors=[new DoorDefinition
        {
            Centre=new Point2(0,4),Width=0.9
        }]
    };
    Assert(BuildingStoreys.Validate(building,out var reason),reason);
    var options=AiPartialRoomResizePlanner.FindCandidates(building,0);
    Assert(options.Any(c=>c.GainingRoom=="L-shaped living room"),
        "Existing L-shaped living rooms should gain a validated area strip.");
    var winning=options.First(c=>c.GainingRoom=="L-shaped living room");
    var floor=winning.Suggestion.Building;
    Assert(BuildingStoreys.Validate(floor,out reason),reason);
    Assert(floor.Doors.Single().Id==building.Doors.Single().Id &&
        floor.Doors.Single().Centre.DistanceTo(building.Doors.Single().Centre)<1e-7,
        "External door must not be moved or deleted.");
    Assert(PolygonGeometry.Area(floor.Rooms.Single(r=>r.Id==irregular.Id).Outline)
        > PolygonGeometry.Area(irregular.Outline));
    Assert(!BuildingGenerator.SameRoomLayout(building,floor,[0]));
    Assert(PolygonGeometry.Area(irregular.Outline)==36,
        "The old L shape must remain unchanged until accepting the preview.");

    var protectedDoor=BuildingStoreys.Copy(building);
    protectedDoor.Doors.Add(new DoorDefinition
    {
        Centre=new Point2(4,5),Width=0.9
    });
    Assert(BuildingStoreys.Validate(protectedDoor,out reason),reason);
    var protectedChoices=AiPartialRoomResizePlanner.FindCandidates(
        protectedDoor,0);
    Assert(protectedChoices.All(c =>
        BuildingStoreys.Validate(c.Suggestion.Building,out _) &&
        c.Suggestion.Building.Doors.Count==protectedDoor.Doors.Count &&
        c.Suggestion.Building.Doors.All(d =>
            protectedDoor.Doors.Any(original =>
                original.Id==d.Id &&
                original.Centre.DistanceTo(d.Centre)<1e-7))),
        "Other eligible shared edges may still move, but no candidate may " +
        "relocate/delete a doorway or invalidate the floor.");
});


Check("AI partial-wall demo fixture is completely allocated and improvable", () =>
{
    List<Point2> R(double x0,double y0,double x1,double y1) =>
        [new(x0,y0),new(x1,y0),new(x1,y1),new(x0,y1)];
    var living=new RoomDefinition
    {
        Id=Guid.Parse("8c6ad1c9-cad4-4a45-a7b8-2a329d7d9d21"),
        Name="L-shaped Living Room",Type="Living room",
        Outline=[new(0,0),new(10,0),new(10,2),
                 new(4,2),new(4,8),new(0,8)]
    };
    var bedroom=new RoomDefinition
    {
        Id=Guid.Parse("b6c7cfd7-bc4d-4b8d-b681-28157b8dba22"),
        Name="Bedroom",Type="Bedroom",
        Outline=R(4,2,10,8)
    };
    var full=new BuildingDocument
    {
        SchemaVersion=1,
        Perimeter=R(0,0,10,8),
        Rooms=[living,bedroom]
    };
    Assert(BuildingStoreys.Validate(full,out var reason),reason);
    Assert(Math.Abs(RoomLayoutPlanner.UnassignedArea(full))<0.01,
        "Demo should have no unallocated floor area.");
    var proposed=AiPartialRoomResizePlanner.FindCandidates(full,0);
    Assert(proposed.Count>0,
        "The downloadable demo with two shared wall segments must be improvable.");
    Assert(proposed.All(c =>
        BuildingStoreys.Validate(c.Suggestion.Building,out _) &&
        Math.Abs(RoomLayoutPlanner.UnassignedArea(c.Suggestion.Building))<0.01),
        "Every proposed demo layout must conserve floor coverage and geometry.");
    var combined=AiLayoutRecommendationPlanner.FindOptions(full,0);
    Assert(combined.Any(o=>o.Kind=="Resize partial / L-shaped rooms"),
        "Ollama's numbered candidate list must include the demo geometry.");
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
