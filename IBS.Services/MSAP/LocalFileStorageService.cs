using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IBS.Services.MSAP
{
    public class LocalFileStorageService : ICloudStorageService
    {
        private readonly ILogger<LocalFileStorageService> _logger;
        private readonly string _storagePath;

        private string GetFilePath(string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            string relativePath = string.Empty;
            foreach (string segment in fileName.Replace('\\', '/').Split('/'))
            {
                if (segment is "" or "." or ".." || segment.Contains(':')
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || segment.Any(char.IsControl))
                {
                    throw new ArgumentException("The file must use a relative path within MSAP storage.", nameof(fileName));
                }

                relativePath = Path.Combine(relativePath, Path.GetFileName(segment));
            }

            var storageRoot = Path.GetFullPath(_storagePath);
            if (!Path.EndsInDirectorySeparator(storageRoot))
            {
                storageRoot += Path.DirectorySeparatorChar;
            }
            var path = Path.GetFullPath(relativePath, storageRoot);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(storageRoot, comparison))
            {
                throw new ArgumentException("The file must stay within MSAP storage.", nameof(fileName));
            }

            return path;
        }

        public LocalFileStorageService(
            ILogger<LocalFileStorageService> logger,
            IWebHostEnvironment environment,
            IConfiguration configuration)
        {
            _logger = logger;

            // Use a local folder within the project for development storage
            // Cross-platform: works on Windows and Linux
            var localStoragePathConfig = configuration["MSAP:LocalStoragePath"] ?? "App_Data/MSAP/LocalStorage";
            _storagePath = Path.IsPathRooted(localStoragePathConfig)
                ? localStoragePathConfig
                : Path.Combine(environment.ContentRootPath, localStoragePathConfig);

            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }

            _logger.LogInformation("╔════════════════════════════════════════════════════════╗");
            _logger.LogInformation("║  LOCAL STORAGE MODE (Development)                      ║");
            _logger.LogInformation("║  Storage Path: {StoragePath}                           ║", _storagePath.PadRight(43));
            _logger.LogInformation("║  Works on: Windows & Linux                             ║");
            _logger.LogInformation("║  No GCP credentials required                           ║");
            _logger.LogInformation("╚════════════════════════════════════════════════════════╝");
        }

        public Task DeleteFileAsync(string fileNameToDelete)
        {
            try
            {
                var filePath = GetFilePath(fileNameToDelete);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("File deleted: {FilePath}", filePath);
                }
                else
                {
                    _logger.LogWarning("File not found for deletion: {FilePath}", filePath);
                }
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while deleting file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30)
        {
            // For local development, return a direct URL to the file
            var filePath = GetFilePath(fileNameToRead);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", fileNameToRead);
            }

            // Return a URL that can be used to access the file
            // In development, this will be a relative path
            var relativePath = $"/msap-storage/{fileNameToRead.Replace("\\", "/")}";
            _logger.LogInformation("Local URL generated for file '{FileNameToRead}': {RelativePath}", fileNameToRead, relativePath);
            return Task.FromResult(relativePath);
        }

        public async Task<string> UploadFileAsync(IFormFile? fileToUpload, string fileNameToSave)
        {
            if (fileToUpload == null || fileToUpload.Length == 0)
            {
                _logger.LogError("File upload failed: No file provided or file is empty");
                throw new ArgumentException("File is either null or empty.", nameof(fileToUpload));
            }

            try
            {
                var filePath = GetFilePath(fileNameToSave);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await fileToUpload.CopyToAsync(stream);
                }

                _logger.LogInformation("File uploaded successfully: {FilePath}", filePath.Replace("\r", string.Empty).Replace("\n", string.Empty));
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while uploading file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public Task<Stream> DownloadFileAsync(string fileNameToDownload)
        {
            try
            {
                var filePath = GetFilePath(fileNameToDownload);
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}", fileNameToDownload);
                }

                var memoryStream = new MemoryStream();
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fileStream.CopyTo(memoryStream);
                }
                memoryStream.Seek(0, SeekOrigin.Begin);

                _logger.LogInformation("File {FileNameToDownload} downloaded successfully", fileNameToDownload);
                return Task.FromResult<Stream>(memoryStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while downloading file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public async Task<IFormFile?> GetFileAsFormFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            try
            {
                var fileStream = await DownloadFileAsync(fileName);

                if (fileStream == null || fileStream.Length == 0)
                {
                    throw new FileNotFoundException("File not found.", fileName);
                }

                var formFile = new FormFile(fileStream, 0, fileStream.Length, "file", fileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/octet-stream",
                };

                return formFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {ExMessage}", ex.Message);
                throw;
            }
        }
    }
}
