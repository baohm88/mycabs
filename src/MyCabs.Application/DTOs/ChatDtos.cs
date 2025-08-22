namespace MyCabs.Application.DTOs;

public record StartChatDto(string PeerUserId);
public record SendChatMessageDto(string Content);
public record ThreadsQuery(int Page = 1, int PageSize = 20);
public record MessagesQuery(int Page = 1, int PageSize = 50);

public record ThreadDto(
    string Id,
    string[] UserIds,
    string PeerUserId,
    string? LastMessage,
    DateTime? LastMessageAt,
    long UnreadCount
);

public record MessageDto(
    string Id,
    string ThreadId,
    string SenderUserId,
    string RecipientUserId,
    string Content,
    DateTime CreatedAt,
    DateTime? ReadAt
);