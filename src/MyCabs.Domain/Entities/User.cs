using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

public class User
{
    [BsonId]
    public ObjectId Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Rider"; // Admin|Rider|Driver|Company
    public bool IsActive { get; set; } = true;
    [BsonElement("emailLower")]
    public string EmailLower { get; set; } = string.Empty; // lưu email lowercase khi tạo user

    [BsonElement("emailVerified")]
    public bool EmailVerified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}