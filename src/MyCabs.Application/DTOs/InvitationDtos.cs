namespace MyCabs.Application.DTOs;

public record MyInvitationDto(
    string Id,
    string CompanyId,
    string? CompanyName, 
    string DriverId,
    string Status,
    DateTime CreatedAt,
    string? Note
);