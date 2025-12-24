namespace pixo_api.Configuration;

public class AwsSettings
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public int PresignedUrlExpirationMinutes { get; set; } = 10;
}
