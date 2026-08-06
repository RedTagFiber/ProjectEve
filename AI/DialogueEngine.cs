using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ProjectEve.AI.Brain
{
    public static class DialogueEngine
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private const string DefaultModel = "dolphin-eve";

        public static string GenerateReply(string prompt)
        {
            return GenerateReplyAsync(prompt).GetAwaiter().GetResult();
        }

        public static async Task<string> GenerateReplyAsync(string prompt)
        {
            bool inPerson = prompt.Contains("[IN PERSON", StringComparison.OrdinalIgnoreCase);

            string systemPrompt = inPerson
                ? """
You speak as the character defined in the prompt.
In-person rules:
- natural conversation
- optional one short *action*
- then spoken line
- no essays
- do not write the player's lines
- never mention AI, models, or systems
"""
                : """
You speak as the character defined in the prompt.
Texting rules:
- short natural phone texts
- no narration
- no stage directions
- no essays
- personal and direct
- never mention AI, models, or systems
""";

            var requestBody = new
            {
                model = DefaultModel,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt }
                },
                options = new
                {
                    temperature = 0.85,
                    top_p = 0.90,
                    repeat_penalty = 1.12
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync("http://localhost:11434/api/chat", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                var reply = doc.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return string.IsNullOrWhiteSpace(reply) ? "..." : reply.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine("DialogueEngine error: " + ex.Message);
                return "...";
            }
        }
    }
}