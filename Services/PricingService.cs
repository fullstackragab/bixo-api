using bixo_api.Models.Enums;

namespace bixo_api.Services;

public interface IPricingService
{
    PricingSuggestion CalculateSuggestedPrice(SeniorityLevel? seniority, int candidateCount, bool isRare = false);
}

public class PricingService : IPricingService
{
    // Base prices by seniority (in EUR)
    private static readonly Dictionary<SeniorityLevel, decimal> BasePrices = new()
    {
        { SeniorityLevel.Junior, 250m },
        { SeniorityLevel.Mid, 300m },
        { SeniorityLevel.Senior, 500m },
        { SeniorityLevel.Lead, 600m },
        { SeniorityLevel.Principal, 700m }
    };

    // Default base price when seniority is unknown
    private const decimal DefaultBasePrice = 400m;

    // Adjustments based on candidate count
    private static decimal GetSizeAdjustment(int candidateCount)
    {
        return candidateCount switch
        {
            <= 3 => -50m,   // Smaller shortlist = slight discount
            <= 4 => -25m,
            <= 6 => 0m,     // Sweet spot
            7 => 50m,       // At max = slight premium
            _ => 100m       // Over max (shouldn't happen with cap)
        };
    }

    // Rare/specialized role premium
    private const decimal RarePremium = 150m;

    public PricingSuggestion CalculateSuggestedPrice(SeniorityLevel? seniority, int candidateCount, bool isRare = false)
    {
        // Base price from seniority
        var basePrice = seniority.HasValue && BasePrices.ContainsKey(seniority.Value)
            ? BasePrices[seniority.Value]
            : DefaultBasePrice;

        // Size adjustment
        var sizeAdjustment = GetSizeAdjustment(candidateCount);

        // Rare role premium
        var rarePremium = isRare ? RarePremium : 0m;

        // Calculate total
        var suggestedPrice = basePrice + sizeAdjustment + rarePremium;

        // Ensure minimum price
        suggestedPrice = Math.Max(suggestedPrice, 200m);

        return new PricingSuggestion
        {
            SuggestedPrice = suggestedPrice,
            Factors = new PricingFactors
            {
                Seniority = seniority?.ToString()?.ToLower(),
                BasePrice = basePrice,
                CandidateCount = candidateCount,
                SizeAdjustment = sizeAdjustment,
                IsRare = isRare,
                RarePremium = rarePremium
            }
        };
    }
}

public class PricingSuggestion
{
    public decimal SuggestedPrice { get; set; }
    public PricingFactors Factors { get; set; } = new();
}

public class PricingFactors
{
    public string? Seniority { get; set; }
    public decimal BasePrice { get; set; }
    public int CandidateCount { get; set; }
    public decimal SizeAdjustment { get; set; }
    public bool IsRare { get; set; }
    public decimal RarePremium { get; set; }
}
