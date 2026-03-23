namespace alphaWriter.ViewModels
{
    public class EntityPreview
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;       // "character" | "location" | "item"
        public string TypeLabel { get; set; } = string.Empty;  // "Character" | "Location" | "Item"
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;  // full filesystem path or empty
        public bool HasImage => !string.IsNullOrEmpty(ImagePath);
        public bool HasFullName => !string.IsNullOrEmpty(FullName);
        public bool HasDescription => !string.IsNullOrEmpty(Description);
        public bool HasNotes => !string.IsNullOrEmpty(Notes);
    }
}
