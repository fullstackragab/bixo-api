using bixo_api.Models.DTOs.Location;

namespace bixo_api.Models.DTOs.Company;

public class UpdateCompanyRequest
{
    public string? CompanyName { get; set; }
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? Website { get; set; }

    // Company HQ/office location
    public UpdateLocationRequest? Location { get; set; }
}
