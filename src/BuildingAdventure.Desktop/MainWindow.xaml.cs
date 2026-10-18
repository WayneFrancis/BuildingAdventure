using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    private EditorLayoutSettings? _savedLayout;
    private WindowState _previousWindowState = WindowState.Normal;
    private BuildingSuggestion? _pendingSuggestion;
    private string _suggestionSource = "";
    private bool _pendingWholeBuilding;
    private readonly List<SavedSuggestionEntry> _suggestionMemory = SuggestionMemory.Load();
    private bool _loadingDoorVisibility;
    private bool _loadingStairPicker;

    public MainWindow()
    {
        InitializeComponent();
        RestoreSavedLayout();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                _previousWindowState = WindowState;
        };
        EditorSurface.UsesGlobalHistory = true;
        EditorSurface.SuggestedRoomSelectionChanged += () =>
        {
            if (AcceptSuggestedRoomButton is not null)
                AcceptSuggestedRoomButton.IsEnabled =
                    _pendingSuggestion is not null &&
                !_pendingWholeBuilding &&
                EditorSurface.SelectedSuggestedRoomId.HasValue;
        };
        UpdateMemoryList();
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
            ClearPendingSuggestion();
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
        EditorSurface.SetChimneyOverlay(_building.Chimneys);
        UpdateAlignmentReferences();
        GridScaleBox.SelectedIndex = _building.GridFeet == 10 ? 2 : _building.GridFeet == 5 ? 1 : 0;
        _lastSavedJson = CurrentJson();
        Loaded += (_, _) =>
        {
            EditorSurface.Focus();
            Refresh();
            if (_savedLayout is not null)
                Dispatcher.BeginInvoke(new Action(() =>
                    SettingsScrollViewer.ScrollToVerticalOffset(
                        Math.Max(0, _savedLayout.SidebarScrollOffset))),
                    DispatcherPriority.Loaded);
        };
        Closing += (_, e) =>
        {
            if (!ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }
            SaveEditorLayout();
        };
        Refresh();
    }

    private (string Name, Expander Control)[] LayoutSections() =>
    [
        ("Building", BuildingSection),
        ("Floors", FloorsSection),
        ("Generator", GeneratorSection),
        ("Staircases", StaircasesSection),
        ("WallConstruction", WallConstructionSection),
        ("Perimeter", PerimeterSection),
        ("Rooms", RoomsSection),
        ("Windows", WindowsSection),
        ("Doors", DoorsSection),
        ("Help", HelpSection)
    ];

    private void RestoreSavedLayout()
    {
        var saved = EditorLayoutSettings.TryLoad();
        if (saved is null) return;
        _savedLayout = saved;

        _loading = true;
        try
        {
            if (double.IsFinite(saved.Width) && double.IsFinite(saved.Height) &&
                double.IsFinite(saved.Left) && double.IsFinite(saved.Top) &&
                saved.Width >= MinWidth && saved.Height >= MinHeight &&
                saved.Width <= 10000 && saved.Height <= 10000)
            {
                var available = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                var lastPosition = new Rect(
                    saved.Left, saved.Top, saved.Width, saved.Height);
                if (lastPosition.IntersectsWith(available))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Width = saved.Width;
                    Height = saved.Height;
                    Left = Math.Clamp(saved.Left,
                        available.Left - saved.Width + 160,
                        available.Right - 160);
                    Top = Math.Clamp(saved.Top,
                        available.Top - saved.Height + 90,
                        available.Bottom - 90);
                }
            }

            foreach (var (name, control) in LayoutSections())
            {
                if (saved.ExpandedSections is not null &&
                    saved.ExpandedSections.TryGetValue(name, out bool expanded))
                    control.IsExpanded = expanded;
            }

            SnapCheckBox.IsChecked = saved.SnapToGrid;
            HighlightUnassignedCheckBox.IsChecked = saved.HighlightUnassigned;
            ShowBelowCheckBox.IsChecked = saved.ShowBelow;
            ShowAboveCheckBox.IsChecked = saved.ShowAbove;
            AllFloorSilhouettesCheckBox.IsChecked = saved.AllFloorSilhouettes;
            GenerateRoomsCheckBox.IsChecked = saved.GenerateRooms;
            GenerateCorridorsCheckBox.IsChecked = saved.GenerateCorridors;
            GenerateDoorsCheckBox.IsChecked = saved.GenerateDoors;
            GenerateWindowsCheckBox.IsChecked = saved.GenerateWindows;
            GenerateStairsCheckBox.IsChecked = saved.GenerateStairs;
            GenerateFireplacesCheckBox.IsChecked = saved.GenerateFireplaces;
            GeneratorPurposeBox.SelectedIndex =
                Math.Clamp(saved.GeneratorPurposeIndex, 0, 4);
            MapModeBox.SelectedIndex = Math.Clamp(saved.MapModeIndex, 0, 2);
            if (double.IsFinite(saved.ReferenceOpacity))
                AlignmentOpacitySlider.Value =
                    Math.Clamp(saved.ReferenceOpacity,
                        AlignmentOpacitySlider.Minimum,
                        AlignmentOpacitySlider.Maximum);

            _previousWindowState = saved.Maximized
                ? WindowState.Maximized : WindowState.Normal;
            if (saved.Maximized)
                WindowState = WindowState.Maximized;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveEditorLayout()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        var settings = new EditorLayoutSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized ||
                (WindowState == WindowState.Minimized &&
                 _previousWindowState == WindowState.Maximized),
            ExpandedSections = LayoutSections().ToDictionary(
                section => section.Name, section => section.Control.IsExpanded),
            SidebarScrollOffset = SettingsScrollViewer.VerticalOffset,
            SnapToGrid = SnapCheckBox.IsChecked == true,
            HighlightUnassigned = HighlightUnassignedCheckBox.IsChecked == true,
            ShowBelow = ShowBelowCheckBox.IsChecked == true,
            ShowAbove = ShowAboveCheckBox.IsChecked == true,
            AllFloorSilhouettes = AllFloorSilhouettesCheckBox.IsChecked == true,
            GenerateRooms = GenerateRoomsCheckBox.IsChecked == true,
            GenerateCorridors = GenerateCorridorsCheckBox.IsChecked == true,
            GenerateDoors = GenerateDoorsCheckBox.IsChecked == true,
            GenerateWindows = GenerateWindowsCheckBox.IsChecked == true,
            GenerateStairs = GenerateStairsCheckBox.IsChecked == true,
            GenerateFireplaces = GenerateFireplacesCheckBox.IsChecked == true,
            GeneratorPurposeIndex = Math.Max(0, GeneratorPurposeBox.SelectedIndex),
            MapModeIndex = Math.Max(0, MapModeBox.SelectedIndex),
            ReferenceOpacity = AlignmentOpacitySlider.Value
        };

        // Preferences are best-effort: the user can always exit even when
        // AppData is read-only or the preferences file is invalid.
        EditorLayoutSettings.TrySave(settings);
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
        bool imperial = _building.GridFeet is 5 or 10;
        double areaFactor = imperial ? 1 / (0.3048 * 0.3048) : 1;
        double lengthFactor = imperial ? 1 / 0.3048 : 1;
        string areaUnit = imperial ? "ft²" : "m²";
        string lengthUnit = imperial ? "ft" : "m";
        AreaText.Text = outline.Count >= 3
            ? $"Area: {PolygonGeometry.Area(outline) * areaFactor:N2} {areaUnit}"
            : "Area: —";
        double slabArea = outline.Count < 3 ? 0
            : PolygonGeometry.Area(outline) -
              BuildingStoreys.GetOpenings(_building.Staircases, _activeFloorIndex)
                  .Sum(o => PolygonGeometry.Area(o.Outline));
        SlabAreaText.Text = outline.Count < 3 ? "Slab area: —"
            : $"Slab area excluding voids: {slabArea * areaFactor:N2} {areaUnit}";
        LengthText.Text = outline.Count >= 3
            ? $"Exterior walls: {PolygonGeometry.Perimeter(outline) * lengthFactor:N2} {lengthUnit}"
            : "Exterior walls: —";
        RoomsText.Text = $"{EditorSurface.GetRooms().Count} rooms, " +
            $"{EditorSurface.GetDoors().Count} doors, {EditorSurface.GetWindows().Count} windows";
        UnassignedAreaText.Text = outline.Count >= 3
            ? $"Unassigned usable floor: {EditorSurface.UnassignedAreaMetres * areaFactor:N2} {areaUnit}"
            : "Unassigned usable floor: —";
        DeleteRoomButton.IsEnabled = EditorSurface.SelectedRoom is not null;
        DeleteDoorButton.IsEnabled = EditorSurface.HasSelectedDoor;
        ApplyDoorButton.IsEnabled = EditorSurface.HasSelectedDoor;
        CentreDoorButton.IsEnabled = EditorSurface.HasSelectedDoor;
        DeleteWindowButton.IsEnabled = EditorSurface.HasSelectedWindow;
        ApplyWindowButton.IsEnabled = EditorSurface.HasSelectedWindow;
        CentreWindowButton.IsEnabled = EditorSurface.HasSelectedWindow;
        DeleteStairsButton.IsEnabled = EditorSurface.SelectedStaircaseId.HasValue;
        ApplyStairsButton.IsEnabled = EditorSurface.SelectedStaircaseId.HasValue;
        StairsText.Text = $"{_building.Staircases.Count} stair flight(s)";
        OpeningText.Text = $"{BuildingStoreys.GetOpenings(_building.Staircases, _activeFloorIndex).Count} opening(s) in this slab";
        AddFloorButton.IsEnabled = BuildingStoreys.FloorCount(_building) < 4;
        RemoveFloorButton.IsEnabled = BuildingStoreys.FloorCount(_building) > 1;
        AddBasementButton.IsEnabled = BuildingStoreys.FloorCount(_building) < 4;
        InspectProposalButton.IsEnabled = _pendingSuggestion is not null;
        UpdateStairFlightChoices();
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
        AcceptSuggestionButton.IsEnabled = RememberSuggestionButton.IsEnabled =
            _pendingSuggestion is not null;
        AcceptSuggestedRoomButton.IsEnabled = _pendingSuggestion is not null &&
            !_pendingWholeBuilding && EditorSurface.SelectedSuggestedRoomId.HasValue;
        SuggestionInfoText.Text = _pendingSuggestion is null
            ? "No pending suggestion. Complete existing floors, or draft a new whole building."
            : _pendingWholeBuilding
                ? $"NEW BUILDING PREVIEW: {BuildingStoreys.FloorCount(_pendingSuggestion.Building)} " +
                  $"floor(s) generated. The editor still contains {BuildingStoreys.FloorCount(_building)} " +
                  "floor(s) until you choose Accept All. Use Inspect all proposed floors " +
                  "to see every generated level. " + _pendingSuggestion.Description
                : _pendingSuggestion.Description +
                  $" | Previewing {BuildingStoreys.FloorLabel(_activeFloorIndex)}. " +
                  "Use Page Up / Page Down or View next selected floor to inspect other existing levels.";
        if (WorkingFloorCountText is not null)
        {
            int count = BuildingStoreys.FloorCount(_building);
            WorkingFloorCountText.Text = $"WORKING BUILDING — {count} FLOOR(S)";
            EditableFloorHint.Text =
                $"Editing {BuildingStoreys.FloorLabel(_activeFloorIndex)} — Select / Edit to select rooms";
        }
        ZoomText.Text = $"{EditorSurface.PixelsPerMetre:0.#} px/m — " +
            (_building.GridFeet == 0 ? "1 m grid" : $"{_building.GridFeet} ft grid");
        string fileLabel = _filePath is null ? "Untitled" : Path.GetFileName(_filePath);
        Title = $"{NameBox.Text} — {fileLabel}{(IsDirty() ? " *" : "")} — BuildingAdventure";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _loading = true;
        _building = new BuildingDocument
        {
            GridFeet = GridScaleBox.SelectedIndex switch { 1 => 5, 2 => 10, _ => 0 }
        };
        _activeFloorIndex = 0;
        ClearPendingSuggestion();
        _history.Clear();
        _nameBeforeEdit = null;
        EditorSurface.SetGridFeet(_building.GridFeet);
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, 0));
        EditorSurface.SetChimneyOverlay(_building.Chimneys);
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
            GridScaleBox.SelectedIndex = _building.GridFeet == 10 ? 2 : _building.GridFeet == 5 ? 1 : 0;
            _activeFloorIndex = BuildingStoreys.HasFloor(_building, 0)
                ? 0 : BuildingStoreys.LowestFloorIndex(_building);
            ClearPendingSuggestion();
            _history.Clear();
            _nameBeforeEdit = null;
            EditorSurface.SetGridFeet(_building.GridFeet);
            EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, _activeFloorIndex));
            EditorSurface.SetChimneyOverlay(_building.Chimneys);
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
    private void CentreDoor_Click(object sender, RoutedEventArgs e) =>
        EditorSurface.CentreSelectedDoorOnWall();
    private void CentreWindow_Click(object sender, RoutedEventArgs e) =>
        EditorSurface.CentreSelectedWindowOnWall();

    private void DeleteDoor_Click(object sender, RoutedEventArgs e) => EditorSurface.DeleteSelectedDoor();

    private void UpdateSelectedDoorControls()
    {
        if (EditorSurface is null || DoorWidthBox is null ||
            ApplyDoorButton is null || DeleteDoorButton is null ||
            CentreDoorButton is null)
            return;
        var door = EditorSurface.SelectedDoor;
        ApplyDoorButton.IsEnabled = DeleteDoorButton.IsEnabled =
            CentreDoorButton.IsEnabled = door is not null;
        if (door is not null)
            DoorWidthBox.Text = door.Width.ToString(
                "R", System.Globalization.CultureInfo.CurrentCulture);
        _loadingDoorVisibility = true;
        try
        {
            SecretDoorCheckBox.IsEnabled = door is not null;
            RevealedDoorCheckBox.IsEnabled = door?.IsSecret == true;
            SecretDoorCheckBox.IsChecked = door?.IsSecret == true;
            RevealedDoorCheckBox.IsChecked = door?.IsRevealed == true;
        }
        finally
        {
            _loadingDoorVisibility = false;
        }
    }

    private void SecretDoorChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingDoorVisibility || EditorSurface is null ||
            SecretDoorCheckBox is null || RevealedDoorCheckBox is null)
            return;
        bool secret = SecretDoorCheckBox.IsChecked == true;
        bool revealed = secret && RevealedDoorCheckBox.IsChecked == true;
        EditorSurface.SetSelectedDoorVisibility(secret, revealed);
        RevealedDoorCheckBox.IsEnabled = secret && EditorSurface.HasSelectedDoor;
    }

    private void MapModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorSurface is null || MapModeBox is null) return;
        var mode = Math.Clamp(MapModeBox.SelectedIndex, 0, 2) switch
        {
            1 => MapVisibilityMode.GameMaster,
            2 => MapVisibilityMode.Player,
            _ => MapVisibilityMode.Editor
        };
        EditorSurface.SetDisplayMode(mode);
        _hint = mode == MapVisibilityMode.Player
            ? "Player preview: undiscovered secret doors appear as solid walls."
            : "Map view updated.";
        Refresh();
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
            ApplyWindowButton is null || DeleteWindowButton is null ||
            CentreWindowButton is null) return;
        var selected = EditorSurface.SelectedWindow;
        ApplyWindowButton.IsEnabled = DeleteWindowButton.IsEnabled =
            CentreWindowButton.IsEnabled = selected is not null;
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

    private void ClearPendingSuggestion()
    {
        _pendingSuggestion = null;
        _suggestionSource = "";
        _pendingWholeBuilding = false;
        EditorSurface?.SetSuggestionPreview(null);
        if (WholeBuildingPreviewPanel is not null) WholeBuildingPreviewPanel.Visibility = Visibility.Collapsed;
        if (WorkingFloorNavigationBar is not null) WorkingFloorNavigationBar.Visibility = Visibility.Visible;
        ProposalFloorPicker?.Items.Clear();
    }

    private void UpdateSuggestionPreview()
    {
        if (EditorSurface is null) return;
        EditorSurface.SetSuggestionPreview(
            _pendingSuggestion is not null &&
            BuildingStoreys.HasFloor(_pendingSuggestion.Building, _activeFloorIndex)
                ? BuildingStoreys.GetFloor(_pendingSuggestion.Building, _activeFloorIndex)
                : null,
            _pendingSuggestion?.Building.Staircases);
    }

    private GenerationOptions SelectedGeneratorOptions() => new()
    {
        Rooms = GenerateRoomsCheckBox.IsChecked == true,
        Corridors = GenerateCorridorsCheckBox.IsChecked == true,
        Doors = GenerateDoorsCheckBox.IsChecked == true,
        Windows = GenerateWindowsCheckBox.IsChecked == true,
        Stairs = GenerateStairsCheckBox.IsChecked == true,
        Fireplaces = GenerateFireplacesCheckBox.IsChecked == true,
        Purpose = Math.Clamp(GeneratorPurposeBox.SelectedIndex, 0, 4) switch
        {
            1 => BuildingUse.Inn,
            2 => BuildingUse.Shop,
            3 => BuildingUse.Dungeon,
            4 => BuildingUse.Mixed,
            _ => BuildingUse.Residence
        }
    };

    private int[] CheckedGeneratorFloors() =>
        GeneratorFloorChecks.Children.OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => (int)cb.Tag).OrderBy(i => i).ToArray();

    private void BeginSuggestion(BuildingSuggestion proposal, bool whole)
    {
        _pendingSuggestion = proposal.Copy();
        _suggestionSource = CurrentJson();
        _pendingWholeBuilding = whole;
        if (!whole)
        {
            WholeBuildingPreviewPanel.Visibility = Visibility.Collapsed;
            WorkingFloorNavigationBar.Visibility = Visibility.Visible;
        }
        if (whole)
        {
            UpdateSuggestionPreview();
            ShowWholeBuildingPreview();
        }
        else if (!proposal.FloorIndexes.Contains(_activeFloorIndex) &&
            proposal.FloorIndexes.Count > 0 &&
            BuildingStoreys.HasFloor(_building, proposal.FloorIndexes[0]))
        {
            _building = CurrentDocument();
            ShowFloor(proposal.FloorIndexes[0]);
        }
        else if (!whole)
            UpdateSuggestionPreview();
        _hint = $"Suggested changes on {proposal.FloorIndexes.Count} floor(s). " +
            "Use View next selected floor or Page Up/Down to inspect, then Accept.";
        Refresh();
    }

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        int[] floors = CheckedGeneratorFloors();
        var seed = Random.Shared.Next(1, int.MaxValue);
        if (!BuildingGenerator.TrySuggest(CurrentDocument(), floors, seed,
            SelectedGeneratorOptions(), out var proposal, out var reason))
        {
            _hint = reason;
            Refresh();
            return;
        }
        BeginSuggestion(proposal, whole: false);
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        var floors = CheckedGeneratorFloors();
        if (!BuildingGenerator.TryRegenerate(CurrentDocument(), floors,
                Random.Shared.Next(1, int.MaxValue), SelectedGeneratorOptions(),
                out var proposal, out var reason))
        {
            _hint = reason;
            Refresh();
            return;
        }
        BeginSuggestion(proposal, whole: false);
        _hint = "Replacement draft ready. Review the checked floors before " +
            "Accept All; other floors and the original perimeter are preserved.";
        Refresh();
    }

    private void NextSuggestedFloor_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSuggestion is null || _pendingSuggestion.FloorIndexes.Count == 0)
        {
            _hint = "Generate a suggestion before reviewing selected floors.";
            Refresh();
            return;
        }
        if (_pendingWholeBuilding)
        {
            // Some proposed levels do not exist in the active document yet.
            // Review the detached draft instead of cycling existing floors.
            ShowWholeBuildingPreview();
            CycleProposedFloor(1);
            return;
        }
        var eligible = _pendingSuggestion.FloorIndexes
            .Where(i => BuildingStoreys.HasFloor(_building, i))
            .Distinct().OrderBy(i => i).ToArray();
        if (eligible.Length == 0)
        {
            _hint = "Accept the whole-building draft to view its new floors.";
            Refresh();
            return;
        }
        int next = eligible.FirstOrDefault(i => i > _activeFloorIndex,
            eligible[0]);
        _building = CurrentDocument();
        ShowFloor(next);
        _hint = $"Previewing suggested rooms on {BuildingStoreys.FloorLabel(next)}.";
        Refresh();
    }

    private void AcceptSuggestedRoom_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSuggestion is null ||
            !EditorSurface.SelectedSuggestedRoomId.HasValue) return;
        if (_pendingWholeBuilding)
        {
            _hint = "Accept the whole-building draft first; individual rooms need an existing building.";
            Refresh();
            return;
        }
        var before = CurrentDocument();
        var suggested = BuildingStoreys.HasFloor(
            _pendingSuggestion.Building, _activeFloorIndex)
            ? BuildingStoreys.GetFloor(_pendingSuggestion.Building, _activeFloorIndex)
            : null;
        var room = suggested?.Rooms.FirstOrDefault(
            r => r.Id == EditorSurface.SelectedSuggestedRoomId.Value);
        if (room is null) return;
        if (!BuildingGenerator.TryAcceptRoom(before, _pendingSuggestion,
                _activeFloorIndex, room.Id, out var updated, out var reason))
        {
            _hint = "Cannot accept this room: " + reason;
            Refresh();
            return;
        }

        _history.RecordBefore(before, _activeFloorIndex);
        _building = updated;
        ShowFloor(_activeFloorIndex);
        _hint = $"Accepted {room.Name} on {BuildingStoreys.FloorLabel(_activeFloorIndex)}. " +
            "Other proposed rooms remain available. Ctrl+Z to undo.";
        Refresh();
    }

    private void SuggestWholeBuilding_Click(object sender, RoutedEventArgs e)
    {
        // Never silently turn a missing/invalid selection into one floor.
        int count = WholeBuildingFloorsBox.SelectedIndex + 1;
        if (count is < 1 or > 4)
        {
            _hint = "Choose 1, 2, 3 or 4 floors before suggesting a whole building.";
            Refresh();
            return;
        }
        int seed = Random.Shared.Next(1, int.MaxValue);
        int gridFeet = GridScaleBox.SelectedIndex switch { 1 => 5, 2 => 10, _ => 0 };
        if (!BuildingGenerator.TrySuggestWholeBuilding(count, seed,
            SelectedGeneratorOptions(), out var proposal, out var reason, gridFeet))
        {
            _hint = reason;
            Refresh();
            return;
        }
        if (BuildingStoreys.FloorCount(proposal.Building) != count)
        {
            _hint = $"The building generator returned an unexpected floor count. Expected {count}, " +
                $"received {BuildingStoreys.FloorCount(proposal.Building)}. No changes were applied.";
            Refresh();
            return;
        }
        BeginSuggestion(proposal, whole: true);
        _hint = $"Created a {count}-floor draft. Browse its floors directly on the plan; " +
            "choose Accept All to add every floor.";
        Refresh();
    }

    private void InspectProposal_Click(object sender, RoutedEventArgs e) =>
        ShowWholeBuildingPreview();

    // The draft is rendered by a separate, read-only surface in the main
    // workspace. The live EditorSurface stays unchanged until Accept All.
    private void ShowWholeBuildingPreview()
    {
        if (_pendingSuggestion is null || WholeBuildingPreviewPanel is null) return;
        var draft = _pendingSuggestion.Building;
        var indices = BuildingStoreys.FloorIndexes(draft);
        if (indices.Count == 0) return;
        int selected = ProposalFloorPicker.SelectedItem is ComboBoxItem previous &&
            previous.Tag is int floorIndex ? floorIndex : indices[0];
        ProposalFloorPicker.SelectionChanged -= ProposalFloorPicker_SelectionChanged;
        ProposalFloorPicker.Items.Clear();
        foreach (int index in indices)
        {
            int roomCount = BuildingStoreys.GetFloor(draft, index).Rooms.Count;
            ProposalFloorPicker.Items.Add(new ComboBoxItem
            {
                Content = $"{BuildingStoreys.FloorLabel(index)} — {roomCount} room(s)",
                Tag = index
            });
        }
        int position = Array.IndexOf(indices.ToArray(), selected);
        ProposalFloorPicker.SelectedIndex = position >= 0 ? position : 0;
        ProposalFloorPicker.SelectionChanged += ProposalFloorPicker_SelectionChanged;
        ProposedBuildingHeader.Text =
            $"PROPOSED {indices.Count}-FLOOR BUILDING — NOT YET ACCEPTED";
        WholeBuildingPreviewPanel.Visibility = Visibility.Visible;
        WorkingFloorNavigationBar.Visibility = Visibility.Collapsed;
        ViewProposedFloor();
    }

    private void ViewProposedFloor()
    {
        if (_pendingSuggestion is null ||
            ProposalFloorPicker.SelectedItem is not ComboBoxItem item ||
            item.Tag is not int floorIndex) return;
        var proposal = _pendingSuggestion.Building;
        WholeBuildingPreviewSurface.SetGridFeet(proposal.GridFeet);
        WholeBuildingPreviewSurface.LoadDocument(
            BuildingStoreys.GetFloor(proposal, floorIndex));
        WholeBuildingPreviewSurface.SetStairOverlay(proposal.Staircases, floorIndex);
        WholeBuildingPreviewSurface.SetChimneyOverlay(proposal.Chimneys);
        WholeBuildingPreviewSurface.SetSuggestionPreview(null);
        WholeBuildingPreviewSurface.SetFloorReferences([], 0);
        WholeBuildingPreviewSurface.SetFloorSilhouettes([]);
        Dispatcher.BeginInvoke(
            new Action(WholeBuildingPreviewSurface.FitToView),
            DispatcherPriority.Loaded);
    }

    private void ProposalFloorPicker_SelectionChanged(
        object sender, SelectionChangedEventArgs e) => ViewProposedFloor();

    private void PreviousProposalFloor_Click(object sender, RoutedEventArgs e) =>
        CycleProposedFloor(-1);

    private void NextProposalFloor_Click(object sender, RoutedEventArgs e) =>
        CycleProposedFloor(1);

    private void CycleProposedFloor(int step)
    {
        int count = ProposalFloorPicker.Items.Count;
        if (count == 0) return;
        ProposalFloorPicker.SelectedIndex =
            ((ProposalFloorPicker.SelectedIndex + step) % count + count) % count;
    }

    // Keep floor navigation visible even when the FLOORS sidebar is collapsed.
    private void WorkingFloorPicker_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EditorSurface is null ||
            WorkingFloorPicker.SelectedItem is not ComboBoxItem item ||
            item.Tag is not int index || index == _activeFloorIndex)
            return;
        if (_pendingWholeBuilding) return; // Pending floors are navigated above the draft.
        if (EditorSurface.HasPendingStep)
        {
            _hint = "Finish or cancel the current drawing before changing floors.";
            Refresh();
            return;
        }
        _building = CurrentDocument();
        ShowFloor(index);
    }

    private void PreviousWorkingFloor_Click(object sender, RoutedEventArgs e) =>
        NavigateFloor(-1);

    private void NextWorkingFloor_Click(object sender, RoutedEventArgs e) =>
        NavigateFloor(1);

    private void AcceptSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSuggestion is null) return;
        var before = CurrentDocument();
        string fingerprint = CurrentJson();
        bool changedBase = _suggestionSource != fingerprint;
        if (_pendingWholeBuilding &&
            (BuildingStoreys.FloorCount(_pendingSuggestion.Building) !=
                _pendingSuggestion.FloorIndexes.Count ||
             !_pendingSuggestion.FloorIndexes.SequenceEqual(
                BuildingStoreys.FloorIndexes(_pendingSuggestion.Building))))
        {
            _hint = "This draft does not contain all its requested floors. " +
                "Discard the preview and generate another building.";
            Refresh();
            return;
        }
        bool regenerating = _pendingSuggestion.Description.StartsWith(
            "REPLACEMENT DRAFT", StringComparison.Ordinal);
        if (_pendingWholeBuilding || changedBase || regenerating)
        {
            string explanation = _pendingWholeBuilding
                ? "Accepting this whole-building draft REPLACES the current building."
                : regenerating
                    ? "Accepting this regeneration REPLACES rooms, doors and windows on the checked floors. The current perimeter and untouched floors are kept."
                    : "The building changed since this suggestion was saved. Applying it will REPLACE the current building with the saved candidate.";
            if (MessageBox.Show(this, explanation +
                    "\n\nThe current design can be restored with Ctrl+Z. Continue?",
                "Apply suggested building", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        if (!BuildingStoreys.Validate(_pendingSuggestion.Building, out var reason))
        {
            _hint = "The suggested layout is invalid: " + reason;
            Refresh();
            return;
        }

        bool acceptedWholeBuilding = _pendingWholeBuilding;
        _history.RecordBefore(before, _activeFloorIndex);
        _building = BuildingStoreys.Copy(_pendingSuggestion.Building);
        GridScaleBox.SelectedIndex = _building.GridFeet == 10 ? 2 : _building.GridFeet == 5 ? 1 : 0;
        int acceptedFloorCount = BuildingStoreys.FloorCount(_building);
        _activeFloorIndex = Math.Clamp(_activeFloorIndex,
            BuildingStoreys.LowestFloorIndex(_building),
            BuildingStoreys.HighestFloorIndex(_building));
        ClearPendingSuggestion();
        NameBox.Text = _building.Name;
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        if (acceptedWholeBuilding)
        {
            // Player preview intentionally rejects editing. A generated draft
            // becomes a working design and must switch to editable map mode.
            MapModeBox.SelectedIndex = 0;
            EditorSurface.SetDisplayMode(MapVisibilityMode.Editor);
            EditorSurface.SetTool(EditorTool.Select);
            EditorSurface.IsHitTestVisible = true;
            EditorSurface.Focus();
        }
        _hint = acceptedWholeBuilding
            ? $"Accepted the {acceptedFloorCount}-floor building. Switch levels using " +
              "the FLOORS selector or Page Up / Page Down. Ctrl+Z restores the old building."
            : "Suggestion accepted on selected floors. Ctrl+Z restores the previous building.";
        Refresh();
    }

    private void DiscardSuggestion_Click(object sender, RoutedEventArgs e)
    {
        ClearPendingSuggestion();
        _hint = "Suggestion discarded; the building was not changed.";
        Refresh();
    }

    private void UpdateMemoryList()
    {
        if (SuggestionMemoryList is null) return;
        int selected = SuggestionMemoryList.SelectedIndex;
        SuggestionMemoryList.Items.Clear();
        foreach (var entry in _suggestionMemory)
            SuggestionMemoryList.Items.Add(entry.Label);
        if (selected >= 0 && selected < SuggestionMemoryList.Items.Count)
            SuggestionMemoryList.SelectedIndex = selected;
        bool hasSelection = SuggestionMemoryList.SelectedIndex >= 0 &&
            SuggestionMemoryList.SelectedIndex < _suggestionMemory.Count;
        RecallSuggestionButton.IsEnabled = RemoveSuggestionButton.IsEnabled = hasSelection;
        ClearSuggestionsButton.IsEnabled = _suggestionMemory.Count > 0;
    }

    private void MemorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionMemoryList is null || RecallSuggestionButton is null ||
            RemoveSuggestionButton is null || ClearSuggestionsButton is null)
            return;
        bool selected = SuggestionMemoryList.SelectedIndex >= 0 &&
            SuggestionMemoryList.SelectedIndex < _suggestionMemory.Count;
        RecallSuggestionButton.IsEnabled = RemoveSuggestionButton.IsEnabled = selected;
        ClearSuggestionsButton.IsEnabled = _suggestionMemory.Count > 0;
    }

    private void RememberSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSuggestion is null) return;
        if (_suggestionMemory.Count >= SuggestionMemory.MaximumItems)
        {
            _hint = "Suggestion memory is full. Remove entries before remembering another.";
            Refresh();
            return;
        }

        var candidate = _pendingSuggestion.Copy();
        _suggestionMemory.Add(new SavedSuggestionEntry
        {
            Label = $"{DateTime.Now:dd MMM HH:mm} — {candidate.Description}",
            Seed = candidate.Seed,
            FloorIndexes = candidate.FloorIndexes.ToArray(),
            Building = BuildingStoreys.Copy(candidate.Building),
            WholeBuilding = _pendingWholeBuilding,
            SourceFingerprint = _suggestionSource
        });
        bool saved = SuggestionMemory.Save(_suggestionMemory);
        UpdateMemoryList();
        SuggestionMemoryList.SelectedIndex = _suggestionMemory.Count - 1;
        _hint = saved
            ? "Candidate remembered and saved for later recall."
            : "Candidate remembered for this session, but could not save to AppData.";
        Refresh();
    }

    private void RecallSuggestion_Click(object sender, RoutedEventArgs e)
    {
        int index = SuggestionMemoryList.SelectedIndex;
        if (index < 0 || index >= _suggestionMemory.Count) return;
        var entry = _suggestionMemory[index];
        _pendingSuggestion = new BuildingSuggestion(
            BuildingStoreys.Copy(entry.Building), entry.Seed,
            entry.FloorIndexes.ToArray(), entry.Label);
        _suggestionSource = entry.SourceFingerprint;
        _pendingWholeBuilding = entry.WholeBuilding;
        UpdateSuggestionPreview();
        if (_pendingWholeBuilding) ShowWholeBuildingPreview();
        else
        {
            WholeBuildingPreviewPanel.Visibility = Visibility.Collapsed;
            WorkingFloorNavigationBar.Visibility = Visibility.Visible;
        }
        _hint = "Saved candidate previewed; Accept to apply it (may replace newer edits).";
        Refresh();
    }

    private void RemoveSuggestion_Click(object sender, RoutedEventArgs e)
    {
        int index = SuggestionMemoryList.SelectedIndex;
        if (index < 0 || index >= _suggestionMemory.Count) return;
        _suggestionMemory.RemoveAt(index);
        bool saved = SuggestionMemory.Save(_suggestionMemory);
        UpdateMemoryList();
        _hint = saved ? "Saved candidate removed." :
            "Removed from this session; disk update failed.";
        Refresh();
    }

    private void ClearSuggestions_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestionMemory.Count == 0) return;
        if (MessageBox.Show(this, "Permanently clear all remembered suggestions?",
                "Clear suggestion memory", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _suggestionMemory.Clear();
        bool saved = SuggestionMemory.Save(_suggestionMemory);
        UpdateMemoryList();
        _hint = saved ? "Saved suggestion list cleared." :
            "List cleared in this session; disk update failed.";
        Refresh();
    }

    private void RebuildFloorPicker()
    {
        if (FloorPicker is null) return;
        var priorSelection = GeneratorFloorChecks?.Children
            .OfType<CheckBox>().ToDictionary(
                cb => (int)cb.Tag, cb => cb.IsChecked == true) ??
            new Dictionary<int, bool>();
        bool prior = _loading;
        _loading = true;
        FloorPicker.Items.Clear();
        foreach (int i in BuildingStoreys.FloorIndexes(_building))
            FloorPicker.Items.Add(i == 0 ? "Ground Floor" : i < 0
                ? _building.BasementFloors[-i - 1].Name
                : _building.UpperFloors[i - 1].Name);
        FloorPicker.SelectedIndex =
            _activeFloorIndex - BuildingStoreys.LowestFloorIndex(_building);
        if (WorkingFloorPicker is not null)
        {
            WorkingFloorPicker.Items.Clear();
            foreach (int index in BuildingStoreys.FloorIndexes(_building))
            {
                int rooms = BuildingStoreys.GetFloor(_building, index).Rooms.Count;
                WorkingFloorPicker.Items.Add(new ComboBoxItem
                {
                    Content = $"{BuildingStoreys.FloorLabel(index)} — {rooms} room(s)",
                    Tag = index
                });
            }
            WorkingFloorPicker.SelectedIndex =
                _activeFloorIndex - BuildingStoreys.LowestFloorIndex(_building);
        }
        if (GeneratorFloorChecks is not null)
        {
            GeneratorFloorChecks.Children.Clear();
            foreach (int i in BuildingStoreys.FloorIndexes(_building))
                GeneratorFloorChecks.Children.Add(new CheckBox
                {
                    Content = BuildingStoreys.FloorLabel(i),
                    Tag = i,
                    IsChecked = priorSelection.TryGetValue(i, out bool selected)
                        ? selected : i == 0,
                    Margin = new Thickness(0, 0, 0, 5)
                });
        }
        _loading = prior;
    }

    private void ShowFloor(int index)
    {
        if (!BuildingStoreys.HasFloor(_building, index)) return;
        _loading = true;
        _activeFloorIndex = index;
        EditorSurface.SetGridFeet(_building.GridFeet);
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, index));
        UpdateSuggestionPreview();
        LoadWallThicknessControls();
        EditorSurface.SetStairOverlay(_building.Staircases, index);
        EditorSurface.SetChimneyOverlay(_building.Chimneys);
        UpdateAlignmentReferences();
        EditorSurface.SetTool(EditorTool.Select);
        FloorPicker.SelectedIndex =
            index - BuildingStoreys.LowestFloorIndex(_building);
        if (WorkingFloorPicker is not null)
            WorkingFloorPicker.SelectedIndex =
                index - BuildingStoreys.LowestFloorIndex(_building);
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
        if (AllFloorSilhouettesCheckBox?.IsChecked == true)
        {
            var all = BuildingStoreys.FloorIndexes(_building)
                .Where(i => i != _activeFloorIndex)
                .Select(i => new AdjacentFloorPlan(
                    i, i > _activeFloorIndex, BuildingStoreys.GetFloor(_building, i)))
                .ToList();
            EditorSurface.SetFloorSilhouettes(all);
        }
        else
            EditorSurface.SetFloorSilhouettes([]);
    }

    private void AlignmentChanged(object sender, RoutedEventArgs e) =>
        UpdateAlignmentReferences();

    private void AlignmentOpacityChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e) =>
        UpdateAlignmentReferences();

    private void FloorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EditorSurface is null || FloorPicker is null) return;
        int selected = FloorPicker.SelectedIndex;
        if (selected < 0) return;
        int next = selected + BuildingStoreys.LowestFloorIndex(_building);
        if (next == _activeFloorIndex) return;
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
        ClearPendingSuggestion();
        _activeFloorIndex = BuildingStoreys.HighestFloorIndex(_building);
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        _hint = "Upper floor created. Exterior copied; rooms and doors are independent.";
        Refresh();
    }

    private void AddBasement_Click(object sender, RoutedEventArgs e)
    {
        var before = CurrentDocument();
        if (!BuildingStoreys.TryAddBasement(before, out var updated, out var reason))
        {
            MessageBox.Show(this, reason, "Cannot add basement",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _history.RecordBefore(before, _activeFloorIndex);
        _building = updated;
        _activeFloorIndex = BuildingStoreys.LowestFloorIndex(updated);
        ClearPendingSuggestion();
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        _hint = "Basement added below the current lowest level.";
        Refresh();
    }

    private void RemoveFloor_Click(object sender, RoutedEventArgs e)
    {
        var before = CurrentDocument();
        int selected = _activeFloorIndex;
        if (!BuildingStoreys.TryRemoveFloor(before, selected,
            out var result, out int affectedStairs, out var reason))
        {
            MessageBox.Show(this, reason, "Cannot delete floor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string stairsText = affectedStairs == 0
            ? "No staircase connections will be removed."
            : $"{affectedStairs} connected staircase flight(s) will also be removed.";
        string prompt = $"Delete {BuildingStoreys.FloorLabel(selected)}?\n\n" +
            "Every surviving floor above it shifts down one level. " +
            stairsText + "\n\nThis can be undone with Ctrl+Z. Continue?";
        if (MessageBox.Show(this, prompt, "Delete selected floor",
                MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        _history.RecordBefore(before, selected);
        _building = result;
        _activeFloorIndex = BuildingStoreys.HasFloor(result, selected)
            ? selected : BuildingStoreys.FloorIndexes(result)
                .OrderBy(index => Math.Abs(index - selected)).First();
        ClearPendingSuggestion();
        RebuildFloorPicker();
        ShowFloor(_activeFloorIndex);
        _hint = $"Deleted {BuildingStoreys.FloorLabel(selected)}; " +
            $"{affectedStairs} staircase connection(s) removed. Ctrl+Z to undo.";
        Refresh();
    }

    private void UpdateStairFlightChoices()
    {
        if (StairFlightPicker is null || EditorSurface is null) return;
        _loadingStairPicker = true;
        try
        {
            Guid? selectedId = EditorSurface.SelectedStaircaseId;
            StairFlightPicker.Items.Clear();
            foreach (var flight in _building.Staircases.Where(s =>
                s.LowerFloorIndex == _activeFloorIndex ||
                s.LowerFloorIndex + 1 == _activeFloorIndex)
                .OrderBy(s => s.LowerFloorIndex))
            {
                string direction = flight.LowerFloorIndex == _activeFloorIndex
                    ? $"UP to {BuildingStoreys.FloorLabel(_activeFloorIndex + 1)}"
                    : $"DOWN to {BuildingStoreys.FloorLabel(_activeFloorIndex - 1)}";
                StairFlightPicker.Items.Add(new ComboBoxItem
                {
                    Content = $"{direction} — {flight.Centre.X:0.##}, {flight.Centre.Y:0.##} m",
                    Tag = flight.Id
                });
            }
            int choice = -1;
            for (int i = 0; i < StairFlightPicker.Items.Count; i++)
            {
                if ((Guid)((ComboBoxItem)StairFlightPicker.Items[i]).Tag == selectedId)
                {
                    choice = i;
                    break;
                }
            }
            StairFlightPicker.SelectedIndex = choice;
        }
        finally { _loadingStairPicker = false; }
    }

    private void StairFlightPicker_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStairPicker || StairFlightPicker.SelectedItem is not ComboBoxItem selected)
            return;
        if (selected.Tag is Guid id)
            EditorSurface.SelectStairById(id);
    }

    private void NavigateFloor(int change)
    {
        if (_pendingWholeBuilding)
        {
            // Page Up/Page Down navigate draft storeys as well as real floors.
            CycleProposedFloor(change);
            return;
        }
        if (EditorSurface.HasPendingStep)
        {
            _hint = "Finish or cancel the current drawing before switching floors.";
            Refresh();
            return;
        }
        int next = _activeFloorIndex + change;
        if (!BuildingStoreys.HasFloor(_building, next)) return;
        _building = CurrentDocument();
        ShowFloor(next);
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
        // Prefer stairs up from the current level. At the top of the
        // building, allow placing stairs down into the level below,
        // including the basement-to-ground connection.
        int lowerFloor = _activeFloorIndex < BuildingStoreys.HighestFloorIndex(_building)
            ? _activeFloorIndex : _activeFloorIndex - 1;
        if (StairLengthBox is null || StairWidthBox is null ||
            OpeningWidthBox is null || OpeningLengthBox is null ||
            StairRotationBox is null || EditorSurface is null ||
            lowerFloor < BuildingStoreys.LowestFloorIndex(_building) ||
            lowerFloor >= BuildingStoreys.HighestFloorIndex(_building) ||
            !TryReadMetres(StairLengthBox.Text, out double length) ||
            !TryReadMetres(StairLandingBox.Text, out double landing) ||
            !TryReadMetres(StairWidthBox.Text, out double width) ||
            !TryReadMetres(OpeningWidthBox.Text, out double extraWidth) ||
            !TryReadMetres(OpeningLengthBox.Text, out double extraLength) ||
            !TryReadMetres(StairRotationBox.Text, out double angle) ||
            !double.IsFinite(length) || !double.IsFinite(landing) ||
            !double.IsFinite(width) ||
            !double.IsFinite(extraWidth) || !double.IsFinite(extraLength) ||
            !double.IsFinite(angle))
            return null;

        return new StaircaseDefinition
        {
            LowerFloorIndex = lowerFloor,
            Length = length,
            Width = width,
            LandingLength = landing,
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
        StairLandingBox.Text = stair.LandingLength.ToString("R", culture);
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
            !TryReadMetres(StairLandingBox.Text, out double landing) ||
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
        stair.LandingLength = landing;
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
        GridScaleBox.SelectedIndex = _building.GridFeet == 10 ? 2 : _building.GridFeet == 5 ? 1 : 0;
        _activeFloorIndex = state.FloorIndex;
        ClearPendingSuggestion();
        _nameBeforeEdit = null;
        NameBox.Text = _building.Name;
        RebuildFloorPicker();
        EditorSurface.SetGridFeet(_building.GridFeet);
        EditorSurface.LoadDocument(BuildingStoreys.GetFloor(_building, _activeFloorIndex));
        EditorSurface.SetChimneyOverlay(_building.Chimneys);
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
    private void SelectTool_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingWholeBuilding)
        {
            _hint = "This is a read-only draft. Accept all proposed floors first to edit rooms.";
            Refresh();
            return;
        }
        if (MapModeBox.SelectedIndex == 2)
            MapModeBox.SelectedIndex = 0;
        EditorSurface.SetTool(EditorTool.Select);
        EditorSurface.Focus();
    }
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

    private void GridScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EditorSurface is null || GridScaleBox is null) return;
        int selected = GridScaleBox.SelectedIndex switch { 1 => 5, 2 => 10, _ => 0 };
        if (_building.GridFeet == selected)
        {
            EditorSurface.SetGridFeet(selected);
            return;
        }
        var before = CurrentDocument();
        var changed = BuildingStoreys.Copy(before);
        changed.GridFeet = selected;
        _history.RecordBefore(before, _activeFloorIndex);
        _building = changed;
        ClearPendingSuggestion();
        EditorSurface.SetGridFeet(selected);
        _hint = selected == 0 ? "Metric grid selected." :
            $"Using {selected}-foot squares for drawing and grid display.";
        Refresh();
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
        else if (!ctrl && !shift && e.Key == Key.PageUp &&
                 Keyboard.FocusedElement is not TextBox)
            NavigateFloor(+1);
        else if (!ctrl && !shift && e.Key == Key.PageDown &&
                 Keyboard.FocusedElement is not TextBox)
            NavigateFloor(-1);
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
