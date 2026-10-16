using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildingAdventure.Core.Buildings;
using BuildingAdventure.Core.Geometry;
using BuildingAdventure.Desktop.Controls;
using Microsoft.Win32;

namespace BuildingAdventure.Desktop;

public partial class MainWindow : Window
{
    private string? _filePath;
    private string _lastSavedJson = "";
    private bool _loading;
    private string _hint = "Select Draw Perimeter, then click corners to begin.";
    private BuildingDocument _building = new();
    private int _activeFloorIndex;
    private readonly BuildingEditHistory _history = new();
    private string? _nameBeforeEdit;

    public MainWindow()
    {
        InitializeComponent();
        EditorSurface.UsesGlobalHistory = true;
        EditorSurface.HighlightUnassigned = true;
        EditorSurface.WindowWidthProvider = () =>
            TryReadMetres(WindowWidthBox.Text, out double width) &&
            double.IsFinite(width) ? width : double.NaN;
        EditorSurface.Changed += (_, _) => Refresh();
        EditorSurface.DocumentEditCommitted += previousFloor =>
        {
            if (_loading) return;
            var before = CurrentDocument();
            BuildingStoreys.SetFloor(before, _activeFloorIndex, previousFloor);
            _history.RecordBefore(before, _activeFloorIndex);
            Refresh();
        };
        EditorSurface.SelectionChanged += (_, _) =>
        {
            UpdateSelectedRoomControls();
            UpdateSelectedStairControls();
            UpdateSelectedWindowControls();
            UpdateSelectedDoorControls();
        };
        EditorSurface.Hint += message => { _hint = message; Refresh(); };
        EditorSurface.StairPlacementRequested += PlaceStairAt;
        EditorSurface.StairEditRequested += ApplyStairEdit;
        EditorSurface.StairTemplateProvider = GetStairTemplate;
        EditorSurface.StairValidator = ValidateStairCandidate;
        EditorSurface.AdditionalValidator = editedFloor =>
        {
            var candidate = BuildingStoreys.Copy(_building);
            BuildingStoreys.SetFloor(candidate, _activeFloorIndex, editedFloor);
            bool valid = BuildingStoreys.Validate(candidate, out var reason);
            return (valid, reason);
        };
        EditorSurface.SetTool(EditorTool.DrawPerimeter);
        RebuildFloorPicker();
        EditorSurface.SetStairOverlay(_building.Staircases, 0);
        UpdateAlignmentReferences();
        _lastSavedJson = CurrentJson();
        Loaded += (_, _) => { EditorSurface.Focus(); Refresh(); };
        Closing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
        Refresh();
    }

    private BuildingDocument CurrentDocument()
    {
        var result = BuildingStoreys.Copy(_building);
        result.Name = NameBox.Text;
        BuildingStoreys.SetFloor(result, _activeFloorIndex, new BuildingDocument
        {
            Perimeter = EditorSurface.GetOutline().ToList(),
            Rooms = EditorSurface.GetRooms().ToList(),
            Doors = EditorSurface.GetDoors().ToList(),
            Windows = EditorSurface.GetWindows().ToList(),
            WallThickness = EditorSurface.WallThickness
        });
        return result;
    }

    private string CurrentJson() => BuildingDocumentJson.Serialize(CurrentDocument());

    private bool IsDirty()
    {
        try { return CurrentJson() != _lastSavedJson; }
        catch (InvalidDataException) { return true; }
    }

    private bool ConfirmDiscard() =>
        !IsDirty() ||
        MessageBox.Show(this, "Discard unsaved changes?", "BuildingAdventure",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void Refresh()
    {
        if (_loading || EditorSurface is null || CornersText is null) return;
        var outline = EditorSurface.GetOutline();
        CornersText.Text = $"{outline.Count} corners";
        AreaText.Text = outline.Count >= 3 ? $"Area: {PolygonGeometry.Area(outline):N2} m²" : "Area: —";
        double slabArea = outline.Count < 3 ? 0
            : PolygonGeometry.Area(outline) -
              BuildingStoreys.GetOpenings(_building.Staircases, _activeFloorIndex)
                  .Sum(o => PolygonGeometry.Area(o.Outline));
        SlabAreaText.Text = outline.Count < 3 ? "Slab area: —"
            : $"Slab area excluding voids: {slabArea:N2} m²";
        LengthText.Text = outline.Count >= 3 ? $"Exterior walls: {PolygonGeometry.Perimeter(outline):N2} m" : "Exterior walls: —";
        RoomsText.Text = $"{EditorSurface.GetRooms().Count} rooms, " +
            $"{EditorSurface.GetDoors().Count} doors, {EditorSurface.GetWindows().Count} windows";
        UnassignedAreaText.Text = outline.Count >= 3
            ? $"Unassigned usable floor: {EditorSurface.UnassignedAreaMetres:N2} m²"
            : "Unassigned usable floor: —";
        DeleteRoomButton.IsEnabled = EditorSurface.SelectedRoom is not null;
        DeleteDoorButton.IsEnabled = EditorSurface.HasSelectedDoor;
        ApplyDoorButton.IsEnabled = EditorSurface.HasSelectedDoor;
        DeleteWindowButton.IsEnabled = EditorSurface.HasSelectedWindow;
        ApplyWindowButton.IsEnabled = EditorSurface.HasSelectedWindow;
        DeleteStairsButton.IsEnabled = EditorSurface.SelectedStaircaseId.HasValue;
        ApplyStairsButton.IsEnabled = EditorSurface.SelectedStaircaseId.HasValue;
        StairsText.Text = $"{_building.Staircases.Count} stair flight(s)";
        OpeningText.Text = $"{BuildingStoreys.GetOpenings(_building.Staircases, _activeFloorIndex).Count} opening(s) in this slab";
        AddFloorButton.IsEnabled = BuildingStoreys.FloorCount(_building) < 4;
        RemoveFloorButton.IsEnabled = BuildingStoreys.FloorCount(_building) > 1;
        UndoButton.IsEnabled = UndoMenu.IsEnabled =
            EditorSurface.HasPendingStep || _history.CanUndo;
        RedoButton.IsEnabled = RedoMenu.IsEnabled =
            !EditorSurface.HasPendingStep && _history.CanRedo;
        FinishButton.IsEnabled = EditorSurface.IsDrawing;
        SelectButton.FontWeight = EditorSurface.Tool == EditorTool.Select ? FontWeights.Bold : FontWeights.Normal;
        DrawButton.FontWeight = EditorSurface.Tool == EditorTool.DrawPerimeter ? FontWeights.Bold : FontWeights.Normal;
        SplitButton.FontWeight = EditorSurface.Tool == EditorTool.SplitPerimeter ? FontWeights.Bold : FontWeights.Normal;
        RemoveCornerButton.FontWeight = EditorSurface.Tool == EditorTool.RemoveCorner ? FontWeights.Bold : FontWeights.Normal;
        RoomButton.FontWeight = EditorSurface.Tool == EditorTool.DrawRoom ? FontWeights.Bold : FontWeights.Normal;
        SplitRoomButton.FontWeight = EditorSurface.Tool == EditorTool.SplitRoom ? FontWeights.Bold : FontWeights.Normal;
        MergeRoomsButton.FontWeight = EditorSurface.Tool == EditorTool.MergeRooms ? FontWeights.Bold : FontWeights.Normal;
        MoveWallButton.FontWeight = EditorSurface.Tool == EditorTool.MoveWall ? FontWeights.Bold : FontWeights.Normal;
        DoorButton.FontWeight = EditorSurface.Tool == EditorTool.AddDoor ? FontWeights.Bold : FontWeights.Normal;
        WindowButton.FontWeight = EditorSurface.Tool == EditorTool.AddWindow ? FontWeights.Bold : FontWeights.Normal;
        PlaceStairsButton.FontWeight = EditorSurface.Tool == EditorTool.PlaceStairs ? FontWeights.Bold : FontWeights.Normal;
        PanButton.FontWeight = EditorSurface.Tool == EditorTool.Pan ? FontWeights.Bold : FontWeights.Normal;
        HintText.Text = _hint;
        ZoomText.Text = $"{EditorSurface.PixelsPerMetre:0.#} px/m";
        string fileLabel = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
        Title = $"{NameBox.Text} — {fileLabel}{(IsDirty() ? " *" : "")} — BuildingAdventure";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _loading = true;
        _building = new BuildingDocument();
        _activeFloorIndex = 0;
        _history.Clear();
        _nameBeforeEdit = null;
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, 0));
        LoadWallThicknessControls();
        EditorSurface.SetStairOverlay(_building.Staircases, 0);
        RebuildFloorPicker();
        UpdateAlignmentReferences();
        NameBox.Text = "Untitled Building";
        _filePath = null;
        _loading = false;
        EditorSurface.SetTool(EditorTool.DrawPerimeter);
        _lastSavedJson = CurrentJson();
        _hint = "New building. Click corners to draw the perimeter.";
        Refresh();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Filter = "BuildingAdventure JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open a building"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = BuildingDocumentJson.Deserialize(File.ReadAllText(dialog.FileName));
            _loading = true;
            _building = BuildingStoreys.Copy(document);
            _activeFloorIndex = 0;
            _history.Clear();
            _nameBeforeEdit = null;
            EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, 0));
            LoadWallThicknessControls();
            EditorSurface.SetStairOverlay(_building.Staircases, 0);
            RebuildFloorPicker();
            UpdateAlignmentReferences();
            NameBox.Text = document.Name;
            _filePath = dialog.FileName;
            _loading = false;
            _lastSavedJson = CurrentJson();
            EditorSurface.SetTool(EditorTool.Select);
            EditorSurface.FitToView();
            _hint = "Building opened.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidDataException)
        {
            _loading = false;
            MessageBox.Show(this, ex.Message, "Cannot open building", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveFile(saveAs: false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveFile(saveAs: true);

    private void SaveFile(bool saveAs)
    {
        string? target = saveAs ? null : _filePath;
        if (target is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "BuildingAdventure JSON (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = _filePath is null ? "building.json" : Path.GetFileName(_filePath),
                Title = "Save building"
            };
            if (dialog.ShowDialog(this) != true) return;
            target = dialog.FileName;
        }

        try
        {
            var json = CurrentJson();
            File.WriteAllText(target, json);
            _filePath = target;
            _lastSavedJson = json;
            _hint = $"Saved {Path.GetFileName(target)}.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Cannot save building", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateSelectedRoomControls()
    {
        if (RoomNameBox is null || RoomTypeBox is null || EditorSurface is null) return;
        var room = EditorSurface.SelectedRoom;
        RoomNameBox.IsEnabled = RoomTypeBox.IsEnabled = ApplyRoomButton.IsEnabled = room is not null;
        RoomNameBox.Text = room?.Name ?? "";
        if (room is not null)
        {
            var item = RoomTypeBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Content?.ToString(), room.Type, StringComparison.Ordinal));
            RoomTypeBox.SelectedItem = item ?? RoomTypeBox.Items[0];
        }
        else RoomTypeBox.SelectedIndex = -1;
        Refresh();
    }

    private void ApplyRoom_Click(object sender, RoutedEventArgs e)
    {
        if (RoomTypeBox.SelectedItem is ComboBoxItem item)
            EditorSurface.SetSelectedRoomProperties(RoomNameBox.Text, item.Content?.ToString() ?? "Other");
    }

    private void DeleteRoom_Click(object sender, RoutedEventArgs e) => EditorSurface.DeleteSelectedRoom();
    private void DeleteDoor_Click(object sender, RoutedEventArgs e) => EditorSurface.DeleteSelectedDoor();

    private void UpdateSelectedDoorControls()
    {
        if (EditorSurface is null || DoorWidthBox is null ||
            ApplyDoorButton is null || DeleteDoorButton is null)
            return;
        var door = EditorSurface.SelectedDoor;
        ApplyDoorButton.IsEnabled = DeleteDoorButton.IsEnabled = door is not null;
        if (door is not null)
            DoorWidthBox.Text = door.Width.ToString(
                "R", System.Globalization.CultureInfo.CurrentCulture);
    }

    private void ApplyDoor_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadMetres(DoorWidthBox.Text, out double width) ||
            !double.IsFinite(width))
        {
            _hint = "Enter a valid numeric door width.";
            Refresh();
            return;
        }
        EditorSurface.SetSelectedDoorWidth(width);
        UpdateSelectedDoorControls();
    }
    private void DeleteWindow_Click(object sender, RoutedEventArgs e) =>
        EditorSurface.DeleteSelectedWindow();

    private void WindowTool_Click(object sender, RoutedEventArgs e) =>
        EditorSurface.SetTool(EditorTool.AddWindow);

    private void UpdateSelectedWindowControls()
    {
        if (EditorSurface is null || WindowWidthBox is null ||
            ApplyWindowButton is null || DeleteWindowButton is null) return;
        var selected = EditorSurface.SelectedWindow;
        ApplyWindowButton.IsEnabled = DeleteWindowButton.IsEnabled = selected is not null;
        if (selected is not null)
            WindowWidthBox.Text = selected.Width.ToString(
                "R", System.Globalization.CultureInfo.CurrentCulture);
    }

    private void ApplyWindow_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadMetres(WindowWidthBox.Text, out double width) ||
            !double.IsFinite(width))
        {
            _hint = "Enter a valid numeric window width.";
            Refresh();
            return;
        }
        EditorSurface.SetSelectedWindowWidth(width);
        UpdateSelectedWindowControls();
    }

    private void UnassignedToggleChanged(object sender, RoutedEventArgs e)
    {
        if (EditorSurface is null || HighlightUnassignedCheckBox is null) return;
        EditorSurface.HighlightUnassigned = HighlightUnassignedCheckBox.IsChecked == true;
        EditorSurface.InvalidateVisual();
        Refresh();
    }

    private void RebuildFloorPicker()
    {
        if (FloorPicker is null) return;
        bool prior = _loading;
        _loading = true;
        FloorPicker.Items.Clear();
        for (int i = 0; i < BuildingStoreys.FloorCount(_building); i++)
            FloorPicker.Items.Add(i == 0 ? "Ground Floor" : _building.UpperFloors[i - 1].Name);
        FloorPicker.SelectedIndex = _activeFloorIndex;
        _loading = prior;
    }

    private void ShowFloor(int index)
    {
        if (index < 0 || index >= BuildingStoreys.FloorCount(_building)) return;
        _loading = true;
        _activeFloorIndex = index;
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, index));
        LoadWallThicknessControls();
        EditorSurface.SetStairOverlay(_building.Staircases, index);
        UpdateAlignmentReferences();
        EditorSurface.SetTool(EditorTool.Select);
        FloorPicker.SelectedIndex = index;
        _loading = false;
        EditorSurface.FitToView();
        _hint = $"Editing {BuildingStoreys.FloorLabel(index)}.";
        Refresh();
    }

    private void LoadWallThicknessControls()
    {
        if (ExteriorWallBox is null || InteriorWallBox is null || EditorSurface is null)
            return;
        var size = EditorSurface.WallThickness;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        ExteriorWallBox.Text = size.ExteriorMetres.ToString("R", culture);
        InteriorWallBox.Text = size.InteriorMetres.ToString("R", culture);
    }

    private void ApplyWallThickness_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadMetres(ExteriorWallBox.Text, out double exterior) ||
            !TryReadMetres(InteriorWallBox.Text, out double interior))
        {
            _hint = "Enter valid numeric wall thickness values in metres.";
            LoadWallThicknessControls();
            Refresh();
            return;
        }
        var desired = new WallThicknessSettings
        {
            ExteriorMetres = exterior, InteriorMetres = interior
        };
        if (!desired.Validate(out var reason))
        {
            _hint = reason;
            LoadWallThicknessControls();
            Refresh();
            return;
        }
        var previous = EditorSurface.WallThickness;
        if (Math.Abs(previous.ExteriorMetres - exterior) < 1e-9 &&
            Math.Abs(previous.InteriorMetres - interior) < 1e-9)
            return;

        var before = CurrentDocument();
        var updated = BuildingStoreys.Copy(before);
        BuildingStoreys.SetFloor(updated, _activeFloorIndex,
            new BuildingDocument
            {
                Perimeter = EditorSurface.GetOutline().ToList(),
                Rooms = EditorSurface.GetRooms().ToList(),
                Doors = EditorSurface.GetDoors().ToList(),
                Windows = EditorSurface.GetWindows().ToList(),
                WallThickness = desired
            });
        if (!BuildingStoreys.Validate(updated, out reason))
        {
            _hint = reason;
            LoadWallThicknessControls();
            Refresh();
            return;
        }

        _history.RecordBefore(before, _activeFloorIndex);
        _building = updated;
        EditorSurface.SetWallThickness(desired);
        _hint = "Wall thickness updated on this floor. Door and window openings preserved.";
        Refresh();
    }

    private void UpdateAlignmentReferences()
    {
        if (EditorSurface is null || ShowBelowCheckBox is null ||
            ShowAboveCheckBox is null || AlignmentOpacitySlider is null)
            return;

        var references = BuildingStoreys.AdjacentFloors(
            _building, _activeFloorIndex,
            ShowBelowCheckBox.IsChecked == true,
            ShowAboveCheckBox.IsChecked == true);
        EditorSurface.SetFloorReferences(references, AlignmentOpacitySlider.Value);
    }

    private void AlignmentChanged(object sender, RoutedEventArgs e) =>
        UpdateAlignmentReferences();

    private void AlignmentOpacityChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e) =>
        UpdateAlignmentReferences();

    private void FloorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EditorSurface is null || FloorPicker is null) return;
        int next = FloorPicker.SelectedIndex;
        if (next < 0 || next == _activeFloorIndex) return;
        _building = CurrentDocument();
        ShowFloor(next);
    }

    private void AddFloor_Click(object sender, RoutedEventArgs e)
    {
        if (!BuildingStoreys.TryAddFloor(CurrentDocument(), out var added, out var reason))
        {
            MessageBox.Show(this, reason, "Cannot add floor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _history.RecordBefore(CurrentDocument(), _activeFloorIndex);
        _building = added;
        _activeFloorIndex = BuildingStoreys.FloorCount(_building) - 1;
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        _hint = "Upper floor created. Exterior copied; rooms and doors are independent.";
        Refresh();
    }

    private void RemoveFloor_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Remove the highest floor and any staircase leading to it?",
                "Remove floor", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

        if (!BuildingStoreys.TryRemoveTopFloor(CurrentDocument(), out var updated, out var reason))
        {
            MessageBox.Show(this, reason, "Cannot remove floor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _history.RecordBefore(CurrentDocument(), _activeFloorIndex);
        _building = updated;
        _activeFloorIndex = Math.Min(_activeFloorIndex,
            BuildingStoreys.FloorCount(_building) - 1);
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        _hint = "Highest floor removed.";
        Refresh();
    }

    private void PlaceStairsTool_Click(object sender, RoutedEventArgs e) =>
        EditorSurface.SetTool(EditorTool.PlaceStairs);

    private static bool TryReadMetres(string input, out double value) =>
        double.TryParse(input, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture, out value) ||
        double.TryParse(input, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private StaircaseDefinition? GetStairTemplate()
    {
        if (StairLengthBox is null || StairWidthBox is null ||
            OpeningWidthBox is null || OpeningLengthBox is null ||
            StairRotationBox is null || EditorSurface is null ||
            _activeFloorIndex >= BuildingStoreys.FloorCount(_building) - 1 ||
            !TryReadMetres(StairLengthBox.Text, out double length) ||
            !TryReadMetres(StairWidthBox.Text, out double width) ||
            !TryReadMetres(OpeningWidthBox.Text, out double extraWidth) ||
            !TryReadMetres(OpeningLengthBox.Text, out double extraLength) ||
            !TryReadMetres(StairRotationBox.Text, out double angle) ||
            !double.IsFinite(length) || !double.IsFinite(width) ||
            !double.IsFinite(extraWidth) || !double.IsFinite(extraLength) ||
            !double.IsFinite(angle))
            return null;

        return new StaircaseDefinition
        {
            LowerFloorIndex = _activeFloorIndex,
            Length = length,
            Width = width,
            OpeningExtraWidth = extraWidth,
            OpeningExtraLength = extraLength,
            RotationDegrees = angle
        };
    }

    private (bool Valid, string Reason) ValidateStairCandidate(StaircaseDefinition stair)
    {
        var source = CurrentDocument();
        bool valid;
        string reason;
        if (source.Staircases.Any(s => s.Id == stair.Id))
            valid = BuildingStoreys.TryUpdateStaircase(source, stair, out _, out reason);
        else
            valid = BuildingStoreys.TryAddStaircase(source, stair, out _, out reason);
        return (valid, reason);
    }

    private void UpdateSelectedStairControls()
    {
        if (StairValidationText is not null)
            StairValidationText.Text = "";
        if (EditorSurface is null || StairLengthBox is null ||
            StairWidthBox is null || OpeningWidthBox is null ||
            OpeningLengthBox is null || StairRotationBox is null ||
            StairXBox is null || StairYBox is null ||
            ApplyStairsButton is null)
            return;

        var stair = EditorSurface.SelectedStaircase;
        bool selected = stair is not null;
        StairXBox.IsEnabled = StairYBox.IsEnabled =
            ApplyStairsButton.IsEnabled = selected;
        if (stair is null)
        {
            StairXBox.Text = "";
            StairYBox.Text = "";
            return;
        }

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        StairLengthBox.Text = stair.Length.ToString("R", culture);
        StairWidthBox.Text = stair.Width.ToString("R", culture);
        OpeningWidthBox.Text = stair.OpeningExtraWidth.ToString("R", culture);
        OpeningLengthBox.Text = stair.OpeningExtraLength.ToString("R", culture);
        StairRotationBox.Text = stair.RotationDegrees.ToString("R", culture);
        StairXBox.Text = stair.Centre.X.ToString("R", culture);
        StairYBox.Text = stair.Centre.Y.ToString("R", culture);
    }

    /// <summary>
    /// A rejected edit has not changed the underlying staircase. Restore the
    /// controls from that authoritative model so the displayed angle, width,
    /// length and coordinates all agree with the drawing.
    /// </summary>
    private void RejectStairEdit(string reason)
    {
        UpdateSelectedStairControls();
        StairValidationText.Text = "Change rejected: " + reason +
            " Previous values restored.";
        _hint = "Staircase unchanged: " + reason;
        Refresh();
    }

    private void StairSettingsChanged(object sender, TextChangedEventArgs e)
    {
        if (EditorSurface is not null)
            EditorSurface.RefreshStairPreview();
    }

    private void PlaceStairAt(Point2 centre)
    {
        var stair = GetStairTemplate();
        if (stair is null)
        {
            _hint = "Select a lower floor and enter valid stair length, width and rotation.";
            Refresh();
            return;
        }
        stair.Centre = centre;
        var before = CurrentDocument();
        if (!BuildingStoreys.TryAddStaircase(before, stair,
                out var updated, out var reason))
        {
            _hint = $"Cannot place stairs: {reason}";
            Refresh();
            return;
        }

        _history.RecordBefore(before, _activeFloorIndex);
        _building = updated;
        EditorSurface.SetStairOverlay(_building.Staircases, _activeFloorIndex, stair.Id);
        EditorSurface.SetTool(EditorTool.Select);
        _hint = "Staircase placed. Drag it to move, or edit its dimensions and rotation on the right.";
        Refresh();
    }

    private void ApplyStairEdit(StaircaseDefinition proposed)
    {
        var before = CurrentDocument();
        if (!BuildingStoreys.TryUpdateStaircase(before, proposed,
                out var updated, out var reason))
        {
            RejectStairEdit(reason);
            return;
        }

        _history.RecordBefore(before, _activeFloorIndex);
        _building = updated;
        EditorSurface.SetStairOverlay(_building.Staircases, _activeFloorIndex, proposed.Id);
        StairValidationText.Text = "";
        _hint = "Staircase updated on both floors.";
        Refresh();
    }

    private void ApplyStairs_Click(object sender, RoutedEventArgs e)
    {
        var stair = EditorSurface.SelectedStaircase;
        if (stair is null) return;

        if (!TryReadMetres(StairLengthBox.Text, out double length) ||
            !TryReadMetres(StairWidthBox.Text, out double width) ||
            !TryReadMetres(OpeningWidthBox.Text, out double extraWidth) ||
            !TryReadMetres(OpeningLengthBox.Text, out double extraLength) ||
            !TryReadMetres(StairRotationBox.Text, out double rotation) ||
            !TryReadMetres(StairXBox.Text, out double x) ||
            !TryReadMetres(StairYBox.Text, out double y) ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            RejectStairEdit("Enter valid numeric staircase size, rotation, X and Y values.");
            return;
        }

        stair.Length = length;
        stair.Width = width;
        stair.OpeningExtraWidth = extraWidth;
        stair.OpeningExtraLength = extraLength;
        stair.RotationDegrees = rotation;
        stair.Centre = new Point2(x, y);
        ApplyStairEdit(stair);
    }

    private void RotateStairLeft_Click(object sender, RoutedEventArgs e) =>
        RotateStairs(-15);

    private void RotateStairRight_Click(object sender, RoutedEventArgs e) =>
        RotateStairs(15);

    private void RotateStairs(double degrees)
    {
        if (!TryReadMetres(StairRotationBox.Text, out double current) ||
            !double.IsFinite(current))
        {
            if (EditorSurface.SelectedStaircaseId.HasValue)
                RejectStairEdit("Enter a valid rotation angle.");
            else
            {
                _hint = "Enter a valid angle before rotating the staircase.";
                Refresh();
            }
            return;
        }

        double rotation = ((current + degrees) % 360 + 360) % 360;
        StairRotationBox.Text = rotation.ToString(
            "0.###", System.Globalization.CultureInfo.CurrentCulture);
        if (EditorSurface.SelectedStaircaseId.HasValue)
            ApplyStairs_Click(this, new RoutedEventArgs());
        else
            EditorSurface.RefreshStairPreview();
    }

    private void DeleteStairs_Click(object sender, RoutedEventArgs e)
    {
        Guid? id = EditorSurface.SelectedStaircaseId;
        if (!id.HasValue) return;
        var before = CurrentDocument();
        _history.RecordBefore(before, _activeFloorIndex);
        _building = before;
        _building.Staircases.RemoveAll(s => s.Id == id.Value);
        EditorSurface.SetStairOverlay(_building.Staircases, _activeFloorIndex);
        _hint = "Staircase removed from both connected floors.";
        Refresh();
    }

    private void RestoreHistory(BuildingHistoryState state, string message)
    {
        _loading = true;
        _building = BuildingStoreys.Copy(state.Building);
        _activeFloorIndex = state.FloorIndex;
        _nameBeforeEdit = null;
        NameBox.Text = _building.Name;
        RebuildFloorPicker();
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, _activeFloorIndex));
        LoadWallThicknessControls();
        EditorSurface.SetStairOverlay(_building.Staircases, _activeFloorIndex);
        UpdateAlignmentReferences();
        EditorSurface.SetTool(EditorTool.Select);
        _loading = false;
        _hint = $"{message} — {BuildingStoreys.FloorLabel(_activeFloorIndex)}.";
        Refresh();
    }

    private void UndoDocument()
    {
        if (EditorSurface.HasPendingStep)
        {
            EditorSurface.Undo();
            Refresh();
            return;
        }

        if (_history.TryUndo(CurrentDocument(), _activeFloorIndex, out var target))
            RestoreHistory(target, "Undo");
    }

    private void RedoDocument()
    {
        if (EditorSurface.HasPendingStep) return;
        if (_history.TryRedo(CurrentDocument(), _activeFloorIndex, out var target))
            RestoreHistory(target, "Redo");
    }

    private void NameBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_loading)
            _nameBeforeEdit = NameBox.Text;
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        string? previousName = _nameBeforeEdit;
        _nameBeforeEdit = null;
        if (_loading || previousName is null || previousName == NameBox.Text)
            return;

        var before = CurrentDocument();
        before.Name = previousName;
        _history.RecordBefore(before, _activeFloorIndex);
        Refresh();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoDocument();
    private void Redo_Click(object sender, RoutedEventArgs e) => RedoDocument();
    private void Delete_Click(object sender, RoutedEventArgs e) => EditorSurface.DeleteSelectedVertex();
    private void Cancel_Click(object sender, RoutedEventArgs e) => EditorSurface.CancelDrawing();
    private void Finish_Click(object sender, RoutedEventArgs e) => EditorSurface.FinishDrawing();
    private void SelectTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.Select);
    private void DrawTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.DrawPerimeter);
    private void SplitPerimeter_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.SplitPerimeter);
    private void RemoveCornerTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.RemoveCorner);
    private void RoomTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.DrawRoom);
    private void SplitRoomTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.SplitRoom);
    private void MergeRoomsTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.MergeRooms);
    private void MoveWallTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.MoveWall);
    private void DoorTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.AddDoor);
    private void PanTool_Click(object sender, RoutedEventArgs e) => EditorSurface.SetTool(EditorTool.Pan);
    private void Fit_Click(object sender, RoutedEventArgs e) => EditorSurface.FitToView();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => EditorSurface.ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => EditorSurface.ZoomBy(0.8);

    private void SnapChanged(object sender, RoutedEventArgs e)
    {
        if (EditorSurface is not null)
            EditorSurface.SnapToGrid = SnapCheckBox.IsChecked == true;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // Preserve native text editing shortcuts when renaming the building.
        if (Keyboard.FocusedElement is TextBox &&
            ((ctrl && (e.Key == Key.Z || e.Key == Key.Y)) || e.Key == Key.Delete))
            return;

        if (ctrl && e.Key == Key.N) New_Click(this, new RoutedEventArgs());
        else if (ctrl && e.Key == Key.O) Open_Click(this, new RoutedEventArgs());
        else if (ctrl && e.Key == Key.S)
        {
            if (shift) SaveAs_Click(this, new RoutedEventArgs());
            else Save_Click(this, new RoutedEventArgs());
        }
        else if (ctrl && e.Key == Key.Z) UndoDocument();
        else if (ctrl && e.Key == Key.Y) RedoDocument();
        else if (ctrl && e.Key == Key.D0) EditorSurface.FitToView();
        else if (e.Key == Key.Escape) EditorSurface.CancelDrawing();
        else if (e.Key == Key.Delete && !NameBox.IsKeyboardFocusWithin && !RoomNameBox.IsKeyboardFocusWithin)
        {
            if (EditorSurface.SelectedStaircaseId.HasValue)
                DeleteStairs_Click(this, new RoutedEventArgs());
            else if (EditorSurface.HasSelectedWindow) EditorSurface.DeleteSelectedWindow();
            else if (EditorSurface.HasSelectedDoor) EditorSurface.DeleteSelectedDoor();
            else EditorSurface.DeleteSelectedVertex();
        }
        else return;
        e.Handled = true;
    }
}
