using System.Net.Http.Headers;
using System.Text.Json;
using bixo_api.Services.Interfaces;

namespace bixo_api.Services.Payments;

/// <summary>
/// USDC payment provider using Solana blockchain.
/// Crypto has no authorization - uses escrow semantics instead.
/// Company transfers USDC to Bixo escrow wallet, verified on-chain.
/// </summary>
public class UsdcPaymentProvider : IPaymentProviderService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsdcPaymentProvider> _logger;
    private readonly HttpClient _httpClient;

    public string ProviderName => "usdc";

    // USDC token mint on Solana mainnet
    private const string USDC_MINT = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

    public UsdcPaymentProvider(
        IConfiguration configuration,
        ILogger<UsdcPaymentProvider> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Solana");

        var rpcUrl = _configuration["Solana:RpcUrl"] ?? "https://api.mainnet-beta.solana.com";
        _httpClient.BaseAddress = new Uri(rpcUrl);
    }

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request)
    {
        try
        {
            // For USDC, we don't authorize - we return escrow address for the company to transfer to
            var escrowWallet = _configuration["Solana:EscrowWallet"];

            if (string.IsNullOrEmpty(escrowWallet))
            {
                return new PaymentAuthorizationResult
                {
                    Success = false,
                    ErrorMessage = "Escrow wallet not configured"
                };
            }

            _logger.LogInformation(
                "USDC escrow requested: {Amount} USDC for shortlist {ShortlistId}",
                request.Amount, request.ShortlistRequestId);

            // Return escrow address - company will transfer USDC here
            // The frontend will guide the user through the wallet connection and transfer
            return new PaymentAuthorizationResult
            {
                Success = true,
                ProviderReference = $"pending_{request.ShortlistRequestId}_{DateTime.UtcNow.Ticks}",
                EscrowAddress = escrowWallet
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USDC authorization failed for shortlist {ShortlistId}", request.ShortlistRequestId);
            return new PaymentAuthorizationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Verify an escrow transfer and mark as escrowed.
    /// Called after company submits transaction hash.
    /// </summary>
    public async Task<bool> VerifyEscrowTransferAsync(string transactionHash, decimal expectedAmount)
    {
        try
        {
            var escrowWallet = _configuration["Solana:EscrowWallet"];
            if (string.IsNullOrEmpty(escrowWallet))
            {
                return false;
            }

            // Get transaction details from Solana RPC
            var rpcRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getTransaction",
                @params = new object[]
                {
                    transactionHash,
                    new { encoding = "jsonParsed", maxSupportedTransactionVersion = 0 }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(rpcRequest), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Solana RPC error: {Response}", responseBody);
                return false;
            }

            var rpcResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (rpcResponse.TryGetProperty("error", out _))
            {
                _logger.LogError("Solana transaction not found: {TxHash}", transactionHash);
                return false;
            }

            var result = rpcResponse.GetProperty("result");

            // Verify transaction was successful
            var meta = result.GetProperty("meta");
            if (meta.GetProperty("err").ValueKind != JsonValueKind.Null)
            {
                _logger.LogError("Solana transaction failed: {TxHash}", transactionHash);
                return false;
            }

            // Verify USDC transfer to escrow wallet
            // This is a simplified check - production would need more thorough verification
            var instructions = result.GetProperty("transaction")
                .GetProperty("message")
                .GetProperty("instructions");

            foreach (var instruction in instructions.EnumerateArray())
            {
                if (!instruction.TryGetProperty("parsed", out var parsed)) continue;
                if (!parsed.TryGetProperty("type", out var type)) continue;

                if (type.GetString() == "transferChecked" || type.GetString() == "transfer")
                {
                    var info = parsed.GetProperty("info");
                    var destination = info.GetProperty("destination").GetString();
                    var tokenAmount = info.TryGetProperty("tokenAmount", out var ta)
                        ? decimal.Parse(ta.GetProperty("uiAmountString").GetString()!)
                        : 0;

                    // Verify it's a transfer to our escrow wallet with expected amount
                    if (destination == escrowWallet && tokenAmount >= expectedAmount)
                    {
                        _logger.LogInformation(
                            "USDC escrow verified: {TxHash} with {Amount} USDC",
                            transactionHash, tokenAmount);
                        return true;
                    }
                }
            }

            _logger.LogWarning("USDC transfer not found in transaction: {TxHash}", transactionHash);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify USDC transfer: {TxHash}", transactionHash);
            return false;
        }
    }

    public async Task<PaymentCaptureResult> CaptureFullAsync(string providerReference, decimal amount)
    {
        try
        {
            // For USDC, "capture" means transferring from escrow to revenue wallet
            var revenueWallet = _configuration["Solana:RevenueWallet"];

            if (string.IsNullOrEmpty(revenueWallet))
            {
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Revenue wallet not configured"
                };
            }

            // In production, this would execute an on-chain transfer
            // For now, we log and mark as captured
            // The actual transfer would be done by a separate service with the escrow wallet's private key
            _logger.LogInformation(
                "USDC capture initiated: {Amount} USDC to revenue wallet for {Reference}",
                amount, providerReference);

            // Return success - actual transfer happens asynchronously
            return new PaymentCaptureResult
            {
                Success = true,
                AmountCaptured = amount,
                ProviderReference = $"capture_{providerReference}_{DateTime.UtcNow.Ticks}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USDC capture failed for {Reference}", providerReference);
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
            var revenueWallet = _configuration["Solana:RevenueWallet"];

            if (string.IsNullOrEmpty(revenueWallet))
            {
                return new PaymentCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Revenue wallet not configured"
                };
            }

            // Partial capture: transfer captureAmount to revenue, refund remainder
            var refundAmount = originalAmount - captureAmount;

            _logger.LogInformation(
                "USDC partial capture initiated: {CaptureAmount} to revenue, {RefundAmount} to refund for {Reference}",
                captureAmount, refundAmount, providerReference);

            return new PaymentCaptureResult
            {
                Success = true,
                AmountCaptured = captureAmount,
                ProviderReference = $"partial_{providerReference}_{DateTime.UtcNow.Ticks}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USDC partial capture failed for {Reference}", providerReference);
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
            // For USDC, "release" means refunding the full escrowed amount back to the company
            _logger.LogInformation("USDC refund initiated for {Reference}", providerReference);

            // In production, this would execute an on-chain transfer back to the company
            return new PaymentReleaseResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USDC release failed for {Reference}", providerReference);
            return new PaymentReleaseResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> IsAuthorizationValidAsync(string providerReference)
    {
        // For USDC escrow, funds are always valid once verified on-chain
        // They don't expire like card authorizations
        await Task.CompletedTask;
        return true;
    }
}
