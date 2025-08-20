namespace MyCabs.Application.Realtime;

public interface IRealtimeNotifier
{
    // gửi sự kiện tuỳ tên (dùng cho PublishAsync)
    Task NotifyUserAsync(string userId, string eventName, object payload);

    // tiện ích: gửi event "notification"
    Task PushNotificationAsync(string userId, object payload);

    // tiện ích: cập nhật badge số chưa đọc
    Task PushUnreadCountAsync(string userId);
}
