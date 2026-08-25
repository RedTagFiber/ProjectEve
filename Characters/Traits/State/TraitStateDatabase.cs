using ProjectEve.Characters.Traits.State;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// Trait-state compatibility API.
///
/// Canonical intensity: NpcTraitValues via NpcTraitRepository.
/// Canonical expression control: NpcTraitControl.
///
/// This class no longer creates or owns a second Traits table.
/// </summary>
public class TraitStateDatabase
{
    public TraitStateDatabase(string? dbPath = null)
    {
        // dbPath remains only for source compatibility.
        // Canonical routing is owned by ProjectEveDatabaseConnections.
    }

    public void SaveTraitState(TraitState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.TraitId))
            return;

        NpcTraitRepository.SaveOne(state.NpcId, state.TraitId, state.Intensity);
        NpcTraitRepository.SaveControl(
            state.NpcId,
            state.TraitId,
            state.Control,
            state.LastUpdated == default ? DateTime.UtcNow : state.LastUpdated);
    }

    public TraitState? Load(int npcId, string traitId)
    {
        var row = NpcTraitRepository.LoadOne(npcId, traitId);
        if (row == null)
            return null;

        return new TraitState
        {
            NpcId = npcId,
            TraitId = traitId,
            Intensity = (int)Math.Round(row.Value.Value),
            Control = row.Value.Control,
            LastUpdated = row.Value.LastUpdated
        };
    }

    public List<TraitState> LoadAllForNpc(int npcId)
    {
        var values = NpcTraitRepository.LoadAll(npcId);
        var list = new List<TraitState>(values.Count);

        foreach (var pair in values)
        {
            var row = NpcTraitRepository.LoadOne(npcId, pair.Key);

            list.Add(new TraitState
            {
                NpcId = npcId,
                TraitId = pair.Key,
                Intensity = (int)Math.Round(pair.Value),
                Control = row?.Control ?? 50,
                LastUpdated = row?.LastUpdated ?? DateTime.UtcNow
            });
        }

        return list;
    }
}
