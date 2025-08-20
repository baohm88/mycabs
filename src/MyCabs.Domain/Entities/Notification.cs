using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Notification
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("userId")] public ObjectId UserId { get; set; }
    [BsonElement("type")] public string Type { get; set; } = "info"; // info|warn|payment|chat|...
    [BsonElement("title")] public string? Title { get; set; }
    [BsonElement("message")] public string Message { get; set; } = string.Empty;
    [BsonElement("data")] public BsonDocument? Data { get; set; }
    // [BsonElement("isRead")] public bool IsRead { get; set; } = false;
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("readAt")] public DateTime? ReadAt { get; set; }
    // Thêm (nếu chưa có)

    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


    // Helper (không cần BsonElement)
    [BsonIgnore] public bool IsRead => ReadAt != null;
}