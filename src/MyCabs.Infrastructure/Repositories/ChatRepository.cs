using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class ChatRepository : IChatRepository, IIndexInitializer
{
    private readonly IMongoCollection<ChatThread> _threads;
    private readonly IMongoCollection<ChatMessage> _messages;

    public ChatRepository(IMongoContext ctx)
    {
        _threads = ctx.GetCollection<ChatThread>("chat_threads");
        _messages = ctx.GetCollection<ChatMessage>("chat_messages");
    }

    private static (ObjectId a, ObjectId b, string key) NormalizePair(string userA, string userB)
    {
        var oa = ObjectId.Parse(userA);
        var ob = ObjectId.Parse(userB);
        var (min, max) = oa.CompareTo(ob) <= 0 ? (oa, ob) : (ob, oa);
        return (min, max, $"{min}_{max}");
    }

    public async Task<ChatThread> GetOrCreateThreadAsync(string userA, string userB)
    {
        var (a, b, key) = NormalizePair(userA, userB);
        var exist = await _threads.Find(x => x.Key == key).FirstOrDefaultAsync();
        if (exist != null) return exist;

        var t = new ChatThread
        {
            Id = ObjectId.GenerateNewId(),
            Key = key,
            Users = new[] { a, b },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _threads.InsertOneAsync(t);
        return t;
    }

    public Task<ChatThread?> GetThreadByIdAsync(string threadId)
    {
        if (!ObjectId.TryParse(threadId, out var oid)) return Task.FromResult<ChatThread?>(null);
        return _threads.Find(x => x.Id == oid).FirstOrDefaultAsync();
    }

    public async Task<(IEnumerable<ChatThread> Items, long Total)> ListThreadsForUserAsync(string userId, int page, int pageSize)
    {
        var uid = ObjectId.Parse(userId);
        var f = Builders<ChatThread>.Filter.AnyEq(x => x.Users, uid);
        var total = await _threads.CountDocumentsAsync(f);
        var items = await _threads.Find(f)
            .SortByDescending(x => x.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<ChatMessage> AddMessageAsync(ChatMessage msg)
    {
        await _messages.InsertOneAsync(msg);
        var upd = Builders<ChatThread>.Update
            .Set(x => x.LastMessage, msg.Content)
            .Set(x => x.LastMessageAt, msg.CreatedAt)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _threads.UpdateOneAsync(x => x.Id == msg.ThreadId, upd);
        return msg;
    }

    public async Task<(IEnumerable<ChatMessage> Items, long Total)> ListMessagesAsync(string threadId, int page, int pageSize)
    {
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid);
        var total = await _messages.CountDocumentsAsync(f);
        var items = await _messages.Find(f)
            .SortBy(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<long> MarkThreadReadAsync(string userId, string threadId)
    {
        var uid = ObjectId.Parse(userId);
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid)
              & Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        var upd = Builders<ChatMessage>.Update.Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _messages.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public Task<long> CountUnreadForUserAsync(string userId)
    {
        var uid = ObjectId.Parse(userId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        return _messages.CountDocumentsAsync(f);
    }

    public Task<long> CountUnreadInThreadAsync(string userId, string threadId)
    {
        var uid = ObjectId.Parse(userId);
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid)
              & Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        return _messages.CountDocumentsAsync(f);
    }

    public async Task EnsureIndexesAsync()
    {
        var ixThreads = new List<CreateIndexModel<ChatThread>>
        {
            new(Builders<ChatThread>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true }),
            new(Builders<ChatThread>.IndexKeys.Ascending(x => x.Users).Descending(x => x.LastMessageAt))
        };
        await _threads.Indexes.CreateManyAsync(ixThreads);

        var ixMsgs = new List<CreateIndexModel<ChatMessage>>
        {
            new(Builders<ChatMessage>.IndexKeys.Ascending(x => x.ThreadId).Ascending(x => x.CreatedAt)),
            new(Builders<ChatMessage>.IndexKeys.Ascending(x => x.RecipientUserId).Ascending(x => x.ReadAt))
        };
        await _messages.Indexes.CreateManyAsync(ixMsgs);
    }
}
