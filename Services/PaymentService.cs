using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using bixo_api.Configuration;
using bixo_api.Data;
using bixo_api.Models.Entities;
using bixo_api.Models.Enums;
using bixo_api.Services.Interfaces;
using bixo_api.Services.Payments;
using Stripe;
using Stripe.Checkout;

namespace bixo_api.Services;

public class PaymentService : IPaymentService
{
    private readonly IDbConnectionFactory _db;
    private readonly StripeSettings _settings;
    private readonly ILogger<PaymentService> _logger;
    private readonly Dictionary<string, IPaymentProviderService> _providers;

    public PaymentService(
        IDbConnectionFactory db,
        IOptions<StripeSettings> settings,
        ILogger<PaymentService> logger,
        IEnumerable<IPaymentProviderService> providers)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _providers = providers.ToDictionary(p => p.ProviderName, p => p);

        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    // === New Authorization Flow Methods ===

    public async Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentInitiationRequest request)
    {
        using var connection = _db.CreateConnection();

        // Get company info
        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.stripe_customer_id, u.email
            FROM companies c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CompanyId",
            new { request.CompanyId });

        if (company == null)
        {
            return new PaymentInitiationResult
            {
                Success = false,
                ErrorMessage = "Company not found"
            };
        }

        // Get provider
        if (!_providers.TryGetValue(request.Provider.ToLower(), out var provider))
        {
            return new PaymentInitiationResult
            {
                Success = false,
                ErrorMessage = $"Unknown payment provider: {request.Provider}"
            };
        }

        // Create payment record
        var paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(@"
            INSERT INTO payments (id, company_id, shortlist_request_id, provider, provider_reference,
                                  amount_authorized, amount_captured, currency, status, created_at, updated_at)
            VALUES (@Id, @CompanyId, @ShortlistRequestId, @Provider, @ProviderReference,
                    @AmountAuthorized, 0, @Currency, @Status, @CreatedAt, @UpdatedAt)",
            new
            {
                Id = paymentId,
                request.CompanyId,
                request.ShortlistRequestId,
                Provider = request.Provider.ToLower(),
                ProviderReference = "pending",
                AmountAuthorized = request.Amount,
                request.Currency,
                Status = "pending_approval",
                CreatedAt = now,
                UpdatedAt = now
            });

        // Link payment to shortlist request
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests SET payment_id = @PaymentId WHERE id = @ShortlistRequestId",
            new { PaymentId = paymentId, request.ShortlistRequestId });

        // Log audit entry
        await LogPaymentAuditAsync(connection, paymentId, null, "pending_approval", "payment_created", null);

        // Authorize with provider
        var authRequest = new PaymentAuthorizationRequest
        {
            CompanyId = request.CompanyId,
            ShortlistRequestId = request.ShortlistRequestId,
            Amount = request.Amount,
            Currency = request.Currency,
            CustomerEmail = (string?)company.email,
            CustomerId = company.stripe_customer_id as string,
            Description = request.Description
        };

        var authResult = await provider.AuthorizeAsync(authRequest);

        if (!authResult.Success)
        {
            // Update payment status to failed
            await connection.ExecuteAsync(@"
                UPDATE payments SET status = 'failed', error_message = @ErrorMessage, updated_at = @UpdatedAt
                WHERE id = @PaymentId",
                new { ErrorMessage = authResult.ErrorMessage, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

            await LogPaymentAuditAsync(connection, paymentId, "pending_approval", "failed", "authorization_failed",
                new { authResult.ErrorMessage, authResult.ErrorCode });

            return new PaymentInitiationResult
            {
                Success = false,
                PaymentId = paymentId,
                ErrorMessage = authResult.ErrorMessage
            };
        }

        // Update payment with provider reference
        await connection.ExecuteAsync(@"
            UPDATE payments SET provider_reference = @ProviderReference, updated_at = @UpdatedAt
            WHERE id = @PaymentId",
            new { authResult.ProviderReference, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        return new PaymentInitiationResult
        {
            Success = true,
            PaymentId = paymentId,
            ClientSecret = authResult.ClientSecret,
            ApprovalUrl = authResult.ApprovalUrl,
            EscrowAddress = authResult.EscrowAddress
        };
    }

    public async Task<bool> ConfirmAuthorizationAsync(Guid paymentId, string? providerReference = null)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.provider, p.provider_reference, p.status, sr.id as shortlist_id
            FROM payments p
            LEFT JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE p.id = @PaymentId",
            new { PaymentId = paymentId });

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for confirmation: {PaymentId}", paymentId);
            return false;
        }

        var currentStatus = (string)payment.status;
        if (currentStatus != "pending_approval")
        {
            _logger.LogWarning("Payment {PaymentId} already in status {Status}", paymentId, currentStatus);
            return currentStatus == "authorized";
        }

        var newStatus = "authorized";

        // Update provider reference if provided
        if (!string.IsNullOrEmpty(providerReference))
        {
            await connection.ExecuteAsync(@"
                UPDATE payments SET provider_reference = @ProviderReference, updated_at = @UpdatedAt
                WHERE id = @PaymentId",
                new { ProviderReference = providerReference, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });
        }

        // Update payment status
        await connection.ExecuteAsync(@"
            UPDATE payments SET status = @Status, updated_at = @UpdatedAt WHERE id = @PaymentId",
            new { Status = newStatus, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        // Update shortlist status to Authorized
        // CRITICAL: Only now is the payment confirmed and funds held
        var shortlistId = payment.shortlist_id as Guid?;
        if (shortlistId.HasValue)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status
                WHERE id = @ShortlistId AND status = @PricingApprovedStatus",
                new
                {
                    Status = (int)Models.Enums.ShortlistStatus.Approved,
                    ShortlistId = shortlistId.Value,
                    PricingApprovedStatus = (int)Models.Enums.ShortlistStatus.Approved
                });

            _logger.LogInformation(
                "Shortlist {ShortlistId} status set to Authorized after payment confirmation",
                shortlistId.Value);
        }

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, newStatus, "authorization_confirmed", null);

        _logger.LogInformation("Payment {PaymentId} confirmed with status {Status}", paymentId, newStatus);
        return true;
    }

    public async Task<PaymentFinalizationResult> FinalizePaymentAsync(Guid shortlistRequestId, ShortlistOutcome outcome)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.provider, p.provider_reference, p.amount_authorized, p.status
            FROM payments p
            JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE sr.id = @ShortlistRequestId",
            new { ShortlistRequestId = shortlistRequestId });

        if (payment == null)
        {
            return new PaymentFinalizationResult
            {
                Success = false,
                ErrorMessage = "Payment not found for shortlist"
            };
        }

        var currentStatus = (string)payment.status;
        if (currentStatus != "authorized")
        {
            return new PaymentFinalizationResult
            {
                Success = false,
                ErrorMessage = $"Payment cannot be finalized from status: {currentStatus}"
            };
        }

        var providerName = (string)payment.provider;
        if (!_providers.TryGetValue(providerName, out var provider))
        {
            return new PaymentFinalizationResult
            {
                Success = false,
                ErrorMessage = $"Payment provider not found: {providerName}"
            };
        }

        var paymentId = (Guid)payment.id;
        var providerReference = (string)payment.provider_reference;
        var amountAuthorized = (decimal)payment.amount_authorized;

        string action;
        string newStatus;
        decimal amountCaptured = 0;
        string? pricingType;
        decimal? finalPrice;

        switch (outcome.Status.ToLower())
        {
            case "fulfilled":
                // Full capture
                var captureResult = await provider.CaptureFullAsync(providerReference, amountAuthorized);
                if (!captureResult.Success)
                {
                    await LogPaymentAuditAsync(connection, paymentId, currentStatus, "failed", "capture_failed",
                        new { captureResult.ErrorMessage });
                    return new PaymentFinalizationResult
                    {
                        Success = false,
                        ErrorMessage = captureResult.ErrorMessage
                    };
                }
                action = "captured";
                newStatus = "captured";
                amountCaptured = captureResult.AmountCaptured;
                pricingType = "full";
                finalPrice = amountCaptured;
                break;

            case "partial":
                // Partial capture
                var finalAmount = outcome.FinalAmount ?? (amountAuthorized * (1 - (outcome.DiscountPercent ?? 0) / 100));
                var partialResult = await provider.CapturePartialAsync(providerReference, amountAuthorized, finalAmount);
                if (!partialResult.Success)
                {
                    await LogPaymentAuditAsync(connection, paymentId, currentStatus, "failed", "partial_capture_failed",
                        new { partialResult.ErrorMessage });
                    return new PaymentFinalizationResult
                    {
                        Success = false,
                        ErrorMessage = partialResult.ErrorMessage
                    };
                }
                action = "partial";
                newStatus = "partial";
                amountCaptured = partialResult.AmountCaptured;
                pricingType = "partial";
                finalPrice = amountCaptured;
                break;

            case "no_match":
                // Release authorization
                var releaseResult = await provider.ReleaseAsync(providerReference);
                if (!releaseResult.Success)
                {
                    await LogPaymentAuditAsync(connection, paymentId, currentStatus, "failed", "release_failed",
                        new { releaseResult.ErrorMessage });
                    return new PaymentFinalizationResult
                    {
                        Success = false,
                        ErrorMessage = releaseResult.ErrorMessage
                    };
                }
                action = "released";
                newStatus = "released";
                amountCaptured = 0;
                pricingType = "free";
                finalPrice = 0;
                break;

            default:
                return new PaymentFinalizationResult
                {
                    Success = false,
                    ErrorMessage = $"Unknown outcome status: {outcome.Status}"
                };
        }

        // Update payment record
        await connection.ExecuteAsync(@"
            UPDATE payments SET status = @Status, amount_captured = @AmountCaptured, updated_at = @UpdatedAt
            WHERE id = @PaymentId",
            new { Status = newStatus, AmountCaptured = amountCaptured, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        // Update shortlist request
        await connection.ExecuteAsync(@"
            UPDATE shortlist_requests SET pricing_type = @PricingType, final_price = @FinalPrice
            WHERE id = @ShortlistRequestId",
            new { PricingType = pricingType, FinalPrice = finalPrice, ShortlistRequestId = shortlistRequestId });

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, newStatus, $"payment_{action}",
            new { outcome.Status, amountCaptured, outcome.CandidatesDelivered });

        _logger.LogInformation(
            "Payment {PaymentId} finalized: {Action} with amount {Amount}",
            paymentId, action, amountCaptured);

        return new PaymentFinalizationResult
        {
            Success = true,
            Action = action,
            AmountCaptured = amountCaptured
        };
    }

    public async Task<PaymentStatusResponse?> GetPaymentStatusAsync(Guid shortlistRequestId)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.provider, p.status, p.amount_authorized, p.amount_captured, p.created_at
            FROM payments p
            JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE sr.id = @ShortlistRequestId",
            new { ShortlistRequestId = shortlistRequestId });

        if (payment == null) return null;

        return new PaymentStatusResponse
        {
            PaymentId = (Guid)payment.id,
            Provider = (string)payment.provider,
            Status = (string)payment.status,
            AmountAuthorized = (decimal)payment.amount_authorized,
            AmountCaptured = (decimal)payment.amount_captured,
            CreatedAt = (DateTime)payment.created_at
        };
    }

    public async Task<bool> IsAuthorizationValidAsync(Guid paymentId)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT provider, provider_reference, status FROM payments WHERE id = @PaymentId",
            new { PaymentId = paymentId });

        if (payment == null)
        {
            return false;
        }

        var status = (string)payment.status;
        if (status != "authorized")
        {
            return false;
        }

        var providerName = (string)payment.provider;
        var providerReference = (string)payment.provider_reference;

        if (!_providers.TryGetValue(providerName, out var provider))
        {
            _logger.LogWarning("Provider {Provider} not found for payment {PaymentId}", providerName, paymentId);
            return false;
        }

        // Check with provider if authorization is still valid
        return await provider.IsAuthorizationValidAsync(providerReference);
    }

    public async Task HandleExpiredAuthorizationAsync(Guid paymentId)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.status, sr.id as shortlist_id
            FROM payments p
            LEFT JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE p.id = @PaymentId",
            new { PaymentId = paymentId });

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for expiry handling: {PaymentId}", paymentId);
            return;
        }

        var currentStatus = (string)payment.status;
        if (currentStatus != "authorized")
        {
            return; // Only handle authorized payments
        }

        // Update payment status to expired
        await connection.ExecuteAsync(@"
            UPDATE payments SET status = 'expired', updated_at = @UpdatedAt WHERE id = @PaymentId",
            new { UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        // Revert shortlist to PricingApproved - company must re-authorize
        var shortlistId = payment.shortlist_id as Guid?;
        if (shortlistId.HasValue)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status, payment_id = NULL, payment_authorization_id = NULL
                WHERE id = @ShortlistId",
                new
                {
                    Status = (int)Models.Enums.ShortlistStatus.Approved,
                    ShortlistId = shortlistId.Value
                });

            _logger.LogWarning(
                "Authorization expired for payment {PaymentId}. Shortlist {ShortlistId} reverted to PricingApproved.",
                paymentId, shortlistId.Value);
        }

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, "expired", "authorization_expired", null);
    }

    private async Task LogPaymentAuditAsync(
        System.Data.IDbConnection connection,
        Guid paymentId,
        string? previousStatus,
        string newStatus,
        string action,
        object? providerResponse)
    {
        await connection.ExecuteAsync(@"
            INSERT INTO payment_audit_log (id, payment_id, previous_status, new_status, action, provider_response, created_at)
            VALUES (@Id, @PaymentId, @PreviousStatus, @NewStatus, @Action, @ProviderResponse::jsonb, @CreatedAt)",
            new
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                Action = action,
                ProviderResponse = providerResponse != null ? JsonSerializer.Serialize(providerResponse) : null,
                CreatedAt = DateTime.UtcNow
            });
    }

    // === Legacy Methods ===

    public async Task<string> CreateShortlistPaymentSessionAsync(Guid companyId, Guid shortlistId, Guid pricingId)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.stripe_customer_id, u.email
            FROM companies c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CompanyId",
            new { CompanyId = companyId });

        if (company == null)
        {
            throw new InvalidOperationException("Company not found");
        }

        var pricing = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, name, price, shortlist_count
            FROM shortlist_pricing
            WHERE id = @PricingId",
            new { PricingId = pricingId });

        if (pricing == null)
        {
            throw new InvalidOperationException("Pricing not found");
        }

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)((decimal)pricing.price * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Shortlist - {(string)pricing.name}",
                            Description = $"{(int)pricing.shortlist_count} shortlist(s)"
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "payment",
            SuccessUrl = "http://localhost:3000/company/shortlists?success=true",
            CancelUrl = "http://localhost:3000/company/shortlists?canceled=true",
            Metadata = new Dictionary<string, string>
            {
                { "companyId", companyId.ToString() },
                { "shortlistId", shortlistId.ToString() },
                { "pricingId", pricingId.ToString() },
                { "type", "shortlist" }
            }
        };

        var stripeCustomerId = company.stripe_customer_id as string;
        if (!string.IsNullOrEmpty(stripeCustomerId))
        {
            options.Customer = stripeCustomerId;
        }
        else
        {
            options.CustomerEmail = (string)company.email;
        }

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return session.Url;
    }

    public async Task<string> CreateSubscriptionSessionAsync(Guid companyId, Guid planId, bool yearly)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT c.id, c.user_id, c.stripe_customer_id, u.email
            FROM companies c
            JOIN users u ON u.id = c.user_id
            WHERE c.id = @CompanyId",
            new { CompanyId = companyId });

        if (company == null)
        {
            throw new InvalidOperationException("Company not found");
        }

        var plan = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, name, monthly_price, yearly_price, stripe_price_id_monthly, stripe_price_id_yearly
            FROM subscription_plans
            WHERE id = @PlanId",
            new { PlanId = planId });

        if (plan == null)
        {
            throw new InvalidOperationException("Plan not found");
        }

        var priceId = yearly ? plan.stripe_price_id_yearly as string : plan.stripe_price_id_monthly as string;
        var price = yearly ? (decimal)plan.yearly_price : (decimal)plan.monthly_price;

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)(price * 100),
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = yearly ? "year" : "month"
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Bixo {(string)plan.name} Plan",
                            Description = yearly ? "Annual subscription" : "Monthly subscription"
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "subscription",
            SuccessUrl = "http://localhost:3000/company/settings?success=true",
            CancelUrl = "http://localhost:3000/company/settings?canceled=true",
            Metadata = new Dictionary<string, string>
            {
                { "companyId", companyId.ToString() },
                { "planId", planId.ToString() },
                { "yearly", yearly.ToString() },
                { "type", "subscription" }
            }
        };

        var stripeCustomerId = company.stripe_customer_id as string;
        if (!string.IsNullOrEmpty(stripeCustomerId))
        {
            options.Customer = stripeCustomerId;
        }
        else
        {
            options.CustomerEmail = (string)company.email;
        }

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return session.Url;
    }

    public async Task HandleWebhookAsync(string payload, string signature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _settings.WebhookSecret
            );

            switch (stripeEvent.Type)
            {
                // === PaymentIntent events (authorization flow) ===
                case "payment_intent.amount_capturable_updated":
                    await HandlePaymentIntentAuthorized((PaymentIntent)stripeEvent.Data.Object);
                    break;

                case "payment_intent.payment_failed":
                    await HandlePaymentIntentFailed((PaymentIntent)stripeEvent.Data.Object);
                    break;

                case "payment_intent.canceled":
                    await HandlePaymentIntentCanceled((PaymentIntent)stripeEvent.Data.Object);
                    break;

                // === Checkout/Subscription events (legacy) ===
                case "checkout.session.completed":
                    await HandleCheckoutCompleted((Session)stripeEvent.Data.Object);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated((Subscription)stripeEvent.Data.Object);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted((Subscription)stripeEvent.Data.Object);
                    break;

                case "invoice.payment_succeeded":
                    await HandlePaymentSucceeded((Invoice)stripeEvent.Data.Object);
                    break;

                case "invoice.payment_failed":
                    await HandlePaymentFailed((Invoice)stripeEvent.Data.Object);
                    break;
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook error");
            throw;
        }
    }

    public async Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT subscription_tier, subscription_expires_at, messages_remaining
            FROM companies
            WHERE id = @CompanyId",
            new { CompanyId = companyId });

        if (company == null)
        {
            throw new InvalidOperationException("Company not found");
        }

        var subscriptionTier = (SubscriptionTier)company.subscription_tier;
        var expiresAt = company.subscription_expires_at != null ? (DateTime?)company.subscription_expires_at : null;

        var isActive = subscriptionTier != SubscriptionTier.Free &&
                       expiresAt.HasValue &&
                       expiresAt.Value > DateTime.UtcNow;

        return new SubscriptionStatusResponse
        {
            IsActive = isActive,
            PlanName = subscriptionTier.ToString(),
            ExpiresAt = expiresAt,
            MessagesRemaining = (int)company.messages_remaining
        };
    }

    public async Task CancelSubscriptionAsync(Guid companyId)
    {
        using var connection = _db.CreateConnection();

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM companies WHERE id = @CompanyId)",
            new { CompanyId = companyId });

        if (!exists)
        {
            throw new InvalidOperationException("Company not found");
        }

        // In a real implementation, we'd cancel the Stripe subscription
        // For now, just mark as free tier
        await connection.ExecuteAsync(@"
            UPDATE companies
            SET subscription_tier = @SubscriptionTier, updated_at = @UpdatedAt
            WHERE id = @CompanyId",
            new
            {
                SubscriptionTier = (int)SubscriptionTier.Free,
                UpdatedAt = DateTime.UtcNow,
                CompanyId = companyId
            });
    }

    private async Task HandleCheckoutCompleted(Session session)
    {
        var type = session.Metadata.GetValueOrDefault("type");
        var companyIdStr = session.Metadata.GetValueOrDefault("companyId");

        if (!Guid.TryParse(companyIdStr, out var companyId)) return;

        using var connection = _db.CreateConnection();

        var company = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id, stripe_customer_id FROM companies WHERE id = @CompanyId",
            new { CompanyId = companyId });

        if (company == null) return;

        // Store Stripe customer ID
        var stripeCustomerId = company.stripe_customer_id as string;
        if (string.IsNullOrEmpty(stripeCustomerId) && !string.IsNullOrEmpty(session.CustomerId))
        {
            await connection.ExecuteAsync(
                "UPDATE companies SET stripe_customer_id = @StripeCustomerId WHERE id = @CompanyId",
                new { StripeCustomerId = session.CustomerId, CompanyId = companyId });
        }

        if (type == "shortlist")
        {
            var shortlistIdStr = session.Metadata.GetValueOrDefault("shortlistId");
            var pricingIdStr = session.Metadata.GetValueOrDefault("pricingId");

            if (Guid.TryParse(shortlistIdStr, out var shortlistId) &&
                Guid.TryParse(pricingIdStr, out var pricingId))
            {
                var pricing = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT price FROM shortlist_pricing WHERE id = @PricingId",
                    new { PricingId = pricingId });

                if (pricing != null)
                {
                    await connection.ExecuteAsync(@"
                        UPDATE shortlist_requests
                        SET price_paid = @PricePaid, payment_intent_id = @PaymentIntentId
                        WHERE id = @ShortlistId",
                        new
                        {
                            PricePaid = (decimal)pricing.price,
                            PaymentIntentId = session.PaymentIntentId,
                            ShortlistId = shortlistId
                        });

                    await connection.ExecuteAsync(@"
                        INSERT INTO payments (id, company_id, type, amount, currency, stripe_payment_intent_id, status, created_at)
                        VALUES (@Id, @CompanyId, @Type, @Amount, @Currency, @StripePaymentIntentId, @Status, @CreatedAt)",
                        new
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            Type = (int)PaymentType.Shortlist,
                            Amount = (decimal)pricing.price,
                            Currency = "USD",
                            StripePaymentIntentId = session.PaymentIntentId,
                            Status = (int)PaymentStatus.Captured,
                            CreatedAt = DateTime.UtcNow
                        });
                }
            }
        }
        else if (type == "subscription")
        {
            var planIdStr = session.Metadata.GetValueOrDefault("planId");
            var yearlyStr = session.Metadata.GetValueOrDefault("yearly");

            if (Guid.TryParse(planIdStr, out var planId))
            {
                var plan = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT name, monthly_price, yearly_price, messages_per_month FROM subscription_plans WHERE id = @PlanId",
                    new { PlanId = planId });

                if (plan != null)
                {
                    var yearly = yearlyStr?.ToLower() == "true";
                    var planName = (string)plan.name;
                    var subscriptionTier = planName == "Starter" ? SubscriptionTier.Starter : SubscriptionTier.Pro;

                    await connection.ExecuteAsync(@"
                        UPDATE companies
                        SET subscription_tier = @SubscriptionTier,
                            subscription_expires_at = @ExpiresAt,
                            messages_remaining = @MessagesRemaining,
                            updated_at = @UpdatedAt
                        WHERE id = @CompanyId",
                        new
                        {
                            SubscriptionTier = (int)subscriptionTier,
                            ExpiresAt = yearly ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1),
                            MessagesRemaining = (int)plan.messages_per_month,
                            UpdatedAt = DateTime.UtcNow,
                            CompanyId = companyId
                        });

                    await connection.ExecuteAsync(@"
                        INSERT INTO payments (id, company_id, type, amount, currency, stripe_subscription_id, status, created_at)
                        VALUES (@Id, @CompanyId, @Type, @Amount, @Currency, @StripeSubscriptionId, @Status, @CreatedAt)",
                        new
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            Type = (int)PaymentType.Subscription,
                            Amount = yearly ? (decimal)plan.yearly_price : (decimal)plan.monthly_price,
                            Currency = "USD",
                            StripeSubscriptionId = session.SubscriptionId,
                            Status = (int)PaymentStatus.Captured,
                            CreatedAt = DateTime.UtcNow
                        });
                }
            }
        }
    }

    private async Task HandleSubscriptionUpdated(Subscription subscription)
    {
        // Handle subscription updates
        _logger.LogInformation("Subscription updated: {SubscriptionId}", subscription.Id);
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionDeleted(Subscription subscription)
    {
        // Handle subscription cancellation
        _logger.LogInformation("Subscription deleted: {SubscriptionId}", subscription.Id);
        await Task.CompletedTask;
    }

    private async Task HandlePaymentSucceeded(Invoice invoice)
    {
        _logger.LogInformation("Payment succeeded for invoice: {InvoiceId}", invoice.Id);
        await Task.CompletedTask;
    }

    private async Task HandlePaymentFailed(Invoice invoice)
    {
        _logger.LogWarning("Payment failed for invoice: {InvoiceId}", invoice.Id);
        await Task.CompletedTask;
    }

    // === PaymentIntent Webhook Handlers ===

    private async Task HandlePaymentIntentAuthorized(PaymentIntent paymentIntent)
    {
        // This is called when authorization is confirmed (amount_capturable_updated)
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.status, sr.id as shortlist_id
            FROM payments p
            LEFT JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE p.provider_reference = @ProviderReference",
            new { ProviderReference = paymentIntent.Id });

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
            return;
        }

        var paymentId = (Guid)payment.id;
        var currentStatus = (string)payment.status;

        // Only process if in pending_approval status
        if (currentStatus != "pending_approval")
        {
            _logger.LogInformation(
                "PaymentIntent {PaymentIntentId} already processed, current status: {Status}",
                paymentIntent.Id, currentStatus);
            return;
        }

        // Update payment to authorized
        await connection.ExecuteAsync(@"
            UPDATE payments SET status = 'authorized', updated_at = @UpdatedAt WHERE id = @PaymentId",
            new { UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        // Update shortlist to Authorized
        var shortlistId = payment.shortlist_id as Guid?;
        if (shortlistId.HasValue)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status
                WHERE id = @ShortlistId AND status = @PricingApprovedStatus",
                new
                {
                    Status = (int)Models.Enums.ShortlistStatus.Approved,
                    ShortlistId = shortlistId.Value,
                    PricingApprovedStatus = (int)Models.Enums.ShortlistStatus.Approved
                });
        }

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, "authorized", "webhook_authorized", null);

        _logger.LogInformation(
            "PaymentIntent {PaymentIntentId} authorized via webhook. Payment: {PaymentId}, Shortlist: {ShortlistId}",
            paymentIntent.Id, paymentId, shortlistId);
    }

    private async Task HandlePaymentIntentFailed(PaymentIntent paymentIntent)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT id, status FROM payments WHERE provider_reference = @ProviderReference",
            new { ProviderReference = paymentIntent.Id });

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for failed PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
            return;
        }

        var paymentId = (Guid)payment.id;
        var currentStatus = (string)payment.status;

        var errorMessage = paymentIntent.LastPaymentError?.Message ?? "Payment failed";

        await connection.ExecuteAsync(@"
            UPDATE payments SET status = 'failed', error_message = @ErrorMessage, updated_at = @UpdatedAt
            WHERE id = @PaymentId",
            new { ErrorMessage = errorMessage, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, "failed", "webhook_payment_failed",
            new { paymentIntent.LastPaymentError?.Code, paymentIntent.LastPaymentError?.Message });

        _logger.LogWarning(
            "PaymentIntent {PaymentIntentId} failed. Payment: {PaymentId}. Error: {Error}",
            paymentIntent.Id, paymentId, errorMessage);
    }

    private async Task HandlePaymentIntentCanceled(PaymentIntent paymentIntent)
    {
        using var connection = _db.CreateConnection();

        var payment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.status, sr.id as shortlist_id
            FROM payments p
            LEFT JOIN shortlist_requests sr ON sr.payment_id = p.id
            WHERE p.provider_reference = @ProviderReference",
            new { ProviderReference = paymentIntent.Id });

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for canceled PaymentIntent: {PaymentIntentId}", paymentIntent.Id);
            return;
        }

        var paymentId = (Guid)payment.id;
        var currentStatus = (string)payment.status;

        // Determine if this is an expiration or manual cancellation
        var reason = paymentIntent.CancellationReason ?? "canceled";
        var newStatus = reason == "automatic" ? "expired" : "released";

        await connection.ExecuteAsync(@"
            UPDATE payments SET status = @Status, updated_at = @UpdatedAt WHERE id = @PaymentId",
            new { Status = newStatus, UpdatedAt = DateTime.UtcNow, PaymentId = paymentId });

        // Revert shortlist to PricingApproved if it was in Authorized state
        var shortlistId = payment.shortlist_id as Guid?;
        if (shortlistId.HasValue)
        {
            await connection.ExecuteAsync(@"
                UPDATE shortlist_requests
                SET status = @Status, payment_id = NULL, payment_authorization_id = NULL
                WHERE id = @ShortlistId AND status = @AuthorizedStatus",
                new
                {
                    Status = (int)Models.Enums.ShortlistStatus.Approved,
                    ShortlistId = shortlistId.Value,
                    AuthorizedStatus = (int)Models.Enums.ShortlistStatus.Approved
                });
        }

        await LogPaymentAuditAsync(connection, paymentId, currentStatus, newStatus, $"webhook_{reason}",
            new { paymentIntent.CancellationReason });

        _logger.LogInformation(
            "PaymentIntent {PaymentIntentId} canceled ({Reason}). Payment: {PaymentId} -> {Status}",
            paymentIntent.Id, reason, paymentId, newStatus);
    }
}
