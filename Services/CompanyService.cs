using Dapper;
using bixo_api.Data;
using bixo_api.Models.DTOs.Candidate;
using bixo_api.Models.DTOs.Company;
using bixo_api.Models.DTOs.Location;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class CompanyService : ICompanyService
{
    private readonly IDbConnectionFactory _db;
    private readonly IS3StorageService _s3Service;
    private readonly INotificationService _notificationService;

    public CompanyService(
        IDbConnectionFactory db,
        IS3StorageService s3Service,
        INotificationService notificationService)
    {
        _db = db;
        _s3Service = s3Service;
        _notificationService = notificationService;
    }

    public async Task<CompanyProfileResponse?> GetProfileAsync(Guid userId)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.company_name, c.industry, c.company_size, c.website, c.logo_file_key,
                   c.subscription_tier, c.subscription_expires_at, c.messages_remaining, c.created_at, c.updated_at,
                   cl.country AS location_country,
                   cl.city AS location_city,
                   cl.timezone AS location_timezone
            FROM companies c
            LEFT JOIN company_locations cl ON cl.company_id = c.id
            WHERE c.user_id = @UserId",
            new { UserId = userId });

        if (company == null) return null;

        string? logoUrl = null;
        if (!string.IsNullOrEmpty((string?)company.logo_file_key))
        {
            logoUrl = await _s3Service.GeneratePresignedDownloadUrlAsync((string)company.logo_file_key);
        }

        // Build location response if location data exists
        LocationResponse? locationResponse = null;
        var locationCountry = company.location_country as string;
        var locationCity = company.location_city as string;
        var locationTimezone = company.location_timezone as string;

        if (!string.IsNullOrEmpty(locationCountry) || !string.IsNullOrEmpty(locationCity) || !string.IsNullOrEmpty(locationTimezone))
        {
            locationResponse = new LocationResponse
            {
                Country = locationCountry,
                City = locationCity,
                Timezone = locationTimezone
            };
        }

        return new CompanyProfileResponse
        {
            Id = (Guid)company.id,
            CompanyName = (string?)company.company_name,
            Industry = (string?)company.industry,
            CompanySize = (string?)company.company_size,
            Website = (string?)company.website,
            LogoUrl = logoUrl,
            Location = locationResponse,
            SubscriptionTier = (SubscriptionTier)(int)company.subscription_tier,
            SubscriptionExpiresAt = company.subscription_expires_at as DateTime?,
            MessagesRemaining = (int)company.messages_remaining,
            CreatedAt = (DateTime)company.created_at
        };
    }

    public async Task<CompanyProfileResponse> UpdateProfileAsync(Guid userId, UpdateCompanyRequest request)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id FROM companies WHERE user_id = @UserId",
            new { UserId = userId });

        if (company == null)
        {
            throw new InvalidOperationException("Company not found");
        }

        var companyId = (Guid)company.id;
        var updateFields = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("Now", DateTime.UtcNow);

        if (request.CompanyName != null)
        {
            updateFields.Add("company_name = @CompanyName");
            parameters.Add("CompanyName", request.CompanyName);
        }

        if (request.Industry != null)
        {
            updateFields.Add("industry = @Industry");
            parameters.Add("Industry", request.Industry);
        }

        if (request.CompanySize != null)
        {
            updateFields.Add("company_size = @CompanySize");
            parameters.Add("CompanySize", request.CompanySize);
        }

        if (request.Website != null)
        {
            updateFields.Add("website = @Website");
            parameters.Add("Website", request.Website);
        }

        if (updateFields.Any())
        {
            updateFields.Add("updated_at = @Now");
            var sql = $@"
                UPDATE companies
                SET {string.Join(", ", updateFields)}
                WHERE user_id = @UserId";

            await connection.ExecuteAsync(sql, parameters);
        }

        // Update structured location data if provided
        if (request.Location != null)
        {
            await UpdateCompanyLocationAsync(connection, companyId, request.Location);
        }

        return (await GetProfileAsync(userId))!;
    }

    private async Task UpdateCompanyLocationAsync(System.Data.IDbConnection connection, Guid companyId, UpdateLocationRequest locationRequest)
    {
        // Check if location record exists
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM company_locations WHERE company_id = @CompanyId)",
            new { CompanyId = companyId });

        if (exists)
        {
            // Update existing location
            await connection.ExecuteAsync(@"
                UPDATE company_locations SET
                    country = COALESCE(@Country, country),
                    city = COALESCE(@City, city),
                    timezone = COALESCE(@Timezone, timezone),
                    updated_at = @Now
                WHERE company_id = @CompanyId",
                new
                {
                    CompanyId = companyId,
                    locationRequest.Country,
                    locationRequest.City,
                    locationRequest.Timezone,
                    Now = DateTime.UtcNow
                });
        }
        else
        {
            // Insert new location record
            await connection.ExecuteAsync(@"
                INSERT INTO company_locations (company_id, country, city, timezone, created_at, updated_at)
                VALUES (@CompanyId, @Country, @City, @Timezone, @Now, @Now)",
                new
                {
                    CompanyId = companyId,
                    locationRequest.Country,
                    locationRequest.City,
                    locationRequest.Timezone,
                    Now = DateTime.UtcNow
                });
        }
    }

    public async Task<TalentListResponse> SearchTalentAsync(Guid companyId, TalentSearchRequest request)
    {
        using var connection = _db.CreateConnection();

        var whereClauses = new List<string>
        {
            "c.profile_visible = TRUE",
            "c.open_to_opportunities = TRUE"
        };
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Offset", (request.Page - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        if (request.Seniority.HasValue)
        {
            whereClauses.Add("c.seniority_estimate = @Seniority");
            parameters.Add("Seniority", (int)request.Seniority.Value);
        }

        if (request.Availability.HasValue)
        {
            whereClauses.Add("c.availability = @Availability");
            parameters.Add("Availability", (int)request.Availability.Value);
        }

        // Remote preference is now a ranking signal, not a hard filter
        // Only apply as filter if explicitly requested
        if (request.RemotePreference.HasValue && request.RemotePreference.Value == RemotePreference.Remote)
        {
            // Only hard filter for remote-only candidates when specifically requested
            whereClauses.Add("(c.remote_preference = @RemotePreference OR c.remote_preference = @Flexible)");
            parameters.Add("RemotePreference", (int)request.RemotePreference.Value);
            parameters.Add("Flexible", (int)RemotePreference.Flexible);
        }

        // Location is now a ranking signal, not a hard filter
        // Legacy location filter is kept for backwards compatibility but uses soft matching
        // Note: We don't add location as a WHERE clause anymore

        if (request.LastActiveDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-request.LastActiveDays.Value);
            whereClauses.Add("u.last_active_at >= @Cutoff");
            parameters.Add("Cutoff", cutoff);
        }

        if (request.Skills != null && request.Skills.Any())
        {
            whereClauses.Add(@"EXISTS (
                SELECT 1 FROM candidate_skills cs
                WHERE cs.candidate_id = c.id
                AND LOWER(cs.skill_name) = ANY(@Skills)
            )");
            parameters.Add("Skills", request.Skills.Select(s => s.ToLower()).ToArray());
        }

        if (!string.IsNullOrEmpty(request.Query))
        {
            var q = request.Query.ToLower();
            whereClauses.Add(@"(
                (c.desired_role IS NOT NULL AND LOWER(c.desired_role) LIKE @Query) OR
                (c.first_name IS NOT NULL AND LOWER(c.first_name) LIKE @Query) OR
                (c.last_name IS NOT NULL AND LOWER(c.last_name) LIKE @Query) OR
                EXISTS (
                    SELECT 1 FROM candidate_skills cs
                    WHERE cs.candidate_id = c.id
                    AND LOWER(cs.skill_name) LIKE @Query
                )
            )");
            parameters.Add("Query", $"%{q}%");
        }

        var whereClause = string.Join(" AND ", whereClauses);

        // Build location ranking score for ORDER BY
        // Location ranking: prefer remote match, same country, same timezone, willing to relocate
        var locationRankingScore = BuildLocationRankingScore(request);

        // Get total count
        var countSql = $@"
            SELECT COUNT(DISTINCT c.id)
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN candidate_locations cl ON cl.candidate_id = c.id
            WHERE {whereClause}";

        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get saved candidate IDs
        var savedCandidateIds = (await connection.QueryAsync<Guid>(@"
            SELECT candidate_id
            FROM saved_candidates
            WHERE company_id = @CompanyId",
            new { CompanyId = companyId })).ToList();

        // Get candidates with location data for ranking
        // Use location ranking score in ORDER BY for better matches
        var candidatesSql = $@"
            SELECT DISTINCT
                c.id,
                c.first_name,
                c.last_name,
                c.desired_role,
                c.location_preference,
                c.remote_preference,
                c.availability,
                c.seniority_estimate,
                u.last_active_at,
                cl.country AS location_country,
                cl.city AS location_city,
                cl.timezone AS location_timezone,
                cl.willing_to_relocate,
                {locationRankingScore} AS location_score
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            LEFT JOIN candidate_locations cl ON cl.candidate_id = c.id
            WHERE {whereClause}
            ORDER BY {locationRankingScore} DESC, u.last_active_at DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";

        // Add location ranking parameters if needed
        AddLocationRankingParameters(parameters, request);

        var candidates = (await connection.QueryAsync<dynamic>(candidatesSql, parameters)).ToList();

        var candidateResponses = new List<TalentResponse>();

        foreach (var candidate in candidates)
        {
            var candidateId = (Guid)candidate.id;

            // Get top 5 skills for this candidate
            var topSkills = (await connection.QueryAsync<string>(@"
                SELECT skill_name
                FROM candidate_skills
                WHERE candidate_id = @CandidateId
                ORDER BY confidence_score DESC
                OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
                new { CandidateId = candidateId })).ToList();

            // Get recommendations count
            var recommendationsCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM candidate_recommendations WHERE candidate_id = @CandidateId",
                new { CandidateId = candidateId });

            candidateResponses.Add(new TalentResponse
            {
                CandidateId = candidateId,
                FirstName = (string?)candidate.first_name,
                LastName = (string?)candidate.last_name,
                DesiredRole = (string?)candidate.desired_role,
                LocationPreference = (string?)candidate.location_preference,
                RemotePreference = candidate.remote_preference != null ? (RemotePreference)(int)candidate.remote_preference : null,
                Availability = (Availability)(int)candidate.availability,
                SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel)(int)candidate.seniority_estimate : null,
                TopSkills = topSkills,
                RecommendationsCount = recommendationsCount,
                LastActiveAt = (DateTime)candidate.last_active_at,
                MatchScore = 0,
                IsSaved = savedCandidateIds.Contains(candidateId)
            });
        }

        return new TalentListResponse
        {
            Candidates = candidateResponses,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }

    public async Task<CandidateDetailResponse?> GetCandidateDetailAsync(Guid companyId, Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        // Get candidate with user email
        var candidate = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.first_name, c.last_name, c.linkedin_url, c.cv_file_key,
                   c.desired_role, c.location_preference, c.remote_preference, c.availability,
                   c.seniority_estimate, c.github_url, c.github_summary, c.github_summary_enabled,
                   u.last_active_at, u.email
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CandidateId AND c.profile_visible = TRUE",
            new { CandidateId = candidateId });

        if (candidate == null) return null;

        // Check if candidate is in any of this company's approved/delivered shortlists
        var isInShortlist = await connection.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(
                SELECT 1 FROM shortlist_candidates sc
                JOIN shortlist_requests sr ON sr.id = sc.shortlist_request_id
                WHERE sc.candidate_id = @CandidateId
                  AND sr.company_id = @CompanyId
                  AND sr.status IN (@Approved, @Delivered)
            )",
            new { CandidateId = candidateId, CompanyId = companyId, Approved = (int)ShortlistStatus.Approved, Delivered = (int)ShortlistStatus.Delivered });

        // Record profile view
        var existingView = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id FROM candidate_profile_views
            WHERE candidate_id = @CandidateId AND company_id = @CompanyId",
            new { CandidateId = candidateId, CompanyId = companyId });

        if (existingView == null)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO candidate_profile_views (id, candidate_id, company_id, viewed_at)
                VALUES (@Id, @CandidateId, @CompanyId, @ViewedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidateId,
                    CompanyId = companyId,
                    ViewedAt = DateTime.UtcNow
                });

            // Note: We intentionally do NOT notify candidates about profile views.
            // Profile views are low-signal events that create noise.
            // Candidates are only notified about high-signal events like shortlisting.
        }

        // Only generate CV URL if candidate is in shortlist
        string? cvDownloadUrl = null;
        if (isInShortlist && !string.IsNullOrEmpty((string?)candidate.cv_file_key))
        {
            cvDownloadUrl = await _s3Service.GeneratePresignedDownloadUrlAsync((string)candidate.cv_file_key);
        }

        // Get skills
        var skills = (await connection.QueryAsync<dynamic>(@"
            SELECT id, skill_name, confidence_score, category, is_verified
            FROM candidate_skills
            WHERE candidate_id = @CandidateId",
            new { CandidateId = candidateId })).ToList();

        var skillResponses = skills.Select(s => new CandidateSkillResponse
        {
            Id = (Guid)s.id,
            SkillName = (string)s.skill_name,
            ConfidenceScore = (double)(decimal)s.confidence_score,
            Category = (SkillCategory)(int)s.category,
            IsVerified = (bool)s.is_verified
        }).ToList();

        // Get recommendations count
        var recommendationsCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM candidate_recommendations WHERE candidate_id = @CandidateId",
            new { CandidateId = candidateId });

        // Check if saved
        var isSaved = await connection.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS(SELECT 1 FROM saved_candidates
            WHERE company_id = @CompanyId AND candidate_id = @CandidateId)",
            new { CompanyId = companyId, CandidateId = candidateId });

        return new CandidateDetailResponse
        {
            Id = (Guid)candidate.id,
            FirstName = (string?)candidate.first_name,
            LastName = (string?)candidate.last_name,
            DesiredRole = (string?)candidate.desired_role,
            LocationPreference = (string?)candidate.location_preference,
            RemotePreference = candidate.remote_preference != null ? (RemotePreference)(int)candidate.remote_preference : null,
            Availability = (Availability)(int)candidate.availability,
            SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel)(int)candidate.seniority_estimate : null,
            Skills = skillResponses,
            Capabilities = CapabilityMapping.DeriveCapabilities(skills.Select(s => (string)s.skill_name)),
            RecommendationsCount = recommendationsCount,
            LastActiveAt = (DateTime)candidate.last_active_at,
            IsSaved = isSaved,
            // Shortlist-only fields - only populated if candidate is in company's approved/delivered shortlist
            IsInShortlist = isInShortlist,
            Email = isInShortlist ? (string?)candidate.email : null,
            LinkedInUrl = isInShortlist ? (string?)candidate.linkedin_url : null,
            CvDownloadUrl = cvDownloadUrl,
            GitHubUrl = isInShortlist ? (string?)candidate.github_url : null,
            GitHubSummary = isInShortlist && (bool?)candidate.github_summary_enabled == true ? (string?)candidate.github_summary : null
        };
    }

    public async Task<SavedCandidateResponse> SaveCandidateAsync(Guid companyId, SaveCandidateRequest request)
    {
        using var connection = _db.CreateConnection();

        var existing = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, saved_at FROM saved_candidates
            WHERE company_id = @CompanyId AND candidate_id = @CandidateId",
            new { CompanyId = companyId, CandidateId = request.CandidateId });

        Guid savedId;
        DateTime savedAt;

        if (existing != null)
        {
            await connection.ExecuteAsync(@"
                UPDATE saved_candidates
                SET notes = @Notes
                WHERE company_id = @CompanyId AND candidate_id = @CandidateId",
                new { CompanyId = companyId, CandidateId = request.CandidateId, Notes = request.Notes });

            savedId = (Guid)existing.id;
            savedAt = (DateTime)existing.saved_at;
        }
        else
        {
            savedId = Guid.NewGuid();
            savedAt = DateTime.UtcNow;

            await connection.ExecuteAsync(@"
                INSERT INTO saved_candidates (id, company_id, candidate_id, notes, saved_at)
                VALUES (@Id, @CompanyId, @CandidateId, @Notes, @SavedAt)",
                new
                {
                    Id = savedId,
                    CompanyId = companyId,
                    CandidateId = request.CandidateId,
                    Notes = request.Notes,
                    SavedAt = savedAt
                });
        }

        // Get candidate details
        var candidate = await connection.QueryFirstAsync<dynamic>(@"
            SELECT c.id, c.first_name, c.last_name, c.desired_role, c.location_preference,
                   c.remote_preference, c.availability, c.seniority_estimate, u.last_active_at
            FROM candidates c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CandidateId",
            new { CandidateId = request.CandidateId });

        // Get top 5 skills
        var topSkills = (await connection.QueryAsync<string>(@"
            SELECT skill_name
            FROM candidate_skills
            WHERE candidate_id = @CandidateId
            ORDER BY confidence_score DESC
            OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
            new { CandidateId = request.CandidateId })).ToList();

        // Get recommendations count
        var recommendationsCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM candidate_recommendations WHERE candidate_id = @CandidateId",
            new { CandidateId = request.CandidateId });

        return new SavedCandidateResponse
        {
            Id = savedId,
            CandidateId = request.CandidateId,
            Candidate = new TalentResponse
            {
                CandidateId = (Guid)candidate.id,
                FirstName = (string?)candidate.first_name,
                LastName = (string?)candidate.last_name,
                DesiredRole = (string?)candidate.desired_role,
                LocationPreference = (string?)candidate.location_preference,
                RemotePreference = candidate.remote_preference != null ? (RemotePreference)(int)candidate.remote_preference : null,
                Availability = (Availability)(int)candidate.availability,
                SeniorityEstimate = candidate.seniority_estimate != null ? (SeniorityLevel)(int)candidate.seniority_estimate : null,
                TopSkills = topSkills,
                RecommendationsCount = recommendationsCount,
                LastActiveAt = (DateTime)candidate.last_active_at,
                IsSaved = true
            },
            Notes = request.Notes,
            SavedAt = savedAt
        };
    }

    public async Task RemoveSavedCandidateAsync(Guid companyId, Guid candidateId)
    {
        using var connection = _db.CreateConnection();

        await connection.ExecuteAsync(@"
            DELETE FROM saved_candidates
            WHERE company_id = @CompanyId AND candidate_id = @CandidateId",
            new { CompanyId = companyId, CandidateId = candidateId });
    }

    public async Task<List<SavedCandidateResponse>> GetSavedCandidatesAsync(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var saved = (await connection.QueryAsync<dynamic>(@"
            SELECT sc.id, sc.candidate_id, sc.notes, sc.saved_at,
                   c.first_name, c.last_name, c.desired_role, c.location_preference,
                   c.remote_preference, c.availability, c.seniority_estimate,
                   u.last_active_at
            FROM saved_candidates sc
            JOIN candidates c ON c.id = sc.candidate_id
            JOIN users u ON u.id = c.user_id
            WHERE sc.company_id = @CompanyId
            ORDER BY sc.saved_at DESC",
            new { CompanyId = companyId })).ToList();

        var responses = new List<SavedCandidateResponse>();

        foreach (var item in saved)
        {
            var candidateId = (Guid)item.candidate_id;

            // Get top 5 skills for this candidate
            var topSkills = (await connection.QueryAsync<string>(@"
                SELECT skill_name
                FROM candidate_skills
                WHERE candidate_id = @CandidateId
                ORDER BY confidence_score DESC
                OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
                new { CandidateId = candidateId })).ToList();

            // Get recommendations count
            var recommendationsCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM candidate_recommendations WHERE candidate_id = @CandidateId",
                new { CandidateId = candidateId });

            responses.Add(new SavedCandidateResponse
            {
                Id = (Guid)item.id,
                CandidateId = candidateId,
                Candidate = new TalentResponse
                {
                    CandidateId = candidateId,
                    FirstName = (string?)item.first_name,
                    LastName = (string?)item.last_name,
                    DesiredRole = (string?)item.desired_role,
                    LocationPreference = (string?)item.location_preference,
                    RemotePreference = item.remote_preference != null ? (RemotePreference)(int)item.remote_preference : null,
                    Availability = (Availability)(int)item.availability,
                    SeniorityEstimate = item.seniority_estimate != null ? (SeniorityLevel)(int)item.seniority_estimate : null,
                    TopSkills = topSkills,
                    RecommendationsCount = recommendationsCount,
                    LastActiveAt = (DateTime)item.last_active_at,
                    IsSaved = true
                },
                Notes = (string?)item.notes,
                SavedAt = (DateTime)item.saved_at
            });
        }

        return responses;
    }

    /// <summary>
    /// Build SQL expression for location ranking score.
    /// Uses CASE statements to add points for location matches.
    /// </summary>
    private string BuildLocationRankingScore(TalentSearchRequest request)
    {
        var scoreParts = new List<string>();

        // Base score - use CAST to prevent PostgreSQL interpreting as positional reference
        scoreParts.Add("CAST(0 AS INTEGER)");

        // Remote preference boost (+25)
        if (request.LocationRanking?.PreferRemote == true || request.RemotePreference == RemotePreference.Remote)
        {
            scoreParts.Add("CASE WHEN c.remote_preference IN (0, 3) THEN 25 ELSE 0 END"); // 0=Remote, 3=Flexible
        }

        // Same country boost (+15)
        if (!string.IsNullOrEmpty(request.LocationRanking?.PreferCountry) || !string.IsNullOrEmpty(request.Location))
        {
            scoreParts.Add("CASE WHEN LOWER(cl.country) = LOWER(@PreferCountry) THEN 15 ELSE 0 END");
        }

        // Same timezone boost (+10)
        if (!string.IsNullOrEmpty(request.LocationRanking?.PreferTimezone))
        {
            scoreParts.Add("CASE WHEN cl.timezone = @PreferTimezone THEN 10 ELSE 0 END");
        }

        // Willing to relocate boost (+10)
        if (request.LocationRanking?.PreferRelocationFriendly == true)
        {
            scoreParts.Add("CASE WHEN cl.willing_to_relocate = TRUE THEN 10 ELSE 0 END");
        }

        return string.Join(" + ", scoreParts);
    }

    /// <summary>
    /// Add location ranking parameters to the query.
    /// </summary>
    private void AddLocationRankingParameters(DynamicParameters parameters, TalentSearchRequest request)
    {
        // Add country preference (use null instead of DBNull.Value for Dapper compatibility)
        var preferCountry = request.LocationRanking?.PreferCountry ?? request.Location;
        parameters.Add("PreferCountry", string.IsNullOrEmpty(preferCountry) ? null : preferCountry);

        // Add timezone preference
        var preferTimezone = request.LocationRanking?.PreferTimezone;
        parameters.Add("PreferTimezone", string.IsNullOrEmpty(preferTimezone) ? null : preferTimezone);
    }
}
