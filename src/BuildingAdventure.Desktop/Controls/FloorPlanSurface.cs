using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Desktop.Controls;

public enum EditorTool { Select, DrawPerimeter, Pan }

/// <summary>
/// Interactive metre-based perimeter editor. This renderer intentionally knows
/// nothing about file IO, building archetypes or generation algorithms.
/// </summary>
public sealed class FloorPlanSurface : FrameworkElement
{
    private List<Point2> _outline = new();
    private readonly List<Point2> _draft = new();
    private readonly Stack<List<Point2>> _undo = new();
    private readonly Stack<List<Point2>> _redo = new();

    private double _pixelsPerMetre = 40;
    private Point _origin = new(120, 110);
    private Point _lastMouse;
    private Point2? _draftCursor;
    private int _selectedVertex = -1;
    private bool _panning;
    private bool _draggingVertex;
    private List<Point2>? _beforeDrag;

    public event EventHandler? Changed;
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
    public bool CanRedo => _redo.Count > 0;
    public IReadOnlyList<Point2> GetOutline() => _outline.AsReadOnly();

    public void LoadOutline(IReadOnlyList<Point2> outline)
    {
        _outline = outline.ToList();
        _draft.Clear();
        _draftCursor = null;
        _selectedVertex = -1;
        _undo.Clear();
        _redo.Clear();
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetTool(EditorTool tool)
    {
        if (Tool != tool)
        {
            _draft.Clear();
            _draftCursor = null;
        }
        Tool = tool;
        Cursor = tool == EditorTool.Pan ? Cursors.Hand :
                 tool == EditorTool.DrawPerimeter ? Cursors.Cross : Cursors.Arrow;
        Say(tool switch
        {
            EditorTool.DrawPerimeter => "Click corners. Click the first point, double-click, or use Finish to close.",
            EditorTool.Select => "Drag corners to edit. Double-click a wall to add a corner.",
            _ => "Drag to pan; scroll the mouse wheel to zoom."
        });
        InvalidateVisual();
    }

    public void CancelDrawing()
    {
        if (_draft.Count == 0) return;
        _draft.Clear();
        _draftCursor = null;
        Say("Unfinished perimeter cancelled. Existing perimeter was kept.");
        InvalidateVisual();
    }

    public bool FinishDrawing()
    {
        if (_draft.Count == 0) return false;
        if (!PolygonGeometry.IsValidOutline(_draft, out var reason))
        {
            Say($"Cannot finish: {reason}");
            return false;
        }

        PushUndo(_outline);
        _outline = _draft.ToList();
        _draft.Clear();
        _draftCursor = null;
        _selectedVertex = -1;
        Tool = EditorTool.Select;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        Say("Perimeter completed. Drag corners or double-click a wall to edit it.");
        return true;
    }

    public void DeleteSelectedVertex()
    {
        if (_selectedVertex < 0 || _selectedVertex >= _outline.Count)
        {
            Say("Select a corner before deleting it.");
            return;
        }
        if (_outline.Count <= 3)
        {
            Say("A closed perimeter must have at least three corners.");
            return;
        }
        var candidate = _outline.ToList();
        candidate.RemoveAt(_selectedVertex);
        if (!PolygonGeometry.IsValidOutline(candidate, out var reason))
        {
            Say($"Cannot delete corner: {reason}");
            return;
        }
        PushUndo(_outline);
        _outline = candidate;
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
            Say("Last unfinished corner removed.");
            return;
        }
        if (_undo.Count == 0) return;
        _redo.Push(_outline.ToList());
        _outline = _undo.Pop();
        _selectedVertex = -1;
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
        Say("Undo.");
    }

    public void Redo()
    {
        if (_redo.Count == 0 || _draft.Count > 0) return;
        _undo.Push(_outline.ToList());
        _outline = _redo.Pop();
        _selectedVertex = -1;
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
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

        if (_outline.Count > 0)
        {
            var polygon = BuildGeometry(_outline, close: true);
            bool valid = PolygonGeometry.IsValidOutline(_outline, out _);
            dc.DrawGeometry(valid ? new SolidColorBrush(Color.FromArgb(100, 187, 216, 239))
                                  : new SolidColorBrush(Color.FromArgb(95, 242, 112, 112)),
                            new Pen(valid ? Brushes.SteelBlue : Brushes.IndianRed, 2.7), polygon);
            DrawLengths(dc, _outline, true);
            for (int i = 0; i < _outline.Count; i++)
            {
                var screen = ToScreen(_outline[i]);
                bool selected = i == _selectedVertex;
                dc.DrawEllipse(selected ? Brushes.Orange : Brushes.White,
                    new Pen(selected ? Brushes.DarkOrange : Brushes.SteelBlue, 2),
                    screen, selected ? 7 : 5, selected ? 7 : 5);
            }
        }

        if (_draft.Count > 0)
        {
            dc.DrawGeometry(null, new Pen(Brushes.DarkOrange, 2.5) { DashStyle = DashStyles.Dash },
                            BuildGeometry(_draft, close: false));
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
        int segments = closed ? vertices.Count : vertices.Count - 1;
        for (int i = 0; i < segments; i++)
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

        if (Tool == EditorTool.DrawPerimeter)
        {
            if (e.ClickCount >= 2)
            {
                FinishDrawing();
            }
            else if (_draft.Count >= 3 && (ToScreen(_draft[0]) - position).Length <= 12)
            {
                FinishDrawing();
            }
            else
            {
                Point2 vertex = Snap(ToWorld(position));
                if (_draft.Count == 0 || _draft[^1].DistanceTo(vertex) >= 0.001)
                {
                    _draft.Add(vertex);
                    Say($"{_draft.Count} draft corners. Double-click or click the starting point to finish.");
                }
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Select)
        {
            int hit = FindVertex(position);
            if (hit >= 0)
            {
                _selectedVertex = hit;
                _beforeDrag = _outline.ToList();
                _draggingVertex = true;
                CaptureMouse();
            }
            else if (e.ClickCount >= 2 && TryFindEdge(position, out int edge, out var nearest))
            {
                var candidate = _outline.ToList();
                candidate.Insert(edge + 1, nearest); // Keep an inserted point exactly on its original wall.
                if (PolygonGeometry.IsValidOutline(candidate, out var reason))
                {
                    PushUndo(_outline);
                    _outline = candidate;
                    _selectedVertex = edge + 1;
                    Changed?.Invoke(this, EventArgs.Empty);
                    Say("Corner inserted. Drag it to adjust the wall.");
                }
                else Say($"Cannot insert corner: {reason}");
            }
            else _selectedVertex = -1;

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

        if (_draggingVertex && _selectedVertex >= 0)
        {
            _outline[_selectedVertex] = Snap(ToWorld(position));
            InvalidateVisual();
            return;
        }

        if (Tool == EditorTool.DrawPerimeter && _draft.Count > 0)
        {
            _draftCursor = Snap(ToWorld(position));
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
            Cursor = Tool == EditorTool.Pan ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_draggingVertex && e.ChangedButton == MouseButton.Left)
        {
            _draggingVertex = false;
            ReleaseMouseCapture();
            if (_beforeDrag is not null && !_outline.SequenceEqual(_beforeDrag))
            {
                if (PolygonGeometry.IsValidOutline(_outline, out var reason))
                {
                    PushUndo(_beforeDrag);
                    Changed?.Invoke(this, EventArgs.Empty);
                    Say("Corner moved.");
                }
                else
                {
                    _outline = _beforeDrag;
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

    private Point2 ToWorld(Point point) =>
        new((point.X - _origin.X) / _pixelsPerMetre,
            (point.Y - _origin.Y) / _pixelsPerMetre);

    private Point ToScreen(Point2 point) =>
        new(point.X * _pixelsPerMetre + _origin.X,
            point.Y * _pixelsPerMetre + _origin.Y);

    private Point2 Snap(Point2 point) =>
        !SnapToGrid || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? point
            : new Point2(Math.Round(point.X * 2, MidpointRounding.AwayFromZero) / 2,
                         Math.Round(point.Y * 2, MidpointRounding.AwayFromZero) / 2);

    private int FindVertex(Point position)
    {
        for (int i = _outline.Count - 1; i >= 0; i--)
            if ((ToScreen(_outline[i]) - position).Length <= 10) return i;
        return -1;
    }

    private bool TryFindEdge(Point position, out int edgeIndex, out Point2 nearest)
    {
        edgeIndex = -1;
        nearest = default;
        double bestDistance = 10;
        Point2 worldPoint = ToWorld(position);
        for (int i = 0; i < _outline.Count; i++)
        {
            Point2 candidate = PolygonGeometry.ClosestPointOnSegment(
                worldPoint, _outline[i], _outline[(i + 1) % _outline.Count]);
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

    private void PushUndo(IReadOnlyList<Point2> previous)
    {
        _undo.Push(previous.ToList());
        _redo.Clear();
    }

    private void Say(string message) => Hint?.Invoke(message);
}
