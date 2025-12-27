namespace bixo_api.Configuration;

public class EmailSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Bixo";
    public string SupportInboxEmail { get; set; } = string.Empty;
    public string SalesInboxEmail { get; set; } = string.Empty;
    public string AdminInboxEmail { get; set; } = string.Empty;
    public string RegisterFromEmail { get; set; } = string.Empty;
    public string WelcomeFromEmail { get; set; } = "welcome@bixo.io";
    public string RecommendationsFromEmail { get; set; } = "recommendations@bixo.io";
    public string ShortlistFromEmail { get; set; } = "shortlist@bixo.io";
    public string DataResidency { get; set; } = "eu"; // "eu" or "global"

    /// <summary>Frontend URL for generating deep links in emails</summary>
    public string FrontendUrl { get; set; } = "https://bixo.io";
}
