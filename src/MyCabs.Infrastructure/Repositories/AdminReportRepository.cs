using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Application.Interfaces;
using MyCabs.Infrastructure.Persistence;

namespace MyCabs.Infrastructure.Repositories;

public class AdminReportRepository : IAdminReportRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly IMongoCollection<Company> _companies;
    private readonly IMongoCollection<Driver> _drivers;
    private readonly IMongoCollection<Wallet> _wallets;
    private readonly IMongoCollection<Transaction> _txs;

    public AdminReportRepository(IMongoContext ctx)
    {
        _users = ctx.GetCollection<User>("users");
        _companies = ctx.GetCollection<Company>("companies");
        _drivers = ctx.GetCollection<Driver>("drivers");
        _wallets = ctx.GetCollection<Wallet>("wallets");
        _txs = ctx.GetCollection<Transaction>("transactions");
    }

    public async Task<AdminOverviewDto> GetOverviewAsync(DateTime from, DateTime to)
    {
        // 1) Totals
        var usersTotal = await _users.CountDocumentsAsync(FilterDefinition<User>.Empty);
        var companiesTotal = await _companies.CountDocumentsAsync(FilterDefinition<Company>.Empty);
        var driversTotal = await _drivers.CountDocumentsAsync(FilterDefinition<Driver>.Empty);

        // 2) Wallet sums
        var walletsAgg = await _wallets.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "sum", new BsonDocument("$sum", "$balance") } })
            .FirstOrDefaultAsync();
        var walletsTotalBalance = walletsAgg == null ? 0 : walletsAgg["sum"].ToDecimal();

        // 3) Transactions in range
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to);

        var txCount = await _txs.CountDocumentsAsync(match);

        var txAmountAgg = await _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "amount", new BsonDocument("$sum", "$amount") } })
            .FirstOrDefaultAsync();
        var txAmount = txAmountAgg == null ? 0 : txAmountAgg["amount"].ToDecimal();

        // 4) Breakdown by Type
        var byType = await _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument {
                {"_id", "$type"},
                {"amount", new BsonDocument("$sum", "$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .ToListAsync();
        var amountByType = byType.ToDictionary(d => d["_id"].AsString, d => d["amount"].ToDecimal());
        var countByType = byType.ToDictionary(d => d["_id"].AsString, d => d["count"].ToInt64());

        return new AdminOverviewDto(usersTotal, companiesTotal, driversTotal, walletsTotalBalance, txCount, txAmount, amountByType, countByType);
    }

    public async Task<IEnumerable<TimePointDto>> GetTransactionsDailyAsync(DateTime from, DateTime to)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to);
        // group by day using $dateToString to keep compat
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument {
                {"_id", new BsonDocument("$dateToString", new BsonDocument{ {"format","%Y-%m-%d"}, {"date","$createdAt"} })},
                {"amount", new BsonDocument("$sum", "$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("_id", 1));
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TimePointDto(d["_id"].AsString, d["count"].ToInt64(), d["amount"].ToDecimal()));
    }

    public async Task<IEnumerable<TopCompanyDto>> GetTopCompaniesAsync(DateTime from, DateTime to, int limit)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to)
                  & Builders<Transaction>.Filter.Ne(x => x.CompanyId, null);
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument{
                {"_id","$companyId"},
                {"amount", new BsonDocument("$sum","$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("amount", -1))
            .Limit(limit)
            .Lookup("companies", "_id", "_id", "cmp")
            .Project(new BsonDocument{
                {"companyId", new BsonDocument("$toString", "$_id")},
                {"name", new BsonDocument("$let", new BsonDocument{{"vars", new BsonDocument("c", new BsonDocument("$arrayElemAt", new BsonArray{ "$cmp", 0 }))},{"in", "$$c.name"}})},
                {"amount", 1},
                {"count", 1}
            });
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TopCompanyDto(
            d["companyId"].AsString,
            d.Contains("name") && d["name"].BsonType != BsonType.Null ? d["name"].AsString : null,
            d["amount"].ToDecimal(),
            d["count"].ToInt64()
        ));
    }

    public async Task<IEnumerable<TopDriverDto>> GetTopDriversAsync(DateTime from, DateTime to, int limit)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to)
                  & Builders<Transaction>.Filter.Ne(x => x.DriverId, null);
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument{
                {"_id","$driverId"},
                {"amount", new BsonDocument("$sum","$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("amount", -1))
            .Limit(limit)
            .Lookup("drivers", "_id", "_id", "drv")
            .Project(new BsonDocument{
                {"driverId", new BsonDocument("$toString", "$_id")},
                {"name", new BsonDocument("$let", new BsonDocument{{"vars", new BsonDocument("d", new BsonDocument("$arrayElemAt", new BsonArray{ "$drv", 0 }))},{"in", "$$d.fullName"}})},
                {"amount", 1},
                {"count", 1}
            });
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TopDriverDto(
            d["driverId"].AsString,
            d.Contains("name") && d["name"].BsonType != BsonType.Null ? d["name"].AsString : null,
            d["amount"].ToDecimal(),
            d["count"].ToInt64()
        ));
    }

    public async Task<IEnumerable<LowWalletDto>> GetLowWalletsAsync(decimal threshold, int limit, string ownerType = "Company")
    {
        var f = Builders<Wallet>.Filter.Eq(x => x.OwnerType, ownerType) & Builders<Wallet>.Filter.Lt(x => x.Balance, threshold);
        var items = await _wallets.Find(f).SortBy(x => x.Balance).Limit(limit).ToListAsync();
        return items.Select(w => new LowWalletDto(w.Id.ToString(), w.OwnerType, w.OwnerId.ToString(), w.Balance, w.LowBalanceThreshold));
    }
}