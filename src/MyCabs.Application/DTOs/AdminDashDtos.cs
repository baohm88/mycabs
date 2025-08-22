namespace MyCabs.Application.DTOs;

public record DateRangeQuery(DateTime? From = null, DateTime? To = null);
public record PagingQuery(int Page = 1, int PageSize = 20);

// Tổng quan
public record AdminOverviewDto(
    long UsersTotal,
    long CompaniesTotal,
    long DriversTotal,
    decimal WalletsTotalBalance,
    long TxCount,
    decimal TxAmount,
    IReadOnlyDictionary<string, decimal> TxAmountByType,
    IReadOnlyDictionary<string, long> TxCountByType
);

// Time-series (daily)
public record TimePointDto(string Date, long Count, decimal Amount);

// Top entity
public record TopCompanyDto(string CompanyId, string? Name, decimal Amount, long Count);
public record TopDriverDto(string DriverId, string? Name, decimal Amount, long Count);

// Ví thấp
public record LowWalletDto(string WalletId, string OwnerType, string OwnerId, decimal Balance, decimal? Threshold);