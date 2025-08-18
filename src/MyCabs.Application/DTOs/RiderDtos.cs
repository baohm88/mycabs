namespace MyCabs.Application.DTOs;

public record RiderCompaniesQuery(int Page = 1, int PageSize = 10, string? Search = null, string? ServiceType = null, string? Plan = null, string? Sort = null);
public record RiderDriversQuery(int Page = 1, int PageSize = 10, string? Search = null, string? CompanyId = null, string? Sort = null);

public record CreateRatingDto(string TargetType, string TargetId, int Stars, string? Comment);
public record RatingsQuery(string TargetType, string TargetId, int Page = 1, int PageSize = 10);

public record RatingDto(string Id, string TargetType, string TargetId, string UserId, int Stars, string? Comment, DateTime CreatedAt);
public record RatingSummaryDto(long Count, double Average);