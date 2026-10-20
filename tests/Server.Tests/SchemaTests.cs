using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace MAGNOS.Server.Tests;

public class SchemaTests
{
    static AppDb Open(SqliteConnection conn) => new(new DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options);

    [Fact]
    public void A_new_database_gets_every_table_and_upgrading_twice_is_safe()
    {
        using var conn = new SqliteConnection("Data Source=:memory:"); conn.Open();
        using var db = Open(conn);

        Schema.Upgrade(db);
        Schema.Upgrade(db);

        db.Users.Add(new User { Username = "a", PasswordHash = "x", Email = "a@example.com" });
        db.Products.Add(new Product { Name = "Water", Price = 10m, Stock = 5 });
        db.Chats.Add(new ChatMsg { PcName = "PC-01", Text = "hi" });
        db.Activities.Add(new ActivityLog { PcName = "PC-01", App = "notepad" });
        db.SaveChanges();
        Assert.Equal(1, db.Users.Count());
        Assert.Equal(1, db.Products.Count());
    }

    [Fact]
    public void An_old_database_is_upgraded_without_losing_data()
    {
        using var conn = new SqliteConnection("Data Source=:memory:"); conn.Open();
        // The Users table as it looked before the Email column and the shop tables existed.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE ""Users"" (""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY AUTOINCREMENT,
                ""Username"" TEXT NOT NULL, ""PasswordHash"" TEXT NOT NULL, ""FullName"" TEXT NOT NULL,
                ""Tier"" TEXT NOT NULL, ""Wallet"" TEXT NOT NULL, ""CreatedAt"" TEXT NOT NULL);
                INSERT INTO ""Users"" (""Username"",""PasswordHash"",""FullName"",""Tier"",""Wallet"",""CreatedAt"") VALUES ('maria','x','Maria','Member','20','2026-01-01 00:00:00');";
            cmd.ExecuteNonQuery();
        }
        using var db = Open(conn);

        Schema.Upgrade(db);

        var maria = db.Users.Single();
        Assert.Equal("maria", maria.Username);
        Assert.Equal(20m, maria.Wallet);
        Assert.Equal("", maria.Email);
        db.Products.Add(new Product { Name = "Coffee", Price = 25m });
        db.SaveChanges();
    }
}

