using System.Data;
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
    private readonly IEmailService _emailService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ShortlistService> _logger;

    // Similarity threshold for detecting follow-up shortlists (0-100)
    private const int FOLLOW_UP_SIMILARITY_THRESHOLD = 70;

    // Default days for follow-up pricing eligibility
    private const int DEFAULT_FOLLOW_UP_DAYS = 30;

    // Default base price per shortlist
    private const decimal DEFAULT_BASE_PRICE = 299m;

    public ShortlistService(
        IDbConnectionFactory db,
        IMatchingService matchingService,
        IEmailService emailService,
        IPaymentService paymentService,
        ILogger<ShortlistService> logger)
    {
        _db = db;
        _matchingService = matchingService;
        _emailService = emailService;
        _paymentService = paymentService;
        _logger = logger;
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

        // Detect if this is a follow-up shortlist
        Guid? previousRequestId = request.PreviousRequestId;
        string pricingType = "new";
        decimal followUpDiscount = 0;

        // If no explicit previous request, try to detect similar shortlists
        if (!previousRequestId.HasValue)
        {
            var similarShortlist = await DetectSimilarShortlistAsync(connection, companyId, request, now);
            if (similarShortlist.HasValue)
            {
                previousRequestId = similarShortlist.Value.Id;
            }
        }

        // If this is a follow-up, calculate pricing
        if (previousRequestId.HasValue)
        {
            var previousRequest = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id, created_at, status FROM shortlist_requests
                WHERE id = @PreviousRequestId AND company_id = @CompanyId AND status = 2",
                new { PreviousRequestId = previousRequestId.Value, CompanyId = companyId });

            if (previousRequest != null)
            {
                pricingType = "follow_up";
                var previousCreatedAt = (DateTime)previousRequest.created_at;
                followUpDiscount = await CalculateFollowUpDiscountAsync(connection, previousCreatedAt, now);
            }
            else
            {
                // Invalid previous request - treat as new
                previousRequestId = null;
            }
        }

        await connection.ExecuteAsync(@"
            INSERT INTO shortlist_requests (id, company_id, role_title, tech_stack_required, seniority_required,
                                           location_preference, is_remote, location_country, location_city,
                                           location_timezone, additional_notes, status, created_at,
                                           previous_request_id, pricing_type, follow_up_discount)
            VALUES (@Id, @CompanyId, @RoleTitle, @TechStackRequired::jsonb, @SeniorityRequired,
                    @LocationPreference, @IsRemote, @LocationCountry, @LocationCity,
                    @LocationTimezone, @AdditionalNotes, @Status, @CreatedAt,
                    @PreviousRequestId, @PricingType, @FollowUpDiscount)",
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
                Status = (int)ShortlistStatus.Submitted,
                CreatedAt = now,
                PreviousRequestId = previousRequestId,
                PricingType = pricingType,
                FollowUpDiscount = followUpDiscount
            });

        // Get company name for notification
        var companyName = await connection.QueryFirstOrDefaultAsync<string>(@"
            SELECT company_name FROM companies WHERE id = @CompanyId",
            new { CompanyId = companyId }) ?? "Unknown Company";

        // Build location string
        var locationStr = isRemote ? "Remote" :
            string.Join(", ", new[] { locationCity, locationCountry }.Where(x => !string.IsNullOrEmpty(x)));

        // Send email notification
        _ = _emailService.SendShortlistCreatedNotificationAsync(new ShortlistCreatedNotification
        {
            ShortlistId = shortlistId,
            CompanyName = companyName,
            RoleTitle = request.RoleTitle,
            TechStack = request.TechStackRequired ?? new List<string>(),
            Seniority = request.SeniorityRequired?.ToString(),
            Location = locationStr,
            IsRemote = isRemote,
            AdditionalNotes = request.AdditionalNotes,
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
            Status = ShortlistStatus.Submitted,
            PricePaid = null,
            CreatedAt = now,
            CompletedAt = null,
            CandidatesCount = 0,
            PreviousRequestId = previousRequestId,
            PricingType = pricingType,
            FollowUpDiscount = followUpDiscount,
            NewCandidatesCount = 0,
            RepeatedCandidatesCount = 0
        };
    }

    /// <summary>
    /// Detect similar shortlists to determine if this should be a follow-up.
    /// Uses role title, seniority, tech stack, and location similarity.
    /// </summary>
    private async Task<(Guid Id, DateTime CreatedAt)?> DetectSimilarShortlistAsync(
        System.Data.IDbConnection connection,
        Guid companyId,
        CreateShortlistRequest request,
        DateTime now)
    {
        // Look for completed shortlists from the same company within the last X days
        var cutoffDate = now.AddDays(-DEFAULT_FOLLOW_UP_DAYS);

        var recentShortlists = await connection.QueryAsync<dynamic>(@"
            SELECT id, role_title, seniority_required, tech_stack_required, is_remote,
                   location_country, location_city, created_at
            FROM shortlist_requests
            WHERE company_id = @CompanyId
                AND status = 2  -- Completed
                AND created_at > @CutoffDate
            ORDER BY created_at DESC",
            new { CompanyId = companyId, CutoffDate = cutoffDate });

        foreach (var shortlist in recentShortlists)
        {
            var similarity = CalculateSimilarity(request, shortlist);
            if (similarity >= FOLLOW_UP_SIMILARITY_THRESHOLD)
            {
                return ((Guid)shortlist.id, (DateTime)shortlist.created_at);
            }
        }

        return null;
    }

    /// <summary>
    /// Calculate similarity between a new request and an existing shortlist.
    /// Returns a score from 0-100.
    /// </summary>
    private int CalculateSimilarity(CreateShortlistRequest request, dynamic existingShortlist)
    {
        int score = 0;

        // Role title similarity (30%)
        var existingRole = (string)existingShortlist.role_title;
        if (string.Equals(request.RoleTitle, existingRole, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }
        else if (request.RoleTitle.Contains(existingRole, StringComparison.OrdinalIgnoreCase) ||
                 existingRole.Contains(request.RoleTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        // Seniority similarity (20%)
        var existingSeniority = existingShortlist.seniority_required as int?;
        if (request.SeniorityRequired.HasValue && existingSeniority.HasValue)
        {
            if ((int)request.SeniorityRequired.Value == existingSeniority.Value)
            {
                score += 20;
            }
        }
        else if (!request.SeniorityRequired.HasValue && !existingSeniority.HasValue)
        {
            score += 10; // Both unspecified
        }

        // Remote/location similarity (15%)
        var existingIsRemote = existingShortlist.is_remote as bool? ?? true;
        if (request.GetEffectiveIsRemote() == existingIsRemote)
        {
            score += 10;
        }

        var existingCountry = existingShortlist.location_country as string;
        if (!string.IsNullOrEmpty(request.HiringLocation?.Country) &&
            string.Equals(request.HiringLocation.Country, existingCountry, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        // Tech stack similarity (35%)
        var existingTechStack = ParseTechStack(existingShortlist.tech_stack_required as string);
        if (request.TechStackRequired.Any() && existingTechStack.Any())
        {
            var intersection = request.TechStackRequired
                .Select(s => s.ToLower())
                .Intersect(existingTechStack.Select(s => s.ToLower()))
                .Count();

            var union = request.TechStackRequired
                .Select(s => s.ToLower())
                .Union(existingTechStack.Select(s => s.ToLower()))
                .Count();

            if (union > 0)
            {
                var jaccardSimilarity = (double)intersection / union;
                score += (int)(jaccardSimilarity * 35);
            }
        }
        else if (!request.TechStackRequired.Any() && !existingTechStack.Any())
        {
            score += 15; // Both unspecified
        }

        return score;
    }

    /// <summary>
    /// Calculate follow-up discount based on days since previous shortlist.
    /// </summary>
    private async Task<decimal> CalculateFollowUpDiscountAsync(
        System.Data.IDbConnection connection,
        DateTime previousCreatedAt,
        DateTime now)
    {
        var daysSince = (int)(now - previousCreatedAt).TotalDays;

        // Get applicable pricing rule
        var rule = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT discount_percent
            FROM follow_up_pricing_rules
            WHERE is_active = TRUE AND days_threshold >= @DaysSince
            ORDER BY days_threshold ASC
            LIMIT 1",
            new { DaysSince = daysSince });

        if (rule != null)
        {
            return (decimal)rule.discount_percent;
        }

        return 0;
    }

    public async Task<ShortlistDetailResponse?> GetShortlistAsync(Guid companyId, Guid shortlistId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, company_id, role_title, tech_stack_required, seniority_required,
                   location_preference, is_remote, location_country, location_city, location_timezone,
                   additional_notes, status, price_paid, created_at, completed_at,
                   previous_request_id, pricing_type, follow_up_discount,
                   proposed_price, proposed_candidates, scope_proposed_at, scope_approval_notes
            FROM shortlist_requests
            WHERE id = @ShortlistId AND company_id = @CompanyId",
            new { ShortlistId = shortlistId, CompanyId = companyId });

        if (shortlist == null) return null;

        var candidates = await connection.QueryAsync<dynamic>(@"
            SELECT sc.id, sc.candidate_id, sc.match_score, sc.match_reason, sc.rank, sc.admin_approved,
                   sc.is_new, sc.previously_recommended_in, sc.re_inclusion_reason,
                   c.first_name, c.last_name, c.desired_role, c.seniority_estimate, c.availability
            FROM shortlist_candidates sc
            JOIN candidates c ON c.id = sc.candidate_id
            WHERE sc.shortlist_request_id = @ShortlistId
            ORDER BY sc.rank",
            new { ShortlistId = shortlistId });

        var status = (ShortlistStatus)shortlist.status;
        var filteredCandidates = candidates
            .Where(c => (bool)c.admin_approved || status == ShortlistStatus.Delivered);

        var candidatesList = new List<ShortlistCandidateResponse>();
        int newCandidatesCount = 0;
        int repeatedCandidatesCount = 0;

        foreach (var c in filteredCandidates)
        {
            var skills = await connection.QueryAsync<dynamic>(@"
                SELECT skill_name, confidence_score
                FROM candidate_skills
                WHERE candidate_id = @CandidateId
                ORDER BY confidence_score DESC
                LIMIT 5",
                new { CandidateId = (Guid)c.candidate_id });

            var isNew = c.is_new as bool? ?? true;
            if (isNew)
                newCandidatesCount++;
            else
                repeatedCandidatesCount++;

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
                Availability = (Availability)c.availability,
                IsNew = isNew,
                PreviouslyRecommendedIn = c.previously_recommended_in as Guid?,
                ReInclusionReason = c.re_inclusion_reason as string
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
            Candidates = candidatesList,
            PreviousRequestId = shortlist.previous_request_id as Guid?,
            PricingType = shortlist.pricing_type as string ?? "new",
            FollowUpDiscount = shortlist.follow_up_discount as decimal? ?? 0,
            NewCandidatesCount = newCandidatesCount,
            RepeatedCandidatesCount = repeatedCandidatesCount,
            // Pricing proposal fields
            ProposedPrice = shortlist.proposed_price as decimal?,
            ProposedCandidates = shortlist.proposed_candidates as int?,
            ScopeProposedAt = shortlist.scope_proposed_at as DateTime?,
            ScopeNotes = shortlist.scope_approval_notes as string
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
                   sr.previous_request_id, sr.pricing_type, sr.follow_up_discount,
                   sr.proposed_price, sr.proposed_candidates, sr.scope_proposed_at, sr.scope_approval_notes,
                   COUNT(sc.id) as candidates_count,
                   COUNT(CASE WHEN sc.is_new = TRUE THEN 1 END) as new_candidates_count,
                   COUNT(CASE WHEN sc.is_new = FALSE THEN 1 END) as repeated_candidates_count
            FROM shortlist_requests sr
            LEFT JOIN shortlist_candidates sc ON sc.shortlist_request_id = sr.id
            WHERE sr.company_id = @CompanyId
            GROUP BY sr.id, sr.role_title, sr.tech_stack_required, sr.seniority_required,
                     sr.location_preference, sr.is_remote, sr.location_country, sr.location_city,
                     sr.location_timezone, sr.additional_notes, sr.status,
                     sr.price_paid, sr.created_at, sr.completed_at,
                     sr.previous_request_id, sr.pricing_type, sr.follow_up_discount,
                     sr.proposed_price, sr.proposed_candidates, sr.scope_proposed_at, sr.scope_approval_notes
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
                CandidatesCount = (int)s.candidates_count,
                PreviousRequestId = s.previous_request_id as Guid?,
                PricingType = s.pricing_type as string ?? "new",
                FollowUpDiscount = s.follow_up_discount as decimal? ?? 0,
                NewCandidatesCount = (int)(s.new_candidates_count ?? 0),
                RepeatedCandidatesCount = (int)(s.repeated_candidates_count ?? 0),
                // Pricing proposal fields
                ProposedPrice = s.proposed_price as decimal?,
                ProposedCandidates = s.proposed_candidates as int?,
                ScopeProposedAt = s.scope_proposed_at as DateTime?,
                ScopeNotes = s.scope_approval_notes as string
            };
        }).ToList();
    }

    public async Task ProcessShortlistAsync(Guid shortlistId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, company_id, role_title, tech_stack_required, seniority_required,
                   location_preference, is_remote, location_country, location_city, location_timezone,
                   additional_notes, status, previous_request_id, pricing_type
            FROM shortlist_requests
            WHERE id = @ShortlistId",
            new { ShortlistId = shortlistId });

        if (shortlist == null) return;

        await connection.ExecuteAsync(
            "UPDATE shortlist_requests SET status = @Status WHERE id = @Id",
            new { Status = (int)ShortlistStatus.Processing, Id = shortlistId });

        // Check if this is a follow-up shortlist
        var previousRequestId = shortlist.previous_request_id as Guid?;
        var isFollowUp = previousRequestId.HasValue;
        DateTime? previousShortlistCreatedAt = null;
        HashSet<Guid>? excludeCandidateIds = null;

        if (isFollowUp)
        {
            // Get previous shortlist info and candidates to exclude
            var previousRequest = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT created_at FROM shortlist_requests WHERE id = @PreviousRequestId",
                new { PreviousRequestId = previousRequestId.Value });

            if (previousRequest != null)
            {
                previousShortlistCreatedAt = previousRequest.created_at as DateTime?;
            }

            // Get all candidates from the previous shortlist chain (recursively)
            excludeCandidateIds = await GetPreviousCandidatesAsync(connection, previousRequestId.Value);
        }

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

        // Find matches with candidate exclusion and freshness boost
        var matches = await _matchingService.FindMatchesAsync(
            request,
            maxResults: 15,
            excludeCandidateIds: excludeCandidateIds,
            isFollowUp: isFollowUp,
            previousShortlistCreatedAt: previousShortlistCreatedAt);

        var rank = 1;
        foreach (var match in matches)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO shortlist_candidates (id, shortlist_request_id, candidate_id, match_score, match_reason, rank, admin_approved, added_at, is_new, previously_recommended_in, re_inclusion_reason)
                VALUES (@Id, @ShortlistRequestId, @CandidateId, @MatchScore, @MatchReason, @Rank, FALSE, @AddedAt, @IsNew, @PreviouslyRecommendedIn, @ReInclusionReason)",
                new
                {
                    Id = Guid.NewGuid(),
                    ShortlistRequestId = shortlistId,
                    CandidateId = match.CandidateId,
                    MatchScore = match.Score,
                    MatchReason = match.Reason,
                    Rank = rank++,
                    AddedAt = DateTime.UtcNow,
                    IsNew = match.IsNew,
                    PreviouslyRecommendedIn = match.IsNew ? (Guid?)null : previousRequestId,
                    ReInclusionReason = match.ReInclusionReason
                });
        }
    }

    /// <summary>
    /// Get all candidates from the previous shortlist chain (including recursive follow-ups).
    /// </summary>
    private async Task<HashSet<Guid>> GetPreviousCandidatesAsync(System.Data.IDbConnection connection, Guid previousRequestId)
    {
        var candidates = new HashSet<Guid>();
        var currentRequestId = previousRequestId;

        // Walk through the chain of previous shortlists
        while (currentRequestId != Guid.Empty)
        {
            // Get candidates from this shortlist
            var shortlistCandidates = await connection.QueryAsync<Guid>(@"
                SELECT candidate_id FROM shortlist_candidates
                WHERE shortlist_request_id = @RequestId",
                new { RequestId = currentRequestId });

            foreach (var candidateId in shortlistCandidates)
            {
                candidates.Add(candidateId);
            }

            // Get the previous request in the chain
            var previousId = await connection.QueryFirstOrDefaultAsync<Guid?>(@"
                SELECT previous_request_id FROM shortlist_requests
                WHERE id = @RequestId",
                new { RequestId = currentRequestId });

            currentRequestId = previousId ?? Guid.Empty;
        }

        return candidates;
    }

    public async Task<ShortlistPriceEstimate> GetPriceEstimateAsync(Guid shortlistRequestId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, pricing_type, follow_up_discount,
                   (SELECT COUNT(*) FROM shortlist_candidates WHERE shortlist_request_id = sr.id AND admin_approved = TRUE) as candidates_count
            FROM shortlist_requests sr
            WHERE id = @ShortlistRequestId",
            new { ShortlistRequestId = shortlistRequestId });

        if (shortlist == null)
        {
            return new ShortlistPriceEstimate
            {
                ShortlistRequestId = shortlistRequestId,
                BasePrice = 0,
                FinalPrice = 0
            };
        }

        var pricingType = shortlist.pricing_type as string ?? "new";
        var followUpDiscount = shortlist.follow_up_discount as decimal? ?? 0;
        var candidatesCount = (int)(shortlist.candidates_count ?? 0);

        // Get base price from shortlist_pricing table
        var basePrice = await connection.QueryFirstOrDefaultAsync<decimal?>(@"
            SELECT price FROM shortlist_pricing
            WHERE is_active = TRUE
            ORDER BY shortlist_count
            LIMIT 1") ?? DEFAULT_BASE_PRICE;

        var discountAmount = basePrice * (followUpDiscount / 100);
        var finalPrice = basePrice - discountAmount;

        return new ShortlistPriceEstimate
        {
            ShortlistRequestId = shortlistRequestId,
            BasePrice = basePrice,
            FollowUpDiscount = followUpDiscount,
            FinalPrice = finalPrice,
            PricingType = pricingType,
            CandidatesRequested = candidatesCount
        };
    }

    public async Task<ShortlistDeliveryResult> DeliverShortlistAsync(Guid shortlistRequestId, ShortlistDeliveryRequest request)
    {
        using var connection = _db.CreateConnection();

        // Verify shortlist exists and has payment
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT sr.id, sr.status, sr.payment_id, sr.role_title, sr.company_id, p.status as payment_status, p.amount_authorized,
                   u.email as company_email
            FROM shortlist_requests sr
            LEFT JOIN payments p ON p.id = sr.payment_id
            LEFT JOIN companies c ON c.id = sr.company_id
            LEFT JOIN users u ON u.id = c.user_id
            WHERE sr.id = @ShortlistRequestId",
            new { ShortlistRequestId = shortlistRequestId });

        if (shortlist == null)
        {
            return new ShortlistDeliveryResult
            {
                Success = false,
                ErrorMessage = "Shortlist not found"
            };
        }

        var paymentId = shortlist.payment_id as Guid?;
        var paymentStatus = shortlist.payment_status as string;

        // Determine outcome
        string outcomeStatus;
        decimal? discountPercent = null;
        decimal? finalAmount = request.OverridePrice;

        if (request.CandidatesDelivered == 0)
        {
            outcomeStatus = "no_match";
        }
        else if (request.CandidatesDelivered >= request.CandidatesRequested)
        {
            outcomeStatus = "fulfilled";
        }
        else
        {
            outcomeStatus = "partial";
            // Calculate partial discount based on delivery ratio
            if (!finalAmount.HasValue)
            {
                var deliveryRatio = (decimal)request.CandidatesDelivered / request.CandidatesRequested;
                discountPercent = (1 - deliveryRatio) * 100;
            }
        }

        // Step 1: Mark shortlist as Delivered (candidates now visible to company)
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status, delivered_at = @DeliveredAt
            WHERE id = @ShortlistRequestId",
            new
            {
                Status = (int)ShortlistStatus.Delivered,
                DeliveredAt = now,
                ShortlistRequestId = shortlistRequestId
            });

        _logger.LogInformation("Shortlist {ShortlistId} delivered at {DeliveredAt}", shortlistRequestId, now);

        // Step 2: Capture payment AFTER delivery
        string paymentAction = "no_payment";
        decimal amountCaptured = 0;

        if (paymentId.HasValue && paymentStatus == "authorized")
        {
            var outcome = new ShortlistOutcome
            {
                Status = outcomeStatus,
                CandidatesDelivered = request.CandidatesDelivered,
                CandidatesRequested = request.CandidatesRequested,
                DiscountPercent = discountPercent,
                FinalAmount = finalAmount
            };

            var finalizationResult = await _paymentService.FinalizePaymentAsync(shortlistRequestId, outcome);

            if (!finalizationResult.Success)
            {
                // CRITICAL: Delivery succeeded but capture failed
                // Lock candidate visibility and flag for manual resolution
                _logger.LogError(
                    "Payment capture failed for shortlist {ShortlistId} after delivery. Manual resolution required. Error: {Error}",
                    shortlistRequestId, finalizationResult.ErrorMessage);

                // Mark shortlist with capture failure - do NOT change delivered status
                await connection.ExecuteAsync(@"
                    UPDATE shortlist_requests
                    SET cancellation_reason = @Reason
                    WHERE id = @ShortlistRequestId",
                    new
                    {
                        Reason = $"CAPTURE_FAILED: {finalizationResult.ErrorMessage}",
                        ShortlistRequestId = shortlistRequestId
                    });

                return new ShortlistDeliveryResult
                {
                    Success = true, // Delivery succeeded
                    PaymentAction = "capture_failed",
                    AmountCaptured = 0,
                    ErrorMessage = $"Shortlist delivered but payment capture failed: {finalizationResult.ErrorMessage}"
                };
            }

            paymentAction = finalizationResult.Action;
            amountCaptured = finalizationResult.AmountCaptured;

            // Step 3: Mark as Completed after successful capture
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status, completed_at = @CompletedAt, final_price = @FinalPrice
                WHERE id = @ShortlistRequestId",
                new
                {
                    Status = (int)ShortlistStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    FinalPrice = amountCaptured,
                    ShortlistRequestId = shortlistRequestId
                });

            _logger.LogInformation(
                "Payment captured for shortlist {ShortlistId}. Action: {Action}, Amount: {Amount}",
                shortlistRequestId, paymentAction, amountCaptured);
        }
        else if (!paymentId.HasValue)
        {
            // No payment associated - mark as completed directly
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status, completed_at = @CompletedAt
                WHERE id = @ShortlistRequestId",
                new
                {
                    Status = (int)ShortlistStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    ShortlistRequestId = shortlistRequestId
                });
        }

        // Send shortlist delivered email to company (fire and forget)
        var companyEmail = shortlist.company_email as string;
        var roleTitle = shortlist.role_title as string;
        var companyId = (Guid)shortlist.company_id;
        if (!string.IsNullOrEmpty(companyEmail))
        {
            _ = _emailService.SendShortlistDeliveredEmailAsync(new ShortlistDeliveredNotification
            {
                Email = companyEmail,
                RoleTitle = roleTitle ?? "Your role",
                CandidatesCount = request.CandidatesDelivered,
                ShortlistId = shortlistRequestId
            });
        }

        // Send system message to all approved candidates
        await SendShortlistedSystemMessagesAsync(connection, shortlistRequestId, companyId);

        return new ShortlistDeliveryResult
        {
            Success = true,
            PaymentAction = paymentAction,
            AmountCaptured = amountCaptured
        };
    }

    /// <summary>
    /// Sends immutable system message to all approved candidates in a shortlist.
    /// </summary>
    private async Task SendShortlistedSystemMessagesAsync(IDbConnection connection, Guid shortlistRequestId, Guid companyId)
    {
        const string systemMessage = @"You were shortlisted because your background matches this role.

This does not mean you're expected to respond, interview, or accept anything.

If the role isn't relevant or the timing isn't right, you can safely decline — no explanation required.

Declining will not affect your visibility for future opportunities.";

        // Get all approved candidates who haven't received a system shortlisted message yet
        var approvedCandidates = await connection.QueryAsync<Guid>(@"
            SELECT sc.candidate_id
            FROM shortlist_candidates sc
            WHERE sc.shortlist_request_id = @ShortlistRequestId
              AND sc.admin_approved = TRUE
              AND NOT EXISTS (
                  SELECT 1 FROM shortlist_messages sm
                  WHERE sm.shortlist_id = @ShortlistRequestId
                    AND sm.candidate_id = sc.candidate_id
                    AND sm.is_system = TRUE
                    AND sm.message_type = 'shortlisted'
              )",
            new { ShortlistRequestId = shortlistRequestId });

        var now = DateTime.UtcNow;
        foreach (var candidateId in approvedCandidates)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO shortlist_messages (id, shortlist_id, company_id, candidate_id, message, is_system, message_type, created_at)
                VALUES (@Id, @ShortlistId, @CompanyId, @CandidateId, @Message, TRUE, 'shortlisted', @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    ShortlistId = shortlistRequestId,
                    CompanyId = companyId,
                    CandidateId = candidateId,
                    Message = systemMessage,
                    CreatedAt = now
                });
        }
    }

    // === Scope Confirmation Flow ===

    public async Task<ScopeProposalResult> ProposeScopeAsync(Guid shortlistRequestId, ScopeProposalRequest request)
    {
        using var connection = _db.CreateConnection();

        // Verify shortlist exists and is in correct state
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status FROM shortlist_requests WHERE id = @Id",
            new { Id = shortlistRequestId });

        if (shortlist == null)
        {
            return new ScopeProposalResult { Success = false, ErrorMessage = "Shortlist not found" };
        }

        var currentStatus = (ShortlistStatus)(int)shortlist.status;
        if (currentStatus != ShortlistStatus.Submitted && currentStatus != ShortlistStatus.Processing)
        {
            return new ScopeProposalResult { Success = false, ErrorMessage = $"Cannot set pricing: shortlist is in {currentStatus} status. Must be Submitted or Processing." };
        }

        // Update with proposed scope and price
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status,
                proposed_candidates = @ProposedCandidates,
                proposed_price = @ProposedPrice,
                scope_proposed_at = @Now,
                scope_approval_notes = @Notes
            WHERE id = @Id",
            new
            {
                Status = (int)ShortlistStatus.PricingPending,
                ProposedCandidates = request.ProposedCandidates,
                ProposedPrice = request.ProposedPrice,
                Now = DateTime.UtcNow,
                Notes = request.Notes,
                Id = shortlistRequestId
            });

        // TODO: Send email to company notifying of scope proposal

        return new ScopeProposalResult { Success = true };
    }

    public async Task<ScopeApprovalResult> ApproveScopeAsync(Guid companyId, Guid shortlistRequestId, ScopeApprovalRequest request)
    {
        // CRITICAL: This is the ONLY point where payment authorization may occur
        // Company must explicitly confirm approval

        if (!request.ConfirmApproval)
        {
            return new ScopeApprovalResult { Success = false, ErrorMessage = "Explicit approval confirmation required" };
        }

        using var connection = _db.CreateConnection();

        // Verify shortlist exists, belongs to company, and is in correct state
        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status, company_id, proposed_candidates, proposed_price
            FROM shortlist_requests
            WHERE id = @Id AND company_id = @CompanyId",
            new { Id = shortlistRequestId, CompanyId = companyId });

        if (shortlist == null)
        {
            return new ScopeApprovalResult { Success = false, ErrorMessage = "Shortlist not found" };
        }

        if ((int)shortlist.status != (int)ShortlistStatus.PricingPending)
        {
            return new ScopeApprovalResult { Success = false, ErrorMessage = "Shortlist does not have a pending scope proposal" };
        }

        decimal proposedPrice = shortlist.proposed_price;
        if (proposedPrice <= 0)
        {
            return new ScopeApprovalResult { Success = false, ErrorMessage = "Invalid proposed price" };
        }

        // Now initiate payment authorization with the EXACT approved price
        var paymentRequest = new PaymentInitiationRequest
        {
            CompanyId = companyId,
            ShortlistRequestId = shortlistRequestId,
            Amount = proposedPrice,
            Currency = "USD",
            Provider = request.Provider,
            Description = $"Shortlist authorization for request {shortlistRequestId}"
        };

        var paymentResult = await _paymentService.InitiatePaymentAsync(paymentRequest);

        if (!paymentResult.Success)
        {
            return new ScopeApprovalResult { Success = false, ErrorMessage = paymentResult.ErrorMessage };
        }

        // Update shortlist status to ScopeApproved
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status,
                scope_approved_at = @Now,
                payment_id = @PaymentId
            WHERE id = @Id",
            new
            {
                Status = (int)ShortlistStatus.PricingApproved,
                Now = DateTime.UtcNow,
                PaymentId = paymentResult.PaymentId,
                Id = shortlistRequestId
            });

        return new ScopeApprovalResult
        {
            Success = true,
            PaymentId = paymentResult.PaymentId,
            ClientSecret = paymentResult.ClientSecret,
            ApprovalUrl = paymentResult.ApprovalUrl,
            EscrowAddress = paymentResult.EscrowAddress
        };
    }

    /// <summary>
    /// Step 3: Company approves pricing (no payment yet)
    /// </summary>
    public async Task ApprovePricingAsync(Guid companyId, Guid shortlistRequestId)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status, proposed_price
            FROM shortlist_requests
            WHERE id = @Id AND company_id = @CompanyId",
            new { Id = shortlistRequestId, CompanyId = companyId });

        if (shortlist == null)
        {
            throw new InvalidOperationException("Shortlist not found");
        }

        if ((int)shortlist.status != (int)ShortlistStatus.PricingPending)
        {
            throw new InvalidOperationException($"Cannot approve pricing: shortlist is not in PricingPending status (current: {(ShortlistStatus)(int)shortlist.status})");
        }

        if (shortlist.proposed_price == null || (decimal)shortlist.proposed_price <= 0)
        {
            throw new InvalidOperationException("Cannot approve pricing: no valid price has been set");
        }

        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status, pricing_approved_at = @Now, price_amount = proposed_price
            WHERE id = @Id",
            new
            {
                Status = (int)ShortlistStatus.PricingApproved,
                Now = DateTime.UtcNow,
                Id = shortlistRequestId
            });
    }

    /// <summary>
    /// Company declines pricing - returns to Processing so admin can propose new price
    /// </summary>
    public async Task DeclinePricingAsync(Guid companyId, Guid shortlistRequestId, string? reason)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status, proposed_price
            FROM shortlist_requests
            WHERE id = @Id AND company_id = @CompanyId",
            new { Id = shortlistRequestId, CompanyId = companyId });

        if (shortlist == null)
        {
            throw new InvalidOperationException("Shortlist not found");
        }

        if ((int)shortlist.status != (int)ShortlistStatus.PricingPending)
        {
            throw new InvalidOperationException($"Cannot decline pricing: shortlist is not in PricingPending status (current: {(ShortlistStatus)(int)shortlist.status})");
        }

        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET status = @Status,
                proposed_price = NULL,
                proposed_candidates = NULL,
                scope_proposed_at = NULL,
                scope_approval_notes = CASE WHEN @Reason IS NOT NULL THEN COALESCE(scope_approval_notes || E'\n', '') || 'Declined: ' || @Reason ELSE scope_approval_notes END
            WHERE id = @Id",
            new
            {
                Status = (int)ShortlistStatus.Processing,
                Reason = reason,
                Id = shortlistRequestId
            });
    }

    /// <summary>
    /// Step 4: Authorize payment (hold funds, no capture)
    /// </summary>
    public async Task<PaymentAuthorizationResult> AuthorizePaymentAsync(Guid companyId, Guid shortlistRequestId, string provider)
    {
        using var connection = _db.CreateConnection();

        var shortlist = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status, price_amount, proposed_price
            FROM shortlist_requests
            WHERE id = @Id AND company_id = @CompanyId",
            new { Id = shortlistRequestId, CompanyId = companyId });

        if (shortlist == null)
        {
            throw new InvalidOperationException("Shortlist not found");
        }

        if ((int)shortlist.status != (int)ShortlistStatus.PricingApproved)
        {
            throw new InvalidOperationException($"Cannot authorize payment: pricing must be approved first (current: {(ShortlistStatus)(int)shortlist.status})");
        }

        decimal amount = shortlist.price_amount ?? shortlist.proposed_price;
        if (amount <= 0)
        {
            throw new InvalidOperationException("Cannot authorize payment: no valid price");
        }

        // Initiate payment authorization
        var paymentRequest = new PaymentInitiationRequest
        {
            CompanyId = companyId,
            ShortlistRequestId = shortlistRequestId,
            Amount = amount,
            Currency = "USD",
            Provider = provider,
            Description = $"Shortlist authorization for request {shortlistRequestId}"
        };

        var paymentResult = await _paymentService.InitiatePaymentAsync(paymentRequest);

        if (!paymentResult.Success)
        {
            // Authorization failed - keep status at PricingApproved so company can retry
            throw new InvalidOperationException(paymentResult.ErrorMessage ?? "Payment authorization failed");
        }

        // Link payment to shortlist but do NOT set to Authorized yet
        // Status will be set to Authorized after frontend confirms with Stripe
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests
            SET payment_id = @PaymentId, payment_authorization_id = @AuthId
            WHERE id = @Id",
            new
            {
                PaymentId = paymentResult.PaymentId,
                AuthId = paymentResult.ClientSecret ?? paymentResult.ApprovalUrl ?? paymentResult.EscrowAddress,
                Id = shortlistRequestId
            });

        return new PaymentAuthorizationResult
        {
            Success = true,
            PaymentId = paymentResult.PaymentId ?? Guid.Empty,
            ClientSecret = paymentResult.ClientSecret,
            ApprovalUrl = paymentResult.ApprovalUrl,
            EscrowAddress = paymentResult.EscrowAddress
        };
    }

    public async Task<List<ScopeProposalResponse>> GetPendingScopeProposalsAsync(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var proposals = await connection.QueryAsync<dynamic>(@"
            SELECT id, role_title, proposed_candidates, proposed_price, scope_proposed_at, scope_approval_notes
            FROM shortlist_requests
            WHERE company_id = @CompanyId AND status = @Status
            ORDER BY scope_proposed_at DESC",
            new { CompanyId = companyId, Status = (int)ShortlistStatus.PricingPending });

        return proposals.Select(p => new ScopeProposalResponse
        {
            ShortlistId = (Guid)p.id,
            RoleTitle = p.role_title ?? "",
            ProposedCandidates = (int)(p.proposed_candidates ?? 0),
            ProposedPrice = (decimal)(p.proposed_price ?? 0),
            ProposedAt = (DateTime)(p.scope_proposed_at ?? DateTime.MinValue),
            Notes = p.scope_approval_notes as string
        }).ToList();
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
