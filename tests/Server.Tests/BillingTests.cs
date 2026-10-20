using Microsoft.EntityFrameworkCore;
using Xunit;
namespace MAGNOS.Server.Tests;

// The money-critical part: what a customer is charged when a session ends.
public class BillingTests
{
    static async Task<Session> AddSession(Harness h, Action<Session> setup)
    {
        var s = new Session { Token = Guid.NewGuid().ToString("N"), PcName = "PC-01", Tier = "WalkIn", Mode = "Open", StartedAt = DateTime.UtcNow };
        setup(s);
        h.Db.Sessions.Add(s);
        await h.Db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Open_time_is_billed_by_the_time_used_and_the_pc_is_locked()
    {
        using var h = new Harness();
        var s = await AddSession(h, x => x.StartedAt = DateTime.UtcNow.AddMinutes(-60.5));

        await h.Billing.End(h.Db, s, notify: true, message: "Thank you.");

        Assert.NotNull(s.EndedAt);
        Assert.Equal(25m, s.Amount);
        var lockMsg = Assert.Single(h.Hub.Sent, m => m.Method == "Command");
        Assert.Equal("PC-01", lockMsg.Target);
        Assert.Equal("Lock", lockMsg.Args[0]);
        Assert.Contains("Total", (string)lockMsg.Args[1]!);
    }

    [Fact]
    public async Task Prepaid_sessions_keep_the_price_paid_up_front()
    {
        using var h = new Harness();
        var s = await AddSession(h, x => { x.Mode = "Fixed"; x.PurchasedMinutes = 60; x.Amount = 25m; x.StartedAt = DateTime.UtcNow.AddMinutes(-10); });

        await h.Billing.End(h.Db, s, notify: false);

        Assert.Equal(25m, s.Amount);
        Assert.Empty(h.Hub.Sent);
    }

    [Fact]
    public async Task Wallet_sessions_are_charged_to_the_wallet()
    {
        using var h = new Harness();
        var user = new User { Username = "maria", PasswordHash = "x", Tier = "Member", Wallet = 50m };
        h.Db.Users.Add(user); await h.Db.SaveChangesAsync();
        var s = await AddSession(h, x => { x.Mode = "Wallet"; x.Tier = "Member"; x.UserId = user.Id; x.Customer = "maria"; x.StartedAt = DateTime.UtcNow.AddMinutes(-60.5); });

        await h.Billing.End(h.Db, s, notify: false);

        Assert.Equal(20m, s.Amount);
        Assert.Equal(30m, (await h.Db.Users.FindAsync(user.Id))!.Wallet);
    }

    [Fact]
    public async Task A_wallet_never_goes_below_zero()
    {
        using var h = new Harness();
        var user = new User { Username = "juan", PasswordHash = "x", Tier = "Member", Wallet = 10m };
        h.Db.Users.Add(user); await h.Db.SaveChangesAsync();
        var s = await AddSession(h, x => { x.Mode = "Wallet"; x.Tier = "Member"; x.UserId = user.Id; x.StartedAt = DateTime.UtcNow.AddMinutes(-60.5); });

        await h.Billing.End(h.Db, s, notify: false);

        Assert.Equal(10m, s.Amount);
        Assert.Equal(0m, (await h.Db.Users.FindAsync(user.Id))!.Wallet);
    }

    [Fact]
    public async Task Ending_a_session_twice_never_charges_twice()
    {
        using var h = new Harness();
        var user = new User { Username = "ana", PasswordHash = "x", Tier = "Member", Wallet = 50m };
        h.Db.Users.Add(user); await h.Db.SaveChangesAsync();
        var s = await AddSession(h, x => { x.Mode = "Wallet"; x.Tier = "Member"; x.UserId = user.Id; x.StartedAt = DateTime.UtcNow.AddMinutes(-60.5); });

        await h.Billing.End(h.Db, s, notify: false);
        await h.Billing.End(h.Db, s, notify: false);

        Assert.Equal(30m, (await h.Db.Users.FindAsync(user.Id))!.Wallet);
    }

    [Fact]
    public async Task A_paused_session_is_billed_only_up_to_the_pause()
    {
        using var h = new Harness();
        var s = await AddSession(h, x => { x.StartedAt = DateTime.UtcNow.AddMinutes(-40.5); x.PausedAt = DateTime.UtcNow.AddMinutes(-10); });

        await h.Billing.End(h.Db, s, notify: false);

        Assert.Equal(12.5m, s.Amount);   // 30 active minutes
    }

    [Fact]
    public async Task Push_sends_the_live_state_to_the_pc()
    {
        using var h = new Harness();
        var s = await AddSession(h, x => { x.Mode = "Fixed"; x.PurchasedMinutes = 60; x.Amount = 25m; x.StartedAt = DateTime.UtcNow.AddMinutes(-15); });

        await h.Billing.Push(h.Db, s);

        var m = Assert.Single(h.Hub.Sent);
        Assert.Equal("State", m.Method);
        Assert.InRange((double)m.Args[0]!, 44, 45.1);   // remaining minutes
        Assert.False((bool)m.Args[3]!);                  // not paused
    }
}

