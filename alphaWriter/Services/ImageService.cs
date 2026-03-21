namespace alphaWriter.Services
{
    public class ImageService : IImageService
    {
        public async Task<string> PickAndSaveImageAsync(string bookId)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select an image",
                FileTypes = FilePickerFileType.Images
            });

            if (result is null)
                return string.Empty;

            var dir = Path.Combine(FileSystem.AppDataDirectory, "images", bookId);
            Directory.CreateDirectory(dir);

            var ext = Path.GetExtension(result.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var destPath = Path.Combine(dir, fileName);

            using var source = await result.OpenReadAsync();
            using var dest = File.Create(destPath);
            await source.CopyToAsync(dest);

            return Path.Combine("images", bookId, fileName);
        }

        public string GetFullImagePath(string relativePath)
            => Path.Combine(FileSystem.AppDataDirectory, relativePath);

        public void DeleteImage(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            var fullPath = GetFullImagePath(relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }
}
