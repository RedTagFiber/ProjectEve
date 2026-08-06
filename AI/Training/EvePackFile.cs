using System.Text.Json;

public class EvePackFile
{
    public bool Enabled { get; set; } = true;
    public string Notes { get; set; } = "";
    public List<EveExample> Examples { get; set; } = new();
}

public class EveExample
{
    public List<EveMessage> Messages { get; set; } = new();
}

public class EveMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public static class EvePackLoader
{
    public static List<EveExample> LoadAll(string packsFolder)
    {
        var all = new List<EveExample>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.GetFiles(packsFolder, "*.json"))
        {
            var json = File.ReadAllText(file);
            var pack = JsonSerializer.Deserialize<EvePackFile>(json, options);
            if (pack == null || !pack.Enabled) continue;

            all.AddRange(pack.Examples);
            Console.WriteLine($"Loaded {pack.Examples.Count} from {Path.GetFileName(file)}");
        }

        return all;
    }
}