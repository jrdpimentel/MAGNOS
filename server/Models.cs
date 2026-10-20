using Microsoft.EntityFrameworkCore;
namespace MAGNOS.Server;

public class CafeOptions
{
    public string AdminKey { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public bool AllowRegistration { get; set; } = true;
    public string GoogleClientId { get; set; } = "";   // optional: enables Google sign-in on the member portal
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Tier { get; set; } = "Member";      // Member | VIP
    public decimal Wallet { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Pc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public bool Online { get; set; }
    public DateTime LastSeen { get; set; }
}

public class PricingRule
{
    public int Id { get; set; }
    public string Tier { get; set; } = "WalkIn";
    public int Minutes { get; set; }
    public decimal Price { get; set; }
}

public class Session
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public string PcName { get; set; } = "";
    public int? UserId { get; set; }
    public string Customer { get; set; } = "Guest / Walk-in";
    public string Tier { get; set; } = "WalkIn";
    public string Mode { get; set; } = "Open";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public double PausedSeconds { get; set; }
    public int PurchasedMinutes { get; set; }
    public decimal Amount { get; set; }
}

public class Txn
{
    public int Id { get; set; }
    public string Type { get; set; } = "TopUp";
    public string Customer { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int ReorderLevel { get; set; }
    public bool Active { get; set; } = true;
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
}

public class StockIn
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Qty { get; set; }
    public decimal UnitCost { get; set; }
    public int? SupplierId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Sale
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Customer { get; set; } = "Walk-in";
    public string Payment { get; set; } = "Cash";     // Cash | Wallet
    public decimal Total { get; set; }
    public string ItemsJson { get; set; } = "[]";
}

public class ChatMsg
{
    public int Id { get; set; }
    public string PcName { get; set; } = "";
    public bool FromStaff { get; set; }
    public string Text { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class ActivityLog
{
    public int Id { get; set; }
    public string PcName { get; set; } = "";
    public string Customer { get; set; } = "";
    public string App { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> o) : base(o) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<Pc> Pcs => Set<Pc>();
    public DbSet<PricingRule> Rules => Set<PricingRule>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Txn> Txns => Set<Txn>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockIn> StockIns => Set<StockIn>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<ChatMsg> Chats => Set<ChatMsg>();
    public DbSet<ActivityLog> Activities => Set<ActivityLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();
        b.Entity<Pc>().HasIndex(p => p.Name).IsUnique();
        b.Entity<Session>().HasIndex(s => s.Token);
        b.Entity<ActivityLog>().HasIndex(a => a.PcName);
    }
}

