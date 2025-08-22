using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IChatRepository
{
    Task<ChatThread> GetOrCreateThreadAsync(string userA, string userB);
    Task<ChatThread?> GetThreadByIdAsync(string threadId);
    Task<(IEnumerable<ChatThread> Items, long Total)> ListThreadsForUserAsync(string userId, int page, int pageSize);

    Task<ChatMessage> AddMessageAsync(ChatMessage msg);
    Task<(IEnumerable<ChatMessage> Items, long Total)> ListMessagesAsync(string threadId, int page, int pageSize);

    Task<long> MarkThreadReadAsync(string userId, string threadId);
    Task<long> CountUnreadForUserAsync(string userId);
    Task<long> CountUnreadInThreadAsync(string userId, string threadId);

    Task EnsureIndexesAsync();
}
