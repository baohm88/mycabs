using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Wallet
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("ownerType")] // Company | Driver
    public string OwnerType { get; set; } = "Company";

    [BsonElement("ownerId")]   // Company.Id hoặc Driver.Id
    public ObjectId OwnerId { get; set; }

    [BsonElement("balance")]   // Decimal128 trong Mongo
    public decimal Balance { get; set; } = 0m;

    [BsonElement("lowBalanceThreshold")]
    public decimal LowBalanceThreshold { get; set; } = 100_000m; // cảnh báo số dư thấp

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}