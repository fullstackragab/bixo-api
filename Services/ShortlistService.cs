using System.Text.Json;
using Dapper;
using bixo_api.Data;
using bixo_api.Models.DTOs.Location;
using bixo_api.Models.DTOs.Shortlist;
using bixo_api.Models.Entities;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services;

public class ShortlistService : IShortlistService
{
    private readonly IDbConnectionFactory _db;
    private readonly IMatchingService _matchingService;

    public ShortlistService(IDbConnectionFactory db, IMatchingService matchingService)
    {
        _db = db;
        _matchingService = matchingService;
    }

    public async Task<List<ShortlistPricingResponse>> GetPricingAsync()
    {
        using var connection = _db.CreateConnection();

        var pricings = await connection.QueryAsync<dynamic>(@"
            SELECT id, name, price, shortlist_count, discount_percent
            FROM shortlist_pricing
            WHERE is_active = TRUE
            ORDER BY shortlist_count");

        return pricings.Select(p => new ShortlistPricingResponse
        {
            Id = (Guid)p.id,
            Name = (string)p.name,
            Price = (decimal)p.price,
            ShortlistCount = (int)p.shortlist_count,
            DiscountPercent = p.discount_percent != null ? (decimal)p.discount_percent : 0
        }).ToList();
    }

    public async Task<ShortlistResponse> CreateRequestAsync(Guid companyId, CreateShortlistRequest request)
    {
        using var connection = _db.CreateConnection();

        var shortlistId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Get effective values from request (prefer new HiringLocation, fall back to legacy)
        var isRemote = request.GetEffectiveIsRemote();
        var locationCountry = request.HiringLocation?.Country;
        var locationCity = request.HiringLocation?.City;
        var locationTimezone = request.HiringLocation?.Timezone;

        await connection.ExecuteAsync(@"
            INSERT INTO shortlist_requests (id, company_id, role_title, tech_stack_required, seniority_required,
                                           location_preference, is_remote, location_country, location_city,
                                           location_timezone, additional_notes, status, created_at)
            VALUES (@Id, @CompanyId, @RoleTitle, @TechStackRequired::jsonb, @SeniorityRequired,
                    @LocationPreference, @IsRemote, @LocationCountry, @LocationCity,
                    @LocationTimezone, @AdditionalNotes, @Status, @CreatedAt)",
            new
            {
                Id = shortlistId,
                CompanyId = companyId,
                RoleTitle = request.RoleTitle,
                TechStackRequired = JsonSerializer.Serialize(request.TechStackRequired),
                SeniorityRequired = request.SeniorityRequired.HasValue ? (int?)request.SeniorityRequired.Value : null,
                LocationPreference = request.LocationPreference,
                IsRemote = isRemote,
                LocationCountry = locationCountry,
                LocationCity = locationCity,
                LocationTimezone = locationTimezone,
                AdditionalNotes = request.AdditionalNotes,
                Status = (int)ShortlistStatus.Pending,
                CreatedAt = now
            });

        return new ShortlistResponse
        {
            Id = shortlistId,
            RoleTitle = request.RoleTitle,
            TechStackRequired = request.TechStackRequired,
            SeniorityRequired = request.SeniorityRequired,
            LocationPreference = request.LocationPreference,
            HiringLocation = new HiringLocationResponse
            {
                IsRemote = isRemote,
                Country = locationCountry,
                City = locationCity,
                Timezone = locationTimezone
            },
            RemoteAllowed = isRemote, // Keep legacy field populated
            AdditionalNotes = request.AdditionalNotes,
            Status = ShortlistStatus.Pending,
            PricePaid = null,
            CreatedAt = now,
            CompletedAt = null,
            CandidatesCount = 0
        };
    }

    public async Task<ShortlistDetailResponse?> GetShortlistAsync(Guid companyId, Guid shortlistId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, company_id, role_title, tech_stack_required, seniority_required,
                   location_preference, is_remote, location_country, location_city, location_timezone,
                   additional_notes, status, price_paid, created_at, completed_at
            FROM shortlist_requests
            WHERE id = @ShortlistId AND company_id = @CompanyId",
            new { ShortlistId = shortlistId, CompanyId = companyId });

        if (shortlist == null) return null;

        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT sc.id, sc.candidate_id, sc.match_score, sc.match_reason, sc.rank, sc.admin_approved,
                   c.first_name, c.last_name, c.desired_role, c.seniority_estimate, c.availability
            FROM shortlist_candidates sc
            JOIN candidates c ON c.id = sc.candidate_id
            WHERE sc.shortlist_request_id = @ShortlistId
            ORDER BY sc.rank",
            new { ShortlistId = shortlistId });

        var status = (ShortlistStatus)shortlist.status;
        var filteredCandidates = candidates
            .Where(c => (bool)c.admin_approved || status == ShortlistStatus.Completed);

        var candidatesList = new List<ShortlistCandidateResponse>();

        foreach (var c in filteredCandidates)
        {
            var skills = await connection.QueryAsync<dynamic>(@"
                SELECT skill_name, confidence_score
                FROM candidate_skills
                WHERE candidate_id = @CandidateId
                ORDER BY confidence_score DESC
                LIMIT 5",
                new { CandidateId = (Guid)c.candidate_id });

            candidatesList.Add(new ShortlistCandidateResponse
            {
                Id = (Guid)c.id,
                CandidateId = (Guid)c.candidate_id,
                FirstName = (string)c.first_name,
                LastName = (string)c.last_name,
                DesiredRole = c.desired_role as string,
                SeniorityEstimate = c.seniority_estimate != null ? (SeniorityLevel?)(int)c.seniority_estimate : null,
                TopSkills = skills.Select(s => (string)s.skill_name).ToList(),
                MatchScore = (int)c.match_score,
                MatchReason = (string)c.match_reason,
                Rank = (int)c.rank,
                Availability = (Availability)c.availability
            });
        }

        // Build HiringLocation response
        var isRemote = shortlist.is_remote as bool? ?? true;
        var locationCountry = shortlist.location_country as string;
        var locationCity = shortlist.location_city as string;
        var locationTimezone = shortlist.location_timezone as string;

        return new ShortlistDetailResponse
        {
            Id = (Guid)shortlist.id,
            RoleTitle = (string)shortlist.role_title,
            TechStackRequired = ParseTechStack(shortlist.tech_stack_required as string),
            SeniorityRequired = shortlist.seniority_required != null ? (SeniorityLevel?)(int)shortlist.seniority_required : null,
            LocationPreference = shortlist.location_preference as string,
            HiringLocation = new HiringLocationResponse
            {
                IsRemote = isRemote,
                Country = locationCountry,
                City = locationCity,
                Timezone = locationTimezone
            },
            RemoteAllowed = isRemote, // Keep legacy field populated
            AdditionalNotes = shortlist.additional_notes as string,
            Status = status,
            PricePaid = shortlist.price_paid != null ? (decimal?)shortlist.price_paid : null,
            CreatedAt = (DateTime)shortlist.created_at,
            CompletedAt = shortlist.completed_at != null ? (DateTime?)shortlist.completed_at : null,
            CandidatesCount = candidatesList.Count,
            Candidates = candidatesList
        };
    }

    public async Task<List<ShortlistResponse>> GetCompanyShortlistsAsync(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var shortlists = await connection.QueryAsync<dynamic>(@"
            SELECT sr.id, sr.role_title, sr.tech_stack_required, sr.seniority_required,
                   sr.location_preference, sr.is_remote, sr.location_country, sr.location_city,
                   sr.location_timezone, sr.additional_notes, sr.status,
                   sr.price_paid, sr.created_at, sr.completed_at,
                   COUNT(sc.id) as candidates_count
            FROM shortlist_requests sr
            LEFT JOIN shortlist_candidates sc ON sc.shortlist_request_id = sr.id
            WHERE sr.company_id = @CompanyId
            GROUP BY sr.id, sr.role_title, sr.tech_stack_required, sr.seniority_required,
                     sr.location_preference, sr.is_remote, sr.location_country, sr.location_city,
                     sr.location_timezone, sr.additional_notes, sr.status,
                     sr.price_paid, sr.created_at, sr.completed_at
            ORDER BY sr.created_at DESC",
            new { CompanyId = companyId });

        return shortlists.Select(s =>
        {
            var isRemote = s.is_remote as bool? ?? true;
            return new ShortlistResponse
            {
                Id = (Guid)s.id,
                RoleTitle = (string)s.role_title,
                TechStackRequired = ParseTechStack(s.tech_stack_required as string),
                SeniorityRequired = s.seniority_required != null ? (SeniorityLevel?)(int)s.seniority_required : null,
                LocationPreference = s.location_preference as string,
                HiringLocation = new HiringLocationResponse
                {
                    IsRemote = isRemote,
                    Country = s.location_country as string,
                    City = s.location_city as string,
                    Timezone = s.location_timezone as string
                },
                RemoteAllowed = isRemote, // Keep legacy field populated
                AdditionalNotes = s.additional_notes as string,
                Status = (ShortlistStatus)s.status,
                PricePaid = s.price_paid != null ? (decimal?)s.price_paid : null,
                CreatedAt = (DateTime)s.created_at,
                CompletedAt = s.completed_at != null ? (DateTime?)s.completed_at : null,
                CandidatesCount = (int)s.candidates_count
            };
        }).ToList();
    }

    public async Task ProcessShortlistAsync(Guid shortlistId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, company_id, role_title, tech_stack_required, seniority_required,
                   location_preference, is_remote, location_country, location_city, location_timezone,
                   additional_notes, status
            FROM shortlist_requests
            WHERE id = @ShortlistId",
            new { ShortlistId = shortlistId });

        if (shortlist == null) return;

        await connection.ExecuteAsync(
            "UPDATE shortlist_requests SET status = @Status WHERE id = @Id",
            new { Status = (int)ShortlistStatus.Processing, Id = shortlistId });

        // Create ShortlistRequest entity for matching
        var request = new ShortlistRequest
        {
            Id = (Guid)shortlist.id,
            CompanyId = (Guid)shortlist.company_id,
            RoleTitle = (string)shortlist.role_title,
            TechStackRequired = shortlist.tech_stack_required as string,
            SeniorityRequired = shortlist.seniority_required != null ? (SeniorityLevel?)(int)shortlist.seniority_required : null,
            LocationPreference = shortlist.location_preference as string,
            IsRemote = shortlist.is_remote as bool? ?? true,
            LocationCountry = shortlist.location_country as string,
            LocationCity = shortlist.location_city as string,
            LocationTimezone = shortlist.location_timezone as string,
            AdditionalNotes = shortlist.additional_notes as string,
            Status = ShortlistStatus.Processing
        };

        var matches = await _matchingService.FindMatchesAsync(request);

        var rank = 1;
        foreach (var match in matches)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO shortlist_candidates (id, shortlist_request_id, candidate_id, match_score, match_reason, rank, admin_approved, added_at)
                VALUES (@Id, @ShortlistRequestId, @CandidateId, @MatchScore, @MatchReason, @Rank, FALSE, @AddedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    ShortlistRequestId = shortlistId,
                    CandidateId = match.CandidateId,
                    MatchScore = match.Score,
                    MatchReason = match.Reason,
                    Rank = rank++,
                    AddedAt = DateTime.UtcNow
                });
        }
    }

    private List<string> ParseTechStack(string? techStackJson)
    {
        if (string.IsNullOrEmpty(techStackJson)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(techStackJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
