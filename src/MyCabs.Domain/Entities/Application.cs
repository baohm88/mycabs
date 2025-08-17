using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Application
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("driverId")]
    public ObjectId DriverId { get; set; }

    [BsonElement("companyId")]
    public ObjectId CompanyId { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "Pending"; // Pending|Approved|Rejected

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}