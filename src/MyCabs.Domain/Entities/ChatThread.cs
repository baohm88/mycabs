using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class ChatThread
{
    [BsonId] public ObjectId Id { get; set; }

    // Key unique cho 2 user theo thứ tự tăng dần (ex: "<a>_<b>")
    [BsonElement("key")] public string Key { get; set; } = string.Empty;

    [BsonElement("users")] public ObjectId[] Users { get; set; } = Array.Empty<ObjectId>(); // length 2

    [BsonElement("lastMessage")] public string? LastMessage { get; set; }
    [BsonElement("lastMessageAt")] public DateTime? LastMessageAt { get; set; }

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}