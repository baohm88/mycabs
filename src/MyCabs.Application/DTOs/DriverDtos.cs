namespace MyCabs.Application.DTOs;

// public record DriverApplyDto(string CompanyId);
public record InvitationRespondDto(string Action); // Accept | Decline
public record MyJobAppDto(
    string Id,
    string CompanyId,
    string? CompanyName,
    string Status,
    DateTime CreatedAt,
    string? Note
);