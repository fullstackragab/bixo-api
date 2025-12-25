using Microsoft.Extensions.Options;
using Stripe;
using bixo_api.Configuration;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services.Payments;

public class StripePaymentProvider : IPaymentProviderService
{
    private readonly StripeSettings _settings;
    private readonly ILogger<StripePaymentProvider> _logger;

    public string ProviderName => "stripe";

    public StripePaymentProvider(IOptions<StripeSettings> settings, ILogger<StripePaymentProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(request.Amount * 100), // Convert to cents
                Currency = request.Currency.ToLower(),
                CaptureMethod = "manual", // Manual capture - key for authorization flow
                Description = request.Description ?? $"Shortlist request {request.ShortlistRequestId}",
                Metadata = new Dictionary<string, string>
                {
                    { "companyId", request.CompanyId.ToString() },
                    { "shortlistRequestId", request.ShortlistRequestId.ToString() }
                }
            };

            // Add customer if available
            if (!string.IsNullOrEmpty(request.CustomerId))
            {
                options.Customer = request.CustomerId;
            }
            else if (!string.IsNullOrEmpty(request.CustomerEmail))
            {
                options.ReceiptEmail = request.CustomerEmail;
            }

            // Add any additional metadata
            if (request.Metadata != null)
            {
                foreach (var kvp in request.Metadata)
                {
                    options.Metadata[kvp.Key] = kvp.Value;
                }
            }

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            _logger.LogInformation(
                "Stripe PaymentIntent created: {PaymentIntentId} for amount {Amount} {Currency}",
                paymentIntent.Id, request.Amount, request.Currency);

            return new PaymentAuthorizationResult
            {
                Success = true,
                ProviderReference = paymentIntent.Id,
                ClientSecret = paymentIntent.ClientSecret
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe authorization failed for shortlist {ShortlistId}", request.ShortlistRequestId);
            return new PaymentAuthorizationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ErrorCode = ex.StripeError?.Code
            };
        }
    }

    public async Task<PaymentCaptureResult> CaptureFullAsync(string providerReference, decimal amount)
    {
        try
        {
            var service = new PaymentIntentService();
            var paymentIntent = await service.CaptureAsync(providerReference);

            _logger.LogInformation(
                "Stripe PaymentIntent captured: {PaymentIntentId} for amount {Amount}",
                providerReference, paymentIntent.AmountReceived / 100m);

            return new PaymentCaptureResult
            {
                Success = paymentIntent.Status == "succeeded",
                AmountCaptured = paymentIntent.AmountReceived / 100m,
                ProviderReference = paymentIntent.Id
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe capture failed for PaymentIntent {PaymentIntentId}", providerReference);
            return new PaymentCaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PaymentCaptureResult> CapturePartialAsync(string providerReference, decimal originalAmount, decimal captureAmount)
    {
        try
        {
            var service = new PaymentIntentService();
            var options = new PaymentIntentCaptureOptions
            {
                AmountToCapture = (long)(captureAmount * 100)
            };

            var paymentIntent = await service.CaptureAsync(providerReference, options);

            _logger.LogInformation(
                "Stripe partial capture: {PaymentIntentId} captured {CapturedAmount} of {OriginalAmount}",
                providerReference, captureAmount, originalAmount);

            return new PaymentCaptureResult
            {
                Success = paymentIntent.Status == "succeeded",
                AmountCaptured = paymentIntent.AmountReceived / 100m,
                ProviderReference = paymentIntent.Id
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe partial capture failed for PaymentIntent {PaymentIntentId}", providerReference);
            return new PaymentCaptureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PaymentReleaseResult> ReleaseAsync(string providerReference)
    {
        try
        {
            var service = new PaymentIntentService();
            var paymentIntent = await service.CancelAsync(providerReference);

            _logger.LogInformation("Stripe PaymentIntent canceled: {PaymentIntentId}", providerReference);

            return new PaymentReleaseResult
            {
                Success = paymentIntent.Status == "canceled"
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe release failed for PaymentIntent {PaymentIntentId}", providerReference);
            return new PaymentReleaseResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> IsAuthorizationValidAsync(string providerReference)
    {
        try
        {
            var service = new PaymentIntentService();
            var paymentIntent = await service.GetAsync(providerReference);

            // Check if the payment intent is still capturable
            // Stripe authorizations are valid for 7 days by default
            return paymentIntent.Status == "requires_capture";
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to check authorization validity for {PaymentIntentId}", providerReference);
            return false;
        }
    }
}
