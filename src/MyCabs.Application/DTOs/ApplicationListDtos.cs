namespace MyCabs.Application.DTOs;

public record CompanyApplicationItemDto( // dùng cho GET /companies/{id}/applications
    string Id,
    string DriverId,
    string? DriverFullName,
    string Status,
    DateTime CreatedAt
);

public record DriverApplicationItemDto( // dùng cho GET /drivers/me/applications
    string Id,
    string CompanyId,
    string? CompanyName,
    string Status,
    DateTime CreatedAt
);
