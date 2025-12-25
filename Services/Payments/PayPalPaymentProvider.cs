using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services.Payments;

public class PayPalPaymentProvider : IPaymentProviderService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayPalPaymentProvider> _logger;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private DateTime _tokenExpiry;

    public string ProviderName => "paypal";

    public PayPalPaymentProvider(IConfiguration configuration, ILogger<PayPalPaymentProvider> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("PayPal");

        var isSandbox = _configuration.GetValue<bool>("PayPal:UseSandbox", true);
        _httpClient.BaseAddress = new Uri(isSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com");
    }

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request)
    {
        try
        {
            await EnsureAccessTokenAsync();

            var orderRequest = new
            {
                intent = "AUTHORIZE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = request.ShortlistRequestId.ToString(),
                        description = request.Description ?? $"Shortlist request {request.ShortlistRequestId}",
                        amount = new
                        {
                            currency_code = request.Currency,
                            value = request.Amount.ToString("F2")
                        },
                        custom_id = request.CompanyId.ToString()
                    }
                },
                application_context = new
                {
                    return_url = _configuration["PayPal:ReturnUrl"] ?? "http://localhost:3000/payment/success",
                    cancel_url = _configuration["PayPal:CancelUrl"] ?? "http://localhost:3000/payment/cancel"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(orderRequest), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.PostAsync("/v2/checkout/orders", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal order creation failed: {Response}", responseBody);
                return new PaymentAuthorizationResult
                {
                    Success = false,
                    ErrorMessage = $"PayPal error: {response.StatusCode}"
                };
            }

            var orderResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var orderId = orderResponse.GetProperty("id").GetString();
            var approvalUrl = orderResponse.GetProperty("links")
                .EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("rel").GetString() == "approve")
                .GetProperty("href").GetString();

            _logger.LogInformation("PayPal order created: {OrderId}", orderId);

            return new PaymentAuthorizationResult
            {
                Success = true,
                ProviderReference = orderId,
                ApprovalUrl = approvalUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal authorization failed for shortlist {ShortlistId}", request.ShortlistRequestId);
            return new PaymentAuthorizationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PaymentCaptureResult> CaptureFullAsync(string providerReference, decimal amount)
    {
        try
        {
            await EnsureAccessTokenAsync();

            // First, get the authorization ID from the order
            var authorizationId = await GetAuthorizationIdAsync(providerReference);
            if (string.IsNullOrEmpty(authorizationId))
            {
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Authorization not found"
                };
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/v2/payments/authorizations/{authorizationId}/capture", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal capture failed: {Response}", responseBody);
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = $"PayPal error: {response.StatusCode}"
                };
            }

            var captureResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var capturedAmount = decimal.Parse(captureResponse.GetProperty("amount").GetProperty("value").GetString()!);

            _logger.LogInformation("PayPal capture successful: {AuthorizationId} for {Amount}", authorizationId, capturedAmount);

            return new PaymentCaptureResult
            {
                Success = true,
                AmountCaptured = capturedAmount,
                ProviderReference = captureResponse.GetProperty("id").GetString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal capture failed for order {OrderId}", providerReference);
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
            await EnsureAccessTokenAsync();

            var authorizationId = await GetAuthorizationIdAsync(providerReference);
            if (string.IsNullOrEmpty(authorizationId))
            {
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Authorization not found"
                };
            }

            var captureRequest = new
            {
                amount = new
                {
                    currency_code = "USD",
                    value = captureAmount.ToString("F2")
                },
                final_capture = true
            };

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var content = new StringContent(JsonSerializer.Serialize(captureRequest), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/v2/payments/authorizations/{authorizationId}/capture", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal partial capture failed: {Response}", responseBody);
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = $"PayPal error: {response.StatusCode}"
                };
            }

            var captureResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var capturedAmountValue = decimal.Parse(captureResponse.GetProperty("amount").GetProperty("value").GetString()!);

            _logger.LogInformation(
                "PayPal partial capture: {AuthorizationId} captured {CapturedAmount} of {OriginalAmount}",
                authorizationId, capturedAmountValue, originalAmount);

            return new PaymentCaptureResult
            {
                Success = true,
                AmountCaptured = capturedAmountValue,
                ProviderReference = captureResponse.GetProperty("id").GetString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal partial capture failed for order {OrderId}", providerReference);
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
            await EnsureAccessTokenAsync();

            var authorizationId = await GetAuthorizationIdAsync(providerReference);
            if (string.IsNullOrEmpty(authorizationId))
            {
                return new PaymentReleaseResult
                {
                    Success = false,
                    ErrorMessage = "Authorization not found"
                };
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/v2/payments/authorizations/{authorizationId}/void", content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("PayPal void failed: {Response}", responseBody);
                return new PaymentReleaseResult
                {
                    Success = false,
                    ErrorMessage = $"PayPal error: {response.StatusCode}"
                };
            }

            _logger.LogInformation("PayPal authorization voided: {AuthorizationId}", authorizationId);

            return new PaymentReleaseResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal release failed for order {OrderId}", providerReference);
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
            await EnsureAccessTokenAsync();

            var authorizationId = await GetAuthorizationIdAsync(providerReference);
            if (string.IsNullOrEmpty(authorizationId))
            {
                return false;
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var response = await _httpClient.GetAsync($"/v2/payments/authorizations/{authorizationId}");

            if (!response.IsSuccessStatusCode) return false;

            var responseBody = await response.Content.ReadAsStringAsync();
            var authResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var status = authResponse.GetProperty("status").GetString();

            // PayPal authorization statuses: CREATED, CAPTURED, DENIED, EXPIRED, PARTIALLY_CAPTURED, VOIDED, PENDING
            return status == "CREATED" || status == "PENDING";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check PayPal authorization validity for {OrderId}", providerReference);
            return false;
        }
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return;
        }

        var clientId = _configuration["PayPal:ClientId"];
        var clientSecret = _configuration["PayPal:ClientSecret"];

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" }
        });

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get PayPal access token: {responseBody}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        _accessToken = tokenResponse.GetProperty("access_token").GetString();
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // Refresh 1 minute before expiry
    }

    private async Task<string?> GetAuthorizationIdAsync(string orderId)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _httpClient.GetAsync($"/v2/checkout/orders/{orderId}");

        if (!response.IsSuccessStatusCode) return null;

        var responseBody = await response.Content.ReadAsStringAsync();
        var orderResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        var purchaseUnits = orderResponse.GetProperty("purchase_units");
        if (purchaseUnits.GetArrayLength() == 0) return null;

        var payments = purchaseUnits[0].GetProperty("payments");
        if (!payments.TryGetProperty("authorizations", out var authorizations)) return null;
        if (authorizations.GetArrayLength() == 0) return null;

        return authorizations[0].GetProperty("id").GetString();
    }
}
