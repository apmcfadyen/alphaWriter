using alphaWriter.Models;

namespace alphaWriter.Services
{
    public interface IBookService
    {
        Task<List<Book>> LoadBooksAsync();
        Task SaveBooksAsync(List<Book> books);

        /// <summary>
        /// Runs <paramref name="work"/> (a sync) while saves wait, then, if it changed files on
        /// this device, reloads and returns the library; otherwise returns null.
        /// </summary>
        Task<List<Book>?> RunExclusiveAsync(Func<Task<bool>> work);
    }
}
