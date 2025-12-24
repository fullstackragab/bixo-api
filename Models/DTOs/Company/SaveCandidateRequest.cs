using System.ComponentModel.DataAnnotations;

namespace pixo_api.Models.DTOs.Company;

public class SaveCandidateRequest
{
    [Required]
    public Guid CandidateId { get; set; }
    public string? Notes { get; set; }
}
