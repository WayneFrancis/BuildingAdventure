using System.IO;
using System.Text.Json;
using BuildingAdventure.Core.Buildings;

namespace BuildingAdventure.Desktop;

internal sealed class SavedSuggestionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public string Label { get; set; } = "";
    public int Seed { get; set; }
    public int[] FloorIndexes { get; set; } = [];
    public BuildingDocument Building { get; set; } = new();
    public string SourceFingerprint { get; set; } = "";
    public bool WholeBuilding { get; set; }
}

internal static class SuggestionMemory
{
    public const int MaximumItems = 50;
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BuildingAdventure", "suggestion-memory.json");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static List<SavedSuggestionEntry> Load()
    {
        try
        {
            if (!File.Exists(PathName)) return [];
            var entries = JsonSerializer.Deserialize<List<SavedSuggestionEntry>>(
                File.ReadAllText(PathName), Options) ?? [];
            return entries.Where(e => e is not null &&
                    e.Building is not null && e.FloorIndexes is not null &&
                    !string.IsNullOrWhiteSpace(e.Label) &&
                    BuildingStoreys.Validate(e.Building, out _))
                .Take(MaximumItems).ToList();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    public static bool Save(IReadOnlyCollection<SavedSuggestionEntry> entries)
    {
        try
        {
            string folder = Path.GetDirectoryName(PathName)!;
            Directory.CreateDirectory(folder);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, Options));
            File.Move(temp, PathName, overwrite: true);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }
}
