using alphaWriter.Models;

namespace alphaWriter.Services
{
    public interface IBookService
    {
        Task<List<Book>> LoadBooksAsync();
        Task SaveBooksAsync(List<Book> books);
    }
}
