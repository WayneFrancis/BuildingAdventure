namespace BuildingAdventure.Core.Buildings;

/// <summary>
/// Player-mode geometry includes real walls over unrevealed secret doorways.
/// The original building and actual door definitions are never modified.
/// </summary>
public static class SecretDoorVisibility
{
    public static BuildingDocument PhysicalPlanForPlayer(BuildingDocument source)
    {
        var view = BuildingStoreys.Copy(source);
        view.Doors.RemoveAll(door => door.IsSecret && !door.IsRevealed);
        return view;
    }

    public static bool IsVisibleToPlayer(DoorDefinition door) =>
        !door.IsSecret || door.IsRevealed;
}
