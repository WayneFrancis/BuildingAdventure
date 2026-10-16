using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingAdventure.Core.Buildings;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Desktop.Controls;

public enum EditorTool { Select, DrawPerimeter, SplitPerimeter, RemoveCorner, DrawRoom, SplitRoom, MergeRooms, MoveWall, AddDoor, PlaceStairs, Pan }

/// <summary>Interactive metre-based editor for a single building floor.</summary>
public sealed class FloorPlanSurface : FrameworkElement
{
    private sealed record EditorSnapshot(List<Point2> Perimeter,
        List<RoomDefinition> Rooms, List<DoorDefinition> Doors);

    private List<Point2> _outline = new();
    private List<RoomDefinition> _rooms = new();
    private List<DoorDefinition> _doors = new();
    private readonly List<Point2> _draft = new();
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();

    private double _pixelsPerMetre = 40;
    private Point _origin = new(120, 110);
    private Point _lastMouse;
    private Point2? _draftCursor;

    private Guid? _selectedRoomId;
    private Guid? _selectedDoorId;
    private Guid? _selectedStairId;
    private List<StaircaseDefinition> _staircases = new();
    private int _stairFloorIndex;
    private int _selectedVertex = -1;
    private bool _selectedPerimeterVertex;
    private bool _panning;
    private bool _draggingVertex;
    private EditorSnapshot? _beforeDrag;
    private Point2 _dragOriginalPoint;
    private Point2? _partitionStart;
    private Guid? _mergePrimaryRoomId;
    private WallSegment? _movingWall;
    private Point2 _moveWallMouseStart;
    private bool _movingWallNow;
    private bool _wallMoveAccepted;
    private string _wallMoveError = "";

    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public event Action<string>? Hint;
    public event Action<Point2>? StairPlacementRequested;

    /// <summary>Optional whole-building constraint check for the edited floor.</summary>
    public Func<BuildingDocument, (bool Valid, string Reason)>? AdditionalValidator { get; set; }

    public FloorPlanSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        Tool = EditorTool.DrawPerimeter;
    }

    public EditorTool Tool { get; private set; }
    public bool SnapToGrid { get; set; } = true;
    public double PixelsPerMetre => _pixelsPerMetre;
    public bool IsDrawing => _draft.Count > 0;
    public bool CanUndo => _draft.Count > 0 || _partitionStart.HasValue || _mergePrimaryRoomId.HasValue || _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0 && _draft.Count == 0 && !_partitionStart.HasValue && !_mergePrimaryRoomId.HasValue;
    public bool HasSelectedDoor => _selectedDoorId.HasValue;
    public Guid? SelectedStaircaseId => _selectedStairId;

    /// <summary>
    /// Display all flights touching the active floor. Staircase changes are
    /// owned by the main window's multi-floor document, not the local undo
    /// history of an individual floor.
    /// </summary>
    public void SetStairOverlay(IReadOnlyList<StaircaseDefinition> staircases, int floorIndex)
    {
        _staircases = staircases.Select(s => s.Copy()).ToList();
        _stairFloorIndex = floorIndex;
        _selectedStairId = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<Point2> GetOutline() => _outline.ToList().AsReadOnly();
    public IReadOnlyList<RoomDefinition> GetRooms() => _rooms.Select(r => r.Copy()).ToList().AsReadOnly();
    public IReadOnlyList<DoorDefinition> GetDoors() => _doors.Select(d => d.Copy()).ToList().AsReadOnly();
    public RoomDefinition? SelectedRoom => _rooms.FirstOrDefault(r => r.Id == _selectedRoomId)?.Copy();

    private BuildingDocument Document() => new()
    {
        Perimeter = _outline.ToList(),
        Rooms = _rooms.Select(r => r.Copy()).ToList(),
        Doors = _doors.Select(d => d.Copy()).ToList()
    };

    public void LoadOutline(IReadOnlyList<Point2> outline) =>
        LoadDocument(new BuildingDocument { Perimeter = outline.ToList() });

    public void LoadDocument(BuildingDocument document)
    {
        _outline = document.Perimeter.ToList();
        _rooms = document.Rooms.Select(r => r.Copy()).ToList();
        _doors = document.Doors.Select(d => d.Copy()).ToList();
        _draft.Clear();
        _draftCursor = null;
        _partitionStart = null;
        _mergePrimaryRoomId = null;
        _movingWall = null;
        _movingWallNow = false;
        _selectedVertex = -1;
        _selectedRoomId = null;
        _selectedDoorId = null;
        _selectedStairId = null;
        _undo.Clear();
        _redo.Clear();
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTool(EditorTool tool)
    {
        if (Tool != tool)
        {
            _draft.Clear();
            _draftCursor = null;
            _partitionStart = null;
            _mergePrimaryRoomId = null;
        }
        Tool = tool;
        if (tool == EditorTool.MergeRooms &&
            _selectedRoomId.HasValue &&
            _rooms.Any(room => room.Id == _selectedRoomId.Value))
            _mergePrimaryRoomId = _selectedRoomId;
        Cursor = DefaultCursor();
        Say(tool switch
        {
            EditorTool.DrawPerimeter => "Click exterior corners; click the starting point or Finish to close.",
            EditorTool.SplitPerimeter => "Click any exterior wall to insert a corner. Select / Edit to drag it into a new shape.",
            EditorTool.RemoveCorner => "Click an exterior or room corner to merge its two adjoining walls.",
            EditorTool.DrawRoom => "Click room corners inside the building, then click the first or Finish.",
            EditorTool.SplitRoom => "Select a room, then click two points on its boundary to divide it.",
            EditorTool.MergeRooms => _mergePrimaryRoomId.HasValue
                ? "First room selected. Click an adjoining second room to remove their common wall."
                : "Click the first room, then an adjoining second room to merge them.",
            EditorTool.MoveWall => "Drag a wall perpendicular to itself. Connected corners and doors will follow.",
            EditorTool.AddDoor => "Click an existing wall to add a 0.9 m doorway.",
            EditorTool.PlaceStairs => "Click inside the current floor to place a stair flight to the floor above.",
            EditorTool.Select => "Click a room to select it, drag a corner, or double-click a wall to add a corner.",
            _ => "Drag to pan; use the mouse wheel to zoom."
        });
        InvalidateVisual();
    }

    private Cursor DefaultCursor() => Tool switch
    {
        EditorTool.Pan => Cursors.Hand,
        EditorTool.Select => Cursors.Arrow,
        _ => Cursors.Cross
    };

    public void CancelDrawing()
    {
        if (_draft.Count == 0 && !_partitionStart.HasValue && !_mergePrimaryRoomId.HasValue) return;
        _draft.Clear();
        _draftCursor = null;
        _partitionStart = null;
        _mergePrimaryRoomId = null;
        Say("Unfinished operation cancelled; existing geometry was kept.");
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool FinishDrawing()
    {
        if (_draft.Count == 0) return false;
        if (Tool != EditorTool.DrawRoom && Tool != EditorTool.DrawPerimeter)
            return false;
        if (!PolygonGeometry.IsValidOutline(_draft, out var reason))
        {
            Say($"Cannot finish: {reason}");
            return false;
        }

        var previous = Snapshot();
        if (Tool == EditorTool.DrawPerimeter)
        {
            _outline = _draft.ToList();
        }
        else
        {
            var room = new RoomDefinition
            {
                Name = $"Room {_rooms.Count + 1}",
                Type = "Other",
                Outline = _draft.ToList()
            };
            _rooms.Add(room);
            _selectedRoomId = room.Id;
            _selectedDoorId = null;
        }

        if (!Validate(out var invalid))
        {
            Restore(previous);
            Say($"Cannot finish: {invalid}");
            return false;
        }

        PushUndo(previous);
        _draft.Clear();
        _draftCursor = null;
        _selectedVertex = -1;
        Tool = EditorTool.Select;
        Cursor = DefaultCursor();
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Say("Drawing completed. Select rooms to name them or drag their corners.");
        return true;
    }

    public void SetSelectedRoomProperties(string name, string type)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
        if (room is null) return;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            string.IsNullOrWhiteSpace(type) || type.Length > 80)
        {
            Say("Room name must be 1–100 characters long.");
            return;
        }
        if (room.Name == name && room.Type == type) return;
        var previous = Snapshot();
        room.Name = name;
        room.Type = type;
        PushUndo(previous);
        Changed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSelectedRoom()
    {
        if (!_selectedRoomId.HasValue) return;
        var previous = Snapshot();
        _rooms.RemoveAll(r => r.Id == _selectedRoomId.Value);
        // Remove doors which were attached only to walls of this room.
        var walls = FloorGeometry.BuildWalls(_outline, _rooms);
        _doors.RemoveAll(d => !FloorGeometry.TryFindDoorWall(d, walls, out _));
        _selectedRoomId = null;
        _selectedVertex = -1;
        _selectedDoorId = null;
        PushUndo(previous);
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Say("Room deleted. Any doors on walls which no longer exist were removed.");
    }

    public void DeleteSelectedDoor()
    {
        if (!_selectedDoorId.HasValue) return;
        var previous = Snapshot();
        _doors.RemoveAll(d => d.Id == _selectedDoorId.Value);
        _selectedDoorId = null;
        PushUndo(previous);
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Say("Door deleted.");
    }

    /// <summary>
    /// Remove the selected polygon corner, merging its two incident walls.
    /// The core operation also updates connected rooms and door positions.
    /// </summary>
    public void DeleteSelectedVertex()
    {
        if (_selectedVertex < 0)
        {
            Say("Select a corner, or choose Remove Corner and click one.");
            return;
        }

        bool exterior = _selectedPerimeterVertex;
        Guid? roomId = exterior ? null : _selectedRoomId;
        var previous = Snapshot();

        if (!PerimeterEditing.TryRemoveCorner(Document(), exterior, roomId,
                _selectedVertex, out var updated, out var reason))
        {
            Say($"Cannot remove corner: {reason}");
            return;
        }

        _outline = updated.Perimeter;
        _rooms = updated.Rooms;
        _doors = updated.Doors;
        if (!Validate(out var buildingReason))
        {
            Restore(previous);
            Say($"Cannot remove corner: {buildingReason}");
            return;
        }
        _selectedVertex = -1;
        PushUndo(previous);
        RaiseChanged();
        Say("Corner removed; the adjacent walls are now one segment.");
    }

    public void Undo()
    {
        if (_draft.Count > 0)
        {
            _draft.RemoveAt(_draft.Count - 1);
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
            Say("Last unfinished corner removed.");
            return;
        }
        if (_partitionStart.HasValue)
        {
            _partitionStart = null;
            _draftCursor = null;
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
            Say("Room split starting point cancelled.");
            return;
        }
        if (_mergePrimaryRoomId.HasValue)
        {
            _mergePrimaryRoomId = null;
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
            Say("Room merge selection cancelled.");
            return;
        }
        if (_undo.Count == 0) return;
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        ClearSelection();
        RaiseChanged();
        Say("Undo.");
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        ClearSelection();
        RaiseChanged();
        Say("Redo.");
    }

    public void ZoomBy(double factor, Point? anchor = null)
    {
        Point pivot = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        double oldScale = _pixelsPerMetre;
        _pixelsPerMetre = Math.Clamp(oldScale * factor, 8, 250);
        double ratio = _pixelsPerMetre / oldScale;
        _origin = new Point(pivot.X - (pivot.X - _origin.X) * ratio,
                            pivot.Y - (pivot.Y - _origin.Y) * ratio);
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void FitToView()
    {
        if (ActualWidth < 10 || ActualHeight < 10) return;
        var points = _outline.Count > 0 ? _outline : _draft;
        if (points.Count == 0)
        {
            _pixelsPerMetre = 40;
            _origin = new Point(110, 100);
        }
        else
        {
            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);
            double width = Math.Max(2, maxX - minX);
            double height = Math.Max(2, maxY - minY);
            _pixelsPerMetre = Math.Clamp(
                Math.Min((ActualWidth - 110) / width, (ActualHeight - 110) / height),
                8, 250);
            _origin = new Point(ActualWidth / 2 - (minX + maxX) / 2 * _pixelsPerMetre,
                                ActualHeight / 2 - (minY + maxY) / 2 * _pixelsPerMetre);
        }
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        DrawGrid(dc);
        Brush[] fills =
        [
            new SolidColorBrush(Color.FromArgb(105, 146, 194, 237)),
            new SolidColorBrush(Color.FromArgb(105, 166, 215, 169)),
            new SolidColorBrush(Color.FromArgb(105, 242, 199, 136)),
            new SolidColorBrush(Color.FromArgb(105, 201, 171, 229)),
            new SolidColorBrush(Color.FromArgb(105, 234, 169, 185))
        ];

        if (_outline.Count >= 3)
        {
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(48, 157, 179, 200)),
                null, BuildGeometry(_outline, true));
        }

        for (int index = 0; index < _rooms.Count; index++)
        {
            var room = _rooms[index];
            if (room.Outline.Count < 3) continue;
            bool selected = room.Id == _selectedRoomId;
            dc.DrawGeometry(fills[index % fills.Length],
                selected ? new Pen(Brushes.DodgerBlue, 1.4) : null,
                BuildGeometry(room.Outline, true));
            Point2 labelPoint = FindRoomLabelPoint(room.Outline);
            Point centre = ToScreen(labelPoint);
            DrawText(dc, room.Name, new Point(centre.X + 5, centre.Y - 12),
                     13, selected ? Brushes.DarkBlue : Brushes.DarkSlateGray);
            DrawText(dc, $"{PolygonGeometry.Area(room.Outline):0.#} m²",
                     new Point(centre.X + 5, centre.Y + 4), 10, Brushes.DimGray);
        }

        if (_outline.Count > 0)
        {
            bool valid = PolygonGeometry.IsValidOutline(_outline, out _);
            dc.DrawGeometry(null,
                new Pen(valid ? Brushes.SteelBlue : Brushes.IndianRed, 3),
                BuildGeometry(_outline, true));
            DrawLengths(dc, _outline, true);
        }

        foreach (var wall in FloorGeometry.BuildWalls(_outline, _rooms))
        {
            if (!wall.IsExterior)
                dc.DrawLine(new Pen(Brushes.DarkSlateGray, 2.5),
                    ToScreen(wall.A), ToScreen(wall.B));
        }

        var walls = FloorGeometry.BuildWalls(_outline, _rooms);
        if (_movingWallNow && _movingWall is not null && _beforeDrag is not null)
        {
            // Draw the original wall as a dashed guide under the live preview.
            dc.DrawLine(new Pen(Brushes.DarkOrange, 1.5)
                { DashStyle = DashStyles.Dash },
                ToScreen(_movingWall.A), ToScreen(_movingWall.B));
        }
        foreach (var door in _doors)
        {
            if (!FloorGeometry.TryFindDoorWall(door, walls, out var wall) || wall is null)
                continue;
            double length = wall.Length;
            double dx = (wall.B.X - wall.A.X) / length;
            double dy = (wall.B.Y - wall.A.Y) / length;
            var hinge = new Point2(door.Centre.X - dx * door.Width / 2,
                                   door.Centre.Y - dy * door.Width / 2);
            var latch = new Point2(door.Centre.X + dx * door.Width / 2,
                                   door.Centre.Y + dy * door.Width / 2);
            var openEnd = new Point2(hinge.X - dy * door.Width, hinge.Y + dx * door.Width);
            dc.DrawLine(new Pen(Brushes.White, 6), ToScreen(hinge), ToScreen(latch));
            Pen doorPen = new(door.Id == _selectedDoorId ? Brushes.OrangeRed : Brushes.SaddleBrown, 2);
            dc.DrawLine(doorPen, ToScreen(hinge), ToScreen(openEnd));
            dc.DrawEllipse(Brushes.White, doorPen, ToScreen(hinge), 2.5, 2.5);
        }

        foreach (var stair in _staircases.Where(stair =>
            stair.LowerFloorIndex == _stairFloorIndex ||
            stair.LowerFloorIndex + 1 == _stairFloorIndex))
        {
            var footprint = BuildingStoreys.StairFootprint(stair);
            bool selected = stair.Id == _selectedStairId;
            bool goingUp = stair.LowerFloorIndex == _stairFloorIndex;
            var fill = goingUp
                ? new SolidColorBrush(Color.FromArgb(105, 97, 153, 202))
                : new SolidColorBrush(Color.FromArgb(100, 172, 139, 203));
            dc.DrawGeometry(fill,
                new Pen(selected ? Brushes.DarkOrange : Brushes.DarkSlateBlue, selected ? 3 : 2)
                { DashStyle = DashStyles.Dash },
                BuildGeometry(footprint, true));

            Point middle = ToScreen(stair.Centre);
            string label = goingUp ? $"UP to {stair.LowerFloorIndex + 2}"
                                   : $"DOWN to {stair.LowerFloorIndex + 1}";
            DrawText(dc, label, new Point(middle.X + 5, middle.Y - 8),
                12, Brushes.DarkSlateBlue);

            // Direction arrow along the staircase's local long axis.
            double angle = stair.RotationDegrees * Math.PI / 180;
            double vx = -Math.Sin(angle), vy = Math.Cos(angle);
            Point start = ToScreen(new Point2(
                stair.Centre.X - vx * stair.Length * 0.23,
                stair.Centre.Y - vy * stair.Length * 0.23));
            Point tip = ToScreen(new Point2(
                stair.Centre.X + vx * stair.Length * 0.23,
                stair.Centre.Y + vy * stair.Length * 0.23));
            var pen = new Pen(Brushes.DarkSlateBlue, 1.6);
            dc.DrawLine(pen, start, tip);
            Vector vector = tip - start;
            if (vector.Length > 1)
            {
                vector.Normalize();
                Vector side = new(-vector.Y, vector.X);
                dc.DrawLine(pen, tip, tip - vector * 9 + side * 5);
                dc.DrawLine(pen, tip, tip - vector * 9 - side * 5);
            }
        }

        if (Tool is EditorTool.Select or EditorTool.SplitPerimeter or EditorTool.RemoveCorner or EditorTool.MoveWall or EditorTool.SplitRoom or EditorTool.MergeRooms)
        {
            DrawVertices(dc, _outline, _selectedPerimeterVertex, Brushes.SteelBlue);
            foreach (var room in _rooms)
                if (Tool == EditorTool.RemoveCorner || room.Id == _selectedRoomId)
                    DrawVertices(dc, room.Outline,
                        room.Id == _selectedRoomId && !_selectedPerimeterVertex,
                        Brushes.DarkSlateBlue);
        }

        if (_draft.Count > 0)
        {
            dc.DrawGeometry(null,
                new Pen(Brushes.DarkOrange, 2.5) { DashStyle = DashStyles.Dash },
                BuildGeometry(_draft, false));
            foreach (var vertex in _draft)
                dc.DrawEllipse(Brushes.White, new Pen(Brushes.DarkOrange, 2),
                               ToScreen(vertex), 5, 5);
            if (_draftCursor.HasValue)
                dc.DrawLine(new Pen(Brushes.DarkOrange, 1.5) { DashStyle = DashStyles.Dash },
                    ToScreen(_draft[^1]), ToScreen(_draftCursor.Value));
            DrawLengths(dc, _draft, false);
        }

        if (_partitionStart.HasValue)
        {
            var beginning = ToScreen(_partitionStart.Value);
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.DarkOrange, 2.5),
                           beginning, 6, 6);
            if (_draftCursor.HasValue)
                dc.DrawLine(new Pen(Brushes.DarkOrange, 2)
                    { DashStyle = DashStyles.Dash },
                    beginning, ToScreen(_draftCursor.Value));
        }

        if (_outline.Count == 0 && _draft.Count == 0)
        {
            DrawText(dc, "Choose Draw Perimeter and click to place the first corner.",
                     new Point(20, 22), 15, Brushes.SlateGray);
        }
    }

    private void DrawVertices(DrawingContext dc, IReadOnlyList<Point2> outline,
        bool selectedGeometry, Brush brush)
    {
        for (int i = 0; i < outline.Count; i++)
        {
            Point screen = ToScreen(outline[i]);
            bool selected = selectedGeometry && i == _selectedVertex;
            dc.DrawEllipse(selected ? Brushes.Orange : Brushes.White,
                new Pen(selected ? Brushes.DarkOrange : brush, 2),
                screen, selected ? 7 : 5, selected ? 7 : 5);
        }
    }

    private static Point2 FindRoomLabelPoint(IReadOnlyList<Point2> outline)
    {
        var average = new Point2(outline.Average(v => v.X), outline.Average(v => v.Y));
        if (PolygonGeometry.ContainsPoint(outline, average)) return average;
        var a = outline[0];
        for (int i = 1; i < outline.Count - 1; i++)
        {
            var candidate = new Point2((a.X + outline[i].X + outline[i + 1].X) / 3,
                                       (a.Y + outline[i].Y + outline[i + 1].Y) / 3);
            if (PolygonGeometry.ContainsPoint(outline, candidate)) return candidate;
        }
        return a;
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(new SolidColorBrush(Color.FromRgb(238, 241, 245)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromRgb(212, 221, 231)), 1);
        int minX = (int)Math.Floor(-_origin.X / _pixelsPerMetre);
        int maxX = (int)Math.Ceiling((ActualWidth - _origin.X) / _pixelsPerMetre);
        int minY = (int)Math.Floor(-_origin.Y / _pixelsPerMetre);
        int maxY = (int)Math.Ceiling((ActualHeight - _origin.Y) / _pixelsPerMetre);
        for (int x = minX; x <= maxX; x++)
        {
            double sx = x * _pixelsPerMetre + _origin.X;
            dc.DrawLine(x % 5 == 0 ? major : minor,
                        new Point(sx, 0), new Point(sx, ActualHeight));
        }
        for (int y = minY; y <= maxY; y++)
        {
            double sy = y * _pixelsPerMetre + _origin.Y;
            dc.DrawLine(y % 5 == 0 ? major : minor,
                        new Point(0, sy), new Point(ActualWidth, sy));
        }
    }

    private Geometry BuildGeometry(IReadOnlyList<Point2> vertices, bool close)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreen(vertices[0]), close, close);
            for (int i = 1; i < vertices.Count; i++)
                context.LineTo(ToScreen(vertices[i]), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawLengths(DrawingContext dc, IReadOnlyList<Point2> vertices, bool closed)
    {
        if (_pixelsPerMetre < 17 || vertices.Count > 100) return;
        int count = closed ? vertices.Count : vertices.Count - 1;
        for (int i = 0; i < count; i++)
        {
            Point2 a = vertices[i];
            Point2 b = vertices[(i + 1) % vertices.Count];
            if (a.DistanceTo(b) * _pixelsPerMetre < 48) continue;
            var midpoint = new Point((ToScreen(a).X + ToScreen(b).X) / 2,
                                     (ToScreen(a).Y + ToScreen(b).Y) / 2);
            DrawText(dc, $"{a.DistanceTo(b):0.##} m",
                     new Point(midpoint.X + 5, midpoint.Y + 5), 11, Brushes.DarkSlateGray);
        }
    }

    private void DrawText(DrawingContext dc, string text, Point location, double size, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, location);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        Point position = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Tool == EditorTool.Pan))
        {
            _panning = true;
            _lastMouse = position;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (Tool is EditorTool.DrawPerimeter or EditorTool.DrawRoom)
        {
            if (Tool == EditorTool.DrawRoom && _outline.Count < 3)
            {
                Say("Draw the building perimeter before adding rooms.");
                return;
            }
            if (e.ClickCount >= 2 ||
                (_draft.Count >= 3 && (ToScreen(_draft[0]) - position).Length <= 12))
                FinishDrawing();
            else
            {
                Point2 vertex = SnapForDrawing(ToWorld(position));
                if (_draft.Count == 0 || _draft[^1].DistanceTo(vertex) >= 0.001)
                {
                    _draft.Add(vertex);
                    Say($"{_draft.Count} corners in progress. Close or Finish to accept.");
                }
            }
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.AddDoor)
        {
            TryAddDoor(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.PlaceStairs)
        {
            StairPlacementRequested?.Invoke(ToWorld(position));
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.SplitRoom)
        {
            HandleSplitRoomClick(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.MergeRooms)
        {
            HandleMergeRoomsClick(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.MoveWall)
        {
            StartWallDrag(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.SplitPerimeter)
        {
            TryInsertCorner(position, perimeterOnly: true);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.RemoveCorner)
        {
            TryRemoveCornerAt(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Select)
        {
            if (e.ClickCount >= 2 && TryInsertCorner(position, perimeterOnly: false))
            {
                e.Handled = true;
                return;
            }

            if (TrySelectVertex(position))
            {
                _beforeDrag = Snapshot();
                _draggingVertex = true;
                _dragOriginalPoint = SelectedVertices()![_selectedVertex];
                CaptureMouse();
            }
            else if (TrySelectDoor(position))
            {
                _selectedStairId = null;
                _selectedVertex = -1;
                _selectedRoomId = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (TrySelectStair(position))
            {
                _selectedVertex = -1;
                _selectedDoorId = null;
                _selectedRoomId = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _selectedStairId = null;
                Point2 point = ToWorld(position);
                var room = _rooms.LastOrDefault(r => PolygonGeometry.ContainsPoint(r.Outline, point));
                _selectedRoomId = room?.Id;
                _selectedDoorId = null;
                _selectedVertex = -1;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point position = e.GetPosition(this);
        if (_panning)
        {
            Vector delta = position - _lastMouse;
            _origin = new Point(_origin.X + delta.X, _origin.Y + delta.Y);
            _lastMouse = position;
            InvalidateVisual();
            return;
        }

        if (_movingWallNow && _movingWall is not null && _beforeDrag is not null)
        {
            PreviewWallDrag(position);
            return;
        }

        if (_draggingVertex && _beforeDrag is not null)
        {
            var movedPoint = SnapForMovingVertex(ToWorld(position));
            // Moving a shared junction updates every room/exterior using exactly
            // the same vertex, to avoid accidental slivers or duplicated corners.
            _outline = _beforeDrag.Perimeter.Select(p =>
                IsSamePoint(p, _dragOriginalPoint) ? movedPoint : p).ToList();
            _rooms = _beforeDrag.Rooms.Select(r =>
            {
                var copy = r.Copy();
                copy.Outline = copy.Outline.Select(p =>
                    IsSamePoint(p, _dragOriginalPoint) ? movedPoint : p).ToList();
                return copy;
            }).ToList();

            // Door positions are relative to their supporting wall, not
            // permanently fixed XY coordinates. Recompute from the state
            // at drag start so diagonal walls rotate without losing doors.
            _doors = DoorPlacement.ForMovedVertex(
                _beforeDrag.Perimeter, _beforeDrag.Rooms, _beforeDrag.Doors,
                _dragOriginalPoint, movedPoint);

            InvalidateVisual();
            return;
        }

        if ((Tool is EditorTool.DrawPerimeter or EditorTool.DrawRoom) && _draft.Count > 0)
        {
            _draftCursor = SnapForDrawing(ToWorld(position));
            InvalidateVisual();
        }
        else if (Tool == EditorTool.SplitRoom && _partitionStart.HasValue)
        {
            _draftCursor = ToWorld(position);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_movingWallNow && e.ChangedButton == MouseButton.Left)
        {
            // Run one final preview at the release position before committing.
            PreviewWallDrag(e.GetPosition(this));
            _movingWallNow = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            if (_beforeDrag is not null && _wallMoveAccepted &&
                !SnapshotMatches(_beforeDrag))
            {
                PushUndo(_beforeDrag);
                RaiseChanged();
                Say("Wall moved. Shared boundaries and doors were updated.");
            }
            else
            {
                if (_beforeDrag is not null) Restore(_beforeDrag);
                Say(_wallMoveError.Length > 0
                    ? $"Wall move rejected: {_wallMoveError}"
                    : "Wall not moved.");
                Changed?.Invoke(this, EventArgs.Empty);
            }
            _beforeDrag = null;
            _movingWall = null;
            InvalidateVisual();
        }

        if (_draggingVertex && e.ChangedButton == MouseButton.Left)
        {
            _draggingVertex = false;
            ReleaseMouseCapture();
            if (_beforeDrag is not null &&
                (!_outline.SequenceEqual(_beforeDrag.Perimeter) ||
                 _rooms.Where((r, i) => !r.Outline.SequenceEqual(_beforeDrag.Rooms[i].Outline)).Any()))
            {
                if (Validate(out var reason))
                {
                    PushUndo(_beforeDrag);
                    Changed?.Invoke(this, EventArgs.Empty);
                    Say("Corner moved.");
                }
                else
                {
                    Restore(_beforeDrag);
                    Say($"Move rejected: {reason}");
                }
            }
            _beforeDrag = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(Math.Pow(1.15, e.Delta / 120.0), e.GetPosition(this));
        e.Handled = true;
    }

    private void HandleMergeRoomsClick(Point position)
    {
        Point2 target = ToWorld(position);
        var clicked = _rooms.LastOrDefault(room =>
            PolygonGeometry.ContainsPoint(room.Outline, target));
        if (clicked is null)
        {
            Say("Click inside a room to select it for merging.");
            return;
        }

        if (!_mergePrimaryRoomId.HasValue)
        {
            _mergePrimaryRoomId = clicked.Id;
            _selectedRoomId = clicked.Id;
            _selectedDoorId = null;
            _selectedVertex = -1;
            RaiseChanged();
            Say($"Selected '{clicked.Name}'. Click the adjoining room you want to combine it with.");
            return;
        }

        if (_mergePrimaryRoomId.Value == clicked.Id)
        {
            Say("Select a different adjoining room as the second room.");
            return;
        }

        var previous = Snapshot();
        if (!RoomMerging.TryMergeRooms(Document(), _mergePrimaryRoomId.Value,
                clicked.Id, out var combined, out var reason))
        {
            Say($"Cannot merge rooms: {reason}");
            return;
        }

        string removedName = clicked.Name;
        _outline = combined.Perimeter;
        _rooms = combined.Rooms;
        _doors = combined.Doors;
        if (!Validate(out var buildingReason))
        {
            Restore(previous);
            Say($"Cannot merge rooms: {buildingReason}");
            return;
        }
        _selectedRoomId = _mergePrimaryRoomId;
        _selectedDoorId = null;
        _selectedStairId = null;
        _selectedVertex = -1;
        _mergePrimaryRoomId = null;
        Tool = EditorTool.Select;
        Cursor = DefaultCursor();
        PushUndo(previous);
        RaiseChanged();
        Say($"Rooms merged. '{removedName}' was absorbed into the selected room; rename it if needed.");
    }

    private Point2 SnapForMovingVertex(Point2 target)
    {
        if (_beforeDrag is null)
            return SnapToGridPoint(target);

        var outlines = new List<IReadOnlyList<Point2>>
        {
            _beforeDrag.Perimeter
        };
        outlines.AddRange(_beforeDrag.Rooms.Select(room =>
            (IReadOnlyList<Point2>)room.Outline));

        return GeometrySnap.ForMovedVertex(
            target, _dragOriginalPoint, outlines,
            10 / _pixelsPerMetre, SnapToGrid, 0.5,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void HandleSplitRoomClick(Point position)
    {
        if (_outline.Count < 3 || _rooms.Count == 0)
        {
            Say("Draw a building and at least one room before using Split Room.");
            return;
        }

        var selected = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
        // Clicking inside a room when no room is selected establishes the
        // target. Once selected, click two positions on its outer boundary.
        if (selected is null)
        {
            Point2 location = ToWorld(position);
            var candidate = _rooms.LastOrDefault(r =>
                PolygonGeometry.ContainsPoint(r.Outline, location));
            if (candidate is null)
            {
                Say("Click inside the room you want to divide.");
                return;
            }
            _selectedRoomId = candidate.Id;
            _selectedDoorId = null;
            _selectedVertex = -1;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            selected = candidate;
        }

        if (!TryFindEdge(selected.Outline, position, out _, out var boundaryPoint))
        {
            Say("Click directly on one of the selected room's boundary walls.");
            return;
        }

        // Snap the endpoint precisely to the wall rather than to an
        // independent grid point that could fall fractionally off the edge.
        if (!_partitionStart.HasValue)
        {
            _partitionStart = boundaryPoint;
            _draftCursor = boundaryPoint;
            Say("First partition endpoint selected. Click another boundary wall to split the room.");
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_partitionStart.Value.DistanceTo(boundaryPoint) < 0.1)
        {
            Say("Choose a second endpoint at least 10 cm away from the first.");
            return;
        }

        var previous = Snapshot();
        if (!InteriorEditing.TrySplitRoom(Document(), selected.Id,
                _partitionStart.Value, boundaryPoint,
                out var document, out var reason))
        {
            Say($"Room not split: {reason}");
            return;
        }

        _outline = document.Perimeter;
        _rooms = document.Rooms;
        _doors = document.Doors;
        if (!Validate(out var buildingReason))
        {
            Restore(previous);
            Say($"Room not split: {buildingReason}");
            return;
        }
        _partitionStart = null;
        _draftCursor = null;
        _selectedVertex = -1;
        Tool = EditorTool.Select;
        Cursor = DefaultCursor();
        PushUndo(previous);
        RaiseChanged();
        Say("Room divided into two rooms. Select either room to rename it.");
    }

    private void StartWallDrag(Point mousePosition)
    {
        var origin = ToWorld(mousePosition);
        WallSegment? selected = null;
        double bestDistance = 12;
        foreach (var wall in FloorGeometry.BuildWalls(_outline, _rooms))
        {
            var nearest = PolygonGeometry.ClosestPointOnSegment(origin, wall.A, wall.B);
            double distance = origin.DistanceTo(nearest) * _pixelsPerMetre;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            selected = wall;
        }

        if (selected is null)
        {
            Say("Click directly on a wall, then drag it perpendicular to itself.");
            return;
        }
        _beforeDrag = Snapshot();
        _movingWall = selected;
        _moveWallMouseStart = origin;
        _movingWallNow = true;
        _wallMoveAccepted = false;
        _wallMoveError = "";
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        Say("Dragging wall. Release the mouse to apply a valid position.");
    }

    private void PreviewWallDrag(Point mousePosition)
    {
        if (_beforeDrag is null || _movingWall is null) return;
        var current = ToWorld(mousePosition);
        var wall = _movingWall;
        double length = wall.Length;
        double nx = -(wall.B.Y - wall.A.Y) / length;
        double ny = (wall.B.X - wall.A.X) / length;
        double amount = (current.X - _moveWallMouseStart.X) * nx +
                        (current.Y - _moveWallMouseStart.Y) * ny;

        if (SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            amount = Math.Round(amount * 2, MidpointRounding.AwayFromZero) / 2;

        var original = DocumentFromSnapshot(_beforeDrag);
        if (InteriorEditing.TryMoveWall(original, wall, amount, out var moved, out var reason))
        {
            _outline = moved.Perimeter;
            _rooms = moved.Rooms;
            _doors = moved.Doors;
            if (Validate(out var buildingReason))
            {
                _wallMoveAccepted = Math.Abs(amount) > 1e-7;
                _wallMoveError = "";
            }
            else
            {
                Restore(_beforeDrag);
                _wallMoveAccepted = false;
                _wallMoveError = buildingReason;
            }
        }
        else
        {
            Restore(_beforeDrag);
            _wallMoveAccepted = false;
            _wallMoveError = reason;
        }
        InvalidateVisual();
    }

    private static BuildingDocument DocumentFromSnapshot(EditorSnapshot snapshot) => new()
    {
        Perimeter = snapshot.Perimeter.ToList(),
        Rooms = snapshot.Rooms.Select(r => r.Copy()).ToList(),
        Doors = snapshot.Doors.Select(d => d.Copy()).ToList()
    };

    private bool SnapshotMatches(EditorSnapshot snapshot) =>
        _outline.SequenceEqual(snapshot.Perimeter) &&
        _rooms.Count == snapshot.Rooms.Count &&
        _rooms.Zip(snapshot.Rooms).All(pair =>
            pair.First.Outline.SequenceEqual(pair.Second.Outline)) &&
        _doors.Zip(snapshot.Doors).All(pair =>
            pair.First.Centre.Equals(pair.Second.Centre));

    private void TryAddDoor(Point position)
    {
        var walls = FloorGeometry.BuildWalls(_outline, _rooms);
        WallSegment? closest = null;
        Point2 closestPoint = default;
        double distance = 12;
        foreach (var wall in walls)
        {
            var point = PolygonGeometry.ClosestPointOnSegment(ToWorld(position), wall.A, wall.B);
            double candidateDistance = point.DistanceTo(ToWorld(position)) * _pixelsPerMetre;
            if (candidateDistance < distance)
            {
                closest = wall;
                closestPoint = point;
                distance = candidateDistance;
            }
        }
        if (closest is null)
        {
            Say("Click close to an existing wall to add a doorway.");
            return;
        }
        var door = new DoorDefinition { Centre = closestPoint };
        var previous = Snapshot();
        _doors.Add(door);
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say($"Cannot place door: {reason}");
            return;
        }
        PushUndo(previous);
        _selectedDoorId = door.Id;
        _selectedRoomId = null;
        _selectedVertex = -1;
        RaiseChanged();
        Say("Door added. Select it and use Delete Door to remove it.");
    }

    private bool TrySelectStair(Point position)
    {
        var world = ToWorld(position);
        for (int i = _staircases.Count - 1; i >= 0; i--)
        {
            var stair = _staircases[i];
            if (stair.LowerFloorIndex != _stairFloorIndex &&
                stair.LowerFloorIndex + 1 != _stairFloorIndex)
                continue;
            if (!PolygonGeometry.ContainsPoint(BuildingStoreys.StairFootprint(stair), world))
                continue;
            _selectedStairId = stair.Id;
            return true;
        }
        return false;
    }

    private bool TrySelectDoor(Point position)
    {
        foreach (var door in _doors)
        {
            if ((ToScreen(door.Centre) - position).Length <= 11)
            {
                _selectedDoorId = door.Id;
                return true;
            }
        }
        return false;
    }

    private bool TrySelectVertex(Point position)
    {
        // If a room is already selected, its vertices have precedence.
        var activeRoom = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
        if (activeRoom is not null &&
            TryVertexHit(activeRoom.Outline, position, out int selected))
        {
            _selectedVertex = selected;
            _selectedPerimeterVertex = false;
            _selectedDoorId = null;
            _selectedStairId = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (TryVertexHit(_outline, position, out selected))
        {
            _selectedVertex = selected;
            _selectedPerimeterVertex = true;
            _selectedRoomId = null;
            _selectedDoorId = null;
            _selectedStairId = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            if (!TryVertexHit(_rooms[i].Outline, position, out selected)) continue;
            _selectedRoomId = _rooms[i].Id;
            _selectedDoorId = null;
            _selectedStairId = null;
            _selectedVertex = selected;
            _selectedPerimeterVertex = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private void TryRemoveCornerAt(Point position)
    {
        // The exterior has priority at a shared corner; this keeps the tool
        // intuitive when a room touches the building's outer wall.
        if (TryVertexHit(_outline, position, out int index))
        {
            _selectedPerimeterVertex = true;
            _selectedRoomId = null;
            _selectedDoorId = null;
            _selectedVertex = index;
            DeleteSelectedVertex();
            return;
        }

        var selectedRoom = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
        if (selectedRoom is not null &&
            TryVertexHit(selectedRoom.Outline, position, out index))
        {
            _selectedPerimeterVertex = false;
            _selectedVertex = index;
            _selectedDoorId = null;
            DeleteSelectedVertex();
            return;
        }

        foreach (var room in _rooms)
        {
            if (!TryVertexHit(room.Outline, position, out index)) continue;
            _selectedPerimeterVertex = false;
            _selectedRoomId = room.Id;
            _selectedDoorId = null;
            _selectedVertex = index;
            DeleteSelectedVertex();
            return;
        }

        Say("Click within 10 screen pixels of an existing corner to remove it.");
    }

    private bool TryInsertCorner(Point position, bool perimeterOnly)
    {
        int edge;
        Point2 point;
        bool exterior = false;
        Guid? roomId = null;
        bool found = false;

        // Exterior edges always take precedence over room edges. This is
        // particularly important where a room and the exterior share a wall.
        if (TryFindEdge(_outline, position, out edge, out point))
        {
            exterior = true;
            found = true;
        }
        else if (!perimeterOnly)
        {
            int foundEdge = -1;
            Point2 foundPoint = default;
            var selectedRoom = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
            if (selectedRoom is not null &&
                TryFindEdge(selectedRoom.Outline, position, out foundEdge, out foundPoint))
            {
                roomId = selectedRoom.Id;
                found = true;
            }
            else
            {
                foreach (var room in _rooms)
                {
                    if (!TryFindEdge(room.Outline, position, out foundEdge, out foundPoint))
                        continue;
                    roomId = room.Id;
                    found = true;
                    break;
                }
            }
            edge = foundEdge;
            point = foundPoint;
        }

        if (!found)
        {
            if (perimeterOnly)
                Say("Click within 10 screen pixels of an exterior wall to split it.");
            return false;
        }

        var previous = Snapshot();
        if (!PerimeterEditing.TrySplitWall(Document(), exterior, roomId, edge,
            point, out var updated, out var reason))
        {
            Say($"Cannot split wall: {reason}");
            return true;
        }

        _outline = updated.Perimeter;
        _rooms = updated.Rooms;
        _doors = updated.Doors;
        if (!Validate(out var buildingReason))
        {
            Restore(previous);
            Say($"Cannot split wall: {buildingReason}");
            return true;
        }
        _selectedPerimeterVertex = exterior;
        _selectedRoomId = exterior ? null : roomId;
        _selectedDoorId = null;

        // Use the new polygon's index; inserting at the closing edge wraps
        // naturally to the end of its vertex sequence.
        var vertices = exterior ? _outline :
            _rooms.First(r => r.Id == roomId).Outline;
        _selectedVertex = vertices.FindIndex(p => p.DistanceTo(point) < 1e-7);

        PushUndo(previous);
        RaiseChanged();
        Say(exterior
            ? "Perimeter split. Choose Select / Edit and drag the new blue corner to make another wall segment."
            : "Room wall split. Choose Select / Edit and drag the new corner.");
        return true;
    }

    private void RaiseChanged()
    {
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Validate(out string reason)
    {
        var floor = Document();
        if (!FloorGeometry.Validate(floor, out reason)) return false;
        if (AdditionalValidator is null) return true;
        var result = AdditionalValidator(floor);
        reason = result.Reason;
        return result.Valid;
    }

    private EditorSnapshot Snapshot() =>
        new(_outline.ToList(), _rooms.Select(r => r.Copy()).ToList(),
            _doors.Select(d => d.Copy()).ToList());

    private void Restore(EditorSnapshot snapshot)
    {
        _outline = snapshot.Perimeter.ToList();
        _rooms = snapshot.Rooms.Select(r => r.Copy()).ToList();
        _doors = snapshot.Doors.Select(d => d.Copy()).ToList();
        InvalidateVisual();
    }

    private void PushUndo(EditorSnapshot previous)
    {
        _undo.Push(previous);
        _redo.Clear();
    }

    private void ClearSelection()
    {
        _selectedRoomId = null;
        _selectedDoorId = null;
        _selectedVertex = -1;
        _mergePrimaryRoomId = null;
    }

    private List<Point2>? SelectedVertices()
    {
        if (_selectedVertex < 0) return null;
        return _selectedPerimeterVertex
            ? _outline
            : _rooms.FirstOrDefault(r => r.Id == _selectedRoomId)?.Outline;
    }

    private bool TryVertexHit(List<Point2> points, Point screen, out int index)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if ((ToScreen(points[i]) - screen).Length > 10) continue;
            index = i;
            return true;
        }
        index = -1;
        return false;
    }

    private bool TryFindEdge(IReadOnlyList<Point2> outline, Point position,
        out int edgeIndex, out Point2 nearest)
    {
        edgeIndex = -1;
        nearest = default;
        double bestDistance = 10;
        Point2 worldPoint = ToWorld(position);
        for (int i = 0; i < outline.Count; i++)
        {
            Point2 candidate = PolygonGeometry.ClosestPointOnSegment(
                worldPoint, outline[i], outline[(i + 1) % outline.Count]);
            double distance = candidate.DistanceTo(worldPoint) * _pixelsPerMetre;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                edgeIndex = i;
                nearest = candidate;
            }
        }
        return edgeIndex >= 0;
    }

    private Point2 SnapForDrawing(Point2 point)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return point;
        var target = point;
        double bestPixels = 10;
        foreach (var polygon in EnumeratePolygons())
        {
            foreach (var corner in polygon)
            {
                double distance = corner.DistanceTo(point) * _pixelsPerMetre;
                if (distance < bestPixels) { target = corner; bestPixels = distance; }
            }
        }
        if (bestPixels < 10) return target;

        foreach (var polygon in EnumeratePolygons())
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 closest = PolygonGeometry.ClosestPointOnSegment(
                    point, polygon[i], polygon[(i + 1) % polygon.Count]);
                double distance = closest.DistanceTo(point) * _pixelsPerMetre;
                if (distance < bestPixels) { target = closest; bestPixels = distance; }
            }
        }
        return bestPixels < 10 ? target : SnapToGridPoint(point);
    }

    private IEnumerable<IReadOnlyList<Point2>> EnumeratePolygons()
    {
        if (_outline.Count > 0) yield return _outline;
        foreach (var room in _rooms) yield return room.Outline;
    }

    private Point2 SnapToGridPoint(Point2 point) =>
        !SnapToGrid || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? point
            : new Point2(Math.Round(point.X * 2, MidpointRounding.AwayFromZero) / 2,
                         Math.Round(point.Y * 2, MidpointRounding.AwayFromZero) / 2);

    private static bool IsSamePoint(Point2 a, Point2 b) => a.DistanceTo(b) < 1e-7;
    private Point2 ToWorld(Point point) =>
        new((point.X - _origin.X) / _pixelsPerMetre,
            (point.Y - _origin.Y) / _pixelsPerMetre);

    private Point ToScreen(Point2 point) =>
        new(point.X * _pixelsPerMetre + _origin.X,
            point.Y * _pixelsPerMetre + _origin.Y);

    private void Say(string message) => Hint?.Invoke(message);
}
