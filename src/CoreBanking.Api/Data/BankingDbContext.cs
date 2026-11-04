using Microsoft.EntityFrameworkCore;

namespace CoreBanking.Api.Data;

public class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ProductMapping> Products => Set<ProductMapping>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BalanceAdjustment> Adjustments => Set<BalanceAdjustment>();
    public DbSet<IdempotencyRecord> Idempotency => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>().HasKey(customer => customer.Id);
        model.Entity<Customer>().Property(customer => customer.Id).HasMaxLength(64);
        model.Entity<Customer>().Property(customer => customer.HolderKey).HasMaxLength(64);
        model.Entity<ProductMapping>().HasKey(product => product.Id);
        model.Entity<ProductMapping>().Property(product => product.Id).HasMaxLength(64);
        model.Entity<ProductMapping>().HasIndex(product => new { product.AccountType, product.Currency }).IsUnique();
        model.Entity<ProductMapping>().Property(product => product.AccountType).HasMaxLength(32);
        model.Entity<ProductMapping>().Property(product => product.Currency).HasMaxLength(3);
        var account = model.Entity<Account>();
        account.HasKey(item => item.Id);
        account.Property(item => item.Id).HasMaxLength(64);
        account.Property(item => item.CustomerId).HasMaxLength(64);
        account.Property(item => item.ProductId).HasMaxLength(64);
        account.Property(item => item.Name).HasMaxLength(200);
        account.Property(item => item.AccountType).HasMaxLength(32);
        account.Property(item => item.AccountState).HasMaxLength(32);
        account.Property(item => item.Currency).HasMaxLength(3);
        account.Property(item => item.Version).IsRowVersion();
        account.Property(item => item.AvailableBalance).HasPrecision(28, 8);
        account.Property(item => item.BookBalance).HasPrecision(28, 8);
        account.Property(item => item.Holds).HasPrecision(28, 8);
        account.Property(item => item.OverdraftLimit).HasPrecision(28, 8);
        account.HasOne<Customer>().WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Restrict);
        account.HasOne<ProductMapping>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BalanceAdjustment>().HasKey(item => item.Id);
        model.Entity<BalanceAdjustment>().Property(item => item.Id).HasMaxLength(64);
        model.Entity<BalanceAdjustment>().Property(item => item.AccountId).HasMaxLength(64);
        model.Entity<BalanceAdjustment>().Property(item => item.Amount).HasPrecision(28, 8);
        model.Entity<BalanceAdjustment>().Property(item => item.Currency).HasMaxLength(3);
        model.Entity<BalanceAdjustment>().Property(item => item.Reason).HasMaxLength(1000);
        model.Entity<BalanceAdjustment>().HasOne<Account>().WithMany().HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdempotencyRecord>().HasKey(item => new { item.Consumer, item.Key });
        model.Entity<IdempotencyRecord>().Property(item => item.Consumer).HasMaxLength(64);
        model.Entity<IdempotencyRecord>().Property(item => item.State).HasMaxLength(16);
        model.Entity<IdempotencyRecord>().HasIndex(item => item.ExpiresAt);
        model.Entity<Customer>().HasData(WorkshopSeed.Customers());
        model.Entity<ProductMapping>().HasData(WorkshopSeed.Products());
        model.Entity<Account>().HasData(WorkshopSeed.Accounts().Select(item => new
        {
            item.Id, item.CustomerId, item.ProductId, item.Name, item.AccountType,
            item.Currency, item.AccountState, item.AvailableBalance, item.BookBalance,
            item.Holds, item.OverdraftLimit, item.BalanceInquiryBlocked, item.AsOf
        }));
        model.Entity<IdempotencyRecord>().HasData(new IdempotencyRecord
        {
            Consumer = "workshop-client", Key = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Payload = "acc-50009|1.00|EUR|Pending workshop adjustment", State = "Processing",
            CreatedAt = WorkshopSeed.Cutoff, ExpiresAt = WorkshopSeed.Cutoff.AddYears(10)
        });
    }
}

public class BankingDesignFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BankingDbContext>()
        .UseSqlServer("Server=localhost,14333;Database=CoreBankingWorkshop;Integrated Security=True;TrustServerCertificate=True").Options);
}