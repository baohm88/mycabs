using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IWalletRepository
{
    Task<Wallet> GetOrCreateAsync(string ownerType, string ownerId);
    Task<Wallet?> GetByOwnerAsync(string ownerType, string ownerId);

    // Giao dịch số dư (đảm bảo không âm khi debit)
    Task<bool> TryDebitAsync(string walletId, decimal amount);
    Task CreditAsync(string walletId, decimal amount);

    Task EnsureIndexesAsync();
}