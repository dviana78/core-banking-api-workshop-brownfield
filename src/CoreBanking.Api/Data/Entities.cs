namespace CoreBanking.Api.Data;

public class Customer
{
    public string Id { get; set; } = "";
    public string HolderKey { get; set; } = "";
}

public class ProductMapping
{
    public string Id { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string Currency { get; set; } = "EUR";
}

public class Account
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string AccountState { get; set; } = "ACTIVE";
    public decimal? AvailableBalance { get; set; }
    public decimal? BookBalance { get; set; }
    public decimal Holds { get; set; }
    public decimal OverdraftLimit { get; set; }
    public bool BalanceInquiryBlocked { get; set; }
    public DateTimeOffset AsOf { get; set; }
    public byte[] Version { get; set; } = [];
}

public class BalanceAdjustment
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Reason { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public class IdempotencyRecord
{
    public string Consumer { get; set; } = "";
    public Guid Key { get; set; }
    public string Payload { get; set; } = "";
    public string State { get; set; } = "Processing";
    public int Status { get; set; }
    public string Response { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}