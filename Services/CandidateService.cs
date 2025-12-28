using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using bixo_api.Data;
using bixo_api.Models.DTOs.Candidate;
using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class CandidateService : ICandidateService
{
    private static readonly Regex GitHubUrlRegex = new(@"^https?://github\.com/[a-zA-Z0-9](?:[a-zA-Z0-9]|-(?=[a-zA-Z0-9])){0,38}/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ValidateGitHubUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        if (url.Length > 500)
            throw new InvalidOperationException("GitHub URL must be 500 characters or less");

        if (!GitHubUrlRegex.IsMatch(url))
            throw new InvalidOperationException("GitHub URL must be a valid GitHub profile URL (e.g., https://github.com/username)");
    }

    private readonly IDbConnectionFactory _db;
    private readonly IS3StorageService _s3Service;
    private readonly ICvParsingService _cvParsingService;
    private readonly IEmailService _emailService;
    private readonly ILogger<CandidateService> _logger;

    public CandidateService(
        IDbConnectionFactory db,
        IS3StorageService s3Service,
        ICvParsingService cvParsingService,
        IEmailService emailService,
        ILogger<CandidateService> logger)
    {
        _db = db;
        _s3Service = s3Service;
        _cvParsingService = cvParsingService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<CandidateProfileResponse?> GetProfileAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.first_name, c.last_name, c.linkedin_url, c.github_url,
                   c.github_summary, c.github_summary_generated_at, c.github_summary_enabled,
                   c.github_summary_requested_at,
                   c.cv_file_key, c.cv_original_file_name,
                   c.desired_role, c.location_preference, c.remote_preference, c.availability,
                   c.open_to_opportunities, c.profile_visible, c.profile_approved_at, c.seniority_estimate, c.created_at,
                   u.email, u.last_active_at,
                   cl.country AS location_country,
                   cl.city AS location_city,
                   cl.timezone AS location_timezone,
                   cl.willing_to_relocate
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN candidate_locations cl ON cl.candidate_id = c.id
            WHERE c.user_id = @UserId",
            new { UserId = userId });

        if (candidate == null) return null;

        var skills = await connection.QueryAsync<dynamic>(@"
            SELECT id, skill_name, confidence_score, category, is_verified, skill_level
            FROM candidate_skills
            WHERE candidate_id = @CandidateId
            ORDER BY skill_level ASC, confidence_score DESC",
            new { CandidateId = (Guid)candidate.id });

        var recommendationsCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM candidate_recommendations WHERE candidate_id = @CandidateId",
            new { CandidateId = (Guid)candidate.id });

        var profileViewsCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM candidate_profile_views WHERE candidate_id = @CandidateId",
            new { CandidateId = (Guid)candidate.id });

        string? cvDownloadUrl = null;
        if (!string.IsNullOrEmpty(candidate.cv_file_key))
        {
            cvDownloadUrl = await _s3Service.GeneratePresignedDownloadUrlAsync((string)candidate.cv_file_key);
        }

        // Build location response if location data exists
        CandidateLocationResponse? locationResponse = null;
        var locationCountry = candidate.location_country as string;
        var locationCity = candidate.location_city as string;
        var locationTimezone = candidate.location_timezone as string;
        var willingToRelocate = candidate.willing_to_relocate as bool? ?? false;

        if (!string.IsNullOrEmpty(locationCountry) || !string.IsNullOrEmpty(locationCity) || !string.IsNullOrEmpty(locationTimezone))
        {
            locationResponse = new CandidateLocationResponse
            {
                Country = locationCountry,
                City = locationCity,
                Timezone = locationTimezone,
                WillingToRelocate = willingToRelocate
            };
        }

        return new CandidateProfileResponse
        {
            Id = (Guid)candidate.id,
            Email = (string)candidate.email,
            FirstName = candidate.first_name as string,
            LastName = candidate.last_name as string,
            LinkedInUrl = candidate.linkedin_url as string,
            GitHubUrl = candidate.github_url as string,
            GitHubSummary = candidate.github_summary as string,
            GitHubSummaryGeneratedAt = candidate.github_summary_generated_at as DateTime?,
            GitHubSummaryEnabled = candidate.github_summary_enabled as bool? ?? false,
            GitHubSummaryRequestedAt = candidate.github_summary_requested_at as DateTime?,
            CvFileName = candidate.cv_original_file_name as string,
            CvDownloadUrl = cvDownloadUrl,
            DesiredRole = candidate.desired_role as string,
            LocationPreference = candidate.location_preference as string,
            Location = locationResponse,
            RemotePreference = candidate.remote_preference != null ? (RemotePreference?)(int)candidate.remote_preference : null,
            Availability = (Availability)(int)candidate.availability,
            OpenToOpportunities = (bool)candidate.open_to_opportunities,
            ProfileVisible = (bool)candidate.profile_visible,
            ProfileApprovedAt = candidate.profile_approved_at as DateTime?,
            SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel?)(int)candidate.seniority_estimate : null,
            Skills = skills.Select(s => new CandidateSkillResponse
            {
                Id = (Guid)s.id,
                SkillName = (string)s.skill_name,
                ConfidenceScore = (double)(decimal)s.confidence_score,
                Category = (SkillCategory)(int)s.category,
                IsVerified = (bool)s.is_verified,
                SkillLevel = (SkillLevel)(int)(s.skill_level ?? 1)
            }).ToList(),
            GroupedSkills = new GroupedSkillsResponse
            {
                Primary = skills
                    .Where(s => (int)(s.skill_level ?? 1) == 0)
                    .Select(s => new CandidateSkillResponse
                    {
                        Id = (Guid)s.id,
                        SkillName = (string)s.skill_name,
                        ConfidenceScore = (double)(decimal)s.confidence_score,
                        Category = (SkillCategory)(int)s.category,
                        IsVerified = (bool)s.is_verified,
                        SkillLevel = SkillLevel.Primary
                    }).ToList(),
                Secondary = skills
                    .Where(s => (int)(s.skill_level ?? 1) == 1)
                    .Select(s => new CandidateSkillResponse
                    {
                        Id = (Guid)s.id,
                        SkillName = (string)s.skill_name,
                        ConfidenceScore = (double)(decimal)s.confidence_score,
                        Category = (SkillCategory)(int)s.category,
                        IsVerified = (bool)s.is_verified,
                        SkillLevel = SkillLevel.Secondary
                    }).ToList()
            },
            // Derived capabilities for presentation (does not affect matching)
            Capabilities = CapabilityMapping.DeriveCapabilities(
                skills.Select(s => (string)s.skill_name)),
            RecommendationsCount = recommendationsCount,
            ProfileViewsCount = profileViewsCount,
            CreatedAt = (DateTime)candidate.created_at,
            LastActiveAt = (DateTime)candidate.last_active_at
        };
    }

    public async Task<CandidateProfileResponse> OnboardAsync(Guid userId, CandidateOnboardRequest request)
    {
        // Validate GitHub URL if provided
        ValidateGitHubUrl(request.GitHubUrl);

        using var connection = _db.CreateConnection();

        // CV is mandatory for Bixo profiles - no CV = no matching = no visibility
        var hasCv = await connection.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM candidates
                WHERE user_id = @UserId AND cv_file_key IS NOT NULL
            )",
            new { UserId = userId });

        if (!hasCv)
        {
            throw new InvalidOperationException("CV is required to create a Bixo profile");
        }

        // Update profile but keep profile_visible = false until admin approval
        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                first_name = COALESCE(@FirstName, first_name),
                last_name = COALESCE(@LastName, last_name),
                linkedin_url = COALESCE(@LinkedInUrl, linkedin_url),
                github_url = COALESCE(@GitHubUrl, github_url),
                desired_role = COALESCE(@DesiredRole, desired_role),
                location_preference = COALESCE(@LocationPreference, location_preference),
                remote_preference = COALESCE(@RemotePreference, remote_preference),
                availability = COALESCE(@Availability, availability),
                profile_visible = FALSE,
                updated_at = @Now
            WHERE user_id = @UserId",
            new
            {
                UserId = userId,
                request.FirstName,
                request.LastName,
                request.LinkedInUrl,
                request.GitHubUrl,
                request.DesiredRole,
                request.LocationPreference,
                RemotePreference = request.RemotePreference.HasValue ? (int?)request.RemotePreference.Value : null,
                Availability = request.Availability.HasValue ? (int?)request.Availability.Value : null,
                Now = DateTime.UtcNow
            });

        return (await GetProfileAsync(userId))!;
    }

    public async Task<CandidateProfileResponse> UpdateProfileAsync(Guid userId, UpdateCandidateRequest request)
    {
        // Validate GitHub URL if provided
        ValidateGitHubUrl(request.GitHubUrl);

        using var connection = _db.CreateConnection();

        // Get candidate ID for location update
        var candidateId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        // Update candidate basic info
        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                first_name = COALESCE(@FirstName, first_name),
                last_name = COALESCE(@LastName, last_name),
                linkedin_url = COALESCE(@LinkedInUrl, linkedin_url),
                github_url = COALESCE(@GitHubUrl, github_url),
                github_summary = COALESCE(@GitHubSummary, github_summary),
                github_summary_enabled = COALESCE(@GitHubSummaryEnabled, github_summary_enabled),
                desired_role = COALESCE(@DesiredRole, desired_role),
                location_preference = COALESCE(@LocationPreference, location_preference),
                remote_preference = COALESCE(@RemotePreference, remote_preference),
                availability = COALESCE(@Availability, availability),
                seniority_estimate = COALESCE(@SeniorityEstimate, seniority_estimate),
                open_to_opportunities = COALESCE(@OpenToOpportunities, open_to_opportunities),
                profile_visible = COALESCE(@ProfileVisible, profile_visible),
                updated_at = @Now
            WHERE user_id = @UserId",
            new
            {
                UserId = userId,
                request.FirstName,
                request.LastName,
                request.LinkedInUrl,
                request.GitHubUrl,
                request.GitHubSummary,
                request.GitHubSummaryEnabled,
                request.DesiredRole,
                request.LocationPreference,
                RemotePreference = request.RemotePreference.HasValue ? (int?)request.RemotePreference.Value : null,
                Availability = request.Availability.HasValue ? (int?)request.Availability.Value : null,
                SeniorityEstimate = request.SeniorityEstimate.HasValue ? (int?)request.SeniorityEstimate.Value : null,
                request.OpenToOpportunities,
                request.ProfileVisible,
                Now = DateTime.UtcNow
            });

        // Update structured location data if provided
        if (request.Location != null)
        {
            await UpdateCandidateLocationAsync(connection, candidateId, request.Location);
        }

        return (await GetProfileAsync(userId))!;
    }

    private async Task UpdateCandidateLocationAsync(System.Data.IDbConnection connection, Guid candidateId, UpdateCandidateLocationRequest locationRequest)
    {
        // Check if location record exists
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM candidate_locations WHERE candidate_id = @CandidateId)",
            new { CandidateId = candidateId });

        if (exists)
        {
            // Update existing location
            await connection.ExecuteAsync(@"
                UPDATE candidate_locations SET
                    country = COALESCE(@Country, country),
                    city = COALESCE(@City, city),
                    timezone = COALESCE(@Timezone, timezone),
                    willing_to_relocate = COALESCE(@WillingToRelocate, willing_to_relocate),
                    updated_at = @Now
                WHERE candidate_id = @CandidateId",
                new
                {
                    CandidateId = candidateId,
                    locationRequest.Country,
                    locationRequest.City,
                    locationRequest.Timezone,
                    locationRequest.WillingToRelocate,
                    Now = DateTime.UtcNow
                });
        }
        else
        {
            // Insert new location record
            await connection.ExecuteAsync(@"
                INSERT INTO candidate_locations (candidate_id, country, city, timezone, willing_to_relocate, created_at, updated_at)
                VALUES (@CandidateId, @Country, @City, @Timezone, @WillingToRelocate, @Now, @Now)",
                new
                {
                    CandidateId = candidateId,
                    locationRequest.Country,
                    locationRequest.City,
                    locationRequest.Timezone,
                    WillingToRelocate = locationRequest.WillingToRelocate ?? false,
                    Now = DateTime.UtcNow
                });
        }
    }

    public async Task<CvUploadResponse> GetCvUploadUrlAsync(Guid userId, string fileName)
    {
        using var connection = _db.CreateConnection();

        var candidateId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };

        var fileKey = $"cvs/{candidateId}/{Guid.NewGuid()}{extension}";
        var uploadUrl = await _s3Service.GeneratePresignedUploadUrlAsync(fileKey, contentType);

        return new CvUploadResponse
        {
            UploadUrl = uploadUrl,
            FileKey = fileKey
        };
    }

    public async Task<CvUploadResponse> UploadCvAsync(Guid userId, Stream fileStream, string fileName, string contentType)
    {
        using var connection = _db.CreateConnection();

        var candidateId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var fileKey = $"cvs/{candidateId}/{Guid.NewGuid()}{extension}";

        // Upload to S3
        await _s3Service.UploadFileAsync(fileKey, fileStream, contentType);

        // Process the CV (update database and parse)
        await ProcessCvUploadAsync(userId, fileKey, fileName);

        return new CvUploadResponse
        {
            UploadUrl = string.Empty, // Not needed for direct upload
            FileKey = fileKey
        };
    }

    public async Task ProcessCvUploadAsync(Guid userId, string fileKey, string originalFileName)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT c.id, c.first_name, c.last_name, u.email FROM candidates c JOIN users u ON u.id = c.user_id WHERE c.user_id = @UserId",
            new { UserId = userId });

        if (candidate == null)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        var candidateId = (Guid)candidate.id;

        var oldCvKey = await connection.ExecuteScalarAsync<string?>(
            "SELECT cv_file_key FROM candidates WHERE id = @Id",
            new { Id = candidateId });

        if (!string.IsNullOrEmpty(oldCvKey) && oldCvKey != fileKey)
        {
            try
            {
                await _s3Service.DeleteFileAsync(oldCvKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old CV: {FileKey}", oldCvKey);
            }
        }

        // Save CV reference and mark as pending parse
        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                cv_file_key = @FileKey,
                cv_original_file_name = @FileName,
                cv_parse_status = 'pending',
                cv_parse_error = NULL,
                updated_at = @Now
            WHERE id = @Id",
            new { Id = candidateId, FileKey = fileKey, FileName = originalFileName, Now = DateTime.UtcNow });

        try
        {
            using var stream = await _s3Service.DownloadFileAsync(fileKey);
            var parseResult = await _cvParsingService.ParseCvAsync(stream, originalFileName);

            var hasSkills = parseResult.Skills != null && parseResult.Skills.Count > 0;
            var hasBasicInfo = !string.IsNullOrEmpty(parseResult.FirstName) || !string.IsNullOrEmpty(parseResult.LastName);

            // Determine parse status
            string parseStatus;
            string? parseError = null;

            if (hasSkills)
            {
                parseStatus = "success";
            }
            else if (hasBasicInfo)
            {
                parseStatus = "partial";
                parseError = "Skills could not be extracted from CV";
            }
            else
            {
                parseStatus = "failed";
                parseError = "Could not extract information from CV - may be image-based or encrypted";
            }

            // Update basic info (only if we got something)
            await connection.ExecuteAsync(@"
                UPDATE candidates SET
                    first_name = COALESCE(NULLIF(@FirstName, ''), first_name),
                    last_name = COALESCE(NULLIF(@LastName, ''), last_name),
                    seniority_estimate = COALESCE(@Seniority, seniority_estimate),
                    parsed_profile_json = @ParsedJson,
                    cv_parse_status = @ParseStatus,
                    cv_parse_error = @ParseError,
                    cv_parsed_at = @Now,
                    updated_at = @Now
                WHERE id = @Id",
                new
                {
                    Id = candidateId,
                    FirstName = parseResult.FirstName,
                    LastName = parseResult.LastName,
                    Seniority = parseResult.SeniorityEstimate.HasValue ? (int?)parseResult.SeniorityEstimate.Value : null,
                    ParsedJson = parseResult.RawJson,
                    ParseStatus = parseStatus,
                    ParseError = parseError,
                    Now = DateTime.UtcNow
                });

            // IMPORTANT: Only update skills if parsing succeeded with skills
            // Never delete existing skills on failed/partial parse
            if (hasSkills)
            {
                // Delete old CV-parsed skills (non-verified) before inserting new ones
                await connection.ExecuteAsync(@"
                    DELETE FROM candidate_skills
                    WHERE candidate_id = @CandidateId AND is_verified = FALSE",
                    new { CandidateId = candidateId });

                foreach (var skill in parseResult.Skills)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO candidate_skills (id, candidate_id, skill_name, confidence_score, category, is_verified)
                        VALUES (@Id, @CandidateId, @SkillName, @Confidence, @Category, FALSE)
                        ON CONFLICT (candidate_id, skill_name) DO UPDATE SET
                            confidence_score = EXCLUDED.confidence_score,
                            category = EXCLUDED.category",
                        new
                        {
                            Id = Guid.NewGuid(),
                            CandidateId = candidateId,
                            SkillName = skill.Name,
                            Confidence = skill.ConfidenceScore,
                            Category = (int)skill.Category
                        });
                }

                _logger.LogInformation("CV parsed successfully for candidate {CandidateId}: {SkillCount} skills extracted",
                    candidateId, parseResult.Skills.Count);
            }
            else
            {
                // Notify admin about failed/partial parse
                var candidateName = $"{candidate.first_name} {candidate.last_name}".Trim();
                if (string.IsNullOrEmpty(candidateName)) candidateName = (string?)candidate.email ?? "Unknown";

                await _emailService.SendAdminNotificationAsync(
                    "CV Parse Review Needed",
                    $"CV parsing {parseStatus} for candidate: {candidateName}\n\n" +
                    $"Candidate ID: {candidateId}\n" +
                    $"File: {originalFileName}\n" +
                    $"Issue: {parseError}\n\n" +
                    "Please review the CV manually in the admin dashboard.");

                _logger.LogWarning("CV parse {Status} for candidate {CandidateId}: {Error}",
                    parseStatus, candidateId, parseError);
            }
        }
        catch (Exception ex)
        {
            // Update status to failed
            await connection.ExecuteAsync(@"
                UPDATE candidates SET
                    cv_parse_status = 'failed',
                    cv_parse_error = @Error,
                    cv_parsed_at = @Now,
                    updated_at = @Now
                WHERE id = @Id",
                new { Id = candidateId, Error = ex.Message, Now = DateTime.UtcNow });

            // Notify admin
            var candidateName = $"{candidate.first_name} {candidate.last_name}".Trim();
            if (string.IsNullOrEmpty(candidateName)) candidateName = (string?)candidate.email ?? "Unknown";

            await _emailService.SendAdminNotificationAsync(
                "CV Parse Failed",
                $"CV parsing failed for candidate: {candidateName}\n\n" +
                $"Candidate ID: {candidateId}\n" +
                $"File: {originalFileName}\n" +
                $"Error: {ex.Message}\n\n" +
                "Please review the CV manually in the admin dashboard.");

            _logger.LogError(ex, "Failed to parse CV for candidate {CandidateId}", candidateId);
        }
    }

    public async Task UpdateSkillsAsync(Guid userId, UpdateSkillsRequest request)
    {
        using var connection = _db.CreateConnection();

        var candidateId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        // Validate max 7 Primary skills
        var primarySkillsCount = request.Skills.Count(s => !s.Delete && s.SkillLevel == SkillLevel.Primary);
        if (primarySkillsCount > 7)
        {
            throw new InvalidOperationException("Maximum of 7 Primary skills allowed");
        }

        foreach (var skillUpdate in request.Skills)
        {
            if (skillUpdate.Delete && skillUpdate.Id.HasValue)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM candidate_skills WHERE id = @Id AND candidate_id = @CandidateId",
                    new { Id = skillUpdate.Id.Value, CandidateId = candidateId });
            }
            else if (skillUpdate.Id.HasValue)
            {
                await connection.ExecuteAsync(@"
                    UPDATE candidate_skills SET
                        skill_name = @SkillName,
                        category = @Category,
                        is_verified = @IsVerified,
                        skill_level = @SkillLevel
                    WHERE id = @Id AND candidate_id = @CandidateId",
                    new
                    {
                        Id = skillUpdate.Id.Value,
                        CandidateId = candidateId,
                        skillUpdate.SkillName,
                        Category = (int)skillUpdate.Category,
                        skillUpdate.IsVerified,
                        SkillLevel = (int)skillUpdate.SkillLevel
                    });
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO candidate_skills (id, candidate_id, skill_name, confidence_score, category, is_verified, skill_level)
                    VALUES (@Id, @CandidateId, @SkillName, 1.0, @Category, TRUE, @SkillLevel)
                    ON CONFLICT (candidate_id, skill_name) DO NOTHING",
                    new
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidateId,
                        skillUpdate.SkillName,
                        Category = (int)skillUpdate.Category,
                        SkillLevel = (int)skillUpdate.SkillLevel
                    });
            }
        }

        await connection.ExecuteAsync(
            "UPDATE candidates SET updated_at = @Now WHERE id = @Id",
            new { Id = candidateId, Now = DateTime.UtcNow });
    }

    public async Task SetVisibilityAsync(Guid userId, bool visible)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE candidates SET profile_visible = @Visible, updated_at = @Now WHERE user_id = @UserId",
            new { UserId = userId, Visible = visible, Now = DateTime.UtcNow });
    }

    public async Task<bool> RequestPublicWorkSummaryAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        // Check if candidate has GitHub URL and hasn't already requested
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, github_url, github_summary_requested_at, github_summary
            FROM candidates
            WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidate == null)
        {
            _logger.LogWarning("Candidate not found for user {UserId}", userId);
            return false;
        }

        // Must have GitHub URL to request summary
        if (string.IsNullOrEmpty(candidate.github_url as string))
        {
            _logger.LogWarning("Cannot request public work summary without GitHub URL for user {UserId}", userId);
            return false;
        }

        // Already has a summary - no need to request
        if (!string.IsNullOrEmpty(candidate.github_summary as string))
        {
            _logger.LogInformation("Public work summary already exists for user {UserId}", userId);
            return true;
        }

        // Already requested - idempotent, return success
        if (candidate.github_summary_requested_at != null)
        {
            _logger.LogInformation("Public work summary already requested for user {UserId}", userId);
            return true;
        }

        // Record the request
        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                github_summary_requested_at = @Now,
                updated_at = @Now
            WHERE user_id = @UserId",
            new { UserId = userId, Now = DateTime.UtcNow });

        _logger.LogInformation("Public work summary requested for user {UserId}", userId);
        return true;
    }

    public async Task<CvReparseResult> ReparseCvAsync(Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.cv_file_key, c.cv_original_file_name, c.first_name, c.last_name, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CandidateId",
            new { CandidateId = candidateId });

        if (candidate == null)
        {
            return new CvReparseResult { Success = false, Status = "failed", Error = "Candidate not found" };
        }

        var cvFileKey = candidate.cv_file_key as string;
        var originalFileName = candidate.cv_original_file_name as string ?? "cv.pdf";

        if (string.IsNullOrEmpty(cvFileKey))
        {
            return new CvReparseResult { Success = false, Status = "failed", Error = "Candidate has no CV uploaded" };
        }

        // Use existing ProcessCvUploadAsync logic
        await ProcessCvUploadAsync((Guid)candidate.user_id, cvFileKey, originalFileName);

        // Fetch the updated parse status
        var result = await connection.QueryFirstAsync<dynamic>(
            "SELECT cv_parse_status, cv_parse_error FROM candidates WHERE id = @CandidateId",
            new { CandidateId = candidateId });

        var skillsCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM candidate_skills WHERE candidate_id = @CandidateId",
            new { CandidateId = candidateId });

        return new CvReparseResult
        {
            Success = result.cv_parse_status == "success",
            Status = (string)result.cv_parse_status,
            Error = result.cv_parse_error as string,
            SkillsExtracted = skillsCount
        };
    }
}
