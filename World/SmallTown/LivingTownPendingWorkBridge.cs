using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Consumes pending specialized work created by ProjectEveHumanEventHooks.
    ///
    /// This lets new systems come online without rewriting the core HumanEvent router.
    /// </summary>
    public static class LivingTownPendingWorkBridge
    {
        public static void Initialize()
        {
            WorldActivityEngine.Initialize();
            SocialEncounterEngine.Initialize();
            GossipEngine.Initialize();
            CrimePoliceEngine.Initialize();
            HealthSystem.Initialize();
        }

        public static BridgePassResult Process(DateTime gameTime, int limitPerSystem = 100)
        {
            Initialize();
            var result = new BridgePassResult();

            ProcessLaw(gameTime, limitPerSystem, result);
            ProcessHealth(gameTime, limitPerSystem, result);
            ProcessGossip(gameTime, limitPerSystem, result);

            return result;
        }

        private static void ProcessLaw(DateTime gameTime, int limit, BridgePassResult result)
        {
            foreach (var work in ProjectEveHumanEventHooks.GetPendingHookWork("law", limit))
            {
                var facts = ParseFacts(work.PayloadJson);

                int severity = ReadInt(facts, "severity", 5);
                string location = Read(facts, "LocationId", "");
                string description = Read(facts, "LegalFact", work.EventId);

                long crimeId = CrimePoliceEngine.RecordCrime(
                    work.EventId,
                    work.ActorNpcId,
                    work.TargetNpcId,
                    location,
                    work.GameTime == DateTime.MinValue ? gameTime : work.GameTime,
                    severity,
                    description);

                ProjectEveHumanEventHooks.MarkHookWorkHandled(work.Id);
                result.LawProcessed++;
                result.CreatedCrimeIds.Add(crimeId);
            }
        }

        private static void ProcessHealth(DateTime gameTime, int limit, BridgePassResult result)
        {
            foreach (var work in ProjectEveHumanEventHooks.GetPendingHookWork("health", limit))
            {
                var facts = ParseFacts(work.PayloadJson);

                // If TargetNpcId exists for an attack/fight consequence, the target is
                // normally the person receiving the injury. Otherwise actor is recipient.
                int recipient = work.TargetNpcId ?? work.ActorNpcId;

                string fact = Read(facts, "MedicalFact",
                              Read(facts, "InjurySeverity", work.EventId));

                int severity = ReadInt(facts, "severity", 4);

                long incidentId = HealthSystem.RecordIncident(
                    recipient,
                    work.EventId,
                    severity,
                    fact,
                    work.GameTime == DateTime.MinValue ? gameTime : work.GameTime);

                ProjectEveHumanEventHooks.MarkHookWorkHandled(work.Id);
                result.HealthProcessed++;
                result.CreatedHealthIncidentIds.Add(incidentId);
            }
        }

        private static void ProcessGossip(DateTime gameTime, int limit, BridgePassResult result)
        {
            foreach (var work in ProjectEveHumanEventHooks.GetPendingHookWork("gossip", limit))
            {
                var facts = ParseFacts(work.PayloadJson);

                string text = Read(facts, "rumorText", "");
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Do not fabricate the content of a rumor.
                    // Leave it pending until the event supplies a real fact.
                    continue;
                }

                int? subject = work.TargetNpcId;
                int secretLevel = ReadInt(facts, "secretLevel", 25);

                long rumorId = GossipEngine.CreateRumor(
                    work.ActorNpcId,
                    subject,
                    text,
                    work.GameTime == DateTime.MinValue ? gameTime : work.GameTime,
                    secretLevel);

                ProjectEveHumanEventHooks.MarkHookWorkHandled(work.Id);
                result.GossipProcessed++;
                result.CreatedRumorIds.Add(rumorId);
            }
        }

        private static Dictionary<string, string> ParseFacts(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(json))
                return result;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return result;

                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.NameEquals("Facts") && p.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var f in p.Value.EnumerateObject())
                            result[f.Name] = f.Value.ToString();
                    }
                    else
                    {
                        result[p.Name] = p.Value.ToString();
                    }
                }
            }
            catch { }

            return result;
        }

        private static string Read(
            Dictionary<string, string> facts,
            string key,
            string fallback)
            => facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : fallback;

        private static int ReadInt(
            Dictionary<string, string> facts,
            string key,
            int fallback)
            => facts.TryGetValue(key, out var value) && int.TryParse(value, out int n)
                ? n : fallback;

        public sealed class BridgePassResult
        {
            public int LawProcessed { get; set; }
            public int HealthProcessed { get; set; }
            public int GossipProcessed { get; set; }

            public List<long> CreatedCrimeIds { get; } = new();
            public List<long> CreatedHealthIncidentIds { get; } = new();
            public List<long> CreatedRumorIds { get; } = new();
        }
    }
}
