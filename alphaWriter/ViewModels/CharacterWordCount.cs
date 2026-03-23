namespace alphaWriter.ViewModels
{
    public class CharacterWordCount
    {
        public string CharacterName { get; set; } = string.Empty;
        public int WordCount { get; set; }
        public int SceneCount { get; set; }
        public string Stats => $"{WordCount:N0} words · {SceneCount} scene{(SceneCount == 1 ? "" : "s")}";
    }
}
