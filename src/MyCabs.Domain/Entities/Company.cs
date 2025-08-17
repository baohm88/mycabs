using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Company
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("ownerUserId")]
    public ObjectId OwnerUserId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("address")]
    public string? Address { get; set; }

    [BsonElement("services")]
    public List<CompanyServiceItem> Services { get; set; } = new();

    [BsonElement("membership")]
    public MembershipInfo? Membership { get; set; }

    [BsonElement("walletId")]
    public ObjectId? WalletId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class CompanyServiceItem
{
    [BsonElement("serviceId")]
    public string ServiceId { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("type")]
    public string Type { get; set; } = "taxi"; // taxi|xe_om|hang_hoa|tour

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("basePrice")]
    public decimal BasePrice { get; set; }
}

[BsonIgnoreExtraElements]
public class MembershipInfo
{
    [BsonElement("plan")]
    public string Plan { get; set; } = "Free"; // Free|Basic|Premium

    [BsonElement("billingCycle")]
    public string BillingCycle { get; set; } = "monthly"; // monthly|quarterly

    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
}