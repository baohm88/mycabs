using MyCabs.Application.DTOs;

namespace MyCabs.Application.Interfaces;

public interface IAdminReportRepository
{
    Task<AdminOverviewDto> GetOverviewAsync(DateTime from, DateTime to);
    Task<IEnumerable<TimePointDto>> GetTransactionsDailyAsync(DateTime from, DateTime to);
    Task<IEnumerable<TopCompanyDto>> GetTopCompaniesAsync(DateTime from, DateTime to, int limit);
    Task<IEnumerable<TopDriverDto>> GetTopDriversAsync(DateTime from, DateTime to, int limit);
    Task<IEnumerable<LowWalletDto>> GetLowWalletsAsync(decimal threshold, int limit, string ownerType = "Company");
}
