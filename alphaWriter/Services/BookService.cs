using alphaWriter.Models;
using System.Text.Json;

namespace alphaWriter.Services
{
    public class BookService : IBookService
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public BookService()
        {
            _filePath = Path.Combine(FileSystem.AppDataDirectory, "books.json");
        }

        public async Task<List<Book>> LoadBooksAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return [];

                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<Book>>(json, _jsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public async Task SaveBooksAsync(List<Book> books)
        {
            var tmpPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(books, _jsonOptions);
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
    }
}
