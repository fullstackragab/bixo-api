using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using pixo_api.Configuration;
using pixo_api.Models.DTOs.Candidate;
using pixo_api.Models.Enums;
using pixo_api.Services.Interfaces;

namespace pixo_api.Services;

public class CvParsingService : ICvParsingService
{
    private readonly OpenAISettings _settings;
    private readonly ILogger<CvParsingService> _logger;

    public CvParsingService(IOptions<OpenAISettings> settings, ILogger<CvParsingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CvParseResult> ParseCvAsync(Stream fileStream, string fileName)
    {
        try
        {
            var text = await ExtractTextAsync(fileStream, fileName);

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("No text could be extracted from CV: {FileName}", fileName);
                return new CvParseResult();
            }

            return await ParseWithOpenAIAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing CV: {FileName}", fileName);
            return new CvParseResult();
        }
    }

    private async Task<string> ExtractTextAsync(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension == ".pdf")
        {
            return await ExtractTextFromPdfAsync(fileStream);
        }

        // For now, treat other files as plain text
        using var reader = new StreamReader(fileStream);
        return await reader.ReadToEndAsync();
    }

    private async Task<string> ExtractTextFromPdfAsync(Stream fileStream)
    {
        // For PDF files, we'll read the raw bytes and attempt to extract text
        // In production, you might want to use a more robust PDF library
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        var bytes = memoryStream.ToArray();

        // Simple approach: try to find text content in PDF
        // This is a basic implementation - for production, consider using iTextSharp or similar
        var content = Encoding.UTF8.GetString(bytes);

        // Extract text between stream/endstream markers (simplified PDF text extraction)
        var sb = new StringBuilder();
        var textMarkers = new[] { "(", ")", "Tj", "TJ", "/T" };

        // For now, return the raw content that's readable
        // OpenAI can handle some garbled text and still extract meaning
        foreach (var c in content)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                sb.Append(c);
            }
            else if (c == '\0')
            {
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    private async Task<CvParseResult> ParseWithOpenAIAsync(string cvText)
    {
        var client = new ChatClient(_settings.Model, _settings.ApiKey);

        var systemPrompt = @"You are a CV/resume parser. Extract structured information from the CV text provided.
Return a JSON object with the following structure:
{
    ""firstName"": ""string or null"",
    ""lastName"": ""string or null"",
    ""email"": ""string or null"",
    ""phone"": ""string or null"",
    ""summary"": ""brief professional summary, max 200 chars"",
    ""seniorityEstimate"": ""Junior|Mid|Senior|Lead|Principal"",
    ""yearsOfExperience"": number or null,
    ""skills"": [
        {
            ""name"": ""skill name"",
            ""confidenceScore"": 0.0 to 1.0,
            ""category"": ""Language|Framework|Tool|Database|Cloud|Other""
        }
    ],
    ""roleTypes"": [""Developer"", ""DevOps"", etc]
}

For skills:
- Extract programming languages, frameworks, tools, databases, cloud platforms
- Assign confidence scores based on how prominently the skill appears
- Categorize appropriately

For seniority:
- Junior: 0-2 years or entry-level language
- Mid: 2-5 years or mid-level responsibilities
- Senior: 5-8 years or senior/lead language
- Lead: 8+ years or team lead roles
- Principal: 10+ years or architect/principal titles

Only return valid JSON, no markdown or extra text.";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage($"Parse this CV:\n\n{cvText.Substring(0, Math.Min(cvText.Length, 8000))}")
        };

        var completion = await client.CompleteChatAsync(messages);
        var responseText = completion.Value.Content[0].Text;

        try
        {
            // Clean up response if it has markdown
            responseText = responseText.Trim();
            if (responseText.StartsWith("```json"))
            {
                responseText = responseText.Substring(7);
            }
            if (responseText.StartsWith("```"))
            {
                responseText = responseText.Substring(3);
            }
            if (responseText.EndsWith("```"))
            {
                responseText = responseText.Substring(0, responseText.Length - 3);
            }

            var parsed = JsonSerializer.Deserialize<OpenAICvResponse>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed == null) return new CvParseResult();

            return new CvParseResult
            {
                FirstName = parsed.FirstName,
                LastName = parsed.LastName,
                Email = parsed.Email,
                Phone = parsed.Phone,
                Summary = parsed.Summary,
                SeniorityEstimate = ParseSeniority(parsed.SeniorityEstimate),
                YearsOfExperience = parsed.YearsOfExperience,
                Skills = parsed.Skills?.Select(s => new ParsedSkill
                {
                    Name = s.Name ?? string.Empty,
                    ConfidenceScore = s.ConfidenceScore,
                    Category = ParseCategory(s.Category)
                }).ToList() ?? new List<ParsedSkill>(),
                RoleTypes = parsed.RoleTypes ?? new List<string>(),
                RawJson = responseText
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response as JSON: {Response}", responseText);
            return new CvParseResult { RawJson = responseText };
        }
    }

    private SeniorityLevel? ParseSeniority(string? seniority)
    {
        if (string.IsNullOrEmpty(seniority)) return null;

        return seniority.ToLower() switch
        {
            "junior" => SeniorityLevel.Junior,
            "mid" => SeniorityLevel.Mid,
            "senior" => SeniorityLevel.Senior,
            "lead" => SeniorityLevel.Lead,
            "principal" => SeniorityLevel.Principal,
            _ => null
        };
    }

    private SkillCategory ParseCategory(string? category)
    {
        if (string.IsNullOrEmpty(category)) return SkillCategory.Other;

        return category.ToLower() switch
        {
            "language" => SkillCategory.Language,
            "framework" => SkillCategory.Framework,
            "tool" => SkillCategory.Tool,
            "database" => SkillCategory.Database,
            "cloud" => SkillCategory.Cloud,
            _ => SkillCategory.Other
        };
    }

    private class OpenAICvResponse
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Summary { get; set; }
        public string? SeniorityEstimate { get; set; }
        public int? YearsOfExperience { get; set; }
        public List<OpenAISkill>? Skills { get; set; }
        public List<string>? RoleTypes { get; set; }
    }

    private class OpenAISkill
    {
        public string? Name { get; set; }
        public double ConfidenceScore { get; set; }
        public string? Category { get; set; }
    }
}
