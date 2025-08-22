using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class ChatMessage
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("threadId")] public ObjectId ThreadId { get; set; }
    [BsonElement("senderUserId")] public ObjectId SenderUserId { get; set; }
    [BsonElement("recipientUserId")] public ObjectId RecipientUserId { get; set; }

    [BsonElement("content")] public string Content { get; set; } = string.Empty;

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("readAt")] public DateTime? ReadAt { get; set; }
}