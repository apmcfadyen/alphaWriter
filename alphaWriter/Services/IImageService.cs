namespace alphaWriter.Services
{
    public interface IImageService
    {
        Task<string> PickAndSaveImageAsync(string bookId);
        string GetFullImagePath(string relativePath);
        void DeleteImage(string relativePath);
    }
}
