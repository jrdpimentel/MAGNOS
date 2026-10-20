using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace MAGNOS.Server;

public class CafeHub : Hub
{
    readonly CafeOptions _o; readonly IServiceScopeFactory _sf; readonly Billing _billing;
    public CafeHub(CafeOptions o, IServiceScopeFactory sf, Billing b) { _o = o; _sf = sf; _billing = b; }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext()!;
        string pc = http.Request.Query["pc"].ToString(), key = http.Request.Query["key"].ToString();
        if (key != _o.ClientKey || string.IsNullOrWhiteSpace(pc)) { Context.Abort(); return; }
        var addr = http.Connection.RemoteIpAddress;
        var ip = addr == null ? "" : (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).ToString();
        if (ip == "::1") ip = "127.0.0.1";
        Context.Items["pc"] = pc;
        await Groups.AddToGroupAsync(Context.ConnectionId, pc);
        await SetOnline(pc, true, ip);
        using (var scope = _sf.CreateScope())
        {
            // If this PC already has a running session (client restarted), restore it.
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == pc);
            if (s != null)
            {
                await Clients.Caller.SendAsync("StartSession", new { token = s.Token, mode = s.Mode, customer = s.Customer, tier = s.Tier, startedAt = Engine.Iso(s.StartedAt), purchasedMinutes = s.PurchasedMinutes });
                await _billing.Push(db, s);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? e)
    {
        if (Context.Items["pc"] is string pc) await SetOnline(pc, false, null);
        await base.OnDisconnectedAsync(e);
    }

    async Task SetOnline(string name, bool on, string? ip)
    {
        using var scope = _sf.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var pc = await db.Pcs.FirstOrDefaultAsync(p => p.Name == name);
        if (pc == null) { pc = new Pc { Name = name }; db.Pcs.Add(pc); }
        pc.Online = on; pc.LastSeen = DateTime.UtcNow;
        if (ip != null) pc.IpAddress = ip;
        await db.SaveChangesAsync();
    }
}

public class Billing
{
    readonly IHubContext<CafeHub> _hub;
    public Billing(IHubContext<CafeHub> hub) { _hub = hub; }

    // Ends a session, settles the money, and (optionally) locks the PC.
    public async Task End(AppDb db, Session s, bool notify, string message = "")
    {
        if (s.EndedAt != null) return;
        var now = DateTime.UtcNow;
        s.EndedAt = s.PausedAt ?? now;
        var rules = await db.Rules.ToListAsync();
        var elapsed = Engine.Elapsed(s, now);
        if (s.Mode == "Open") s.Amount = Engine.Price(rules, s.Tier, elapsed);
        else if (s.Mode == "Wallet" && s.UserId != null)
        {
            var u = await db.Users.FindAsync(s.UserId.Value);
            if (u != null)
            {
                var pay = Math.Min(Engine.Price(rules, s.Tier, elapsed), u.Wallet);
                u.Wallet -= pay; s.Amount = pay;
            }
        }
        await db.SaveChangesAsync();
        if (notify)
        {
            var text = message;
            if (text.Length > 0) text += " Total: \u20B1" + s.Amount.ToString("0.00");
            await _hub.Clients.Group(s.PcName).SendAsync("Command", "Lock", text);
        }
    }

    // Sends the live state of a session to its PC: minutes left (-1 = open time), minutes used, amount so far, paused?
    public async Task Push(AppDb db, Session s)
    {
        var rules = await db.Rules.ToListAsync();
        User? u = s.UserId == null ? null : await db.Users.FindAsync(s.UserId.Value);
        var now = DateTime.UtcNow; var el = Engine.Elapsed(s, now);
        var rem = Engine.Remaining(s, u, rules, now);
        var amount = s.Mode == "Fixed" ? s.Amount : Engine.Price(rules, s.Tier, el);
        await _hub.Clients.Group(s.PcName).SendAsync("State", rem ?? -1.0, el, (double)amount, s.PausedAt != null);
    }
}

// Every 15 seconds: push time left to clients and end sessions that ran out.
public class BillingService : BackgroundService
{
    readonly IServiceScopeFactory _sf; readonly IHubContext<CafeHub> _hub; readonly Billing _billing; int _n;
    public BillingService(IServiceScopeFactory sf, IHubContext<CafeHub> hub, Billing b) { _sf = sf; _hub = hub; _billing = b; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
            try
            {
                using var scope = _sf.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var rules = await db.Rules.ToListAsync(ct);
                var users = await db.Users.ToDictionaryAsync(u => u.Id, ct);
                var now = DateTime.UtcNow;
                foreach (var s in await db.Sessions.Where(x => x.EndedAt == null).ToListAsync(ct))
                {
                    var u = s.UserId == null ? null : users.GetValueOrDefault(s.UserId.Value);
                    var rem = Engine.Remaining(s, u, rules, now);
                    if (rem != null && rem <= 0 && s.PausedAt == null)
                    { await _billing.End(db, s, true, "Your time has run out."); continue; }
                    await _billing.Push(db, s);
                }
                if (++_n % 240 == 0)   // about once an hour: delete activity history older than 14 days
                {
                    var cutoff = DateTime.UtcNow.AddDays(-14);
                    db.Activities.RemoveRange(await db.Activities.Where(a => a.At < cutoff).ToListAsync(ct));
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
        }
    }
}

