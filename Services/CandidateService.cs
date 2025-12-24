using System.Text.Json;
using Dapper;
using pixo_api.Data;
using pixo_api.Models.DTOs.Candidate;
using pixo_api.Models.Enums;
using pixo_api.Services.Interfaces;

namespace pixo_api.Services;

public class CandidateService : ICandidateService
{
    private readonly IDbConnectionFactory _db;
    private readonly IS3StorageService _s3Service;
    private readonly ICvParsingService _cvParsingService;
    private readonly ILogger<CandidateService> _logger;

    public CandidateService(
        IDbConnectionFactory db,
        IS3StorageService s3Service,
        ICvParsingService cvParsingService,
        ILogger<CandidateService> logger)
    {
        _db = db;
        _s3Service = s3Service;
        _cvParsingService = cvParsingService;
        _logger = logger;
    }

    public async Task<CandidateProfileResponse?> GetProfileAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.first_name, c.last_name, c.linkedin_url, c.cv_file_key, c.cv_original_file_name,
                   c.desired_role, c.location_preference, c.remote_preference, c.availability,
                   c.open_to_opportunities, c.profile_visible, c.seniority_estimate, c.created_at,
                   u.email, u.last_active_at
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.user_id = @UserId",
            new { UserId = userId });

        if (candidate == null) return null;

        var skills = await connection.QueryAsync<dynamic>(@"
            SELECT id, skill_name, confidence_score, category, is_verified
            FROM candidate_skills
            WHERE candidate_id = @CandidateId",
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

        return new CandidateProfileResponse
        {
            Id = (Guid)candidate.id,
            Email = (string)candidate.email,
            FirstName = candidate.first_name as string,
            LastName = candidate.last_name as string,
            LinkedInUrl = candidate.linkedin_url as string,
            CvFileName = candidate.cv_original_file_name as string,
            CvDownloadUrl = cvDownloadUrl,
            DesiredRole = candidate.desired_role as string,
            LocationPreference = candidate.location_preference as string,
            RemotePreference = candidate.remote_preference != null ? (RemotePreference?)(int)candidate.remote_preference : null,
            Availability = (Availability)(int)candidate.availability,
            OpenToOpportunities = (bool)candidate.open_to_opportunities,
            ProfileVisible = (bool)candidate.profile_visible,
            SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel?)(int)candidate.seniority_estimate : null,
            Skills = skills.Select(s => new CandidateSkillResponse
            {
                Id = (Guid)s.id,
                SkillName = (string)s.skill_name,
                ConfidenceScore = (double)(decimal)s.confidence_score,
                Category = (SkillCategory)(int)s.category,
                IsVerified = (bool)s.is_verified
            }).ToList(),
            RecommendationsCount = recommendationsCount,
            ProfileViewsCount = profileViewsCount,
            CreatedAt = (DateTime)candidate.created_at,
            LastActiveAt = (DateTime)candidate.last_active_at
        };
    }

    public async Task<CandidateProfileResponse> OnboardAsync(Guid userId, CandidateOnboardRequest request)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                first_name = COALESCE(@FirstName, first_name),
                last_name = COALESCE(@LastName, last_name),
                linkedin_url = COALESCE(@LinkedInUrl, linkedin_url),
                desired_role = COALESCE(@DesiredRole, desired_role),
                location_preference = COALESCE(@LocationPreference, location_preference),
                remote_preference = COALESCE(@RemotePreference, remote_preference),
                availability = COALESCE(@Availability, availability),
                updated_at = @Now
            WHERE user_id = @UserId",
            new
            {
                UserId = userId,
                request.FirstName,
                request.LastName,
                request.LinkedInUrl,
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
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                first_name = COALESCE(@FirstName, first_name),
                last_name = COALESCE(@LastName, last_name),
                linkedin_url = COALESCE(@LinkedInUrl, linkedin_url),
                desired_role = COALESCE(@DesiredRole, desired_role),
                location_preference = COALESCE(@LocationPreference, location_preference),
                remote_preference = COALESCE(@RemotePreference, remote_preference),
                availability = COALESCE(@Availability, availability),
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
                request.DesiredRole,
                request.LocationPreference,
                RemotePreference = request.RemotePreference.HasValue ? (int?)request.RemotePreference.Value : null,
                Availability = request.Availability.HasValue ? (int?)request.Availability.Value : null,
                request.OpenToOpportunities,
                request.ProfileVisible,
                Now = DateTime.UtcNow
            });

        return (await GetProfileAsync(userId))!;
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

        var candidateId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM candidates WHERE user_id = @UserId",
            new { UserId = userId });

        if (candidateId == Guid.Empty)
        {
            throw new InvalidOperationException("Candidate not found");
        }

        var oldCvKey = await connection.ExecuteScalarAsync<string?>(
            "SELECT cv_file_key FROM candidates WHERE id = @Id",
            new { Id = candidateId });

        if (!string.IsNullOrEmpty(oldCvKey))
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

        await connection.ExecuteAsync(@"
            UPDATE candidates SET
                cv_file_key = @FileKey,
                cv_original_file_name = @FileName,
                updated_at = @Now
            WHERE id = @Id",
            new { Id = candidateId, FileKey = fileKey, FileName = originalFileName, Now = DateTime.UtcNow });

        try
        {
            using var stream = await _s3Service.DownloadFileAsync(fileKey);
            var parseResult = await _cvParsingService.ParseCvAsync(stream, originalFileName);

            await connection.ExecuteAsync(@"
                UPDATE candidates SET
                    first_name = COALESCE(NULLIF(@FirstName, ''), first_name),
                    last_name = COALESCE(NULLIF(@LastName, ''), last_name),
                    seniority_estimate = COALESCE(@Seniority, seniority_estimate),
                    parsed_profile_json = @ParsedJson,
                    updated_at = @Now
                WHERE id = @Id",
                new
                {
                    Id = candidateId,
                    FirstName = parseResult.FirstName,
                    LastName = parseResult.LastName,
                    Seniority = parseResult.SeniorityEstimate.HasValue ? (int?)parseResult.SeniorityEstimate.Value : null,
                    ParsedJson = parseResult.RawJson,
                    Now = DateTime.UtcNow
                });

            foreach (var skill in parseResult.Skills)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO candidate_skills (id, candidate_id, skill_name, confidence_score, category, is_verified)
                    VALUES (@Id, @CandidateId, @SkillName, @Confidence, @Category, FALSE)
                    ON CONFLICT (candidate_id, skill_name) DO NOTHING",
                    new
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidateId,
                        SkillName = skill.Name,
                        Confidence = skill.ConfidenceScore,
                        Category = (int)skill.Category
                    });
            }
        }
        catch (Exception ex)
        {
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
                        is_verified = @IsVerified
                    WHERE id = @Id AND candidate_id = @CandidateId",
                    new
                    {
                        Id = skillUpdate.Id.Value,
                        CandidateId = candidateId,
                        skillUpdate.SkillName,
                        Category = (int)skillUpdate.Category,
                        skillUpdate.IsVerified
                    });
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO candidate_skills (id, candidate_id, skill_name, confidence_score, category, is_verified)
                    VALUES (@Id, @CandidateId, @SkillName, 1.0, @Category, TRUE)
                    ON CONFLICT (candidate_id, skill_name) DO NOTHING",
                    new
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidateId,
                        skillUpdate.SkillName,
                        Category = (int)skillUpdate.Category
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
}
