namespace bixo_api.Configuration;

public class EmailSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Bixo";
    public string SupportInboxEmail { get; set; } = string.Empty;
    public string DataResidency { get; set; } = "eu"; // "eu" or "global"
}
