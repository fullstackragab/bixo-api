namespace bixo_api.Services.Interfaces;

public interface IS3StorageService
{
    Task<string> GeneratePresignedUploadUrlAsync(string key, string contentType);
    Task<string> GeneratePresignedDownloadUrlAsync(string key);
    Task<string> UploadFileAsync(string key, Stream stream, string contentType);
    Task<Stream> DownloadFileAsync(string key);
    Task DeleteFileAsync(string key);
}
