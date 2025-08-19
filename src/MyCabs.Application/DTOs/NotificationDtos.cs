namespace MyCabs.Application.DTOs;

public record NotificationsQuery(int Page = 1, int PageSize = 10, bool? IsRead = null);
public record CreateNotificationDto(string Type, string? Title, string Message, Dictionary<string, object>? Data = null);
public record NotificationDto(string Id, string Type, string? Title, string Message, bool IsRead, DateTime CreatedAt, DateTime? ReadAt, Dictionary<string, object>? Data);