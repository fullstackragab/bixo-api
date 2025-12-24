namespace bixo_api.Models.Entities;

public class ShortlistPricing
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int ShortlistCount { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? StripePriceId { get; set; }
    public bool IsActive { get; set; } = true;
}
