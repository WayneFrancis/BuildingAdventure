using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingAdventure.Core.Buildings;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Desktop.Controls;

public enum EditorTool { Select, DrawPerimeter, DrawRoom, AddDoor, Pan }

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
    private int _selectedVertex = -1;
    private bool _selectedPerimeterVertex;
    private bool _panning;
    private bool _draggingVertex;
    private EditorSnapshot? _beforeDrag;
    private Point2 _dragOriginalPoint;

    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;
    public event Action<string>? Hint;

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
    public bool CanUndo => _draft.Count > 0 || _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0 && _draft.Count == 0;
    public bool HasSelectedDoor => _selectedDoorId.HasValue;
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
        _selectedVertex = -1;
        _selectedRoomId = null;
        _selectedDoorId = null;
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
        }
        Tool = tool;
        Cursor = DefaultCursor();
        Say(tool switch
        {
            EditorTool.DrawPerimeter => "Click exterior corners; click the starting point or Finish to close.",
            EditorTool.DrawRoom => "Click room corners inside the building, then click the first or Finish.",
            EditorTool.AddDoor => "Click an existing wall to add a 0.9 m doorway.",
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
        if (_draft.Count == 0) return;
        _draft.Clear();
        _draftCursor = null;
        Say("Unfinished drawing cancelled; existing geometry was kept.");
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

    public void DeleteSelectedVertex()
    {
        var vertices = SelectedVertices();
        if (_selectedVertex < 0 || vertices is null || _selectedVertex >= vertices.Count)
        {
            Say("Select a corner before deleting it.");
            return;
        }
        if (vertices.Count <= 3)
        {
            Say("A closed shape needs at least three corners.");
            return;
        }
        var previous = Snapshot();
        vertices.RemoveAt(_selectedVertex);
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say($"Cannot delete corner: {reason}");
            return;
        }
        PushUndo(previous);
        _selectedVertex = -1;
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        Say("Corner deleted.");
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

        if (Tool == EditorTool.Select)
        {
            DrawVertices(dc, _outline, _selectedPerimeterVertex, Brushes.SteelBlue);
            foreach (var room in _rooms)
                if (room.Id == _selectedRoomId)
                    DrawVertices(dc, room.Outline, !_selectedPerimeterVertex, Brushes.DarkSlateBlue);
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

        if (Tool == EditorTool.Select)
        {
            if (e.ClickCount >= 2 && TryInsertCorner(position))
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
                _selectedVertex = -1;
                _selectedRoomId = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
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

        if (_draggingVertex && _beforeDrag is not null)
        {
            var movedPoint = SnapToGridPoint(ToWorld(position));
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
            InvalidateVisual();
            return;
        }

        if ((Tool is EditorTool.DrawPerimeter or EditorTool.DrawRoom) && _draft.Count > 0)
        {
            _draftCursor = SnapForDrawing(ToWorld(position));
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
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (TryVertexHit(_outline, position, out selected))
        {
            _selectedVertex = selected;
            _selectedPerimeterVertex = true;
            _selectedRoomId = null;
            _selectedDoorId = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            if (!TryVertexHit(_rooms[i].Outline, position, out selected)) continue;
            _selectedRoomId = _rooms[i].Id;
            _selectedDoorId = null;
            _selectedVertex = selected;
            _selectedPerimeterVertex = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    private bool TryInsertCorner(Point position)
    {
        List<Point2>? target = null;
        int edge = -1;
        Point2 point = default;
        var selectedRoom = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
        if (selectedRoom is not null && TryFindEdge(selectedRoom.Outline, position, out edge, out point))
        {
            target = selectedRoom.Outline;
            _selectedPerimeterVertex = false;
        }
        else if (TryFindEdge(_outline, position, out edge, out point))
        {
            target = _outline;
            _selectedPerimeterVertex = true;
        }
        if (target is null) return false;
        var previous = Snapshot();
        target.Insert(edge + 1, point); // Exact insertion does not distort wall geometry.
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say($"Cannot insert corner: {reason}");
            return true;
        }
        PushUndo(previous);
        _selectedVertex = edge + 1;
        RaiseChanged();
        Say("Corner inserted. Drag it to move.");
        return true;
    }

    private void RaiseChanged()
    {
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Validate(out string reason) => FloorGeometry.Validate(Document(), out reason);

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
