using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using MyCabs.Domain.Constants;
using MyCabs.Application.Realtime;

namespace MyCabs.Application.Services;

public interface IFinanceService
{
    Task<WalletDto> GetCompanyWalletAsync(string companyId);
    Task<(IEnumerable<TransactionDto> Items, long Total)> GetCompanyTransactionsAsync(string companyId, TransactionsQuery q);
    Task<WalletDto> GetDriverWalletAsync(string driverId);
    Task<(IEnumerable<TransactionDto> Items, long Total)> GetDriverTransactionsAsync(string driverId, TransactionsQuery q);
    Task<bool> TopUpCompanyAsync(string companyId, TopUpDto dto);
    Task<(bool ok, string? err)> PaySalaryAsync(string companyId, PaySalaryDto dto);
    Task<(bool ok, string? err)> PayMembershipAsync(string companyId, PayMembershipDto dto);
}

public class FinanceService : IFinanceService
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _txs;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IConfiguration _cfg;
    private readonly INotificationService _notif;
    private readonly IAdminRealtime _adminRt;

    private const decimal DEFAULT_THRESHOLD = 200_000M;

    public FinanceService(
        IWalletRepository wallets,
        ITransactionRepository txs,
        IDriverRepository drivers,
        ICompanyRepository companies,
        IConfiguration cfg,
        INotificationService notif,
        IAdminRealtime adminRt)
    {
        _wallets = wallets; _txs = txs; _drivers = drivers; _companies = companies; _cfg = cfg; _notif = notif; _adminRt = adminRt;
    }

    static WalletDto Map(Wallet w) => new(w.Id.ToString(), w.OwnerType, w.OwnerId.ToString(), w.Balance, w.LowBalanceThreshold);

    static TransactionDto Map(Transaction t) => new(
        t.Id.ToString(), t.Type, t.Status, t.Amount,
        t.FromWalletId?.ToString(), t.ToWalletId?.ToString(), t.CompanyId?.ToString(), t.DriverId?.ToString(),
        t.Note, t.CreatedAt
    );

    public async Task<WalletDto> GetCompanyWalletAsync(string companyId)
        => Map(await _wallets.GetOrCreateAsync("Company", companyId));

    public async Task<(IEnumerable<TransactionDto> Items, long Total)> GetCompanyTransactionsAsync(string companyId, TransactionsQuery q)
    {
        var (items, total) = await _txs.FindForCompanyAsync(companyId, q.Page, q.PageSize, q.Type, q.Status);
        return (items.Select(Map), total);
    }

    public async Task<WalletDto> GetDriverWalletAsync(string driverId)
        => Map(await _wallets.GetOrCreateAsync("Driver", driverId));

    public async Task<(IEnumerable<TransactionDto> Items, long Total)> GetDriverTransactionsAsync(string driverId, TransactionsQuery q)
    {
        var (items, total) = await _txs.FindForDriverAsync(driverId, q.Page, q.PageSize, q.Type, q.Status);
        return (items.Select(Map), total);
    }

    public async Task<bool> TopUpCompanyAsync(string companyId, TopUpDto dto)
    {
        var w = await _wallets.GetOrCreateAsync("Company", companyId);
        await _wallets.CreditAsync(w.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Topup",
            Status = "Completed",
            Amount = dto.Amount,
            FromWalletId = null,
            ToWalletId = w.Id,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = null,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        return true;
    }

    public async Task<(bool ok, string? err)> PaySalaryAsync(string companyId, PaySalaryDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var driver = await _drivers.GetByUserIdAsync(dto.DriverId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND");
        var drvW = await _wallets.GetOrCreateAsync("Driver", driver.Id.ToString());
        var debited = await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Salary",
            Status = debited ? "Completed" : "Failed",
            Amount = dto.Amount,
            FromWalletId = compW.Id,
            ToWalletId = drvW.Id,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = driver.Id,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        if (!debited) return (false, "INSUFFICIENT_FUNDS");

        await _wallets.CreditAsync(drvW.Id.ToString(), dto.Amount);
        var company = await _companies.GetByIdAsync(companyId);
        var freshWallet = await _wallets.GetOrCreateAsync("Company", companyId);
        if (company != null)
            await MaybeNotifyLowBalanceAsync(company.OwnerUserId.ToString(), freshWallet);
        return (true, null);
    }

    public async Task<(bool ok, string? err)> PayMembershipAsync(string companyId, PayMembershipDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var debited = dto.Amount <= 0 ? true : await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Membership",
            Status = debited ? "Completed" : "Failed",
            Amount = dto.Amount,
            FromWalletId = compW.Id,
            ToWalletId = null,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = null,
            Note = dto.Note ?? $"Plan={dto.Plan}; Cycle={dto.BillingCycle}",
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        if (!debited) return (false, "INSUFFICIENT_FUNDS");

        var expires = DateTime.UtcNow.AddMonths(dto.BillingCycle == "quarterly" ? 3 : 1);
        await _companies.UpdateMembershipAsync(companyId, new MembershipInfo
        {
            Plan = dto.Plan,
            BillingCycle = dto.BillingCycle,
            ExpiresAt = expires
        });

        var company = await _companies.GetByIdAsync(companyId);
        var freshWallet = await _wallets.GetOrCreateAsync("Company", companyId);
        if (company != null)
            await MaybeNotifyLowBalanceAsync(company.OwnerUserId.ToString(), freshWallet);
        return (true, null);
    }

    private async Task MaybeNotifyLowBalanceAsync(string ownerUserId, Wallet w)
    {
        var threshold = _cfg.GetValue<decimal?>("Finance:LowBalanceThreshold") ?? DEFAULT_THRESHOLD;
        if (w.Balance < threshold)
        {
            await _notif.PublishAsync(ownerUserId, new CreateNotificationDto(
    NotificationKinds.WalletLowBalance,
    "Số dư ví thấp",
    $"Số dư còn {w.Balance:N0} < ngưỡng {threshold:N0}",
    new Dictionary<string, object> // <— đổi object?>
    {
        ["walletId"] = w.Id.ToString(),
        ["balance"] = w.Balance,
        ["threshold"] = threshold
    }
));

        }
    }
}
