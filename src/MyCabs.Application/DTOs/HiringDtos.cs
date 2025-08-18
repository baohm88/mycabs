namespace MyCabs.Application.DTOs;

public record InviteDriverDto(string DriverUserId, string? Note);
public record ApplicationsQuery(int Page = 1, int PageSize = 10, string? Status = null);
public record InvitationsQuery(int Page = 1, int PageSize = 10, string? Status = null);

public record ApplicationDto(string Id, string DriverId, string CompanyId, string Status, DateTime CreatedAt);
public record InvitationDto(string Id, string CompanyId, string DriverId, string Status, DateTime CreatedAt, string? Note);