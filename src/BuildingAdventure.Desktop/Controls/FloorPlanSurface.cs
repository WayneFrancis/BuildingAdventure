using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingAdventure.Core.Buildings;
using BuildingAdventure.Core.Geometry;

namespace BuildingAdventure.Desktop.Controls;

public enum EditorTool { Select, DrawPerimeter, SplitPerimeter, RemoveCorner, DrawRoom, SplitRoom, MergeRooms, MoveWall, AddDoor, AddWindow, PlaceStairs, Pan }
public enum MapVisibilityMode { Editor, GameMaster, Player }

/// <summary>Interactive metre-based editor for a single building floor.</summary>
public sealed class FloorPlanSurface : FrameworkElement
{
    private sealed record EditorSnapshot(List<Point2> Perimeter,
        List<RoomDefinition> Rooms, List<DoorDefinition> Doors,
        List<WindowDefinition> Windows, WallThicknessSettings WallThickness);

    private List<Point2> _outline = new();
    private List<RoomDefinition> _rooms = new();
    private List<DoorDefinition> _doors = new();
    private List<WindowDefinition> _windows = new();
    private WallThicknessSettings _wallThickness = new();
    private readonly List<Point2> _draft = new();
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();

    private double _pixelsPerMetre = 40;
    private Point _origin = new(120, 110);
    private Point _lastMouse;
    private Point2? _draftCursor;

    private Guid? _selectedRoomId;
    private Guid? _selectedDoorId;
    private bool _draggingDoor;
    private bool _resizingDoor;
    private DoorDefinition? _originalDoor;
    private DoorDefinition? _doorPreview;
    private WallSegment? _doorSupport;
    private Point2 _doorFixedEnd;
    private Point2 _doorGrabOffset;
    private bool _doorEditValid;
    private string _doorEditError = "";
    private Guid? _selectedWindowId;
    private bool _draggingWindow;
    private bool _resizingWindow;
    private WindowDefinition? _originalWindow;
    private WindowDefinition? _windowPreview;
    private WallSegment? _resizeWindowWall;
    private Point2 _windowFixedEnd;
    private Point2 _windowGrabOffset;
    private bool _windowEditValid;
    private string _windowEditError = "";
    private Guid? _selectedStairId;
    private List<StaircaseDefinition> _staircases = new();
    private List<ChimneyDefinition> _chimneys = new();
    private Guid? _selectedChimneyId;
    private int _stairFloorIndex;
    private List<AdjacentFloorPlan> _referenceFloors = new();
    private double _referenceOpacity = 0.40;
    private List<AdjacentFloorPlan> _silhouetteFloors = new();
    private BuildingDocument? _suggestedFloor;
    private List<StaircaseDefinition> _suggestedStairs = new();
    private Guid? _selectedSuggestedRoomId;
    public Guid? SelectedSuggestedRoomId => _selectedSuggestedRoomId;
    public event Action? SuggestedRoomSelectionChanged;
    private MapVisibilityMode _displayMode;
    private bool _draggingStair;
    private bool _rotatingStair;
    private double _rotationStartPointerAngle;
    private StaircaseDefinition? _dragStartStair;
    private StaircaseDefinition? _dragPreviewStair;
    private Point2 _stairGrabOffset;
    private bool _stairPreviewValid;
    private string _stairPreviewReason = "";
    private Point2? _stairPlacementHover;
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
    public event Action<StaircaseDefinition>? StairEditRequested;

    /// <summary>
    /// Fired once after a successful floor-geometry commit, carrying the
    /// immediately preceding floor state for document-wide history.
    /// </summary>
    public event Action<BuildingDocument>? DocumentEditCommitted;

    /// <summary>Returns proposed dimensions and floor connection for new stairs.</summary>
    public Func<StaircaseDefinition?>? StairTemplateProvider { get; set; }

    /// <summary>Checks the complete multi-storey layout for a candidate flight.</summary>
    public Func<StaircaseDefinition, (bool Valid, string Reason)>? StairValidator { get; set; }

    public Func<double?>? WindowWidthProvider { get; set; }
    public bool HighlightUnassigned { get; set; }
    public MapVisibilityMode DisplayMode => _displayMode;

    public void SetDisplayMode(MapVisibilityMode mode)
    {
        _displayMode = mode;
        InvalidateVisual();
    }

    public void SetChimneyOverlay(IReadOnlyList<ChimneyDefinition> chimneys)
    {
        _chimneys = chimneys.Select(c => c.Copy()).ToList();
        if (!_chimneys.Any(c => c.Id == _selectedChimneyId))
            _selectedChimneyId = null;
        InvalidateVisual();
    }

    public void SetFloorSilhouettes(IReadOnlyList<AdjacentFloorPlan> floors)
    {
        _silhouetteFloors = floors.ToList();
        InvalidateVisual();
    }

    public void SetSuggestionPreview(BuildingDocument? suggestion,
        IReadOnlyList<StaircaseDefinition>? staircases = null)
    {
        _suggestedFloor = suggestion is null ? null : BuildingStoreys.Copy(suggestion);
        _suggestedStairs = staircases?.Select(s => s.Copy()).ToList() ?? new();
        if (_suggestedFloor?.Rooms.All(r => r.Id != _selectedSuggestedRoomId) != false)
            _selectedSuggestedRoomId = null;
        InvalidateVisual();
        SuggestedRoomSelectionChanged?.Invoke();
    }

    /// <summary>Refresh the placement ghost when dimension input changes.</summary>
    public void RefreshStairPreview() => InvalidateVisual();

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

    /// <summary>
    /// When the host maintains a building-wide history, do not also retain
    /// an accumulating second local floor-undo stack.
    /// </summary>
    public bool UsesGlobalHistory { get; set; }
    public double PixelsPerMetre => _pixelsPerMetre;
    public int GridFeet { get; private set; }

    public void SetGridFeet(int feet)
    {
        GridFeet = feet is 5 or 10 ? feet : 0;
        InvalidateVisual();
    }
    public bool IsDrawing => _draft.Count > 0;
    public bool HasPendingStep =>
        _draft.Count > 0 || _partitionStart.HasValue || _mergePrimaryRoomId.HasValue;
    public bool CanUndo => _draft.Count > 0 || _partitionStart.HasValue || _mergePrimaryRoomId.HasValue || _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0 && _draft.Count == 0 && !_partitionStart.HasValue && !_mergePrimaryRoomId.HasValue;
    public bool HasSelectedDoor => _selectedDoorId.HasValue;
    public DoorDefinition? SelectedDoor =>
        _doors.FirstOrDefault(d => d.Id == _selectedDoorId)?.Copy();
    public WallThicknessSettings WallThickness => _wallThickness.Copy();

    public void SetWallThickness(WallThicknessSettings settings)
    {
        _wallThickness = settings.Copy();
        InvalidateVisual();
    }

    public bool HasSelectedWindow => _selectedWindowId.HasValue;
    public WindowDefinition? SelectedWindow =>
        _windows.FirstOrDefault(w => w.Id == _selectedWindowId)?.Copy();
    public Guid? SelectedChimneyId => _selectedChimneyId;
    public bool HasSelectedChimney => _selectedChimneyId.HasValue;
    public bool HasSelectedVertex => _selectedVertex >= 0;
    public Guid? SelectedRoomId => _selectedRoomId;
    public Guid? SelectedStaircaseId => _selectedStairId;
    public StaircaseDefinition? SelectedStaircase =>
        _staircases.FirstOrDefault(s => s.Id == _selectedStairId)?.Copy();

    /// <summary>
    /// Display all flights touching the active floor. Staircase changes are
    /// owned by the main window's multi-floor document, not the local undo
    /// history of an individual floor.
    /// </summary>
    public void SetStairOverlay(IReadOnlyList<StaircaseDefinition> staircases,
        int floorIndex, Guid? selectId = null)
    {
        _staircases = staircases.Select(s => s.Copy()).ToList();
        _stairFloorIndex = floorIndex;
        _selectedStairId = _staircases.Any(s => s.Id == selectId &&
            (s.LowerFloorIndex == floorIndex || s.LowerFloorIndex + 1 == floorIndex))
            ? selectId : null;
        _draggingStair = false;
        _rotatingStair = false;
        _dragStartStair = null;
        _dragPreviewStair = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectStairById(Guid id)
    {
        if (!_staircases.Any(s => s.Id == id &&
            (s.LowerFloorIndex == _stairFloorIndex ||
             s.LowerFloorIndex + 1 == _stairFloorIndex))) return;
        _selectedStairId = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Overlay adjacent floors strictly as non-editable geometry.</summary>
    public void SetFloorReferences(IReadOnlyList<AdjacentFloorPlan> floors, double opacity)
    {
        _referenceFloors = floors.ToList();
        _referenceOpacity = Math.Clamp(opacity, 0.05, 0.85);
        InvalidateVisual();
    }

    public IReadOnlyList<Point2> GetOutline() => _outline.ToList().AsReadOnly();
    public IReadOnlyList<RoomDefinition> GetRooms() => _rooms.Select(r => r.Copy()).ToList().AsReadOnly();
    public IReadOnlyList<DoorDefinition> GetDoors() => _doors.Select(d => d.Copy()).ToList().AsReadOnly();
    public IReadOnlyList<WindowDefinition> GetWindows() =>
        _windows.Select(w => w.Copy()).ToList().AsReadOnly();

    public double UnassignedAreaMetres
    {
        get
        {
            if (_outline.Count < 3 ||
                !PolygonGeometry.IsValidOutline(_outline, out _)) return 0;
            return CreateUnassignedGeometry(
                BuildingStoreys.GetOpenings(_staircases, _stairFloorIndex))
                .GetArea() / (_pixelsPerMetre * _pixelsPerMetre);
        }
    }
    public RoomDefinition? SelectedRoom => _rooms.FirstOrDefault(r => r.Id == _selectedRoomId)?.Copy();

    private BuildingDocument Document() => new()
    {
        Perimeter = _outline.ToList(),
        Rooms = _rooms.Select(r => r.Copy()).ToList(),
        Doors = _doors.Select(d => d.Copy()).ToList(),
        Windows = _windows.Select(w => w.Copy()).ToList(),
        WallThickness = _wallThickness.Copy()
    };

    public void LoadOutline(IReadOnlyList<Point2> outline) =>
        LoadDocument(new BuildingDocument { Perimeter = outline.ToList() });

    public void LoadDocument(BuildingDocument document)
    {
        _outline = document.Perimeter.ToList();
        _rooms = document.Rooms.Select(r => r.Copy()).ToList();
        _doors = document.Doors.Select(d => d.Copy()).ToList();
        _windows = document.Windows.Select(w => w.Copy()).ToList();
        _wallThickness = document.WallThickness.Copy();
        _draft.Clear();
        _draftCursor = null;
        _partitionStart = null;
        _mergePrimaryRoomId = null;
        _movingWall = null;
        _movingWallNow = false;
        _selectedVertex = -1;
        _selectedRoomId = null;
        _selectedDoorId = null;
        _draggingDoor = false;
        _resizingDoor = false;
        _originalDoor = null;
        _doorPreview = null;
        _selectedWindowId = null;
        _draggingWindow = false;
        _resizingWindow = false;
        _originalWindow = null;
        _windowPreview = null;
        _selectedStairId = null;
        _selectedChimneyId = null;
        _stairPlacementHover = null;
        _draggingStair = false;
        _rotatingStair = false;
        _dragPreviewStair = null;
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
            _stairPlacementHover = null;
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
            EditorTool.AddWindow => "Click an exterior wall to add a window. Windows must fit between corners and doors.",
            EditorTool.PlaceStairs => "Move the cursor to preview stairs; click to place. Rotate or resize in the side panel.",
            EditorTool.Select => "Drag windows or door openings to move; select either to drag square endpoint resize handles. Shift disables window grid snapping.",
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
        if (_draggingDoor || _resizingDoor)
        {
            _draggingDoor = false;
            _resizingDoor = false;
            if (_beforeDrag is not null) Restore(_beforeDrag);
            _beforeDrag = null;
            _originalDoor = null;
            _doorPreview = null;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            InvalidateVisual();
            Say("Door edit cancelled.");
            return;
        }
        if (_draggingWindow || _resizingWindow)
        {
            _draggingWindow = false;
            _resizingWindow = false;
            if (_beforeDrag is not null) Restore(_beforeDrag);
            _beforeDrag = null;
            _originalWindow = null;
            _windowPreview = null;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            InvalidateVisual();
            Say("Window edit cancelled.");
            return;
        }
        if (_draggingStair || _rotatingStair)
        {
            _draggingStair = false;
            _rotatingStair = false;
            _dragPreviewStair = null;
            _dragStartStair = null;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            InvalidateVisual();
            Say("Staircase drag cancelled.");
            return;
        }
        if (_draft.Count == 0 && !_partitionStart.HasValue &&
            !_mergePrimaryRoomId.HasValue && Tool != EditorTool.PlaceStairs) return;
        _stairPlacementHover = null;
        if (Tool == EditorTool.PlaceStairs)
        {
            SetTool(EditorTool.Select);
            Say("Staircase placement cancelled.");
            return;
        }
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
        _windows = updated.Windows;
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
        DrawFloorReferences(dc);
        DrawFloorSilhouettes(dc);

        var openings = BuildingStoreys.GetOpenings(_staircases, _stairFloorIndex);
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
                null, WithStairwellHoles(_outline, openings));
        }

        if (HighlightUnassigned && _outline.Count >= 3 &&
            PolygonGeometry.IsValidOutline(_outline, out _))
            dc.DrawGeometry(new SolidColorBrush(
                Color.FromArgb(150, 254, 222, 147)), null,
                CreateUnassignedGeometry(openings));

        for (int index = 0; index < _rooms.Count; index++)
        {
            var room = _rooms[index];
            if (room.Outline.Count < 3) continue;
            bool selected = room.Id == _selectedRoomId;
            dc.DrawGeometry(fills[index % fills.Length],
                null, WithStairwellHoles(room.Outline, openings));
            Point2 labelPoint = FindRoomLabelPoint(room.Outline);
            Point centre = ToScreen(labelPoint);
            DrawText(dc, room.Name, new Point(centre.X + 5, centre.Y - 12),
                     13, selected ? Brushes.DarkBlue : Brushes.DarkSlateGray);
            double area = PolygonGeometry.Area(room.Outline);
            string areaLabel = GridFeet == 0
                ? $"{area:0.#} m²"
                : $"{area / (0.3048 * 0.3048):0.#} ft²";
            DrawText(dc, areaLabel,
                     new Point(centre.X + 5, centre.Y + 4), 10, Brushes.DimGray);
        }

        if (_outline.Count > 0)
        {
            bool valid = PolygonGeometry.IsValidOutline(_outline, out _);
            if (!valid)
                dc.DrawGeometry(null, new Pen(Brushes.IndianRed, 3),
                    BuildGeometry(_outline, true));
            DrawLengths(dc, _outline, true);
        }

        // Render the dark border of all physical wall pieces first, then
        // mitred junction patches, followed by material fills. A separate
        // patch fills the empty wedge left by flat-ended strokes at corners
        // and T-junctions; opening endpoints never receive those patches.
        var wallPlan = Document();
        if (_displayMode == MapVisibilityMode.Player)
            wallPlan = SecretDoorVisibility.PhysicalPlanForPlayer(wallPlan);
        var wallPieces = WallConstruction.SolidPieces(wallPlan);
        double outlinePadding = 1 / _pixelsPerMetre;
        foreach (var piece in wallPieces)
        {
            double width = (piece.IsExterior
                ? _wallThickness.ExteriorMetres
                : _wallThickness.InteriorMetres) * _pixelsPerMetre;
            dc.DrawLine(new Pen(Brushes.DarkSlateGray, width + 2),
                ToScreen(piece.A), ToScreen(piece.B));
        }
        foreach (var join in WallConstruction.JunctionPatches(
            wallPlan, outlinePadding))
            dc.DrawGeometry(Brushes.DarkSlateGray, null,
                BuildGeometry(join.Outline, true));

        foreach (var piece in wallPieces)
        {
            double width = (piece.IsExterior
                ? _wallThickness.ExteriorMetres
                : _wallThickness.InteriorMetres) * _pixelsPerMetre;
            dc.DrawLine(new Pen(piece.IsExterior
                    ? Brushes.SlateGray : Brushes.LightSlateGray, width),
                ToScreen(piece.A), ToScreen(piece.B));
        }
        foreach (var join in WallConstruction.JunctionPatches(wallPlan))
            dc.DrawGeometry(join.IsExterior
                    ? Brushes.SlateGray : Brushes.LightSlateGray, null,
                BuildGeometry(join.Outline, true));

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
            if (_displayMode == MapVisibilityMode.Player &&
                !SecretDoorVisibility.IsVisibleToPlayer(door)) continue;
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
            Pen doorPen = new(door.Id == _selectedDoorId ? Brushes.OrangeRed :
                door.IsSecret ? Brushes.MediumVioletRed : Brushes.SaddleBrown, 2)
            {
                DashStyle = door.IsSecret && _displayMode != MapVisibilityMode.Player
                    ? DashStyles.Dash : DashStyles.Solid
            };
            dc.DrawLine(doorPen, ToScreen(hinge), ToScreen(openEnd));
            dc.DrawEllipse(Brushes.White, doorPen, ToScreen(hinge), 2.5, 2.5);
            if (door.Id == _selectedDoorId && Tool == EditorTool.Select)
            {
                var border = new Pen(Brushes.DarkOrange, 2);
                dc.DrawRoundedRectangle(Brushes.White, border,
                    new Rect(ToScreen(hinge).X - 6, ToScreen(hinge).Y - 6, 12, 12), 2, 2);
                dc.DrawRoundedRectangle(Brushes.White, border,
                    new Rect(ToScreen(latch).X - 6, ToScreen(latch).Y - 6, 12, 12), 2, 2);
                dc.DrawEllipse(Brushes.OrangeRed, null, ToScreen(door.Centre), 5, 5);
            }
        }

        foreach (var window in _windows)
        {
            if (!TryGetWindowEnds(window, out var a, out var b, out _))
                continue;
            DrawWindowSymbol(dc, a, b, window.Id == _selectedWindowId);
        }

        if ((_draggingDoor || _resizingDoor) && !_doorEditValid &&
            _doorPreview is not null && _doorSupport is not null)
        {
            var candidate = _doorPreview;
            var support = _doorSupport;
            double dx = (support.B.X - support.A.X) / support.Length;
            double dy = (support.B.Y - support.A.Y) / support.Length;
            dc.DrawLine(new Pen(Brushes.IndianRed, 6),
                ToScreen(new Point2(candidate.Centre.X - dx * candidate.Width / 2,
                                    candidate.Centre.Y - dy * candidate.Width / 2)),
                ToScreen(new Point2(candidate.Centre.X + dx * candidate.Width / 2,
                                    candidate.Centre.Y + dy * candidate.Width / 2)));
        }

        if ((_draggingWindow || _resizingWindow) && !_windowEditValid &&
            _windowPreview is not null && _resizeWindowWall is not null)
        {
            var preview = _windowPreview;
            var wall = _resizeWindowWall;
            double dx = (wall.B.X - wall.A.X) / wall.Length;
            double dy = (wall.B.Y - wall.A.Y) / wall.Length;
            Point a = ToScreen(new Point2(preview.Centre.X - dx * preview.Width / 2,
                                          preview.Centre.Y - dy * preview.Width / 2));
            Point b = ToScreen(new Point2(preview.Centre.X + dx * preview.Width / 2,
                                          preview.Centre.Y + dy * preview.Width / 2));
            dc.DrawLine(new Pen(Brushes.IndianRed, 7), a, b);
        }

        foreach (var opening in openings)
        {
            // White interior and a dashed boundary indicate missing upper
            // floor slab, not another room or a wall.
            var shape = BuildGeometry(opening.Outline, true);
            dc.DrawGeometry(Brushes.White,
                new Pen(Brushes.DarkOrange, 2.2)
                { DashStyle = DashStyles.Dash },
                shape);
            double x = opening.Outline.Average(p => p.X);
            double y = opening.Outline.Average(p => p.Y);
            Point label = ToScreen(new Point2(x, y));
            DrawText(dc, "OPEN TO BELOW", new Point(label.X + 5, label.Y + 15),
                10, Brushes.DarkRed);
        }

        foreach (var stair in _staircases.Where(stair =>
            stair.LowerFloorIndex == _stairFloorIndex ||
            stair.LowerFloorIndex + 1 == _stairFloorIndex))
        {
            var footprint = BuildingStoreys.StairFootprint(stair);
            // Landing areas are reserved clear floor on both sides of the run.
            foreach (var landing in BuildingStoreys.StairLandings(stair))
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(50, 84, 165, 120)),
                    new Pen(Brushes.SeaGreen, 1.5) { DashStyle = DashStyles.Dash },
                    BuildGeometry(landing, true));
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
            bool stacked = _staircases.Any(other => other.Id != stair.Id &&
                (other.LowerFloorIndex == _stairFloorIndex ||
                 other.LowerFloorIndex + 1 == _stairFloorIndex) &&
                other.LowerFloorIndex != stair.LowerFloorIndex &&
                other.Centre.DistanceTo(stair.Centre) < 0.25);
            if (stacked)
                middle.Offset(goingUp ? -10 : 10, goingUp ? -21 : 21);
            string label = goingUp
                ? $"UP to {BuildingStoreys.FloorLabel(stair.LowerFloorIndex + 1)}"
                : $"DOWN to {BuildingStoreys.FloorLabel(stair.LowerFloorIndex)}";
            DrawText(dc, label, new Point(middle.X + 5, middle.Y - 8),
                stacked ? 11 : 12,
                goingUp ? Brushes.DarkBlue : Brushes.DarkMagenta);

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

            if (selected && Tool == EditorTool.Select && !_draggingStair && !_rotatingStair)
                DrawStairRotationHandle(dc, stair);
        }

        // One shared stack stays at exactly the same world location on every
        // level where the perimeter covers it, including any basement.
        foreach (var chimney in _chimneys)
        {
            var footprint = BuildingStoreys.ChimneyFootprint(chimney);
            if (_outline.Count < 3 ||
                !FloorGeometry.IsWithinPolygon(footprint, _outline)) continue;
            bool selectedChimney = chimney.Id == _selectedChimneyId &&
                Tool == EditorTool.Select;
            dc.DrawGeometry(new SolidColorBrush(selectedChimney
                    ? Color.FromArgb(215, 255, 174, 65)
                    : Color.FromArgb(180, 171, 89, 65)),
                new Pen(selectedChimney ? Brushes.DarkOrange : Brushes.SaddleBrown,
                    selectedChimney ? 5 : 2), BuildGeometry(footprint, true));
            Point centre = ToScreen(chimney.Centre);
            bool linkedHearth = chimney.HearthFloorIndex == _stairFloorIndex &&
                chimney.HearthRoomIds.Any(id => _rooms.Any(r => r.Id == id));
            // Legacy maps did not store the room-to-hearth association.
            bool legacyHearth = chimney.HearthRoomIds.Count == 0 &&
                _stairFloorIndex == 0 && _rooms.Any(r =>
                    new[] { "Common room", "Living room", "Kitchen", "Dining room",
                            "Parlour", "Great hall", "Shop floor", "Workshop",
                            "Bedroom", "Study", "Guest room", "Chamber" }
                        .Contains(r.Type, StringComparer.OrdinalIgnoreCase) &&
                    PolygonGeometry.ContainsPoint(r.Outline, chimney.Centre));
            bool hearthHere = linkedHearth || legacyHearth;
            // Keep the symbol INSIDE its footprint. Long labels could
            // extend into the adjacent exterior window even with valid
            // physical clearance between the fireplace and glazing.
            DrawText(dc, hearthHere ? "FP" : "FL",
                new Point(centre.X - 6, centre.Y - 6), 9, Brushes.White);

            // A shared wall has two fireplace faces, but only one vertical
            // flue. The other face is never drawn on an upstairs bedroom.
            if (linkedHearth && chimney.OtherHearthCentre.HasValue)
            {
                var other = chimney.Copy();
                other.Centre = chimney.OtherHearthCentre.Value;
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(115, 185, 83, 48)),
                    new Pen(selectedChimney ? Brushes.DarkOrange : Brushes.Firebrick,
                        selectedChimney ? 5 : 2),
                    BuildGeometry(BuildingStoreys.ChimneyFootprint(other), true));
                Point otherAt = ToScreen(other.Centre);
                DrawText(dc, "FP",
                    new Point(otherAt.X - 6, otherAt.Y - 6), 9, Brushes.White);
            }
        }

        // Strong selection overlay is drawn AFTER the wall strokes and
        // chimney symbols, so the room remains unmistakably selected
        // even when its walls coincide with the exterior perimeter.
        if (Tool == EditorTool.Select && _selectedRoomId.HasValue)
        {
            var selectedRoom = _rooms.FirstOrDefault(r => r.Id == _selectedRoomId);
            if (selectedRoom is not null)
            {
                var selectedShape = WithStairwellHoles(selectedRoom.Outline, openings);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(75, 38, 136, 245)),
                    new Pen(Brushes.White, 7), selectedShape);
                dc.DrawGeometry(null, new Pen(Brushes.DodgerBlue, 4.5),
                    selectedShape);
                Point label = ToScreen(FindRoomLabelPoint(selectedRoom.Outline));
                DrawText(dc, "SELECTED: " + selectedRoom.Name,
                    new Point(label.X + 5, label.Y + 20), 11, Brushes.DarkBlue);
            }
        }

        if ((_draggingStair || _rotatingStair) && _dragPreviewStair is not null)
            DrawStairGhost(dc, _dragPreviewStair, _stairPreviewValid);
        else if (Tool == EditorTool.PlaceStairs && _stairPlacementHover.HasValue)
        {
            var template = StairTemplateProvider?.Invoke();
            if (template is not null)
            {
                template.Centre = _stairPlacementHover.Value;
                var check = StairValidator?.Invoke(template) ?? (true, "");
                DrawStairGhost(dc, template, check.Item1);
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

        DrawSuggestionPreview(dc);

        if (_outline.Count == 0 && _draft.Count == 0 && _suggestedFloor is null)
        {
            DrawText(dc, "Choose Draw Perimeter and click to place the first corner.",
                     new Point(20, 22), 15, Brushes.SlateGray);
        }
    }

    private Geometry CreateUnassignedGeometry(
        IReadOnlyList<StairwellOpening> openings)
    {
        Geometry available = BuildGeometry(_outline, true);
        foreach (var room in _rooms)
            if (room.Outline.Count >= 3)
                available = new CombinedGeometry(GeometryCombineMode.Exclude,
                    available, BuildGeometry(room.Outline, true));
        foreach (var voidArea in openings)
            available = new CombinedGeometry(GeometryCombineMode.Exclude,
                available, BuildGeometry(voidArea.Outline, true));
        return available;
    }

    private Geometry WithStairwellHoles(IReadOnlyList<Point2> outline,
        IReadOnlyList<StairwellOpening> openings)
    {
        Geometry current = BuildGeometry(outline, true);
        foreach (var opening in openings)
            current = new CombinedGeometry(GeometryCombineMode.Exclude,
                current, BuildGeometry(opening.Outline, true));
        return current;
    }

    private void DrawFloorSilhouettes(DrawingContext dc)
    {
        foreach (var other in _silhouetteFloors)
        {
            if (other.Floor.Perimeter.Count < 3) continue;
            var color = other.IsAbove
                ? Color.FromArgb(180, 15, 143, 136)
                : Color.FromArgb(180, 132, 91, 194);
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, 2.2)
            {
                DashStyle = new DashStyle([6d, 3d, 2d, 3d], 0)
            };
            dc.DrawGeometry(null, pen, BuildGeometry(other.Floor.Perimeter, true));
            Point anchor = ToScreen(other.Floor.Perimeter[0]);
            DrawText(dc, "FLOOR " + (other.FloorIndex + 1) + " SILHOUETTE",
                new Point(anchor.X + 5, anchor.Y - 34), 10, brush);
        }
    }

    private bool SelectSuggestedRoom(Point position)
    {
        if (_suggestedFloor is null) return false;
        var existing = _rooms.Select(room => room.Id).ToHashSet();
        var mouse = ToWorld(position);
        var found = _suggestedFloor.Rooms.LastOrDefault(room =>
            !existing.Contains(room.Id) &&
            PolygonGeometry.ContainsPoint(room.Outline, mouse));
        if (found is null) return false;
        _selectedSuggestedRoomId = found.Id;
        SuggestedRoomSelectionChanged?.Invoke();
        InvalidateVisual();
        Say("Selected suggestion: " + found.Name + ". Use Accept selected suggested room.");
        return true;
    }

    private void DrawSuggestionPreview(DrawingContext dc)
    {
        if (_suggestedFloor is null) return;
        var proposed = _suggestedFloor;
        if (proposed.Perimeter.Count >= 3 &&
            !_outline.SequenceEqual(proposed.Perimeter))
        {
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(35, 42, 171, 90)),
                new Pen(Brushes.ForestGreen, 3) { DashStyle = DashStyles.Dash },
                BuildGeometry(proposed.Perimeter, true));
        }
        var currentIds = _rooms.Select(r => r.Id).ToHashSet();
        foreach (var room in proposed.Rooms.Where(r => !currentIds.Contains(r.Id)))
        {
            var shape = BuildGeometry(room.Outline, true);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(85, 35, 173, 100)),
                new Pen(room.Id == _selectedSuggestedRoomId
                    ? Brushes.OrangeRed : Brushes.ForestGreen,
                    room.Id == _selectedSuggestedRoomId ? 3.5 : 2.3)
                { DashStyle = DashStyles.Dash },
                shape);
            Point at = ToScreen(FindRoomLabelPoint(room.Outline));
            DrawText(dc, "+ " + room.Name, at, 12, Brushes.DarkGreen);
        }

        var previewWalls = FloorGeometry.BuildWalls(proposed.Perimeter, proposed.Rooms);
        var newDoors = proposed.Doors.Where(d => _doors.All(o => o.Id != d.Id));
        foreach (var door in newDoors)
        {
            if (!FloorGeometry.TryFindDoorWall(door, previewWalls, out var wall) ||
                wall is null || wall.Length < 1e-6) continue;
            double ux = (wall.B.X - wall.A.X) / wall.Length;
            double uy = (wall.B.Y - wall.A.Y) / wall.Length;
            dc.DrawLine(new Pen(Brushes.DarkOrange, 6) { DashStyle = DashStyles.Dash },
                ToScreen(new Point2(door.Centre.X - ux * door.Width / 2,
                    door.Centre.Y - uy * door.Width / 2)),
                ToScreen(new Point2(door.Centre.X + ux * door.Width / 2,
                    door.Centre.Y + uy * door.Width / 2)));
        }

        foreach (var window in proposed.Windows.Where(w =>
            _windows.All(o => o.Id != w.Id)))
        {
            var proxy = new DoorDefinition { Centre = window.Centre, Width = window.Width };
            if (!FloorGeometry.TryFindDoorWall(proxy,
                previewWalls.Where(w => w.IsExterior).ToList(), out var wall) ||
                wall is null || wall.Length < 1e-6) continue;
            double ux = (wall.B.X - wall.A.X) / wall.Length;
            double uy = (wall.B.Y - wall.A.Y) / wall.Length;
            dc.DrawLine(new Pen(Brushes.DodgerBlue, 6) { DashStyle = DashStyles.Dash },
                ToScreen(new Point2(window.Centre.X - ux * window.Width / 2,
                    window.Centre.Y - uy * window.Width / 2)),
                ToScreen(new Point2(window.Centre.X + ux * window.Width / 2,
                    window.Centre.Y + uy * window.Width / 2)));
        }

        foreach (var stair in _suggestedStairs.Where(s =>
            _staircases.All(old => old.Id != s.Id) &&
            (s.LowerFloorIndex == _stairFloorIndex ||
             s.LowerFloorIndex + 1 == _stairFloorIndex)))
        {
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(60, 31, 145, 94)),
                new Pen(Brushes.ForestGreen, 2.5) { DashStyle = DashStyles.Dash },
                BuildGeometry(BuildingStoreys.StairFootprint(stair), true));
            DrawText(dc, stair.LowerFloorIndex == _stairFloorIndex
                    ? "+ STAIRS UP" : "+ STAIRS DOWN",
                ToScreen(stair.Centre), 11, Brushes.DarkGreen);
        }
    }

    private void DrawFloorReferences(DrawingContext dc)
    {
        foreach (var reference in _referenceFloors)
        {
            if (reference.Floor.Perimeter.Count < 3) continue;
            bool above = reference.IsAbove;
            Color baseColor = above
                ? Color.FromRgb(20, 146, 143)
                : Color.FromRgb(151, 90, 178);
            byte outlineAlpha = (byte)Math.Round(220 * _referenceOpacity);
            byte fillAlpha = (byte)Math.Round(28 * _referenceOpacity);
            var lineBrush = new SolidColorBrush(Color.FromArgb(
                outlineAlpha, baseColor.R, baseColor.G, baseColor.B));
            var fillBrush = new SolidColorBrush(Color.FromArgb(
                fillAlpha, baseColor.R, baseColor.G, baseColor.B));
            var outlinePen = new Pen(lineBrush, 2)
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawGeometry(fillBrush, outlinePen,
                BuildGeometry(reference.Floor.Perimeter, true));

            foreach (var wall in FloorGeometry.BuildWalls(
                reference.Floor.Perimeter, reference.Floor.Rooms))
            {
                if (wall.IsExterior) continue;
                dc.DrawLine(new Pen(lineBrush, 1.3)
                    { DashStyle = DashStyles.Dash },
                    ToScreen(wall.A), ToScreen(wall.B));
            }

            // Show the overlay name near its exterior corner instead of
            // blocking central room labels.
            var corner = ToScreen(reference.Floor.Perimeter[0]);
            DrawText(dc, above ? "ABOVE" : "BELOW",
                new Point(corner.X + 5, corner.Y - 21), 11, lineBrush);
        }
    }

    private void DrawStairGhost(DrawingContext dc, StaircaseDefinition stair, bool valid)
    {
        Brush stroke = valid ? Brushes.ForestGreen : Brushes.Firebrick;
        Brush fill = valid
            ? new SolidColorBrush(Color.FromArgb(95, 62, 176, 108))
            : new SolidColorBrush(Color.FromArgb(95, 230, 75, 75));

        var footprint = BuildingStoreys.StairFootprint(stair);
        dc.DrawGeometry(fill, new Pen(stroke, 2.5)
            { DashStyle = DashStyles.Dash }, BuildGeometry(footprint, true));

        double radians = stair.RotationDegrees * Math.PI / 180;
        double dx = -Math.Sin(radians), dy = Math.Cos(radians);
        Point centre = ToScreen(stair.Centre);
        dc.DrawLine(new Pen(stroke, 2), centre,
            ToScreen(new Point2(stair.Centre.X + dx * stair.Length * 0.3,
                                 stair.Centre.Y + dy * stair.Length * 0.3)));
        DrawText(dc, valid
                ? $"{StairRotation.Normalize(stair.RotationDegrees):0.#}°"
                : $"Invalid {StairRotation.Normalize(stair.RotationDegrees):0.#}°",
            new Point(centre.X + 10, centre.Y + 10), 11, stroke);

        if (_rotatingStair)
            DrawStairRotationHandle(dc, stair, stroke);
    }

    private void DrawStairRotationHandle(DrawingContext dc,
        StaircaseDefinition stair, Brush? highlight = null)
    {
        Point tip = ToScreen(StairRotation.HandleCentre(stair, _pixelsPerMetre));
        double angle = stair.RotationDegrees * Math.PI / 180;
        Point edge = ToScreen(new Point2(
            stair.Centre.X - Math.Sin(angle) * stair.Length / 2,
            stair.Centre.Y + Math.Cos(angle) * stair.Length / 2));

        Brush color = highlight ?? Brushes.DarkOrange;
        dc.DrawLine(new Pen(color, 1.5) { DashStyle = DashStyles.Dash },
            edge, tip);
        dc.DrawEllipse(Brushes.White, new Pen(color, 2.5), tip, 9, 9);
        DrawText(dc, "↻", new Point(tip.X - 6, tip.Y - 9), 14, color);
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
        // The world is stored in metres; a tabletop square is exactly
        // 5 or 10 feet, never rounded to a convenient metric approximation.
        double cell = GridFeet == 0 ? 1 : GridFeet * 0.3048;
        double pixelCell = cell * _pixelsPerMetre;
        if (pixelCell < 5) return; // Avoid unreadably dense lines.
        var minor = new Pen(new SolidColorBrush(Color.FromRgb(238, 241, 245)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromRgb(212, 221, 231)), 1);
        int minX = (int)Math.Floor(-_origin.X / pixelCell);
        int maxX = (int)Math.Ceiling((ActualWidth - _origin.X) / pixelCell);
        int minY = (int)Math.Floor(-_origin.Y / pixelCell);
        int maxY = (int)Math.Ceiling((ActualHeight - _origin.Y) / pixelCell);
        for (int x = minX; x <= maxX; x++)
        {
            double sx = x * pixelCell + _origin.X;
            dc.DrawLine(x % 5 == 0 ? major : minor,
                        new Point(sx, 0), new Point(sx, ActualHeight));
        }
        for (int y = minY; y <= maxY; y++)
        {
            double sy = y * pixelCell + _origin.Y;
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
            double length = a.DistanceTo(b);
            string label = GridFeet == 0
                ? $"{length:0.##} m"
                : $"{length / 0.3048:0.##} ft";
            DrawText(dc, label,
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

        if (_displayMode == MapVisibilityMode.Player)
        {
            Say("Player preview is presentation-only. Switch to Editor to make changes.");
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Select && SelectSuggestedRoom(position))
        {
            e.Handled = true;
            return;
        }

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
        if (Tool == EditorTool.AddWindow)
        {
            TryAddWindow(position);
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.PlaceStairs)
        {
            var candidate = StairTemplateProvider?.Invoke();
            if (candidate is null)
            {
                Say("Enter valid stair dimensions, rotation, and select a floor with a floor above.");
                e.Handled = true;
                return;
            }
            candidate.Centre = SnapStairCentre(ToWorld(position));
            var validation = StairValidator?.Invoke(candidate) ?? (true, "");
            if (!validation.Item1)
            {
                Say("Cannot place stairs: " + validation.Item2);
                e.Handled = true;
                return;
            }
            StairPlacementRequested?.Invoke(candidate.Centre);
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
            if (TryStartStairRotation(position))
            {
                e.Handled = true;
                return;
            }

            if (TryStartWindowDrag(position) || TryStartDoorDrag(position))
            {
                e.Handled = true;
                return;
            }

            if (e.ClickCount >= 2 && TryInsertCorner(position, perimeterOnly: false))
            {
                e.Handled = true;
                return;
            }

            if (TrySelectVertex(position))
            {
                _selectedChimneyId = null;
                _beforeDrag = Snapshot();
                _draggingVertex = true;
                _dragOriginalPoint = SelectedVertices()![_selectedVertex];
                CaptureMouse();
            }
            else if (TrySelectDoor(position))
            {
                _selectedChimneyId = null;
                _selectedWindowId = null;
                _selectedStairId = null;
                _selectedVertex = -1;
                _selectedRoomId = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (TrySelectChimney(position))
            {
                _selectedStairId = null;
                _selectedWindowId = null;
                _selectedDoorId = null;
                _selectedRoomId = null;
                _selectedVertex = -1;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (TrySelectStair(position))
            {
                _selectedChimneyId = null;
                _selectedWindowId = null;
                _selectedVertex = -1;
                _selectedDoorId = null;
                _selectedRoomId = null;
                _dragStartStair = SelectedStaircase;
                if (_dragStartStair is not null)
                {
                    var pointer = ToWorld(position);
                    _stairGrabOffset = new Point2(
                        pointer.X - _dragStartStair.Centre.X,
                        pointer.Y - _dragStartStair.Centre.Y);
                    _draggingStair = true;
                    _dragPreviewStair = _dragStartStair.Copy();
                    _stairPreviewValid = true;
                    _stairPreviewReason = "";
                    CaptureMouse();
                    Cursor = Cursors.SizeAll;
                }
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _selectedStairId = null;
                _selectedChimneyId = null;
                _selectedWindowId = null;
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

        if (_rotatingStair && _dragStartStair is not null)
        {
            PreviewStairRotation(position);
            return;
        }

        if (_draggingStair && _dragStartStair is not null)
        {
            PreviewStairDrag(position);
            return;
        }

        if ((_draggingDoor || _resizingDoor) &&
            _beforeDrag is not null && _originalDoor is not null)
        {
            PreviewDoorDrag(position);
            return;
        }

        if ((_draggingWindow || _resizingWindow) &&
            _beforeDrag is not null && _originalWindow is not null)
        {
            PreviewWindowDrag(position);
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
            // A perimeter corner also carries any room corners along its
            // incident exterior edges, interpolating their movement.
            // Door/window centres follow their actual supporting wall edges.
            var shifted = SharedBoundaryEditing.MoveVertex(
                DocumentFromSnapshot(_beforeDrag), _dragOriginalPoint, movedPoint);
            _outline = shifted.Perimeter;
            _rooms = shifted.Rooms;
            _doors = shifted.Doors;
            _windows = shifted.Windows;

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
        else if (Tool == EditorTool.PlaceStairs)
        {
            _stairPlacementHover = SnapStairCentre(ToWorld(position));
            InvalidateVisual();
        }
        else if (Tool == EditorTool.Select)
        {
            if (IsOverStairRotationHandle(position))
                Cursor = Cursors.Hand;
            else
            {
                var selected = _windows.FirstOrDefault(w => w.Id == _selectedWindowId);
                if (selected is not null &&
                    TryGetWindowEnds(selected, out var a, out var b, out _) &&
                    ((a - position).Length <= 11 || (b - position).Length <= 11))
                    Cursor = Cursors.SizeAll;
                else if (_windows.Any(w =>
                    TryGetWindowEnds(w, out var from, out var to, out _) &&
                    PolygonGeometry.ClosestPointOnSegment(
                        ToWorld(position), ToWorld(from), ToWorld(to))
                        .DistanceTo(ToWorld(position)) * _pixelsPerMetre <= 10))
                    Cursor = Cursors.SizeAll;
                else if (_doors.Any(d =>
                    d.Id == _selectedDoorId &&
                    TryGetDoorEnds(d, out var first, out var second, out _) &&
                    ((first - position).Length <= 10 ||
                     (second - position).Length <= 10)) ||
                    _doors.Any(d =>
                        (ToScreen(d.Centre) - position).Length <= 12))
                    Cursor = Cursors.SizeAll;
                else
                    Cursor = DefaultCursor();
            }
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Tool == EditorTool.PlaceStairs && !_draggingStair)
        {
            _stairPlacementHover = null;
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

        if (_rotatingStair && e.ChangedButton == MouseButton.Left)
        {
            PreviewStairRotation(e.GetPosition(this));
            _rotatingStair = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();

            if (_stairPreviewValid && _dragPreviewStair is not null &&
                _dragStartStair is not null &&
                Math.Abs(StairRotation.Normalize(_dragPreviewStair.RotationDegrees) -
                         StairRotation.Normalize(_dragStartStair.RotationDegrees)) > 1e-6)
            {
                StairEditRequested?.Invoke(_dragPreviewStair.Copy());
            }
            else if (!_stairPreviewValid)
                Say("Staircase rotation rejected: " + _stairPreviewReason);

            _dragStartStair = null;
            _dragPreviewStair = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_draggingStair && e.ChangedButton == MouseButton.Left)
        {
            PreviewStairDrag(e.GetPosition(this));
            _draggingStair = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            if (_stairPreviewValid && _dragPreviewStair is not null &&
                _dragStartStair is not null &&
                _dragPreviewStair.Centre.DistanceTo(_dragStartStair.Centre) > 1e-7)
            {
                StairEditRequested?.Invoke(_dragPreviewStair.Copy());
            }
            else if (!_stairPreviewValid)
                Say("Staircase cannot move here: " + _stairPreviewReason);
            _dragStartStair = null;
            _dragPreviewStair = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if ((_draggingDoor || _resizingDoor) &&
            e.ChangedButton == MouseButton.Left)
        {
            PreviewDoorDrag(e.GetPosition(this));
            bool changed = _originalDoor is not null &&
                _doorPreview is not null &&
                (_doorPreview.Centre.DistanceTo(_originalDoor.Centre) > 1e-7 ||
                 Math.Abs(_doorPreview.Width - _originalDoor.Width) > 1e-7);
            _draggingDoor = false;
            _resizingDoor = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            if (_beforeDrag is not null && changed && _doorEditValid)
            {
                PushUndo(_beforeDrag);
                RaiseChanged();
                Say("Door moved or resized.");
            }
            else
            {
                if (_beforeDrag is not null) Restore(_beforeDrag);
                if (!_doorEditValid)
                    Say("Door edit rejected: " + _doorEditError);
            }
            _beforeDrag = null;
            _originalDoor = null;
            _doorPreview = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if ((_draggingWindow || _resizingWindow) &&
            e.ChangedButton == MouseButton.Left)
        {
            PreviewWindowDrag(e.GetPosition(this));
            bool changed = _originalWindow is not null &&
                _windowPreview is not null &&
                (_windowPreview.Centre.DistanceTo(_originalWindow.Centre) > 1e-7 ||
                 Math.Abs(_windowPreview.Width - _originalWindow.Width) > 1e-7);
            _draggingWindow = false;
            _resizingWindow = false;
            ReleaseMouseCapture();
            Cursor = DefaultCursor();
            if (_beforeDrag is not null && changed && _windowEditValid)
            {
                PushUndo(_beforeDrag);
                RaiseChanged();
                Say("Window moved or resized.");
            }
            else
            {
                if (_beforeDrag is not null) Restore(_beforeDrag);
                if (!_windowEditValid)
                    Say("Window edit rejected: " + _windowEditError);
            }
            _beforeDrag = null;
            _originalWindow = null;
            _windowPreview = null;
            InvalidateVisual();
            e.Handled = true;
            return;
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
        _windows = combined.Windows;
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
            10 / _pixelsPerMetre, SnapToGrid, GridStepMetres,
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
        _windows = document.Windows;
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
            amount = Math.Round(amount / GridStepMetres,
                MidpointRounding.AwayFromZero) * GridStepMetres;

        var original = DocumentFromSnapshot(_beforeDrag);
        if (InteriorEditing.TryMoveWall(original, wall, amount, out var moved, out var reason))
        {
            _outline = moved.Perimeter;
            _rooms = moved.Rooms;
            _doors = moved.Doors;
            _windows = moved.Windows;
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
        Doors = snapshot.Doors.Select(d => d.Copy()).ToList(),
        Windows = snapshot.Windows.Select(w => w.Copy()).ToList(),
        WallThickness = snapshot.WallThickness.Copy()
    };

    private bool SnapshotMatches(EditorSnapshot snapshot) =>
        _outline.SequenceEqual(snapshot.Perimeter) &&
        _rooms.Count == snapshot.Rooms.Count &&
        _rooms.Zip(snapshot.Rooms).All(pair =>
            pair.First.Outline.SequenceEqual(pair.Second.Outline)) &&
        _doors.Zip(snapshot.Doors).All(pair =>
            pair.First.Centre.Equals(pair.Second.Centre)) &&
        _windows.Count == snapshot.Windows.Count &&
        _windows.Zip(snapshot.Windows).All(pair =>
            pair.First.Centre.Equals(pair.Second.Centre));

    private bool IsOverStairRotationHandle(Point position)
    {
        var selected = SelectedStaircase;
        if (selected is null ||
            (selected.LowerFloorIndex != _stairFloorIndex &&
             selected.LowerFloorIndex + 1 != _stairFloorIndex))
            return false;
        Point handle = ToScreen(StairRotation.HandleCentre(selected, _pixelsPerMetre));
        return (handle - position).Length <= 13;
    }

    private bool TryStartStairRotation(Point position)
    {
        var selected = SelectedStaircase;
        if (selected is null) return false;
        if (selected.LowerFloorIndex != _stairFloorIndex &&
            selected.LowerFloorIndex + 1 != _stairFloorIndex)
            return false;

        if (!IsOverStairRotationHandle(position))
            return false;

        Point2 initial = ToWorld(position);
        if (initial.DistanceTo(selected.Centre) < 1e-6)
            return false;

        _dragStartStair = selected;
        _dragPreviewStair = selected.Copy();
        _rotationStartPointerAngle = StairRotation.PointerAngle(selected.Centre, initial);
        _rotatingStair = true;
        _stairPreviewValid = true;
        _stairPreviewReason = "";
        CaptureMouse();
        Cursor = Cursors.Hand;
        Say("Rotating stairs. Drag the handle; hold Shift to snap to 15°. Release to apply.");
        InvalidateVisual();
        return true;
    }

    private void PreviewStairRotation(Point position)
    {
        if (_dragStartStair is null) return;
        Point2 pointer = ToWorld(position);
        if (pointer.DistanceTo(_dragStartStair.Centre) < 1e-6)
            return;

        var proposed = _dragStartStair.Copy();
        proposed.RotationDegrees = StairRotation.FromDrag(
            _dragStartStair.RotationDegrees,
            _rotationStartPointerAngle,
            StairRotation.PointerAngle(_dragStartStair.Centre, pointer),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var check = StairValidator?.Invoke(proposed) ?? (true, "");
        _dragPreviewStair = proposed;
        _stairPreviewValid = check.Item1;
        _stairPreviewReason = check.Item2;
        InvalidateVisual();
    }

    private Point2 SnapStairCentre(Point2 point)
    {
        if (!SnapToGrid || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return point;
        return new Point2(
            Math.Round(point.X * 2, MidpointRounding.AwayFromZero) / 2,
            Math.Round(point.Y * 2, MidpointRounding.AwayFromZero) / 2);
    }

    private void PreviewStairDrag(Point position)
    {
        if (_dragStartStair is null) return;
        var pointer = ToWorld(position);
        var candidate = _dragStartStair.Copy();
        candidate.Centre = SnapStairCentre(new Point2(
            pointer.X - _stairGrabOffset.X,
            pointer.Y - _stairGrabOffset.Y));
        var check = StairValidator?.Invoke(candidate) ?? (true, "");
        _dragPreviewStair = candidate;
        _stairPreviewValid = check.Item1;
        _stairPreviewReason = check.Item2;
        InvalidateVisual();
    }

    public void DeleteSelectedWindow()
    {
        if (!_selectedWindowId.HasValue) return;
        var previous = Snapshot();
        _windows.RemoveAll(w => w.Id == _selectedWindowId.Value);
        _selectedWindowId = null;
        PushUndo(previous);
        RaiseChanged();
        Say("Window removed.");
    }

    public void SetSelectedWindowWidth(double width)
    {
        var window = _windows.FirstOrDefault(w => w.Id == _selectedWindowId);
        if (window is null || Math.Abs(window.Width - width) < 1e-9) return;
        var previous = Snapshot();
        window.Width = width;
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say($"Cannot resize window: {reason}");
            return;
        }
        PushUndo(previous);
        RaiseChanged();
        Say("Window width updated.");
    }

    /// <summary>Centre the selected window on its adjacent room wall edge.</summary>
    public void CentreSelectedWindowOnWall()
    {
        if (!_selectedWindowId.HasValue)
        {
            Say("Select a window to centre.");
            return;
        }

        var previous = Snapshot();
        if (!OpeningEditing.TryCentreWindow(Document(), _selectedWindowId.Value,
            out var updated, out var message))
        {
            Say(message);
            return;
        }

        var before = _windows.First(w => w.Id == _selectedWindowId.Value);
        var after = updated.Windows.First(w => w.Id == _selectedWindowId.Value);
        if (before.Centre.DistanceTo(after.Centre) < 1e-7)
        {
            Say("Window is already centred on its wall segment.");
            return;
        }

        _windows = updated.Windows.Select(w => w.Copy()).ToList();
        PushUndo(previous);
        RaiseChanged();
        Say(message);
    }

    /// <summary>Centre the selected door on its current physical wall piece.</summary>
    public void CentreSelectedDoorOnWall()
    {
        if (!_selectedDoorId.HasValue)
        {
            Say("Select a door to centre.");
            return;
        }

        var previous = Snapshot();
        if (!OpeningEditing.TryCentreDoor(Document(), _selectedDoorId.Value,
            out var updated, out var message))
        {
            Say(message);
            return;
        }

        var before = _doors.First(d => d.Id == _selectedDoorId.Value);
        var after = updated.Doors.First(d => d.Id == _selectedDoorId.Value);
        if (before.Centre.DistanceTo(after.Centre) < 1e-7)
        {
            Say("Door is already centred on its wall segment.");
            return;
        }

        _doors = updated.Doors.Select(d => d.Copy()).ToList();
        PushUndo(previous);
        RaiseChanged();
        Say(message);
    }

    public void SetSelectedDoorVisibility(bool secret, bool revealed)
    {
        var door = _doors.FirstOrDefault(d => d.Id == _selectedDoorId);
        if (door is null || (door.IsSecret == secret &&
            door.IsRevealed == (secret && revealed))) return;
        var previous = Snapshot();
        door.IsSecret = secret;
        door.IsRevealed = secret && revealed;
        PushUndo(previous);
        RaiseChanged();
        Say(secret ? "Secret door updated." : "Door is now ordinary.");
    }

    public void SetSelectedDoorWidth(double width)
    {
        var door = _doors.FirstOrDefault(d => d.Id == _selectedDoorId);
        if (door is null || Math.Abs(door.Width - width) < 1e-9) return;
        var previous = Snapshot();
        door.Width = width;
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say("Cannot resize door: " + reason);
            return;
        }
        PushUndo(previous);
        RaiseChanged();
        Say("Door width updated.");
    }

    private bool TryGetDoorEnds(DoorDefinition door,
        out Point a, out Point b, out WallSegment? support)
    {
        a = default;
        b = default;
        if (!FloorGeometry.TryFindDoorWall(
            door, FloorGeometry.BuildWalls(_outline, _rooms), out support) ||
            support is null || support.Length < 1e-6)
            return false;
        double dx = (support.B.X - support.A.X) / support.Length;
        double dy = (support.B.Y - support.A.Y) / support.Length;
        a = ToScreen(new Point2(
            door.Centre.X - dx * door.Width / 2,
            door.Centre.Y - dy * door.Width / 2));
        b = ToScreen(new Point2(
            door.Centre.X + dx * door.Width / 2,
            door.Centre.Y + dy * door.Width / 2));
        return true;
    }

    private bool TryStartDoorDrag(Point position)
    {
        // The door centre is the drag target. Resize handles are shown only
        // after selection so short doorways remain easy to move.
        foreach (var door in _doors.OrderByDescending(d => d.Id == _selectedDoorId))
        {
            if (!TryGetDoorEnds(door, out var a, out var b, out var support) ||
                support is null) continue;
            bool selected = door.Id == _selectedDoorId;
            bool endA = (position - a).Length <= 10;
            bool endB = (position - b).Length <= 10;
            bool atCentre = (position - ToScreen(door.Centre)).Length <= 12;
            if (!atCentre && !(selected && (endA || endB))) continue;

            _selectedDoorId = door.Id;
            _selectedWindowId = null;
            _selectedChimneyId = null;
            _selectedStairId = null;
            _selectedRoomId = null;
            _selectedVertex = -1;
            _beforeDrag = Snapshot();
            _originalDoor = door.Copy();
            _doorPreview = door.Copy();
            _doorSupport = support;
            // Narrow doorways may have handles inside the centre hit zone;
            // a click directly on the centre marker still moves the door.
            _resizingDoor = selected && (endA || endB) &&
                (position - ToScreen(door.Centre)).Length > 5;
            _draggingDoor = !_resizingDoor;
            _doorEditValid = true;
            _doorEditError = "";
            if (_resizingDoor)
                _doorFixedEnd = ToWorld(endA ? b : a);
            var mouse = ToWorld(position);
            _doorGrabOffset = new Point2(
                mouse.X - door.Centre.X, mouse.Y - door.Centre.Y);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return true;
        }
        return false;
    }

    private void PreviewDoorDrag(Point position)
    {
        if (_beforeDrag is null || _originalDoor is null) return;
        var candidate = _originalDoor.Copy();
        if (_resizingDoor && _doorSupport is not null)
        {
            Point2 dragged = OpeningEditing.OnWall(
                ToWorld(position), _doorSupport,
                SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            candidate = OpeningEditing.ResizeDoor(
                _originalDoor, _doorFixedEnd, dragged);
        }
        else if (_draggingDoor)
        {
            var pointer = ToWorld(position);
            Point2 target = new(
                pointer.X - _doorGrabOffset.X,
                pointer.Y - _doorGrabOffset.Y);
            double closest = 20;
            bool found = false;
            foreach (var wall in FloorGeometry.BuildWalls(_outline, _rooms))
            {
                Point2 projection = PolygonGeometry.ClosestPointOnSegment(
                    target, wall.A, wall.B);
                double distance = projection.DistanceTo(target) * _pixelsPerMetre;
                if (distance >= closest) continue;
                closest = distance;
                candidate.Centre = OpeningEditing.OnWall(target, wall,
                    SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                found = true;
            }
            if (!found)
            {
                _doorEditValid = false;
                _doorEditError = "Move onto an existing wall.";
                _doorPreview = candidate;
                _doors = _beforeDrag.Doors.Select(d => d.Copy()).ToList();
                InvalidateVisual();
                return;
            }
        }

        _doorPreview = candidate;
        _doors = _beforeDrag.Doors.Select(d =>
            d.Id == candidate.Id ? candidate.Copy() : d.Copy()).ToList();
        _doorEditValid = Validate(out _doorEditError);
        if (!_doorEditValid)
            _doors = _beforeDrag.Doors.Select(d => d.Copy()).ToList();
        InvalidateVisual();
    }

    private void DrawWindowSymbol(
        DrawingContext dc, Point a, Point b, bool selected)
    {
        // The light casing clearly separates the glass from the 3 px blue
        // exterior wall, even at normal zoom. Handles are deliberately
        // larger than the visible window line when selected.
        dc.DrawLine(new Pen(Brushes.White, 15), a, b);
        dc.DrawLine(new Pen(Brushes.MidnightBlue, 11), a, b);
        dc.DrawLine(new Pen(selected ? Brushes.OrangeRed : Brushes.Cyan, 8), a, b);
        dc.DrawLine(new Pen(Brushes.White, 2), a, b);

        if (selected && Tool == EditorTool.Select)
        {
            var border = new Pen(Brushes.DarkOrange, 2);
            dc.DrawRoundedRectangle(Brushes.White, border,
                new Rect(a.X - 6, a.Y - 6, 12, 12), 2, 2);
            dc.DrawRoundedRectangle(Brushes.White, border,
                new Rect(b.X - 6, b.Y - 6, 12, 12), 2, 2);
        }
    }

    private bool TryGetWindowEnds(WindowDefinition window,
        out Point a, out Point b, out WallSegment? support)
    {
        a = default;
        b = default;
        support = null;
        var pseudoDoor = new DoorDefinition
        {
            Centre = window.Centre, Width = window.Width
        };
        if (!FloorGeometry.TryFindDoorWall(pseudoDoor,
            FloorGeometry.BuildWalls(_outline, _rooms)
                .Where(w => w.IsExterior).ToList(), out support) ||
            support is null || support.Length <= 1e-7)
            return false;
        double dx = (support.B.X - support.A.X) / support.Length;
        double dy = (support.B.Y - support.A.Y) / support.Length;
        a = ToScreen(new Point2(
            window.Centre.X - dx * window.Width / 2,
            window.Centre.Y - dy * window.Width / 2));
        b = ToScreen(new Point2(
            window.Centre.X + dx * window.Width / 2,
            window.Centre.Y + dy * window.Width / 2));
        return true;
    }

    private bool TryStartWindowDrag(Point position)
    {
        // A selected window's handles have priority over all other hit targets.
        // Other window lines can be picked and dragged in a single gesture.
        var ordered = _windows.OrderByDescending(w => w.Id == _selectedWindowId);
        foreach (var window in ordered)
        {
            if (!TryGetWindowEnds(window, out var a, out var b, out var wall) ||
                wall is null) continue;
            bool selected = window.Id == _selectedWindowId;
            bool endA = (a - position).Length <= 11;
            bool endB = (b - position).Length <= 11;
            bool onLine = PolygonGeometry.ClosestPointOnSegment(
                ToWorld(position), ToWorld(a), ToWorld(b))
                    .DistanceTo(ToWorld(position)) * _pixelsPerMetre <= 10;
            if (!onLine && !(selected && (endA || endB))) continue;

            _selectedWindowId = window.Id;
            _selectedChimneyId = null;
            _selectedDoorId = null;
            _selectedRoomId = null;
            _selectedStairId = null;
            _selectedVertex = -1;
            _beforeDrag = Snapshot();
            _originalWindow = window.Copy();
            _windowPreview = window.Copy();
            _resizingWindow = selected && (endA || endB);
            _draggingWindow = !_resizingWindow;
            _windowEditValid = true;
            _windowEditError = "";
            _resizeWindowWall = wall;
            if (_resizingWindow)
                _windowFixedEnd = ToWorld(endA ? b : a);
            var world = ToWorld(position);
            _windowGrabOffset = new Point2(
                world.X - window.Centre.X, world.Y - window.Centre.Y);
            CaptureMouse();
            Cursor = _resizingWindow ? Cursors.SizeWE : Cursors.SizeAll;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return true;
        }
        return false;
    }

    private void PreviewWindowDrag(Point position)
    {
        if (_beforeDrag is null || _originalWindow is null) return;
        var candidate = _originalWindow.Copy();
        if (_resizingWindow && _resizeWindowWall is not null)
        {
            Point2 pointer = ToWorld(position);
            Point2 dragged = OpeningEditing.OnWall(pointer, _resizeWindowWall,
                SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            candidate = WindowPlacement.ResizeFromEndpoint(
                _originalWindow, _windowFixedEnd, dragged);
        }
        else if (_draggingWindow)
        {
            Point2 pointer = ToWorld(position);
            Point2 target = new(pointer.X - _windowGrabOffset.X,
                                pointer.Y - _windowGrabOffset.Y);
            double bestPixels = 20;
            bool found = false;
            foreach (var wall in FloorGeometry.BuildWalls(_outline, _rooms)
                .Where(w => w.IsExterior))
            {
                Point2 projected = PolygonGeometry.ClosestPointOnSegment(
                    target, wall.A, wall.B);
                double pixels = target.DistanceTo(projected) * _pixelsPerMetre;
                if (pixels >= bestPixels) continue;
                bestPixels = pixels;
                candidate.Centre = OpeningEditing.OnWall(target, wall,
                    SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                found = true;
            }
            if (!found)
            {
                _windowEditValid = false;
                _windowEditError = "Move the window onto an exterior wall.";
                _windowPreview = candidate;
                _windows = _beforeDrag.Windows.Select(w => w.Copy()).ToList();
                InvalidateVisual();
                return;
            }
        }

        _windowPreview = candidate;
        _windows = _beforeDrag.Windows.Select(w =>
            w.Id == candidate.Id ? candidate.Copy() : w.Copy()).ToList();

        _windowEditValid = Validate(out _windowEditError);
        if (!_windowEditValid)
            _windows = _beforeDrag.Windows.Select(w => w.Copy()).ToList();
        InvalidateVisual();
    }

    private void TryAddWindow(Point position)
    {
        var walls = FloorGeometry.BuildWalls(_outline, _rooms)
            .Where(w => w.IsExterior);
        WallSegment? nearestWall = null;
        Point2 centre = default;
        double bestDistance = 12;
        Point2 mouse = ToWorld(position);
        foreach (var wall in walls)
        {
            Point2 projected = PolygonGeometry.ClosestPointOnSegment(mouse, wall.A, wall.B);
            double pixels = mouse.DistanceTo(projected) * _pixelsPerMetre;
            if (pixels >= bestDistance) continue;
            centre = projected;
            nearestWall = wall;
            bestDistance = pixels;
        }
        if (nearestWall is null)
        {
            Say("Click near an exterior wall to place a window.");
            return;
        }

        centre = OpeningEditing.OnWall(mouse, nearestWall,
            SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        double width = WindowWidthProvider?.Invoke() ?? 1.2;
        var previous = Snapshot();
        var window = new WindowDefinition { Centre = centre, Width = width };
        _windows.Add(window);
        if (!Validate(out var reason))
        {
            Restore(previous);
            Say($"Cannot place window: {reason}");
            return;
        }
        PushUndo(previous);
        _selectedWindowId = window.Id;
        _selectedDoorId = null;
        _selectedRoomId = null;
        _selectedVertex = -1;
        RaiseChanged();
        Say("Window placed. Select it to resize or remove it.");
    }

    private bool TrySelectWindow(Point position)
    {
        foreach (var window in _windows)
        {
            if ((ToScreen(window.Centre) - position).Length > 11) continue;
            _selectedWindowId = window.Id;
            return true;
        }
        return false;
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
        _selectedWindowId = null;
        _selectedRoomId = null;
        _selectedVertex = -1;
        RaiseChanged();
        Say("Door added. Select it and use Delete Door to remove it.");
    }

    private bool TrySelectChimney(Point position)
    {
        var world = ToWorld(position);
        foreach (var chimney in _chimneys)
        {
            if (_outline.Count < 3 ||
                !FloorGeometry.IsWithinPolygon(
                    BuildingStoreys.ChimneyFootprint(chimney), _outline))
                continue;
            bool hit = PolygonGeometry.ContainsPoint(
                BuildingStoreys.ChimneyFootprint(chimney), world);
            if (!hit && chimney.HearthFloorIndex == _stairFloorIndex &&
                chimney.OtherHearthCentre.HasValue)
            {
                var other = chimney.Copy();
                other.Centre = chimney.OtherHearthCentre.Value;
                hit = PolygonGeometry.ContainsPoint(
                    BuildingStoreys.ChimneyFootprint(other), world);
            }
            if (!hit) continue;
            _selectedChimneyId = chimney.Id;
            return true;
        }
        return false;
    }

    private bool TrySelectStair(Point position)
    {
        var world = ToWorld(position);
        var matches = _staircases.Where(stair =>
            (stair.LowerFloorIndex == _stairFloorIndex ||
             stair.LowerFloorIndex + 1 == _stairFloorIndex) &&
            PolygonGeometry.ContainsPoint(BuildingStoreys.StairFootprint(stair), world))
            .OrderBy(stair => stair.LowerFloorIndex).ToList();
        if (matches.Count == 0) return false;
        // Repeated clicks through stacked flights cycle the active one.
        int current = matches.FindIndex(s => s.Id == _selectedStairId);
        _selectedStairId = matches[(current + 1) % matches.Count].Id;
        return true;
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
            _selectedWindowId = null;
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
            _selectedWindowId = null;
            _selectedStairId = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            if (!TryVertexHit(_rooms[i].Outline, position, out selected)) continue;
            _selectedRoomId = _rooms[i].Id;
            _selectedDoorId = null;
            _selectedWindowId = null;
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

        // When inserting an exterior corner into an existing straight
        // wall, snap its new along-wall coordinate to the selected map grid.
        // Reproject onto the wall so a diagonal edge remains geometrically
        // exact. Shift continues to bypass snapping.
        if (SnapToGrid && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            IReadOnlyList<Point2> wallVertices = exterior
                ? _outline : _rooms.First(r => r.Id == roomId).Outline;
            point = PolygonGeometry.ClosestPointOnSegment(
                SnapToGridPoint(point), wallVertices[edge],
                wallVertices[(edge + 1) % wallVertices.Count]);
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
            _doors.Select(d => d.Copy()).ToList(),
            _windows.Select(w => w.Copy()).ToList(),
            _wallThickness.Copy());

    private void Restore(EditorSnapshot snapshot)
    {
        _outline = snapshot.Perimeter.ToList();
        _rooms = snapshot.Rooms.Select(r => r.Copy()).ToList();
        _doors = snapshot.Doors.Select(d => d.Copy()).ToList();
        _windows = snapshot.Windows.Select(w => w.Copy()).ToList();
        _wallThickness = snapshot.WallThickness.Copy();
        InvalidateVisual();
    }

    private void PushUndo(EditorSnapshot previous)
    {
        if (!UsesGlobalHistory)
            _undo.Push(previous);
        else
            _undo.Clear();
        _redo.Clear();
        DocumentEditCommitted?.Invoke(DocumentFromSnapshot(previous));
    }

    private void ClearSelection()
    {
        _selectedChimneyId = null;
        _selectedRoomId = null;
        _selectedDoorId = null;
        _selectedWindowId = null;
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

    private double GridStepMetres => GridFeet == 0 ? 0.5 : GridFeet * 0.3048;

    private Point2 SnapToGridPoint(Point2 point)
    {
        if (!SnapToGrid || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return point;
        double step = GridStepMetres;
        return new Point2(
            Math.Round(point.X / step, MidpointRounding.AwayFromZero) * step,
            Math.Round(point.Y / step, MidpointRounding.AwayFromZero) * step);
    }

    private static bool IsSamePoint(Point2 a, Point2 b) => a.DistanceTo(b) < 1e-7;
    private Point2 ToWorld(Point point) =>
        new((point.X - _origin.X) / _pixelsPerMetre,
            (point.Y - _origin.Y) / _pixelsPerMetre);

    private Point ToScreen(Point2 point) =>
        new(point.X * _pixelsPerMetre + _origin.X,
            point.Y * _pixelsPerMetre + _origin.Y);

    private void Say(string message) => Hint?.Invoke(message);
}
