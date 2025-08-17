using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyCabs.Infrastructure.Settings;

namespace MyCabs.Infrastructure.Persistence;

public interface IMongoContext
{
    IMongoCollection<T> GetCollection<T>(string name);
}

public class MongoContext : IMongoContext
{
    private readonly IMongoDatabase _db;
    public MongoContext(IOptions<MongoSettings> opts)
    {
        var client = new MongoClient(opts.Value.ConnectionString);
        _db = client.GetDatabase(opts.Value.Database);
    }
    public IMongoCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);
}