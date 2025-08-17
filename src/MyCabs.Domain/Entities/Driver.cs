using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Driver
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("bio")]
    public string? Bio { get; set; }

    [BsonElement("companyId")]
    public ObjectId? CompanyId { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "offline"; // available|busy|offline

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}