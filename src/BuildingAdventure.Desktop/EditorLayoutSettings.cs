using System.IO;
using System.Text.Json;

namespace BuildingAdventure.Desktop;

/// <summary>
/// Machine-local UI preferences, separate from building JSON.
/// Corrupt/obsolete preferences are ignored rather than preventing startup.
/// </summary>
internal sealed class EditorLayoutSettings
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public Dictionary<string, bool> ExpandedSections { get; set; } = new();
    public double SidebarScrollOffset { get; set; }
    public bool SnapToGrid { get; set; } = true;
    public bool HighlightUnassigned { get; set; } = true;
    public bool ShowBelow { get; set; }
    public bool ShowAbove { get; set; }
    public double ReferenceOpacity { get; set; } = 0.45;
    public bool AllFloorSilhouettes { get; set; }
    public int MapModeIndex { get; set; }
    public bool GenerateRooms { get; set; } = true;
    public bool GenerateCorridors { get; set; } = true;
    public bool GenerateDoors { get; set; } = true;
    public bool GenerateWindows { get; set; } = true;
    public bool GenerateStairs { get; set; } = true;
    public bool GenerateFireplaces { get; set; }
    public bool HallHouseLayout { get; set; } = true;
    public int GeneratorPurposeIndex { get; set; }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BuildingAdventure", "ui-settings.json");

    public static EditorLayoutSettings? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return null;
            return JsonSerializer.Deserialize<EditorLayoutSettings>(
                File.ReadAllText(SettingsFile));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool TrySave(EditorLayoutSettings value)
    {
        try
        {
            string path = SettingsFile;
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
