using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IRatingRepository
{
    Task CreateAsync(Rating r);
    Task<(IEnumerable<Rating> Items, long Total)> FindForTargetAsync(string targetType, string targetId, int page, int pageSize);
    Task<(long Count, double Average)> GetSummaryAsync(string targetType, string targetId);
    Task EnsureIndexesAsync();
}
