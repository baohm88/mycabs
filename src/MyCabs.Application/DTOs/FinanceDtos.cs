namespace MyCabs.Application.DTOs;

public record TopUpDto(decimal Amount, string? Note);
// DriverId = _id của USER role=Driver (dùng để tìm Driver profile)
public record PaySalaryDto(string DriverId, decimal Amount, string? Note);
public record PayMembershipDto(string Plan, string BillingCycle, decimal Amount, string? Note);
public record TransactionsQuery(int Page = 1, int PageSize = 10, string? Type = null, string? Status = null);

public record WalletDto(string Id, string OwnerType, string OwnerId, decimal Balance, decimal LowBalanceThreshold);
public record TransactionDto(string Id, string Type, string Status, decimal Amount,
    string? FromWalletId, string? ToWalletId, string? CompanyId, string? DriverId,
    string? Note, DateTime CreatedAt);