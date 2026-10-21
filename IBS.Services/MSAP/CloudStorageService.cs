using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using IBS.Utility.MSAP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBS.Services.MSAP
{
    public interface ICloudStorageService
    {
        Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30);

        Task<string> UploadFileAsync(IFormFile fileToUpload, string fileNameToSave);

        Task DeleteFileAsync(string fileNameToDelete);

        Task<Stream> DownloadFileAsync(string fileNameToDownload);

        Task<IFormFile?> GetFileAsFormFile(string fileName);
    }

    public class CloudStorageService(IOptions<GCSConfigOptions> options, ILogger<CloudStorageService> logger)
        : ICloudStorageService
    {
        private readonly GCSConfigOptions _options = options.Value;
        private GoogleCredential? _googleCredential;
        private StorageClient? _storageClient;
        private readonly Lock _lock = new();

        private void EnsureInitialized()
        {
            if (_storageClient != null)
            {
                return;
            }

            lock (_lock)
            {
                if (_storageClient != null)
                {
                    return;
                }

                try
                {
                    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                    if (environment == Environments.Production)
                    {
                        _googleCredential = GoogleCredential.GetApplicationDefault();
                    }
                    else
                    {
                        logger.LogInformation("Environment: {Environment}, Auth File: {OptionsGcpStorageAuthFile}", environment, _options.GCPStorageAuthFile);

                        if (!File.Exists(_options.GCPStorageAuthFile))
                        {
                            throw new FileNotFoundException($"Auth file not found: {_options.GCPStorageAuthFile}");
                        }

                        using var stream = File.OpenRead(_options.GCPStorageAuthFile);
                        var serviceAccountCredential = CredentialFactory.FromStream<ServiceAccountCredential>(stream);
                        _googleCredential = serviceAccountCredential.ToGoogleCredential();
                    }

                    _storageClient = StorageClient.Create(_googleCredential);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to initialize Google Cloud Storage client: {ExMessage}", ex.Message);
                    throw;
                }
            }
        }

        public async Task DeleteFileAsync(string fileNameToDelete)
        {
            fileNameToDelete = $"msap/{fileNameToDelete}";
            EnsureInitialized();
            try
            {
                await _storageClient!.DeleteObjectAsync(_options.GoogleCloudStorageBucketName, fileNameToDelete);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while deleting file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public async Task<string> GetSignedUrlAsync(string fileNameToRead, int timeOutInMinutes = 30)
        {
            fileNameToRead = $"msap/{fileNameToRead}";
            EnsureInitialized();
            try
            {
                var bucketName = _options.GoogleCloudStorageBucketName;
                var urlSigner = UrlSigner.FromCredential(_googleCredential!);

                var signedUrl = await urlSigner.SignAsync(bucketName, fileNameToRead, TimeSpan.FromMinutes(timeOutInMinutes));

                logger.LogInformation("Signed URL obtained for file '{FileNameToRead}'", fileNameToRead);
                return signedUrl;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while obtaining signed URL for file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public async Task<string> UploadFileAsync(IFormFile? fileToUpload, string fileNameToSave)
        {
            fileNameToSave = $"msap/{fileNameToSave}";
            EnsureInitialized();
            if (fileToUpload == null || fileToUpload.Length == 0)
            {
                logger.LogError("File upload failed: No file provided or file is empty");
                throw new ArgumentException("File is either null or empty.", nameof(fileToUpload));
            }

            try
            {
                using var memoryStream = new MemoryStream();
                await fileToUpload.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // Reset stream position after copying

                var uploadedFile = await _storageClient!.UploadObjectAsync(
                    _options.GoogleCloudStorageBucketName,
                    fileNameToSave,
                    fileToUpload.ContentType,
                    memoryStream
                );
                return uploadedFile.MediaLink;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while uploading file: {ExMessage}", ex.Message);
                throw;
            }
        }

        public async Task<Stream> DownloadFileAsync(string fileNameToDownload)
        {
            fileNameToDownload = $"msap/{fileNameToDownload}";
            EnsureInitialized();
            try
            {
                var memoryStream = new MemoryStream();
                await _storageClient!.DownloadObjectAsync(_options.GoogleCloudStorageBucketName, fileNameToDownload, memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin); // Reset stream position to the beginning for reading
                logger.LogInformation("File {FileNameToDownload} downloaded successfully", fileNameToDownload);
                return memoryStream;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while downloading file: {ExMessage}", ex.Message);
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
                logger.LogError(ex, "Error processing file: {ExMessage}", ex.Message);
                throw;
            }
        }
    }
}
