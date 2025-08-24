namespace MyCabs.Application.DTOs;

public record DriverOpeningServiceDto(string Type, string Title, decimal BasePrice);

public record DriverOpeningDto(
    string CompanyId,
    string Name,
    string? Address,
    string Plan,
    DateTime? MembershipExpiresAt,
    IEnumerable<DriverOpeningServiceDto> Services,
    bool CanApply // computed on BE
);