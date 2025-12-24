using Dapper;
using Microsoft.Extensions.Options;
using pixo_api.Configuration;
using pixo_api.Data;
using pixo_api.Models.Entities;
using pixo_api.Models.Enums;
using pixo_api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace pixo_api.Services;

public class PaymentService : IPaymentService
{
    private readonly IDbConnectionFactory _db;
    private readonly StripeSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IDbConnectionFactory db,
        IOptions<StripeSettings> settings,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;

        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

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
                            Name = $"Pixo {(string)plan.name} Plan",
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
                            Status = (int)PaymentStatus.Completed,
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
                            Status = (int)PaymentStatus.Completed,
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
}
