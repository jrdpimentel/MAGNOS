using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MAGNOS.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
var opts = builder.Configuration.GetSection("Cafe").Get<CafeOptions>() ?? new CafeOptions();
builder.Services.AddSingleton(opts);
var dbPath = Path.Combine(AppContext.BaseDirectory, "magnos.db");
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddSignalR();
builder.Services.AddSingleton<Billing>();
builder.Services.AddHostedService<BillingService>();
builder.Services.AddHostedService<DiscoveryService>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Schema.Upgrade(db);
    if (!await db.Rules.AnyAsync())
    {
        db.Rules.AddRange(
            Engine.Rule("WalkIn", 30, 12.5m), Engine.Rule("WalkIn", 60, 25m),
            Engine.Rule("Member", 30, 10m), Engine.Rule("Member", 60, 20m),
            Engine.Rule("VIP", 30, 5m), Engine.Rule("VIP", 60, 10m));
        await db.SaveChangesAsync();
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<CafeHub>("/hub");

IResult Err(int code, string msg) => Results.Text(msg, "text/plain", System.Text.Encoding.UTF8, code);
bool ClientOk(HttpRequest r) => r.Headers["X-Client-Key"] == opts.ClientKey;
string Tok() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
string[] Tiers = { "WalkIn", "Member", "VIP" };

// ================= Client API =================
app.MapPost("/api/register", async (HttpRequest r, AppDb db, Creds c) =>
{
    if (!ClientOk(r)) return Results.Unauthorized();
    if (!opts.AllowRegistration) return Err(403, "Registration is disabled.");
    var name = Norm(c.Username);
    if (name.Length < 3 || (c.Password ?? "").Length < 4) return Err(400, "Username min 3 chars, password min 4 chars.");
    if (await db.Users.AnyAsync(u => u.Username == name)) return Err(409, "Username already taken.");
    db.Users.Add(new User { Username = name, FullName = (c.FullName ?? "").Trim(), PasswordHash = BCrypt.Net.BCrypt.HashPassword(c.Password) });
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/api/change-password", async (HttpRequest r, AppDb db, PwReq q) =>
{
    if (!ClientOk(r)) return Results.Unauthorized();
    var name = Norm(q.Username);
    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == name);
    if (u == null || !BCrypt.Net.BCrypt.Verify(q.OldPassword ?? "", u.PasswordHash)) return Err(401, "Wrong username or password.");
    if ((q.NewPassword ?? "").Length < 4) return Err(400, "New password min 4 chars.");
    u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(q.NewPassword);
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/api/session/start", async (HttpRequest r, AppDb db, StartReq q) =>
{
    if (!ClientOk(r)) return Results.Unauthorized();
    var name = Norm(q.Username);
    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == name);
    if (u == null || !BCrypt.Net.BCrypt.Verify(q.Password ?? "", u.PasswordHash)) return Err(401, "Wrong username or password.");
    var rules = await db.Rules.ToListAsync();
    var aff = Engine.Affordable(rules, u.Tier, u.Wallet);
    if (aff <= 0) return Err(402, "No balance left. Please top up at the counter.");
    if (await db.Sessions.AnyAsync(s => s.EndedAt == null && (s.UserId == u.Id || s.PcName == q.PcName)))
        return Err(409, "Account or PC is already in use.");
    var s = new Session { Token = Tok(), PcName = q.PcName ?? "", UserId = u.Id, Customer = u.Username, Tier = u.Tier, Mode = "Wallet", StartedAt = DateTime.UtcNow };
    db.Sessions.Add(s);
    await db.SaveChangesAsync();
    return Results.Ok(new { token = s.Token, balanceMinutes = (double)aff, username = u.Username });
});

app.MapPost("/api/session/end", async (HttpRequest r, AppDb db, Billing b, EndReq q) =>
{
    if (!ClientOk(r)) return Results.Unauthorized();
    var s = await db.Sessions.FirstOrDefaultAsync(x => x.Token == q.Token && x.EndedAt == null);
    if (s != null) await b.End(db, s, false);
    return Results.Ok(new { amount = s?.Amount ?? 0m });
});

// ================= Admin API =================
var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
    ctx.HttpContext.Request.Headers["X-Admin-Key"] == opts.AdminKey ? await next(ctx) : Results.Unauthorized());

admin.MapGet("/pcs", async (AppDb db) =>
{
    var now = DateTime.UtcNow;
    var rules = await db.Rules.ToListAsync();
    var users = await db.Users.ToDictionaryAsync(u => u.Id);
    var active = await db.Sessions.Where(s => s.EndedAt == null).ToListAsync();
    var pcs = (await db.Pcs.ToListAsync()).OrderBy(p => p.Name.Length).ThenBy(p => p.Name);
    return pcs.Select(p =>
    {
        var s = active.FirstOrDefault(x => x.PcName == p.Name);
        var status = !p.Online ? "Offline" : s == null ? "Available" : s.PausedAt != null ? "Paused" : "Occupied";
        object? sess = null;
        if (s != null)
        {
            var u = s.UserId == null ? null : users.GetValueOrDefault(s.UserId.Value);
            var el = Engine.Elapsed(s, now);
            sess = new
            {
                s.Customer, s.Tier, s.Mode, startedAt = Engine.Iso(s.StartedAt), purchased = s.PurchasedMinutes,
                elapsed = el, remaining = Engine.Remaining(s, u, rules, now),
                amount = s.Mode == "Fixed" ? s.Amount : Engine.Price(rules, s.Tier, el)
            };
        }
        return new { p.Name, ip = p.IpAddress, status, session = sess };
    }).ToList();
});

admin.MapPost("/pcs", async (AppDb db, NameReq q) =>
{
    var n = (q.Name ?? "").Trim().Replace(' ', '-');
    if (n.Length == 0) return Err(400, "Enter a name.");
    if (await db.Pcs.AnyAsync(p => p.Name == n)) return Err(409, "That PC already exists.");
    db.Pcs.Add(new Pc { Name = n }); await db.SaveChangesAsync();
    return Results.Ok();
});

admin.MapDelete("/pcs/{name}", async (string name, AppDb db) =>
{
    if (await db.Sessions.AnyAsync(s => s.EndedAt == null && s.PcName == name)) return Err(409, "PC is in use.");
    var p = await db.Pcs.FirstOrDefaultAsync(x => x.Name == name);
    if (p != null) { db.Pcs.Remove(p); await db.SaveChangesAsync(); }
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/start", async (string name, AppDb db, Billing b, IHubContext<CafeHub> hub, GuestStart q) =>
{
    var pc = await db.Pcs.FirstOrDefaultAsync(p => p.Name == name);
    if (pc == null || !pc.Online) return Err(409, "That PC is offline.");
    if (await db.Sessions.AnyAsync(s => s.EndedAt == null && s.PcName == name)) return Err(409, "PC is already in use.");
    var tier = Tiers.Contains(q.Tier) ? q.Tier! : "WalkIn";
    var mode = q.Mode == "Fixed" ? "Fixed" : "Open";
    var rules = await db.Rules.ToListAsync();
    var s = new Session { Token = Tok(), PcName = name, Tier = tier, Mode = mode, StartedAt = DateTime.UtcNow, PurchasedMinutes = mode == "Fixed" ? q.Minutes : 0 };
    if (mode == "Fixed") { if (q.Minutes <= 0) return Err(400, "Enter a duration."); s.Amount = Engine.Price(rules, tier, q.Minutes); }
    db.Sessions.Add(s); await db.SaveChangesAsync();
    await hub.Clients.Group(name).SendAsync("StartSession", new { token = s.Token, mode, customer = s.Customer, tier, startedAt = Engine.Iso(s.StartedAt), purchasedMinutes = s.PurchasedMinutes });
    await b.Push(db, s);
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/pause", async (string name, AppDb db, Billing b) =>
{
    var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == name);
    if (s == null) return Err(404, "No active session.");
    s.PausedAt ??= DateTime.UtcNow; await db.SaveChangesAsync();
    await b.Push(db, s);
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/resume", async (string name, AppDb db, Billing b) =>
{
    var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == name);
    if (s == null) return Err(404, "No active session.");
    if (s.PausedAt != null) { s.PausedSeconds += (DateTime.UtcNow - s.PausedAt.Value).TotalSeconds; s.PausedAt = null; }
    await db.SaveChangesAsync();
    await b.Push(db, s);
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/stop", async (string name, AppDb db, Billing b) =>
{
    var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == name);
    if (s == null) return Err(404, "No active session.");
    await b.End(db, s, true, "Session ended by staff.");
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/transfer", async (string name, AppDb db, Billing b, IHubContext<CafeHub> hub, TransferReq q) =>
{
    var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == name);
    if (s == null) return Err(404, "No active session.");
    var to = await db.Pcs.FirstOrDefaultAsync(p => p.Name == q.To);
    if (to == null || !to.Online) return Err(409, "Target PC is offline.");
    if (await db.Sessions.AnyAsync(x => x.EndedAt == null && x.PcName == to.Name)) return Err(409, "Target PC is in use.");
    s.PcName = to.Name; await db.SaveChangesAsync();
    await hub.Clients.Group(name).SendAsync("Command", "Lock", "Session moved to " + to.Name + ".");
    await hub.Clients.Group(to.Name).SendAsync("StartSession", new { token = s.Token, mode = s.Mode, customer = s.Customer, tier = s.Tier, startedAt = Engine.Iso(s.StartedAt), purchasedMinutes = s.PurchasedMinutes });
    await b.Push(db, s);
    return Results.Ok();
});

admin.MapPost("/pcs/{name}/command", async (string name, AppDb db, Billing b, IHubContext<CafeHub> hub, CmdReq q) =>
{
    var allowed = new[] { "Lock", "Shutdown", "Restart", "Message" };
    if (!allowed.Contains(q.Command)) return Err(400, "Unknown command.");
    if (q.Command != "Message")
        foreach (var s in await db.Sessions.Where(s => s.EndedAt == null && s.PcName == name).ToListAsync())
            await b.End(db, s, false);
    await hub.Clients.Group(name).SendAsync("Command", q.Command, q.Text ?? "");
    return Results.Ok();
});

admin.MapGet("/pricing", async (AppDb db) =>
    (await db.Rules.ToListAsync()).OrderBy(r => r.Tier).ThenBy(r => r.Minutes).Select(r => new { r.Id, r.Tier, r.Minutes, r.Price }).ToList());

admin.MapPost("/pricing", async (AppDb db, RuleReq q) =>
{
    if (!Tiers.Contains(q.Tier) || q.Minutes <= 0 || q.Price < 0) return Err(400, "Check tier, duration, and price.");
    var r = await db.Rules.FirstOrDefaultAsync(x => x.Tier == q.Tier && x.Minutes == q.Minutes);
    if (r == null) db.Rules.Add(new PricingRule { Tier = q.Tier!, Minutes = q.Minutes, Price = q.Price }); else r.Price = q.Price;
    await db.SaveChangesAsync();
    return Results.Ok();
});

admin.MapDelete("/pricing/{id:int}", async (int id, AppDb db) =>
{
    var r = await db.Rules.FindAsync(id);
    if (r != null) { db.Rules.Remove(r); await db.SaveChangesAsync(); }
    return Results.Ok();
});

admin.MapGet("/billings", async (AppDb db, string? from, string? to) =>
{
    var f = DateTime.TryParse(from, out var a) ? a.ToUniversalTime() : DateTime.UtcNow.AddDays(-30);
    var t = DateTime.TryParse(to, out var b2) ? b2.ToUniversalTime() : DateTime.UtcNow.AddDays(1);
    var now = DateTime.UtcNow; var rules = await db.Rules.ToListAsync();
    return (await db.Sessions.ToListAsync()).Where(s => s.StartedAt >= f && s.StartedAt <= t).OrderByDescending(s => s.Id)
        .Select(s => new
        {
            s.Id, pc = s.PcName, s.Customer, s.Tier, s.Mode, active = s.EndedAt == null,
            minutes = (int)Math.Floor(Engine.Elapsed(s, now)),
            amount = s.EndedAt == null && s.Mode == "Open" ? Engine.Price(rules, s.Tier, Engine.Elapsed(s, now)) : s.Amount,
            at = Engine.Iso(s.StartedAt)
        }).ToList();
});

admin.MapGet("/summary", async (AppDb db, int? days) =>
{
    var d = Math.Clamp(days ?? 30, 1, 365);
    var from = DateTime.UtcNow.Date.AddDays(-(d - 1));
    var now = DateTime.UtcNow; var rules = await db.Rules.ToListAsync();
    decimal Amt(Session s) => s.EndedAt == null && s.Mode == "Open" ? Engine.Price(rules, s.Tier, Engine.Elapsed(s, now)) : s.Amount;
    var sessions = (await db.Sessions.ToListAsync()).Where(s => s.StartedAt >= from).ToList();
    var topups = (await db.Txns.ToListAsync()).Where(t => t.Type == "TopUp" && t.CreatedAt >= from).ToList();
    var series = Enumerable.Range(0, d).Select(i =>
    {
        var day = from.AddDays(i);
        return new { date = day.ToString("yyyy-MM-dd"), amount = sessions.Where(s => s.StartedAt.Date == day).Sum(s2 => Amt(s2)) };
    }).ToList();
    return new
    {
        totalTransactions = sessions.Count,
        members = await db.Users.CountAsync(),
        walkIn = sessions.Count(s => s.UserId == null),
        memberSessions = sessions.Count(s => s.UserId != null),
        rentalIncome = sessions.Sum(s2 => Amt(s2)),
        topUpTotal = topups.Sum(t => t.Amount),
        salesRevenue = (await db.Sales.ToListAsync()).Where(x => x.CreatedAt >= from).Sum(x => x.Total),
        restockCost = (await db.StockIns.ToListAsync()).Where(x => x.CreatedAt >= from).Sum(x => x.Qty * x.UnitCost),
        inventoryItems = await db.Products.CountAsync(p => p.Active),
        lowStock = await db.Products.CountAsync(p => p.Active && p.Stock <= p.ReorderLevel),
        series
    };
});

admin.MapGet("/users", async (AppDb db) =>
    (await db.Users.ToListAsync()).OrderBy(u => u.Username).Select(u => new { u.Username, u.FullName, u.Tier, u.Wallet }).ToList());

admin.MapPost("/topup", async (AppDb db, TopupReq q) =>
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == q.Username.Trim().ToLowerInvariant());
    if (u == null) return Err(404, "User not found.");
    if (q.Amount == 0) return Err(400, "Enter an amount.");
    u.Wallet = Math.Max(0, u.Wallet + q.Amount);
    db.Txns.Add(new Txn { Type = "TopUp", Customer = u.Username, Amount = q.Amount });
    await db.SaveChangesAsync();
    return Results.Ok(new { u.Username, u.Wallet });
});

admin.MapPost("/users/{username}/tier", async (string username, AppDb db, TierReq q) =>
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == username.Trim().ToLowerInvariant());
    if (u == null || (q.Tier != "Member" && q.Tier != "VIP")) return Err(400, "Invalid user or tier.");
    u.Tier = q.Tier; await db.SaveChangesAsync();
    return Results.Ok();
});

admin.MapPost("/users/{username}/password", async (string username, AppDb db, PwSet q) =>
{
    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == username.Trim().ToLowerInvariant());
    if (u == null) return Err(404, "User not found.");
    if ((q.Password ?? "").Length < 4) return Err(400, "Password min 4 chars.");
    u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(q.Password); await db.SaveChangesAsync();
    return Results.Ok();
});

Extras.Map(app, admin, opts);

// ---------- Wallpaper ----------
var wpPath = Path.Combine(AppContext.BaseDirectory, "wallpaper.bin");
var wpType = Path.Combine(AppContext.BaseDirectory, "wallpaper.type");
IResult WallpaperFile() => File.Exists(wpPath)
    ? Results.File(wpPath, File.Exists(wpType) ? File.ReadAllText(wpType).Trim() : "image/jpeg")
    : Err(404, "No wallpaper.");

app.MapGet("/api/wallpaper", (HttpRequest r) => ClientOk(r) ? WallpaperFile() : Results.Unauthorized());
admin.MapGet("/wallpaper", () => WallpaperFile());

admin.MapPost("/wallpaper", async (HttpRequest r, IHubContext<CafeHub> hub) =>
{
    var ct = r.ContentType ?? "";
    if (!ct.StartsWith("image/")) return Err(400, "Please choose an image file.");
    using var ms = new MemoryStream();
    await r.Body.CopyToAsync(ms);
    if (ms.Length == 0 || ms.Length > 8 * 1024 * 1024) return Err(400, "Image must be under 8 MB.");
    await File.WriteAllBytesAsync(wpPath, ms.ToArray());
    await File.WriteAllTextAsync(wpType, ct);
    await hub.Clients.All.SendAsync("Wallpaper");
    return Results.Ok();
});

admin.MapDelete("/wallpaper", async (IHubContext<CafeHub> hub) =>
{
    if (File.Exists(wpPath)) File.Delete(wpPath);
    await hub.Clients.All.SendAsync("Wallpaper");
    return Results.Ok();
});

app.Run();

record Creds(string? Username, string? Password, string? FullName);
record StartReq(string? Username, string? Password, string? PcName);
record EndReq(string? Token);
record PwReq(string? Username, string? OldPassword, string? NewPassword);
record NameReq(string? Name);
record GuestStart(string? Mode, int Minutes, string? Tier);
record TransferReq(string? To);
record CmdReq(string Command, string? Text);
record RuleReq(string? Tier, int Minutes, decimal Price);
record TopupReq(string Username, decimal Amount);
record TierReq(string? Tier);
record PwSet(string? Password);

