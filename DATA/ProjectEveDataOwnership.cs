namespace ProjectEve.Data;

/// <summary>
/// Canonical data ownership rules for ProjectEve.
/// One fact has one authoritative store.
/// </summary>
public static class ProjectEveDataOwnership
{
    public const string Main =
        "NPC identity/build/appearance/traits/cognition/job/voice/media/current account definitions";

    public const string History =
        "Objective events/conversations/calls/movements/purchases/paychecks/financial transaction ledger";

    public const string Relationships =
        "Directed relationships/personal memory/beliefs/knowledge/rumors/secrets/interpretation";

    public const string Locations =
        "Location/room/scene definitions/assets/audio/motion/current location/visits/occupancy";

    public const string FileSystem =
        "Actual binary media such as PNG/WAV/MP4 and generated workflow outputs";
}
