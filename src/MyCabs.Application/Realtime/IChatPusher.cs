namespace MyCabs.Application.Realtime;

public interface IChatPusher
{
    Task SendToThreadAsync(string threadId, string eventName, object payload);
    Task SendToUserAsync(string userId, string eventName, object payload);
}