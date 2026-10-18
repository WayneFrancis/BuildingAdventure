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
            Assert(!PolygonGeometry.ContainsPoint(hall.Outline, first.Centre),
                "The stairs cannot block the hall's walking route.");
            Assert(floor.Doors.Any(d => Enumerable.Range(0, hall.Outline.Count).Any(i =>
                PolygonGeometry.ClosestPointOnSegment(d.Centre, hall.Outline[i],
                    hall.Outline[(i + 1) % hall.Outline.Count])
                    .DistanceTo(d.Centre) < 0.01)),
                "The hall needs at least one doorway.");
        }
        Assert(BuildingStoreys.Validate(building, out reason), reason);
    }
});

Check("Hall-only multi-floor stair suggestions never block a pre-existing corridor", () =>
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
    Assert(!BuildingGenerator.TrySuggest(source, [0, 1], 1223, options,
        out _, out reason));
    Assert(source.Staircases.Count == 0);
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
            Assert(!PolygonGeometry.ContainsPoint(
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

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
