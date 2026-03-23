namespace alphaWriter.Models
{
    public class Character
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Aka { get; set; } = string.Empty;
    }
}
