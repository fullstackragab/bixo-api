using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using pixo_api.Configuration;
using pixo_api.Services.Interfaces;

namespace pixo_api.Services;

public class S3StorageService : IS3StorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly AwsSettings _settings;

    public S3StorageService(IOptions<AwsSettings> settings)
    {
        _settings = settings.Value;
        _s3Client = new AmazonS3Client(
            _settings.AccessKeyId,
            _settings.SecretAccessKey,
            RegionEndpoint.GetBySystemName(_settings.Region)
        );
    }

    public async Task<string> GeneratePresignedUploadUrlAsync(string key, string contentType)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(_settings.PresignedUrlExpirationMinutes),
            ContentType = contentType
        };

        return await Task.FromResult(_s3Client.GetPreSignedURL(request));
    }

    public async Task<string> GeneratePresignedDownloadUrlAsync(string key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_settings.PresignedUrlExpirationMinutes)
        };

        return await Task.FromResult(_s3Client.GetPreSignedURL(request));
    }

    public async Task<string> UploadFileAsync(string key, Stream stream, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request);
        return key;
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        var response = await _s3Client.GetObjectAsync(_settings.BucketName, key);
        return response.ResponseStream;
    }

    public async Task DeleteFileAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(_settings.BucketName, key);
    }
}
