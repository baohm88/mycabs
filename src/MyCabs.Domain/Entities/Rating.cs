using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Rating
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("targetType")] public string TargetType { get; set; } = "Company"; // Company|Driver
    [BsonElement("targetId")] public ObjectId TargetId { get; set; }
    [BsonElement("userId")] public ObjectId UserId { get; set; } // Rider user _id
    [BsonElement("stars")] public int Stars { get; set; }        // 1..5
    [BsonElement("comment")] public string? Comment { get; set; }
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
