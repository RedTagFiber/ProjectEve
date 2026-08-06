// SceneHistory.cs
using System.Collections.Generic;

namespace ProjectEve.Narrative.Scenes
{
    public class SceneHistory
    {
        private readonly List<string> _pastScenes = new();

        public void Add(string sceneText)
        {
            if (!string.IsNullOrWhiteSpace(sceneText))
                _pastScenes.Add(sceneText);
        }

        public IReadOnlyList<string> GetAll() => _pastScenes.AsReadOnly();

        public string? Last()
            => _pastScenes.Count == 0 ? null : _pastScenes[^1];
    }
}