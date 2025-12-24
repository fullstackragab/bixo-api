namespace bixo_api.Models.DTOs.Shortlist;

public class ShortlistPricingResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int ShortlistCount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal PricePerShortlist => ShortlistCount > 0 ? Price / ShortlistCount : Price;
}
