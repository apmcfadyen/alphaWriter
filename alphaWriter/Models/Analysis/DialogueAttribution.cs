namespace alphaWriter.Models.Analysis
{
    public class DialogueAttribution
    {
        public string CharacterId { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public string DialogueText { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        public int SentenceIndex { get; set; }
    }
}
