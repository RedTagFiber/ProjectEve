using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-1: core-person canon population for Eve Sinclair (NpcId = 1).
///
/// This intentionally populates only facts already established for Eve.
/// Unknown measurements/details remain NULL or blank instead of being invented.
///
/// Safe to run repeatedly:
/// - singleton/profile tables use UPSERT
/// - habits use stable IDs
/// - trait-control rows are initialized from Eve's existing canonical trait rows
/// </summary>
public static class ProjectEveGoldenNpcCorePopulation
{
    public static void PopulateEve()
    {
        const int npcId = 1;

        using var connection = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");

        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");

        using var transaction = connection.BeginTransaction();

        EnsureEveExists(connection, transaction, npcId);
        PopulateCharacterCanon(connection, transaction, npcId);
        PopulatePhysicalProfile(connection, transaction, npcId);
        PopulateCognitionProfile(connection, transaction, npcId);
        PopulatePersona(connection, transaction, npcId);
        PopulateArchetype(connection, transaction, npcId);
        PopulateTraitControl(connection, transaction, npcId);
        PopulateHabitsAndInterests(connection, transaction, npcId);
        PopulateSocialBehavior(connection, transaction, npcId);

        transaction.Commit();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-1 populated for Eve Sinclair.");
        Console.WriteLine("  Core person/current-character canon");
        Console.WriteLine("  Physical profile");
        Console.WriteLine("  Cognition profile");
        Console.WriteLine("  Persona");
        Console.WriteLine("  Archetype");
        Console.WriteLine("  Trait control");
        Console.WriteLine("  Habits / interests");
        Console.WriteLine("  Social behavior baseline");
        Console.WriteLine();
        Console.WriteLine("No unknown height, weight, IQ, eye color, tattoos, scars,");
        Console.WriteLine("or other unsupported physical facts were invented.");
    }

    private static void EnsureEveExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM Characters
            WHERE Id = $npcId
              AND lower(trim(Name)) = lower('Eve Sinclair');
            """;
        command.Parameters.AddWithValue("$npcId", npcId);

        long count = Convert.ToInt64(command.ExecuteScalar());

        if (count != 1)
            throw new InvalidOperationException(
                "Expected Characters.Id=1 to be Eve Sinclair. Population aborted.");
    }

    private static void PopulateCharacterCanon(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Characters
            SET
                Goal = CASE
                    WHEN trim(Goal) = '' THEN
                        'Build a meaningful life while staying connected to the people and community she cares about.'
                    ELSE Goal
                END,
                Need = CASE
                    WHEN trim(Need) = '' THEN
                        'Trust, emotional safety, belonging, and room to choose her own future.'
                    ELSE Need
                END,
                Fear = CASE
                    WHEN trim(Fear) = '' THEN
                        'Hurting people she loves or losing the trust of people who depend on her.'
                    ELSE Fear
                END,
                Want = CASE
                    WHEN trim(Want) = '' THEN
                        'A life that feels like her own, without giving up the people and place that shaped her.'
                    ELSE Want
                END,
                PersonalityContext = CASE
                    WHEN trim(PersonalityContext) = '' THEN
                        'Kind, intelligent, emotionally strong, observant, and approachable. Eve listens more than she speaks, remembers small details about people, and is someone others naturally trust.'
                    ELSE PersonalityContext
                END,
                BackstoryShort = CASE
                    WHEN trim(BackstoryShort) = '' THEN
                        'Eve Sinclair grew up in Bellefontaine, Ohio, knowing the town, its families, and many of its quiet stories. At 25 she manages the local coffee shop and is deeply woven into everyday community life.'
                    ELSE BackstoryShort
                END,
                PersonalitySummary = CASE
                    WHEN trim(PersonalitySummary) = '' THEN
                        'Warm, capable, perceptive, trustworthy, and quietly private. Her friendly exterior is genuine, but she keeps deeper memories, dreams, and unresolved feelings to herself.'
                    ELSE PersonalitySummary
                END,
                SpeakingStyle = CASE
                    WHEN trim(SpeakingStyle) = '' THEN
                        'Warm, natural, attentive, and understated; she tends to listen first and speak thoughtfully.'
                    ELSE SpeakingStyle
                END,
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE Id = $npcId;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulatePhysicalProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcPhysicalProfiles
            (
                NpcId,
                HeightCm,
                WeightKg,
                BodyType,
                HairColor,
                HairLength,
                HairStyle,
                EyeColor,
                EyeStyle,
                SkinTone,
                FaceShape,
                FacialFeatures,
                DistinctiveFeatures,
                Glasses,
                ScarNotes,
                Tattoos,
                Piercings,
                DefaultClothingStyle,
                DefaultExpression,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                NULL,
                NULL,
                '',
                'Brown',
                'Medium-length',
                'Soft, natural style',
                '',
                'Warm and expressive',
                '',
                '',
                'Natural, approachable features',
                '',
                '',
                '',
                '',
                '',
                'Casual small-town style: cozy sweater or simple blouse, jeans, practical boots, coffee shop apron, minimal jewelry.',
                'Genuine, comforting smile with a thoughtful undertone.',
                'Only established visual canon is populated here. Unknown measurements and physical specifics remain unset.',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                HairColor = excluded.HairColor,
                HairLength = excluded.HairLength,
                HairStyle = excluded.HairStyle,
                EyeStyle = excluded.EyeStyle,
                FacialFeatures = excluded.FacialFeatures,
                DefaultClothingStyle = excluded.DefaultClothingStyle,
                DefaultExpression = excluded.DefaultExpression,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulateCognitionProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcCognitionProfiles
            (
                NpcId,
                IqScore,
                IntelligenceBand,
                EducationLevel,
                LearningStyle,
                ProblemSolvingStyle,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                NULL,
                'Intelligent',
                '',
                'Observant and people-focused; retains small interpersonal details and learns strongly from lived experience.',
                'Patient, attentive, practical, and context-sensitive; she listens before acting.',
                'Canon supports intelligence and strong social observation. No numeric IQ or unsupported education credential is assigned.',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                IntelligenceBand = excluded.IntelligenceBand,
                LearningStyle = excluded.LearningStyle,
                ProblemSolvingStyle = excluded.ProblemSolvingStyle,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulatePersona(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcPersonas
            (
                NpcId,
                Energy,
                PublicPersona,
                PrivatePersona,
                HiddenBehavior,
                ReputationSummary,
                PersonalitySnapshot,
                AiDossierSummary,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                55,
                'Warm, dependable, approachable coffee shop manager who knows the town and makes people feel heard.',
                'Thoughtful and more emotionally guarded than she appears publicly; she carries private memories, dreams, and unresolved feelings.',
                'Keeps deeper personal thoughts to herself even while being emotionally available to others.',
                'Trusted, kind, competent, and attentive; the kind of person people naturally confide in.',
                'Kind, intelligent, emotionally strong, observant, community-rooted, and quietly private.',
                'Eve Sinclair is a 25-year-old Bellefontaine coffee shop manager. She is deeply connected to her community, remembers small details about people, listens more than she speaks, and is widely trusted. Her warmth is real, but parts of her inner life remain deliberately private.',
                'Energy is a moderate behavioral baseline, not a medical or psychological measurement.',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                Energy = excluded.Energy,
                PublicPersona = excluded.PublicPersona,
                PrivatePersona = excluded.PrivatePersona,
                HiddenBehavior = excluded.HiddenBehavior,
                ReputationSummary = excluded.ReputationSummary,
                PersonalitySnapshot = excluded.PersonalitySnapshot,
                AiDossierSummary = excluded.AiDossierSummary,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulateArchetype(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcArchetypes
            (
                NpcId,
                PrimaryType,
                SecondaryType,
                TertiaryType,
                Notes
            )
            VALUES
            (
                $npcId,
                'Confidante',
                'Caretaker',
                'Quiet Seeker',
                'Archetypes summarize established character function: trusted listener, community caretaker, and a young woman with private dreams beyond the role others know.'
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                PrimaryType = excluded.PrimaryType,
                SecondaryType = excluded.SecondaryType,
                TertiaryType = excluded.TertiaryType,
                Notes = excluded.Notes;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulateTraitControl(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        // Eve already has canonical trait rows. We create control rows for those exact
        // traits rather than inventing additional trait names.
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcTraitControl
            (
                NpcId,
                TraitId,
                Control,
                LastUpdatedRealAt
            )
            SELECT
                NpcId,
                TraitId,
                50,
                CURRENT_TIMESTAMP
            FROM NpcTraitValues
            WHERE NpcId = $npcId
              AND IsEnabled = 1
              AND trim(TraitId) <> ''
            ON CONFLICT(NpcId, TraitId) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void PopulateHabitsAndInterests(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        UpsertHabit(
            connection, transaction,
            "eve-interest-coffee-community",
            npcId,
            "Interest",
            "Coffee and coffee-shop culture",
            85,
            1,
            "Her work at Sinclair Coffee is part of her daily identity and community role.");

        UpsertHabit(
            connection, transaction,
            "eve-habit-listens-first",
            npcId,
            "Habit",
            "Listens before speaking",
            90,
            1,
            "A defining interpersonal habit; she gives people room to talk before responding.");

        UpsertHabit(
            connection, transaction,
            "eve-habit-remembers-details",
            npcId,
            "Habit",
            "Remembers small details about people",
            90,
            1,
            "Part of why people in town feel known and comfortable around her.");

        UpsertHabit(
            connection, transaction,
            "eve-interest-community",
            npcId,
            "Interest",
            "Local community and the people of Bellefontaine",
            85,
            1,
            "She grew up knowing the town, its streets, families, routines, and stories.");

        UpsertHabit(
            connection, transaction,
            "eve-habit-private-inner-life",
            npcId,
            "Habit",
            "Keeps deeper feelings private",
            75,
            0,
            "Her warmth is genuine, but she does not readily disclose every memory, dream, or unresolved feeling.");
    }

    private static void UpsertHabit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        int npcId,
        string itemType,
        string name,
        int strength,
        int isPublic,
        string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcHabitsAndInterests
            (
                Id,
                NpcId,
                ItemType,
                Name,
                Strength,
                IsPublic,
                Notes
            )
            VALUES
            (
                $id,
                $npcId,
                $itemType,
                $name,
                $strength,
                $isPublic,
                $notes
            )
            ON CONFLICT(Id) DO UPDATE SET
                NpcId = excluded.NpcId,
                ItemType = excluded.ItemType,
                Name = excluded.Name,
                Strength = excluded.Strength,
                IsPublic = excluded.IsPublic,
                Notes = excluded.Notes;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$npcId", npcId);
        command.Parameters.AddWithValue("$itemType", itemType);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$strength", strength);
        command.Parameters.AddWithValue("$isPublic", isPublic);
        command.Parameters.AddWithValue("$notes", notes);
        command.ExecuteNonQuery();
    }

    private static void PopulateSocialBehavior(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int npcId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcSocialBehavior
            (
                NpcId,
                BookPostScore,
                GramPostScore,
                CommentScore,
                TrollScore,
                LastBookPostGameTime,
                LastGramPostGameTime,
                LastCommentGameTime,
                LastTrollActionGameTime,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                35,
                40,
                60,
                0,
                '',
                '',
                '',
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                BookPostScore = excluded.BookPostScore,
                GramPostScore = excluded.GramPostScore,
                CommentScore = excluded.CommentScore,
                TrollScore = excluded.TrollScore,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", npcId);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
