using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public Task SaveRelationshipAsync(NpcRelationshipRow rel)
    {
        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        cmd.CommandText = """
        UPDATE RelationshipStates SET
            RelationshipType = $type,
            FamilyRole = $familyRole,
            Love = $love,
            Trust = $trust,
            Respect = $respect,
            Loyalty = $loyalty,
            Anger = $anger,
            Resentment = $resentment,
            Fear = $fear,
            Jealousy = $jealousy,
            Attraction = $attraction,
            Tension = $tension,
            Importance = $importance,
            Notes = $notes,
            UpdatedRealAt = CURRENT_TIMESTAMP
        WHERE RelationshipId = $id;
        """;

        cmd.Parameters.AddWithValue("$id", rel.Id ?? "");
        cmd.Parameters.AddWithValue("$type", rel.RelationshipType ?? "");
        cmd.Parameters.AddWithValue("$familyRole", rel.FamilyRole ?? "");
        cmd.Parameters.AddWithValue("$love", Clamp(rel.Affection));
        cmd.Parameters.AddWithValue("$trust", Clamp(rel.Trust));
        cmd.Parameters.AddWithValue("$respect", Clamp(rel.Respect));
        cmd.Parameters.AddWithValue("$loyalty", Clamp(rel.Loyalty));
        cmd.Parameters.AddWithValue("$anger", Clamp(rel.Anger));
        cmd.Parameters.AddWithValue("$resentment", Clamp(rel.Resentment));
        cmd.Parameters.AddWithValue("$fear", Clamp(rel.Fear));
        cmd.Parameters.AddWithValue("$jealousy", Clamp(rel.Jealousy));
        cmd.Parameters.AddWithValue("$attraction", Clamp(rel.Attraction));
        cmd.Parameters.AddWithValue("$tension", Clamp(rel.Tension));
        cmd.Parameters.AddWithValue("$importance", Clamp(rel.Importance));
        cmd.Parameters.AddWithValue("$notes", rel.Notes ?? "");
        cmd.ExecuteNonQuery();

        using var mainConn = Open();
        AddRevision(
            mainConn,
            rel.NpcId,
            "Relationship",
            "Relationship updated",
            $"Updated {rel.TargetName}: {rel.RelationshipType}.");

        return Task.CompletedTask;
    }

    public Task<List<NpcRelationshipReason>> GetRelationshipReasonsAsync(string relationshipId)
    {
        using var conn = OpenRelationships();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        SELECT
            Id,
            RelationshipId,
            EventId,
            ScoreName,
            Delta,
            Reason,
            Interpretation,
            IsStillActive,
            CreatedRealAt
        FROM RelationshipReasons
        WHERE RelationshipId = $relationshipId
        ORDER BY IsStillActive DESC, ABS(Delta) DESC, CreatedRealAt DESC;
        """;
        cmd.Parameters.AddWithValue("$relationshipId", relationshipId ?? "");

        var list = new List<NpcRelationshipReason>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcRelationshipReason
            {
                Id = ReadString(reader, "Id"),
                RelationshipId = ReadString(reader, "RelationshipId"),
                Metric = ReadString(reader, "ScoreName"),
                Impact = ReadInt(reader, "Delta"),
                Title = ReadString(reader, "Reason"),
                Details = ReadString(reader, "Interpretation"),
                HistoryEventId = ReadString(reader, "EventId"),
                StillActive = ReadBool(reader, "IsStillActive"),
                CreatedRealAt = ReadString(reader, "CreatedRealAt")
            });
        }

        return Task.FromResult(list);
    }

    public Task AddRelationshipReasonAsync(NpcRelationshipReason reason)
    {
        var id = string.IsNullOrWhiteSpace(reason.Id)
            ? Guid.NewGuid().ToString("N")
            : reason.Id;

        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO RelationshipReasons
        (
            Id,
            RelationshipId,
            EventId,
            ScoreName,
            Delta,
            Reason,
            Interpretation,
            IsStillActive,
            Importance,
            GameTime,
            CreatedRealAt
        )
        VALUES
        (
            $id,
            $relationshipId,
            $eventId,
            $scoreName,
            $delta,
            $reason,
            $interpretation,
            $active,
            50,
            '',
            CURRENT_TIMESTAMP
        );
        """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$relationshipId", reason.RelationshipId ?? "");
        cmd.Parameters.AddWithValue("$eventId", reason.HistoryEventId ?? "");
        cmd.Parameters.AddWithValue("$scoreName", reason.Metric ?? "");
        cmd.Parameters.AddWithValue("$delta", Math.Clamp(reason.Impact, -100, 100));
        cmd.Parameters.AddWithValue("$reason", reason.Title ?? "");
        cmd.Parameters.AddWithValue("$interpretation", reason.Details ?? "");
        cmd.Parameters.AddWithValue("$active", reason.StillActive ? 1 : 0);
        cmd.ExecuteNonQuery();

        reason.Id = id;

        using var mainConn = Open();
        AddRevision(
            mainConn,
            reason.NpcId,
            "RelationshipReason",
            "Relationship reason added",
            $"{reason.Metric}: {reason.Title} ({reason.Impact:+#;-#;0}).");

        return Task.CompletedTask;
    }

    public Task SaveRelationshipReasonAsync(NpcRelationshipReason reason)
    {
        if (string.IsNullOrWhiteSpace(reason.Id))
            return AddRelationshipReasonAsync(reason);

        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        cmd.CommandText = """
        UPDATE RelationshipReasons SET
            EventId = $eventId,
            ScoreName = $scoreName,
            Delta = $delta,
            Reason = $reason,
            Interpretation = $interpretation,
            IsStillActive = $active
        WHERE Id = $id;
        """;

        cmd.Parameters.AddWithValue("$id", reason.Id);
        cmd.Parameters.AddWithValue("$eventId", reason.HistoryEventId ?? "");
        cmd.Parameters.AddWithValue("$scoreName", reason.Metric ?? "");
        cmd.Parameters.AddWithValue("$delta", Math.Clamp(reason.Impact, -100, 100));
        cmd.Parameters.AddWithValue("$reason", reason.Title ?? "");
        cmd.Parameters.AddWithValue("$interpretation", reason.Details ?? "");
        cmd.Parameters.AddWithValue("$active", reason.StillActive ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var mainConn = Open();
        AddRevision(
            mainConn,
            reason.NpcId,
            "RelationshipReason",
            "Relationship reason updated",
            $"{reason.Metric}: {reason.Title} ({reason.Impact:+#;-#;0}) / {(reason.StillActive ? "Active" : "Inactive")}.");

        return Task.CompletedTask;
    }

    public Task SetRelationshipReasonActiveAsync(string id, int npcId, bool isActive)
    {
        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        cmd.CommandText = """
        UPDATE RelationshipReasons
        SET IsStillActive = $active
        WHERE Id = $id;
        """;
        cmd.Parameters.AddWithValue("$id", id ?? "");
        cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var mainConn = Open();
        AddRevision(
            mainConn,
            npcId,
            "RelationshipReason",
            isActive ? "Relationship reason reactivated" : "Relationship reason deactivated",
            $"ReasonId={id}.");

        return Task.CompletedTask;
    }

    // Kept for backward compatibility with older Studio components.
    // Patch 14 UI no longer hard-deletes authored relationship reasons.
    public Task DeleteRelationshipReasonAsync(string id)
    {
        using var conn = OpenRelationships();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "DELETE FROM RelationshipReasons WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id ?? "");
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public (string BaseUrl, string Model) GetAiRuntimeConfig()
        => (_options.OllamaBaseUrl, _options.OllamaModel);

    public Task<List<NpcMemoryParticipantOption>> GetMemoryParticipantOptionsAsync()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT
            Id,
            IFNULL(Name, '') AS Name,
            IFNULL(Tier, 5) AS Tier,
            IFNULL(Occupation, '') AS Occupation,
            IFNULL(Location, '') AS Location
        FROM Characters
        WHERE trim(IFNULL(Name, '')) <> ''
        ORDER BY Name, Id;
        """;

        var list = new List<NpcMemoryParticipantOption>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new NpcMemoryParticipantOption
            {
                CharacterId = Convert.ToInt32(reader["Id"]),
                Name = reader["Name"]?.ToString() ?? "",
                Tier = Convert.ToInt32(reader["Tier"]),
                Occupation = reader["Occupation"]?.ToString() ?? "",
                Location = reader["Location"]?.ToString() ?? ""
            });
        }

        return Task.FromResult(list);
    }

    public Task<NpcSharedEventSaveResult> SaveSharedEventAsync(NpcSharedEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var title = (draft.Title ?? "").Trim();
        var trueSummary = (draft.TrueEventSummary ?? "").Trim();
        var sharedKnown = (draft.SharedKnownHistory ?? "").Trim();
        var sharedMemory = (draft.SharedBaseMemory ?? "").Trim();

        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("True event title is required.");
        if (string.IsNullOrWhiteSpace(trueSummary))
            throw new InvalidOperationException("True event summary is required.");
        if (string.IsNullOrWhiteSpace(sharedKnown))
            throw new InvalidOperationException("Shared known history is required.");
        if (string.IsNullOrWhiteSpace(sharedMemory))
            throw new InvalidOperationException("Shared base memory is required.");

        var participants = (draft.Participants ?? new List<NpcSharedEventParticipantDraft>())
            .Where(x => x.CharacterId > 0)
            .GroupBy(x => x.CharacterId)
            .Select(x => x.First())
            .ToList();

        if (draft.AuthoringCharacterId > 0 &&
            participants.All(x => x.CharacterId != draft.AuthoringCharacterId))
        {
            participants.Insert(0, new NpcSharedEventParticipantDraft
            {
                CharacterId = draft.AuthoringCharacterId
            });
        }

        if (participants.Count == 0)
            throw new InvalidOperationException("At least one real NPC participant is required.");

        var eventId = $"event:studio:{Guid.NewGuid():N}";
        var historyCommitted = false;

        try
        {
            using (var historyConn = new SqliteConnection("Data Source=" + _options.HistoryDbPath))
            {
                historyConn.Open();
                using var tx = historyConn.BeginTransaction();

                using (var evt = historyConn.CreateCommand())
                {
                    evt.Transaction = tx;
                    evt.CommandText = """
                    INSERT INTO WorldEvents
                    (
                        EventId, WorldId, EventType, Title, Summary, Details,
                        LocationId, PlaceText, Channel, Status, GameTime, GameTimeEnd,
                        RealTime, RealTimeEnd, Source, Confidence, CreatedRealAt, UpdatedRealAt
                    )
                    VALUES
                    (
                        $eventId, 'smalltown', $eventType, $title, $summary, $details,
                        '', $place, 'NpcStudio', 'Closed', $gameTime, '',
                        CURRENT_TIMESTAMP, '', 'NpcStudio', 100, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                    );
                    """;
                    evt.Parameters.AddWithValue("$eventId", eventId);
                    evt.Parameters.AddWithValue("$eventType",
                        string.IsNullOrWhiteSpace(draft.EventType) ? "Shared Experience" : draft.EventType.Trim());
                    evt.Parameters.AddWithValue("$title", title);
                    evt.Parameters.AddWithValue("$summary", trueSummary);
                    evt.Parameters.AddWithValue("$details", trueSummary);
                    evt.Parameters.AddWithValue("$place", (draft.PlaceText ?? "").Trim());
                    evt.Parameters.AddWithValue("$gameTime", (draft.GameTime ?? "").Trim());
                    evt.ExecuteNonQuery();
                }

                var trueParticipants = participants
                    .Where(x => x.IsTrueEventParticipant)
                    .ToList();

                foreach (var view in trueParticipants)
                {
                    using var participant = historyConn.CreateCommand();
                    participant.Transaction = tx;
                    participant.CommandText = """
                    INSERT INTO EventParticipants (EventId, CharacterId, Role)
                    VALUES ($eventId, $characterId, 'Present');
                    """;
                    participant.Parameters.AddWithValue("$eventId", eventId);
                    participant.Parameters.AddWithValue("$characterId", view.CharacterId);
                    participant.ExecuteNonQuery();
                }

                using (var verify = historyConn.CreateCommand())
                {
                    verify.Transaction = tx;
                    verify.CommandText = """
                    SELECT COUNT(DISTINCT CharacterId)
                    FROM EventParticipants
                    WHERE EventId = $eventId;
                    """;
                    verify.Parameters.AddWithValue("$eventId", eventId);
                    var savedParticipantCount = Convert.ToInt32(verify.ExecuteScalar() ?? 0);
                    if (savedParticipantCount != trueParticipants.Count)
                        throw new InvalidOperationException(
                            $"TRUE HISTORY participant verification failed. Expected {trueParticipants.Count}, saved {savedParticipantCount}.");
                }

                tx.Commit();
                historyCommitted = true;
            }

            var knowledgeCreated = 0;
            var memoriesCreated = 0;

            using (var relConn = OpenRelationships())
            using (var tx = relConn.BeginTransaction())
            {
                foreach (var view in participants)
                {
                    var knowledgeLevel = (view.KnowledgeLevel ?? "Shared").Trim();

                    var knownText = !string.IsNullOrWhiteSpace(view.KnownHistoryOverride)
                        ? view.KnownHistoryOverride.Trim()
                        : knowledgeLevel.Equals("FullTruth", StringComparison.OrdinalIgnoreCase)
                            ? trueSummary
                            : knowledgeLevel.Equals("None", StringComparison.OrdinalIgnoreCase)
                                ? ""
                                : sharedKnown;

                    var memoryText = !string.IsNullOrWhiteSpace(view.MemoryViewOverride)
                        ? view.MemoryViewOverride.Trim()
                        : sharedMemory;

                    if (!knowledgeLevel.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        using var knowledge = relConn.CreateCommand();
                        knowledge.Transaction = tx;
                        knowledge.CommandText = """
                        INSERT INTO KnowledgeItems
                        (
                            Id, KnowerCharacterId, SubjectCharacterId, EventId, KnowledgeType,
                            WhatTheyKnow, HowTheyLearnedIt, SourceCharacterId, Confidence,
                            IsRumor, IsSecret, IsFalseBelief,
                            LearnedGameTime, LastUpdatedGameTime, CreatedRealAt
                        )
                        VALUES
                        (
                            $id, $knower, NULL, $eventId, 'EventHistory',
                            $whatTheyKnow, $howLearned, NULL, $confidence,
                            0, 0, 0,
                            $gameTime, $gameTime, CURRENT_TIMESTAMP
                        );
                        """;
                        knowledge.Parameters.AddWithValue("$id",
                            $"knowledge:{view.CharacterId}:{Guid.NewGuid():N}");
                        knowledge.Parameters.AddWithValue("$knower", view.CharacterId);
                        knowledge.Parameters.AddWithValue("$eventId", eventId);
                        knowledge.Parameters.AddWithValue("$whatTheyKnow", knownText);
                        knowledge.Parameters.AddWithValue("$howLearned",
                            view.IsTrueEventParticipant ? "Present at event" : "Learned about event");
                        knowledge.Parameters.AddWithValue("$confidence", Math.Clamp(draft.Confidence, 0, 100));
                        knowledge.Parameters.AddWithValue("$gameTime", (draft.GameTime ?? "").Trim());
                        knowledgeCreated += knowledge.ExecuteNonQuery();
                    }

                    if (view.CreateMemory)
                    {
                        using var memory = relConn.CreateCommand();
                        memory.Transaction = tx;
                        memory.CommandText = """
                        INSERT INTO PersonalMemories
                        (
                            Id, KnowerCharacterId, SubjectCharacterId, EventId, MemoryType,
                            MemoryText, Interpretation, EmotionalMeaning,
                            Importance, Strength, Confidence, IsLockedPeak,
                            LearnedGameTime, LastUpdatedGameTime, CreatedRealAt
                        )
                        VALUES
                        (
                            $id, $knower, NULL, $eventId, $memoryType,
                            $memoryText, $interpretation, $emotionalMeaning,
                            $importance, $strength, $confidence, 0,
                            $gameTime, $gameTime, CURRENT_TIMESTAMP
                        );
                        """;
                        memory.Parameters.AddWithValue("$id",
                            $"memory:{view.CharacterId}:{Guid.NewGuid():N}");
                        memory.Parameters.AddWithValue("$knower", view.CharacterId);
                        memory.Parameters.AddWithValue("$eventId", eventId);
                        memory.Parameters.AddWithValue("$memoryType",
                            view.IsTrueEventParticipant ? "SharedEvent" : "LearnedEvent");
                        memory.Parameters.AddWithValue("$memoryText", memoryText);
                        memory.Parameters.AddWithValue("$interpretation", (view.Interpretation ?? "").Trim());
                        memory.Parameters.AddWithValue("$emotionalMeaning", (view.EmotionalMeaning ?? "").Trim());
                        memory.Parameters.AddWithValue("$importance", Math.Clamp(draft.Importance, 0, 100));
                        memory.Parameters.AddWithValue("$strength", Math.Clamp(draft.Strength, 0, 100));
                        memory.Parameters.AddWithValue("$confidence", Math.Clamp(draft.Confidence, 0, 100));
                        memory.Parameters.AddWithValue("$gameTime", (draft.GameTime ?? "").Trim());
                        memoriesCreated += memory.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }

            using (var mainConn = Open())
            {
                AddRevision(
                    mainConn,
                    draft.AuthoringCharacterId,
                    "SharedEvent",
                    "Shared event created",
                    $"{title} | EventId {eventId} | {participants.Count(x => x.IsTrueEventParticipant)} true participants | {participants.Count} subjective viewers");
            }

            return Task.FromResult(new NpcSharedEventSaveResult
            {
                EventId = eventId,
                ParticipantCount = participants.Count,
                KnownHistoryRowsCreated = knowledgeCreated,
                MemoryRowsCreated = memoriesCreated
            });
        }
        catch
        {
            if (historyCommitted)
            {
                try
                {
                    using var historyConn = new SqliteConnection("Data Source=" + _options.HistoryDbPath);
                    historyConn.Open();
                    using var cleanup = historyConn.CreateCommand();
                    cleanup.CommandText = """
                    DELETE FROM EventParticipants WHERE EventId = $eventId;
                    DELETE FROM WorldEvents WHERE EventId = $eventId;
                    """;
                    cleanup.Parameters.AddWithValue("$eventId", eventId);
                    cleanup.ExecuteNonQuery();
                }
                catch
                {
                    // Preserve original exception. EventId is deterministic in the error path for audit/repair.
                }
            }

            throw;
        }
    }

    public Task<List<NpcCanonicalHistoryEventOption>> GetCanonicalHistoryEventsAsync(int npcId)
    {
        using var conn = new SqliteConnection("Data Source=" + _options.HistoryDbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT DISTINCT
            e.EventId,
            e.EventType,
            e.Title,
            e.Summary,
            e.PlaceText,
            e.GameTime,
            e.CreatedRealAt
        FROM WorldEvents e
        WHERE EXISTS
        (
            SELECT 1
            FROM EventParticipants p
            WHERE p.EventId = e.EventId
              AND p.CharacterId = $npcId
        )
        OR EXISTS
        (
            SELECT 1
            FROM ConversationTurns t
            WHERE t.EventId = e.EventId
              AND t.CharacterId = $npcId
        )
        OR EXISTS
        (
            SELECT 1
            FROM Communications c
            WHERE c.EventId = e.EventId
              AND (c.FromCharacterId = $npcId OR c.ToCharacterId = $npcId)
        )
        OR EXISTS
        (
            SELECT 1
            FROM SceneActions a
            WHERE a.EventId = e.EventId
              AND a.CharacterId = $npcId
        )
        ORDER BY
            CASE WHEN IFNULL(e.GameTime, '') = '' THEN 1 ELSE 0 END,
            e.GameTime DESC,
            e.CreatedRealAt DESC
        LIMIT 250;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcCanonicalHistoryEventOption>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcCanonicalHistoryEventOption
            {
                EventId = ReadString(reader, "EventId"),
                EventType = ReadString(reader, "EventType"),
                Title = ReadString(reader, "Title"),
                Summary = ReadString(reader, "Summary"),
                PlaceText = ReadString(reader, "PlaceText"),
                GameTime = ReadString(reader, "GameTime")
            });
        }

        return Task.FromResult(list);
    }

    public Task SavePersonalMemoryAsync(NpcPersonalMemoryDraft memory)
    {
        if (memory.KnowerCharacterId <= 0 || string.IsNullOrWhiteSpace(memory.MemoryText))
            return Task.CompletedTask;

        memory.Importance = Clamp(memory.Importance);
        memory.Strength = Clamp(memory.Strength);
        memory.Confidence = Clamp(memory.Confidence);

        bool isNew = string.IsNullOrWhiteSpace(memory.Id);
        if (isNew)
            memory.Id = $"memory:{memory.KnowerCharacterId}:{Guid.NewGuid():N}";

        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        if (isNew)
        {
            cmd.CommandText = """
            INSERT INTO PersonalMemories
            (
                Id,
                KnowerCharacterId,
                SubjectCharacterId,
                EventId,
                MemoryType,
                MemoryText,
                Interpretation,
                EmotionalMeaning,
                Importance,
                Strength,
                Confidence,
                IsLockedPeak,
                LearnedGameTime,
                LastUpdatedGameTime,
                CreatedRealAt
            )
            VALUES
            (
                $id,
                $knower,
                $subject,
                $eventId,
                $memoryType,
                $memoryText,
                $interpretation,
                $emotionalMeaning,
                $importance,
                $strength,
                $confidence,
                $lockedPeak,
                '',
                '',
                CURRENT_TIMESTAMP
            );
            """;
        }
        else
        {
            cmd.CommandText = """
            UPDATE PersonalMemories SET
                SubjectCharacterId = $subject,
                EventId = $eventId,
                MemoryType = $memoryType,
                MemoryText = $memoryText,
                Interpretation = $interpretation,
                EmotionalMeaning = $emotionalMeaning,
                Importance = $importance,
                Strength = $strength,
                Confidence = $confidence,
                IsLockedPeak = $lockedPeak
            WHERE Id = $id
              AND KnowerCharacterId = $knower;
            """;
        }

        cmd.Parameters.AddWithValue("$id", memory.Id);
        cmd.Parameters.AddWithValue("$knower", memory.KnowerCharacterId);
        cmd.Parameters.AddWithValue(
            "$subject",
            memory.SubjectCharacterId.HasValue
                ? memory.SubjectCharacterId.Value
                : DBNull.Value);
        cmd.Parameters.AddWithValue("$eventId", memory.EventId ?? "");
        cmd.Parameters.AddWithValue("$memoryType", memory.MemoryType ?? "General");
        cmd.Parameters.AddWithValue("$memoryText", memory.MemoryText ?? "");
        cmd.Parameters.AddWithValue("$interpretation", memory.Interpretation ?? "");
        cmd.Parameters.AddWithValue("$emotionalMeaning", memory.EmotionalMeaning ?? "");
        cmd.Parameters.AddWithValue("$importance", memory.Importance);
        cmd.Parameters.AddWithValue("$strength", memory.Strength);
        cmd.Parameters.AddWithValue("$confidence", memory.Confidence);
        cmd.Parameters.AddWithValue("$lockedPeak", memory.IsLockedPeak ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var mainConn = Open();
        AddRevision(
            mainConn,
            memory.KnowerCharacterId,
            "PersonalMemory",
            isNew ? "Personal memory added" : "Personal memory updated",
            $"{memory.MemoryType}: {memory.MemoryText}");

        return Task.CompletedTask;
    }

    public Task SaveKnowledgeItemAsync(NpcKnowledgeDraft item)
    {
        if (item.KnowerCharacterId <= 0 || string.IsNullOrWhiteSpace(item.WhatTheyKnow))
            return Task.CompletedTask;

        item.Confidence = Clamp(item.Confidence);

        bool isNew = string.IsNullOrWhiteSpace(item.Id);
        if (isNew)
            item.Id = $"knowledge:{item.KnowerCharacterId}:{Guid.NewGuid():N}";

        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        if (isNew)
        {
            cmd.CommandText = """
            INSERT INTO KnowledgeItems
            (
                Id,
                KnowerCharacterId,
                SubjectCharacterId,
                EventId,
                KnowledgeType,
                WhatTheyKnow,
                HowTheyLearnedIt,
                SourceCharacterId,
                Confidence,
                IsRumor,
                IsSecret,
                IsFalseBelief,
                LearnedGameTime,
                LastUpdatedGameTime,
                CreatedRealAt
            )
            VALUES
            (
                $id,
                $knower,
                $subject,
                $eventId,
                $knowledgeType,
                $whatTheyKnow,
                $howTheyLearnedIt,
                $sourceCharacterId,
                $confidence,
                $rumor,
                $secret,
                $falseBelief,
                '',
                '',
                CURRENT_TIMESTAMP
            );
            """;
        }
        else
        {
            cmd.CommandText = """
            UPDATE KnowledgeItems SET
                SubjectCharacterId = $subject,
                EventId = $eventId,
                KnowledgeType = $knowledgeType,
                WhatTheyKnow = $whatTheyKnow,
                HowTheyLearnedIt = $howTheyLearnedIt,
                SourceCharacterId = $sourceCharacterId,
                Confidence = $confidence,
                IsRumor = $rumor,
                IsSecret = $secret,
                IsFalseBelief = $falseBelief
            WHERE Id = $id
              AND KnowerCharacterId = $knower;
            """;
        }

        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$knower", item.KnowerCharacterId);
        cmd.Parameters.AddWithValue(
            "$subject",
            item.SubjectCharacterId.HasValue
                ? item.SubjectCharacterId.Value
                : DBNull.Value);
        cmd.Parameters.AddWithValue("$eventId", item.EventId ?? "");
        cmd.Parameters.AddWithValue("$knowledgeType", item.KnowledgeType ?? "Knowledge");
        cmd.Parameters.AddWithValue("$whatTheyKnow", item.WhatTheyKnow ?? "");
        cmd.Parameters.AddWithValue("$howTheyLearnedIt", item.HowTheyLearnedIt ?? "");
        cmd.Parameters.AddWithValue(
            "$sourceCharacterId",
            item.SourceCharacterId.HasValue
                ? item.SourceCharacterId.Value
                : DBNull.Value);
        cmd.Parameters.AddWithValue("$confidence", item.Confidence);
        cmd.Parameters.AddWithValue("$rumor", item.IsRumor ? 1 : 0);
        cmd.Parameters.AddWithValue("$secret", item.IsSecret ? 1 : 0);
        cmd.Parameters.AddWithValue("$falseBelief", item.IsFalseBelief ? 1 : 0);
        cmd.ExecuteNonQuery();

        using var mainConn = Open();
        AddRevision(
            mainConn,
            item.KnowerCharacterId,
            "KnowledgeItem",
            isNew ? "Knowledge / belief added" : "Knowledge / belief updated",
            $"{item.KnowledgeType}: {item.WhatTheyKnow}");

        return Task.CompletedTask;
    }

    public Task<List<NpcEmotionTrigger>> GetEmotionTriggersAsync(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT * FROM NpcEmotionTriggers
        WHERE NpcId = $npcId
        ORDER BY Emotion, ABS(Impact) DESC, TriggerText;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        using var reader = cmd.ExecuteReader();
        var list = new List<NpcEmotionTrigger>();
        while (reader.Read())
        {
            list.Add(new NpcEmotionTrigger
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                Emotion = ReadString(reader, "Emotion"),
                TriggerText = ReadString(reader, "TriggerText"),
                Impact = ReadInt(reader, "Impact"),
                Reason = ReadString(reader, "Reason"),
                CalmedBy = ReadString(reader, "CalmedBy"),
                MadeWorseBy = ReadString(reader, "MadeWorseBy"),
                IsActive = ReadBool(reader, "IsActive")
            });
        }
        return Task.FromResult(list);
    }

    public Task SaveEmotionTriggerAsync(NpcEmotionTrigger trigger)
    {
        using var conn = Open();
        if (string.IsNullOrWhiteSpace(trigger.Id)) trigger.Id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO NpcEmotionTriggers
            (Id, NpcId, Emotion, TriggerText, Impact, Reason, CalmedBy, MadeWorseBy, IsActive, CreatedRealAt)
        VALUES
            ($id, $npcId, $emotion, $triggerText, $impact, $reason, $calmedBy, $madeWorseBy, $active, CURRENT_TIMESTAMP)
        ON CONFLICT(Id) DO UPDATE SET
            Emotion = $emotion,
            TriggerText = $triggerText,
            Impact = $impact,
            Reason = $reason,
            CalmedBy = $calmedBy,
            MadeWorseBy = $madeWorseBy,
            IsActive = $active;
        """;
        cmd.Parameters.AddWithValue("$id", trigger.Id);
        cmd.Parameters.AddWithValue("$npcId", trigger.NpcId);
        cmd.Parameters.AddWithValue("$emotion", trigger.Emotion ?? "");
        cmd.Parameters.AddWithValue("$triggerText", trigger.TriggerText ?? "");
        cmd.Parameters.AddWithValue("$impact", Math.Clamp(trigger.Impact, -100, 100));
        cmd.Parameters.AddWithValue("$reason", trigger.Reason ?? "");
        cmd.Parameters.AddWithValue("$calmedBy", trigger.CalmedBy ?? "");
        cmd.Parameters.AddWithValue("$madeWorseBy", trigger.MadeWorseBy ?? "");
        cmd.Parameters.AddWithValue("$active", trigger.IsActive ? 1 : 0);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteEmotionTriggerAsync(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NpcEmotionTriggers WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id ?? "");
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
