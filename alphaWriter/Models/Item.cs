namespace alphaWriter.Models
{
    public class Item
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Aka { get; set; } = string.Empty;
    }
}
