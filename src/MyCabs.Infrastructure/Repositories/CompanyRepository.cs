using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class CompanyRepository : ICompanyRepository, IIndexInitializer
{
    private readonly IMongoCollection<Company> _col;
    public CompanyRepository(IMongoContext ctx) { _col = ctx.GetCollection<Company>("companies"); }

    public async Task<(IEnumerable<Company> Items, long Total)> FindAsync(
        int page, int pageSize, string? search, string? plan, string? serviceType, string? sort)
    {
        var filter = Builders<Company>.Filter.Empty;
        var fb = Builders<Company>.Filter;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            filter &= (fb.Regex(x => x.Name, new BsonRegularExpression(s, "i"))
                    | fb.Regex(x => x.Description, new BsonRegularExpression(s, "i")));
        }
        if (!string.IsNullOrWhiteSpace(plan))
            filter &= fb.Eq(x => x.Membership!.Plan, plan);
        if (!string.IsNullOrWhiteSpace(serviceType))
            filter &= fb.ElemMatch(x => x.Services, sv => sv.Type == serviceType);

        // sort: "-createdAt" | "createdAt" | "name" | "-name"
        SortDefinition<Company> sortDef = Builders<Company>.Sort.Descending(x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var s = sort.Trim();
            bool desc = s.StartsWith("-");
            var field = desc ? s.Substring(1) : s;
            sortDef = field switch
            {
                "name" => desc ? Builders<Company>.Sort.Descending(x => x.Name) : Builders<Company>.Sort.Ascending(x => x.Name),
                _ => desc ? Builders<Company>.Sort.Descending(x => x.CreatedAt) : Builders<Company>.Sort.Ascending(x => x.CreatedAt)
            };
        }

        var total = await _col.CountDocumentsAsync(filter);
        var items = await _col.Find(filter)
            .Sort(sortDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Company?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        return await _col.Find(x => x.Id == oid).FirstOrDefaultAsync();
    }

    public Task AddServiceAsync(string companyId, CompanyServiceItem item)
    {
        if (!ObjectId.TryParse(companyId, out var oid))
            throw new ArgumentException("Invalid companyId", nameof(companyId));

        var update = Builders<Company>.Update
            .Push(x => x.Services, item)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        return _col.UpdateOneAsync(x => x.Id == oid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var idx1 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.Name));
        var idx2 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.CreatedAt));
        var idx3 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.Membership!.Plan));
        var idx4 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending("services.type"));
        await _col.Indexes.CreateManyAsync(new[] { idx1, idx2, idx3, idx4 });
    }

    public Task UpdateMembershipAsync(string companyId, MembershipInfo info)
    {
        if (!ObjectId.TryParse(companyId, out var oid)) throw new ArgumentException("Invalid companyId");
        var update = Builders<Company>.Update.Set(x => x.Membership, info).Set(x => x.UpdatedAt, DateTime.UtcNow);
        return _col.UpdateOneAsync(x => x.Id == oid, update);
    }

    public async Task<bool> UpdateMainAsync(string ownerUserId, string? name, string? description, string? address)
    {
        if (!ObjectId.TryParse(ownerUserId, out var oid)) return false;
        var update = Builders<Company>.Update
            .Set(x => x.Name, string.IsNullOrWhiteSpace(name) ? (string?)null : name)
            .Set(x => x.Description, description)
            .Set(x => x.Address, address)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.OwnerUserId == oid, update);
        return res.ModifiedCount > 0;
    }

    public async Task<Company?> GetByOwnerUserIdAsync(string ownerUserId)
    {
        if (!ObjectId.TryParse(ownerUserId, out var oid)) return null;
        return await _col.Find(x => x.OwnerUserId == oid).FirstOrDefaultAsync();
    }

    public async Task<Company> CreateAsync(Company c)
    {
        c.Id = ObjectId.GenerateNewId();
        c.CreatedAt = DateTime.UtcNow;
        c.UpdatedAt = DateTime.UtcNow;
        await _col.InsertOneAsync(c);
        return c;
    }

    public async Task<Company> UpsertMainByOwnerAsync(string ownerUserId, string? name, string? description, string? address)
    {
        var exist = await GetByOwnerUserIdAsync(ownerUserId);
        if (exist == null)
        {
            if (!ObjectId.TryParse(ownerUserId, out var oid)) throw new ArgumentException("Invalid ownerUserId");
            var c = new Company
            {
                OwnerUserId = oid,
                Name = string.IsNullOrWhiteSpace(name) ? "New Company" : name!.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
                Address = string.IsNullOrWhiteSpace(address) ? null : address!.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _col.InsertOneAsync(c);
            return c;
        }
        else
        {
            var upd = Builders<Company>.Update
                .Set(x => x.Name, string.IsNullOrWhiteSpace(name) ? exist.Name : name!.Trim())
                .Set(x => x.Description, string.IsNullOrWhiteSpace(description) ? exist.Description : description!.Trim())
                .Set(x => x.Address, string.IsNullOrWhiteSpace(address) ? exist.Address : address!.Trim())
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            await _col.UpdateOneAsync(x => x.Id == exist.Id, upd);
            return await GetByOwnerUserIdAsync(ownerUserId) ?? exist;
        }
    }

    public async Task<IEnumerable<Company>> GetByIdsAsync(IEnumerable<string> ids)
    {
        var oids = ids
            .Where(s => ObjectId.TryParse(s, out _))
            .Select(ObjectId.Parse)
            .ToArray();

        if (oids.Length == 0) return Array.Empty<Company>();
        var f = Builders<Company>.Filter.In(x => x.Id, oids);
        return await _col.Find(f).ToListAsync();
    }

    public async Task<IReadOnlyList<Company>> GetManyByIdsAsync(IEnumerable<string> ids)
    {
        var oids = ids
            .Where(s => MongoDB.Bson.ObjectId.TryParse(s, out _))
            .Select(MongoDB.Bson.ObjectId.Parse)
            .ToArray();

        if (oids.Length == 0) return Array.Empty<Company>();
        var f = Builders<Company>.Filter.In(x => x.Id, oids);
        return await _col.Find(f).ToListAsync();
    }

    public async Task<bool> UpdateProfileByOwnerAsync(
        string ownerUserId,
        string? name,
        string? description,
        string? address,
        List<CompanyServiceItem>? services,
        MembershipInfo? membership)
    {
        if (!ObjectId.TryParse(ownerUserId, out var oid)) return false;

        var upd = new List<UpdateDefinition<Company>>();
        if (name != null) upd.Add(Builders<Company>.Update.Set(x => x.Name, name));
        if (description != null) upd.Add(Builders<Company>.Update.Set(x => x.Description, description));
        if (address != null) upd.Add(Builders<Company>.Update.Set(x => x.Address, address));
        if (services != null) upd.Add(Builders<Company>.Update.Set(x => x.Services, services));
        if (membership != null) upd.Add(Builders<Company>.Update.Set(x => x.Membership, membership));

        upd.Add(Builders<Company>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));
        if (upd.Count == 1) return true; // chỉ set UpdatedAt — coi như ok

        var res = await _col.UpdateOneAsync(x => x.OwnerUserId == oid, Builders<Company>.Update.Combine(upd));
        return res.ModifiedCount > 0;
    }
}