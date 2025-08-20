using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class EmailOtp
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("emailLower")] public string EmailLower { get; set; } = string.Empty; // lưu lowercase
    [BsonElement("purpose")] public string Purpose { get; set; } = "verify_email";    // verify_email | reset_password

    [BsonElement("codeHash")] public string CodeHash { get; set; } = string.Empty;      // BCrypt hash
    [BsonElement("expiresAt")] public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);

    [BsonElement("attemptCount")] public int AttemptCount { get; set; } = 0;            // tăng khi verify sai
    [BsonElement("consumedAt")] public DateTime? ConsumedAt { get; set; }               // null = chưa dùng

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}