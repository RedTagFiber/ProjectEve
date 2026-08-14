using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Conversations
{
    public static class ConversationSummaryEngine
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
        public const string Model = "eve-thought";

        public static async Task<ConversationSummaryResult> SummarizeAsync(
            ConversationSessionRow session,
            string exactTranscript,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(exactTranscript))
                return ConversationSummaryResult.Empty("Conversation ended without messages.");

            string prompt = $$"""
SYSTEM ROLE
You convert a FINISHED ProjectEve conversation transcript into a factual event summary.

This is archival processing, not roleplay.
Do not continue the conversation.
Do not invent anything that was not in the transcript.

NPC: {{session.NpcName}} (NpcId {{session.NpcId}})
PLAYER: {{session.PlayerName}}
CHANNEL: {{session.Channel}}
LOCATION: {{session.Location}}
STARTED: {{session.StartedGameTime:O}}

STRICT TRUTH RULES
- Exact transcript is authoritative for what was actually said.
- Speakers may lie or be mistaken; a statement is not automatically world truth.
- Direct player self-disclosure may become a learned player fact.
- Direct NPC self-disclosure may become a learned NPC fact.
- Never invent age, biography, romance history, family facts, crimes, medical facts, or sexual history.
- Plans require actual stated intent/agreement.
- If no supported facts/plans exist, return empty arrays.
- EmotionalOutcome describes how the conversation ended, not hidden truth.
- RelationshipOutcome must be modest; one section does not automatically create love/trust.
- Summary must be 2-5 sentences.

EXACT TRANSCRIPT
{{exactTranscript}}

OUTPUT ONLY VALID JSON:
{
  "summary":"...",
  "emotionalOutcome":"...",
  "relationshipOutcome":"...",
  "facts":[
    {
      "subject":"player|npc|other:name",
      "factKey":"name|age|job|family|preference|other",
      "factValue":"...",
      "confidence":100,
      "sourceType":"direct_player_disclosure|direct_npc_disclosure|claim|observation"
    }
  ],
  "plans":[
    {
      "description":"...",
      "timeText":"...",
      "location":"...",
      "status":"agreed|planned|pending"
    }
  ]
}
""";

            try
            {
                var request = new
                {
                    model = Model,
                    stream = false,
                    think = false,
                    messages = new[]
                    {
                        new { role="system", content="Archive ProjectEve conversations. Extract only supported event summary, facts, and plans. Output JSON only." },
                        new { role="user", content=prompt }
                    },
                    options = new
                    {
                        temperature = 0.15,
                        top_p = 0.85,
                        num_predict = 700,
                        repeat_penalty = 1.05
                    }
                };

                string json = JsonSerializer.Serialize(request);
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync("http://localhost:11434/api/chat", body, cancellationToken);
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);
                string raw = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
                string clean = ExtractJsonObject(raw);

                var parsed = JsonSerializer.Deserialize<ConversationSummaryResult>(
                    clean,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Summary))
                    return Fallback(exactTranscript);

                parsed.Facts ??= new();
                parsed.Plans ??= new();

                foreach (var fact in parsed.Facts)
                    fact.Confidence = Math.Clamp(fact.Confidence, 0, 100);

                return parsed;
            }
            catch
            {
                return Fallback(exactTranscript);
            }
        }

        private static ConversationSummaryResult Fallback(string transcript)
        {
            string preview = (transcript ?? "").Replace('\r',' ').Replace('\n',' ').Trim();
            if (preview.Length > 500) preview = preview[..500] + "…";

            return new ConversationSummaryResult
            {
                Summary = "Conversation archived, but automatic summarization failed. Use exact transcript for authoritative details. Preview: " + preview,
                EmotionalOutcome = "unknown",
                RelationshipOutcome = "unknown"
            };
        }

        private static string ExtractJsonObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";
            string s = raw.Trim();

            if (s.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNl = s.IndexOf('\n');
                int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNl >= 0 && lastFence > firstNl)
                    s = s[(firstNl + 1)..lastFence].Trim();
            }

            int start = s.IndexOf('{');
            int end = s.LastIndexOf('}');
            return start >= 0 && end > start ? s[start..(end + 1)] : s;
        }
    }

    public sealed class ConversationSummaryResult
    {
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("emotionalOutcome")] public string EmotionalOutcome { get; set; } = "";
        [JsonPropertyName("relationshipOutcome")] public string RelationshipOutcome { get; set; } = "";
        [JsonPropertyName("facts")] public List<ConversationFactCandidate> Facts { get; set; } = new();
        [JsonPropertyName("plans")] public List<ConversationPlanCandidate> Plans { get; set; } = new();

        public static ConversationSummaryResult Empty(string summary)
            => new() { Summary=summary, EmotionalOutcome="none", RelationshipOutcome="none" };
    }

    public sealed class ConversationFactCandidate
    {
        [JsonPropertyName("subject")] public string Subject { get; set; } = "";
        [JsonPropertyName("factKey")] public string FactKey { get; set; } = "";
        [JsonPropertyName("factValue")] public string FactValue { get; set; } = "";
        [JsonPropertyName("confidence")] public int Confidence { get; set; } = 100;
        [JsonPropertyName("sourceType")] public string SourceType { get; set; } = "conversation";
    }

    public sealed class ConversationPlanCandidate
    {
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("timeText")] public string TimeText { get; set; } = "";
        [JsonPropertyName("location")] public string Location { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "planned";
    }
}
