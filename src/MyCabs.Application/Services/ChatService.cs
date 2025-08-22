using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Application.Realtime;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IChatService
{
    Task<ThreadDto> StartOrGetThreadAsync(string currentUserId, string peerUserId);
    Task<(IEnumerable<ThreadDto> Items, long Total)> GetThreadsAsync(string currentUserId, ThreadsQuery q);
    Task<(IEnumerable<MessageDto> Items, long Total)> GetMessagesAsync(string currentUserId, string threadId, MessagesQuery q);
    Task<MessageDto> SendMessageAsync(string currentUserId, string threadId, string content);
    Task<long> MarkThreadReadAsync(string currentUserId, string threadId);
    Task<long> GetTotalUnreadAsync(string currentUserId);
}

public class ChatService : IChatService
{
    private readonly IChatRepository _repo;
    private readonly IChatPusher _rt;

    public ChatService(IChatRepository repo, IChatPusher rt)
    { _repo = repo; _rt = rt; }

    public async Task<ThreadDto> StartOrGetThreadAsync(string currentUserId, string peerUserId)
    {
        var t = await _repo.GetOrCreateThreadAsync(currentUserId, peerUserId);
        var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);
        var unread = await _repo.CountUnreadInThreadAsync(currentUserId, t.Id.ToString());
        return new ThreadDto(t.Id.ToString(), t.Users.Select(x => x.ToString()).ToArray(), peer, t.LastMessage, t.LastMessageAt, unread);
    }

    public async Task<(IEnumerable<ThreadDto> Items, long Total)> GetThreadsAsync(string currentUserId, ThreadsQuery q)
    {
        var (items, total) = await _repo.ListThreadsForUserAsync(currentUserId, q.Page, q.PageSize);
        var list = new List<ThreadDto>();
        foreach (var t in items)
        {
            var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);
            var unread = await _repo.CountUnreadInThreadAsync(currentUserId, t.Id.ToString());
            list.Add(new ThreadDto(t.Id.ToString(), t.Users.Select(x => x.ToString()).ToArray(), peer, t.LastMessage, t.LastMessageAt, unread));
        }
        return (list, total);
    }

    public async Task<(IEnumerable<MessageDto> Items, long Total)> GetMessagesAsync(string currentUserId, string threadId, MessagesQuery q)
    {
        var (items, total) = await _repo.ListMessagesAsync(threadId, q.Page, q.PageSize);
        var list = items.Select(m => new MessageDto(
            m.Id.ToString(), m.ThreadId.ToString(), m.SenderUserId.ToString(), m.RecipientUserId.ToString(), m.Content, m.CreatedAt, m.ReadAt
        ));
        return (list, total);
    }

    public async Task<MessageDto> SendMessageAsync(string currentUserId, string threadId, string content)
    {
        if (!ObjectId.TryParse(threadId, out var tid)) throw new ArgumentException("Invalid threadId");
        var t = await _repo.GetThreadByIdAsync(threadId) ?? throw new InvalidOperationException("THREAD_NOT_FOUND");
        var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);

        var msg = new ChatMessage
        {
            Id = ObjectId.GenerateNewId(),
            ThreadId = tid,
            SenderUserId = ObjectId.Parse(currentUserId),
            RecipientUserId = ObjectId.Parse(peer),
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        msg = await _repo.AddMessageAsync(msg);

        var dto = new MessageDto(msg.Id.ToString(), threadId, currentUserId, peer, msg.Content, msg.CreatedAt, msg.ReadAt);
        await _rt.SendToThreadAsync(threadId, "chat.message", dto);

        // cập nhật badge unread cho người nhận
        var totalUnread = await _repo.CountUnreadForUserAsync(peer);
        await _rt.SendToUserAsync(peer, "chat.unread_total", new { count = totalUnread });
        var threadUnread = await _repo.CountUnreadInThreadAsync(peer, threadId);
        await _rt.SendToUserAsync(peer, "chat.thread_unread", new { threadId, count = threadUnread });

        return dto;
    }

    public async Task<long> MarkThreadReadAsync(string currentUserId, string threadId)
    {
        var n = await _repo.MarkThreadReadAsync(currentUserId, threadId);
        var totalUnread = await _repo.CountUnreadForUserAsync(currentUserId);
        await _rt.SendToUserAsync(currentUserId, "chat.unread_total", new { count = totalUnread });
        var threadUnread = await _repo.CountUnreadInThreadAsync(currentUserId, threadId);
        await _rt.SendToUserAsync(currentUserId, "chat.thread_unread", new { threadId, count = threadUnread });
        return n;
    }

    public Task<long> GetTotalUnreadAsync(string currentUserId)
        => _repo.CountUnreadForUserAsync(currentUserId);
}