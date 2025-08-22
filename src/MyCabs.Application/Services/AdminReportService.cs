using MyCabs.Application.DTOs;
using MyCabs.Application.Interfaces;

namespace MyCabs.Application.Services;

public interface IAdminReportService
{
    Task<AdminOverviewDto> OverviewAsync(DateRangeQuery q);
    Task<IEnumerable<TimePointDto>> TransactionsDailyAsync(DateRangeQuery q);
    Task<IEnumerable<TopCompanyDto>> TopCompaniesAsync(DateRangeQuery q, int limit = 10);
    Task<IEnumerable<TopDriverDto>> TopDriversAsync(DateRangeQuery q, int limit = 10);
    Task<IEnumerable<LowWalletDto>> LowWalletsAsync(decimal? threshold = null, int limit = 20, string ownerType = "Company");
}

public class AdminReportService : IAdminReportService
{
    private readonly IAdminReportRepository _repo;
    public AdminReportService(IAdminReportRepository repo) { _repo = repo; }

    private static (DateTime from, DateTime to) Normalize(DateRangeQuery q)
    {
        var to = q.To?.ToUniversalTime() ?? DateTime.UtcNow;
        var from = q.From?.ToUniversalTime() ?? to.AddDays(-30);
        return (from, to);
    }

    public Task<AdminOverviewDto> OverviewAsync(DateRangeQuery q)
    { var (f, t) = Normalize(q); return _repo.GetOverviewAsync(f, t); }

    public Task<IEnumerable<TimePointDto>> TransactionsDailyAsync(DateRangeQuery q)
    { var (f, t) = Normalize(q); return _repo.GetTransactionsDailyAsync(f, t); }

    public Task<IEnumerable<TopCompanyDto>> TopCompaniesAsync(DateRangeQuery q, int limit = 10)
    { var (f, t) = Normalize(q); return _repo.GetTopCompaniesAsync(f, t, limit); }

    public Task<IEnumerable<TopDriverDto>> TopDriversAsync(DateRangeQuery q, int limit = 10)
    { var (f, t) = Normalize(q); return _repo.GetTopDriversAsync(f, t, limit); }

    public Task<IEnumerable<LowWalletDto>> LowWalletsAsync(decimal? threshold = null, int limit = 20, string ownerType = "Company")
    { return _repo.GetLowWalletsAsync(threshold ?? 200_000M, limit, ownerType); }
}