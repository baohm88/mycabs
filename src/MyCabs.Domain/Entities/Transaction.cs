using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Transaction
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("type")]   // Topup | Salary | Membership
    public string Type { get; set; } = "Topup";

    [BsonElement("status")] // Pending | Completed | Failed
    public string Status { get; set; } = "Pending";

    [BsonElement("amount")] public decimal Amount { get; set; }

    [BsonElement("fromWalletId")] public ObjectId? FromWalletId { get; set; }
    [BsonElement("toWalletId")] public ObjectId? ToWalletId { get; set; }

    // Thuận tiện filter theo business
    [BsonElement("companyId")] public ObjectId? CompanyId { get; set; }
    [BsonElement("driverId")] public ObjectId? DriverId { get; set; }

    [BsonElement("note")] public string? Note { get; set; }

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}