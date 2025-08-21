using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Domain.Constants;

namespace MyCabs.Application.Services;

public interface IRiderService
{
    Task<(IEnumerable<Company> Items, long Total)> SearchCompaniesAsync(RiderCompaniesQuery q);
    Task<(IEnumerable<Driver> Items, long Total)> SearchDriversAsync(RiderDriversQuery q);

    Task CreateRatingAsync(string userId, CreateRatingDto dto); // <- giữ kiểu Task
    Task<(IEnumerable<RatingDto> Items, long Total)> GetRatingsAsync(RatingsQuery q);
    Task<RatingSummaryDto> GetRatingSummaryAsync(string targetType, string targetId);
}

public class RiderService : IRiderService
{
    private readonly ICompanyRepository _companies;
    private readonly IDriverRepository _drivers;
    private readonly IRatingRepository _ratings;
    private readonly INotificationService _notif;

    public RiderService(ICompanyRepository companies, IDriverRepository drivers, IRatingRepository ratings, INotificationService notif)
    { _companies = companies; _drivers = drivers; _ratings = ratings; _notif = notif; }

    public Task<(IEnumerable<Company> Items, long Total)> SearchCompaniesAsync(RiderCompaniesQuery q)
        => _companies.FindAsync(q.Page, q.PageSize, q.Search ?? string.Empty, q.Plan ?? string.Empty, q.ServiceType ?? string.Empty, q.Sort ?? string.Empty);

    public Task<(IEnumerable<Driver> Items, long Total)> SearchDriversAsync(RiderDriversQuery q)
        => _drivers.FindAsync(q.Page, q.PageSize, q.Search, q.CompanyId, q.Sort);

    public async Task<(IEnumerable<RatingDto> Items, long Total)> GetRatingsAsync(RatingsQuery q)
    {
        var (items, total) = await _ratings.FindForTargetAsync(q.TargetType, q.TargetId, q.Page, q.PageSize);
        return (items.Select(r => new RatingDto(r.Id.ToString(), r.TargetType, r.TargetId.ToString(), r.UserId.ToString(), r.Stars, r.Comment, r.CreatedAt)), total);
    }

    public async Task<RatingSummaryDto> GetRatingSummaryAsync(string targetType, string targetId)
    {
        var (count, avg) = await _ratings.GetSummaryAsync(targetType, targetId);
        return new RatingSummaryDto(count, Math.Round(avg, 2));
    }

    public async Task CreateRatingAsync(string riderUserId, CreateRatingDto dto)
    {
        // Lưu rating (giả định DTO có TargetType/TargetId)
        if (!ObjectId.TryParse(riderUserId, out var uid)) throw new ArgumentException("Invalid riderUserId");

        if (!ObjectId.TryParse(dto.TargetId, out var targetOid))
            throw new ArgumentException("Invalid targetId");

        var r = new Rating
        {
            Id = ObjectId.GenerateNewId(),
            TargetType = dto.TargetType,
            TargetId = targetOid,
            UserId = uid,
            Stars = dto.Stars,
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow
        };
        await _ratings.CreateAsync(r);

        // Xác định owner nhận thông báo
        string? ownerUserId = null;
        string subjectTitle = "";

        if (dto.TargetType?.ToLowerInvariant() == "company")
        {
            var c = await _companies.GetByIdAsync(dto.TargetId);
            ownerUserId = c?.OwnerUserId.ToString();
            subjectTitle = c?.Name ?? "Company";
        }
        else if (dto.TargetType?.ToLowerInvariant() == "driver")
        {
            var d = await _drivers.GetByUserIdAsync(dto.TargetId);
            ownerUserId = d?.UserId.ToString();
            subjectTitle = d?.FullName ?? "Driver";
        }

        if (!string.IsNullOrEmpty(ownerUserId))
        {
            await _notif.PublishAsync(ownerUserId!, new CreateNotificationDto(
    NotificationKinds.RatingNew,
    $"Bạn có đánh giá mới cho {subjectTitle}",
    $"{dto.Stars}/5★: {dto.Comment}",
    new Dictionary<string, object>
    {
        ["ratingId"] = r.Id.ToString(),
        ["targetType"] = dto.TargetType ?? string.Empty,
        ["targetId"] = dto.TargetId,
        ["stars"] = dto.Stars,
        ["comment"] = dto.Comment ?? string.Empty
    }
));


        }
    }
}
