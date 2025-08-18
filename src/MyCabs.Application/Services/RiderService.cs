using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IRiderService
{
    Task<(IEnumerable<Company> Items, long Total)> SearchCompaniesAsync(RiderCompaniesQuery q);
    Task<(IEnumerable<Driver> Items, long Total)> SearchDriversAsync(RiderDriversQuery q);

    Task CreateRatingAsync(string userId, CreateRatingDto dto);
    Task<(IEnumerable<RatingDto> Items, long Total)> GetRatingsAsync(RatingsQuery q);
    Task<RatingSummaryDto> GetRatingSummaryAsync(string targetType, string targetId);
}

public class RiderService : IRiderService
{
    private readonly ICompanyRepository _companies;
    private readonly IDriverRepository _drivers;
    private readonly IRatingRepository _ratings;

    public RiderService(ICompanyRepository companies, IDriverRepository drivers, IRatingRepository ratings)
    { _companies = companies; _drivers = drivers; _ratings = ratings; }

    public Task<(IEnumerable<Company> Items, long Total)> SearchCompaniesAsync(RiderCompaniesQuery q)
        => _companies.FindAsync(q.Page, q.PageSize, q.Search ?? string.Empty, q.Plan ?? string.Empty, q.ServiceType ?? string.Empty, q.Sort ?? string.Empty);

    public Task<(IEnumerable<Driver> Items, long Total)> SearchDriversAsync(RiderDriversQuery q)
        => _drivers.FindAsync(q.Page, q.PageSize, q.Search, q.CompanyId, q.Sort);

    public async Task CreateRatingAsync(string userId, CreateRatingDto dto)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        if (!ObjectId.TryParse(dto.TargetId, out var tid)) throw new ArgumentException("Invalid targetId");
        await _ratings.CreateAsync(new Rating
        {
            Id = ObjectId.GenerateNewId(),
            TargetType = dto.TargetType,
            TargetId = tid,
            UserId = uid,
            Stars = dto.Stars,
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow
        });
    }

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
}
