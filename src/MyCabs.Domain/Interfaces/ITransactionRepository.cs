using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface ITransactionRepository
{
    Task CreateAsync(Transaction tx);

    Task<(IEnumerable<Transaction> Items, long Total)> FindForCompanyAsync(
        string companyId, int page, int pageSize, string? type, string? status);

    Task<(IEnumerable<Transaction> Items, long Total)> FindForDriverAsync(
        string driverId, int page, int pageSize, string? type, string? status);

    Task EnsureIndexesAsync();
}