using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
namespace MAGNOS.Server;

// Creates missing tables/columns on an existing database so updates never lose data.
public static class Schema
{
    public static void Upgrade(AppDb db)
    {
        db.Database.EnsureCreated();
        var script = db.Database.GenerateCreateScript();
        foreach (var raw in script.Split(';'))
        {
            var st = raw.Trim();
            if (st.Length == 0) continue;
            st = st.Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ")
                   .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ")
                   .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ");
            try { db.Database.ExecuteSqlRaw(st); } catch (Exception ex) { Console.Error.WriteLine("Schema: " + ex.Message); }
        }
        TryAlter(db, "ALTER TABLE Users ADD COLUMN Email TEXT NOT NULL DEFAULT ''");
    }
    static void TryAlter(AppDb db, string sql) { try { db.Database.ExecuteSqlRaw(sql); } catch { } }
}

public static class Extras
{
    static readonly ConcurrentDictionary<string, (string app, string title, DateTime at)> Latest = new();
    static readonly ConcurrentDictionary<string, (int uid, DateTime exp, bool google)> Tokens = new();
    static readonly ConcurrentDictionary<string, (int n, DateTime reset)> Fails = new();
    static readonly HttpClient Http = new();

    static IResult Err(int c, string m) => Results.Text(m, "text/plain", System.Text.Encoding.UTF8, c);
    static string Tok() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    static string Cut(string? s, int n) { s ??= ""; return s.Length <= n ? s : s[..n]; }

    public static void Map(WebApplication app, RouteGroupBuilder admin, CafeOptions opts)
    {
        bool ClientOk(HttpRequest r) => r.Headers["X-Client-Key"] == opts.ClientKey;

        // ================= Inventory =================
        admin.MapGet("/products", async (AppDb db) =>
            (await db.Products.ToListAsync()).OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.Category, p.Price, p.Stock, p.ReorderLevel, active = p.Active }).ToList());

        admin.MapPost("/products", async (AppDb db, ProductReq q) =>
        {
            if (string.IsNullOrWhiteSpace(q.Name) || q.Price < 0) return Err(400, "Enter a name and a price.");
            Product? p = q.Id > 0 ? await db.Products.FindAsync(q.Id) : null;
            if (p == null) { p = new Product { Stock = Math.Max(0, q.Stock) }; db.Products.Add(p); }
            p.Name = q.Name.Trim(); p.Category = (q.Category ?? "").Trim(); p.Price = q.Price; p.ReorderLevel = Math.Max(0, q.ReorderLevel); p.Active = true;
            await db.SaveChangesAsync();
            return Results.Ok(new { p.Id });
        });

        admin.MapDelete("/products/{id:int}", async (int id, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p != null) { p.Active = false; await db.SaveChangesAsync(); }
            return Results.Ok();
        });

        admin.MapPost("/stockin", async (AppDb db, StockReq q) =>
        {
            var p = await db.Products.FindAsync(q.ProductId);
            if (p == null || q.Qty <= 0 || q.UnitCost < 0) return Err(400, "Check the item, quantity, and cost.");
            p.Stock += q.Qty;
            db.StockIns.Add(new StockIn { ProductId = p.Id, Qty = q.Qty, UnitCost = q.UnitCost, SupplierId = q.SupplierId });
            await db.SaveChangesAsync();
            return Results.Ok(new { p.Stock });
        });

        admin.MapGet("/suppliers", async (AppDb db) =>
            (await db.Suppliers.ToListAsync()).OrderBy(s => s.Name).Select(s => new { s.Id, s.Name, s.Phone }).ToList());

        admin.MapPost("/suppliers", async (AppDb db, SupplierReq q) =>
        {
            if (string.IsNullOrWhiteSpace(q.Name)) return Err(400, "Enter a name.");
            Supplier? s = q.Id > 0 ? await db.Suppliers.FindAsync(q.Id) : null;
            if (s == null) { s = new Supplier(); db.Suppliers.Add(s); }
            s.Name = q.Name.Trim(); s.Phone = (q.Phone ?? "").Trim();
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        admin.MapDelete("/suppliers/{id:int}", async (int id, AppDb db) =>
        {
            var s = await db.Suppliers.FindAsync(id);
            if (s != null) { db.Suppliers.Remove(s); await db.SaveChangesAsync(); }
            return Results.Ok();
        });

        // ================= Point of sale =================
        admin.MapPost("/sales", async (AppDb db, SaleReq q) =>
        {
            if (q.Items == null || q.Items.Count == 0) return Err(400, "The order is empty.");
            var items = q.Items.GroupBy(i => i.ProductId).Select(g => (id: g.Key, qty: g.Sum(x => x.Qty))).ToList();
            var lines = new List<object>(); decimal total = 0;
            foreach (var it in items)
            {
                var p = await db.Products.FindAsync(it.id);
                if (p == null || !p.Active || it.qty <= 0) return Err(400, "Invalid item in the order.");
                if (p.Stock < it.qty) return Err(409, $"Not enough stock: {p.Name} ({p.Stock} left).");
                total += p.Price * it.qty;
                lines.Add(new { name = p.Name, qty = it.qty, price = p.Price });
            }
            var customer = "Walk-in";
            if (q.Payment == "Wallet")
            {
                var uname = (q.Username ?? "").Trim().ToLowerInvariant();
                var u = await db.Users.FirstOrDefaultAsync(x => x.Username == uname);
                if (u == null) return Err(404, "Choose a member.");
                if (u.Wallet < total) return Err(402, "Not enough wallet balance.");
                u.Wallet -= total; customer = u.Username;
            }
            foreach (var it in items) { var pp = await db.Products.FindAsync(it.id); pp!.Stock -= it.qty; }
            var sale = new Sale { Customer = customer, Payment = q.Payment == "Wallet" ? "Wallet" : "Cash", Total = total, ItemsJson = JsonSerializer.Serialize(lines) };
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
            return Results.Ok(new { sale.Id, total });
        });

        admin.MapGet("/sales", async (AppDb db, string? from, string? to) =>
        {
            var f = DateTime.TryParse(from, out var a) ? a.ToUniversalTime() : DateTime.UtcNow.AddDays(-30);
            var t = DateTime.TryParse(to, out var b) ? b.ToUniversalTime() : DateTime.UtcNow.AddDays(1);
            return (await db.Sales.ToListAsync()).Where(s => s.CreatedAt >= f && s.CreatedAt <= t).OrderByDescending(s => s.Id)
                .Select(s => new { s.Id, at = Engine.Iso(s.CreatedAt), s.Customer, s.Payment, s.Total, items = s.ItemsJson }).ToList();
        });

        // ================= Chat =================
        app.MapPost("/api/chat", async (HttpRequest r, AppDb db, ClientChat q) =>
        {
            if (!ClientOk(r)) return Results.Unauthorized();
            var text = Cut(q.Text?.Trim(), 500);
            if (text.Length == 0 || string.IsNullOrWhiteSpace(q.PcName)) return Err(400, "Empty message.");
            db.Chats.Add(new ChatMsg { PcName = q.PcName!, FromStaff = false, Text = text });
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        admin.MapGet("/chat", async (AppDb db, int? afterId) =>
        {
            var after = afterId ?? 0;
            var list = await db.Chats.Where(c => c.Id > after).OrderBy(c => c.Id).Take(300).ToListAsync();
            return list.Select(c => new { c.Id, pc = c.PcName, fromStaff = c.FromStaff, c.Text, at = Engine.Iso(c.At) }).ToList();
        });

        admin.MapPost("/chat", async (AppDb db, IHubContext<CafeHub> hub, ChatSend q) =>
        {
            var text = Cut(q.Text?.Trim(), 500);
            if (text.Length == 0 || string.IsNullOrWhiteSpace(q.Pc)) return Err(400, "Empty message.");
            db.Chats.Add(new ChatMsg { PcName = q.Pc!, FromStaff = true, Text = text });
            await db.SaveChangesAsync();
            await hub.Clients.Group(q.Pc!).SendAsync("Chat", text);
            return Results.Ok();
        });

        // ================= Activity monitoring =================
        app.MapPost("/api/activity", async (HttpRequest r, AppDb db, ActReq q) =>
        {
            if (!ClientOk(r)) return Results.Unauthorized();
            var pc = q.PcName ?? "";
            var s = await db.Sessions.FirstOrDefaultAsync(x => x.EndedAt == null && x.PcName == pc);
            if (s == null) return Results.Ok();
            var appName = Cut(q.App, 60); var title = Cut(q.Title, 200);
            Latest.TryGetValue(pc, out var prev);
            Latest[pc] = (appName, title, DateTime.UtcNow);
            if (prev.app != appName || prev.title != title)
            {
                db.Activities.Add(new ActivityLog { PcName = pc, Customer = s.Customer, App = appName, Title = title });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        admin.MapGet("/activity", async (AppDb db) =>
        {
            var active = await db.Sessions.Where(s => s.EndedAt == null).ToListAsync();
            return active.OrderBy(s => s.PcName).Select(s =>
            {
                var has = Latest.TryGetValue(s.PcName, out var l);
                return new { pc = s.PcName, customer = s.Customer, app = has ? l.app : "", title = has ? l.title : "", at = has ? Engine.Iso(l.at) : null };
            }).ToList();
        });

        admin.MapGet("/activity/{pc}", async (string pc, AppDb db) =>
            (await db.Activities.Where(a => a.PcName == pc).OrderByDescending(a => a.Id).Take(60).ToListAsync())
                .Select(a => new { a.Customer, a.App, a.Title, at = Engine.Iso(a.At) }).ToList());

        // ================= Member portal =================
        app.MapGet("/portal", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "portal", "index.html"), "text/html"));
        app.MapGet("/api/portal/config", () => new { googleClientId = opts.GoogleClientId });

        bool Throttled(HttpContext c)
        {
            var ip = c.Connection.RemoteIpAddress?.ToString() ?? "?";
            return Fails.TryGetValue(ip, out var f) && f.reset > DateTime.UtcNow && f.n >= 10;
        }
        void Fail(HttpContext c)
        {
            var ip = c.Connection.RemoteIpAddress?.ToString() ?? "?";
            Fails.AddOrUpdate(ip, (1, DateTime.UtcNow.AddMinutes(1)), (_, v) => v.reset < DateTime.UtcNow ? (1, DateTime.UtcNow.AddMinutes(1)) : (v.n + 1, v.reset));
        }
        string NewToken(int uid, bool google)
        {
            var t = Tok(); Tokens[t] = (uid, DateTime.UtcNow.AddHours(12), google); return t;
        }
        async Task<(User? user, bool google)> Auth(HttpRequest r, AppDb db)
        {
            var h = r.Headers.Authorization.ToString();
            if (!h.StartsWith("Bearer ")) return (null, false);
            if (!Tokens.TryGetValue(h[7..], out var v) || v.exp < DateTime.UtcNow) return (null, false);
            return (await db.Users.FindAsync(v.uid), v.google);
        }

        app.MapPost("/api/portal/login", async (HttpContext c, AppDb db, PLogin q) =>
        {
            if (Throttled(c)) return Err(429, "Too many attempts. Try again in a minute.");
            var name = (q.Username ?? "").Trim().ToLowerInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.Username == name);
            if (u == null || !BCrypt.Net.BCrypt.Verify(q.Password ?? "", u.PasswordHash)) { Fail(c); return Err(401, "Wrong username or password."); }
            return Results.Ok(new { token = NewToken(u.Id, false) });
        });

        app.MapPost("/api/portal/google", async (HttpContext c, AppDb db, PGoogle q) =>
        {
            if (string.IsNullOrEmpty(opts.GoogleClientId)) return Err(400, "Google sign-in is not enabled.");
            if (Throttled(c)) return Err(429, "Too many attempts. Try again in a minute.");
            try
            {
                var resp = await Http.GetAsync("https://oauth2.googleapis.com/tokeninfo?id_token=" + Uri.EscapeDataString(q.Credential ?? ""));
                if (!resp.IsSuccessStatusCode) { Fail(c); return Err(401, "Google sign-in failed."); }
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                var aud = root.TryGetProperty("aud", out var a) ? a.GetString() ?? "" : "";
                var email = (root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "").Trim().ToLowerInvariant();
                var verified = root.TryGetProperty("email_verified", out var ev) && string.Equals(ev.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                var full = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (aud != opts.GoogleClientId || !verified || email.Length == 0) { Fail(c); return Err(401, "Google sign-in failed."); }
                var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
                if (u == null)
                {
                    if (!opts.AllowRegistration) return Err(403, "Registration is disabled.");
                    if (await db.Users.AnyAsync(x => x.Username == email)) return Err(409, "That account name is taken.");
                    u = new User { Username = email, Email = email, FullName = full, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Tok()) };
                    db.Users.Add(u); await db.SaveChangesAsync();
                }
                return Results.Ok(new { token = NewToken(u.Id, true) });
            }
            catch (Exception) { return Err(502, "Could not reach Google. Check the server's internet connection."); }
        });

        app.MapGet("/api/portal/me", async (HttpRequest r, AppDb db) =>
        {
            var (u, google) = await Auth(r, db);
            if (u == null) return Results.Unauthorized();
            var rules = await db.Rules.ToListAsync();
            var uid = u.Id; var uname = u.Username;
            var sessions = (await db.Sessions.Where(s => s.UserId == uid).OrderByDescending(s => s.Id).Take(20).ToListAsync())
                .Select(s => new { pc = s.PcName, at = Engine.Iso(s.StartedAt), minutes = (int)Math.Floor(Engine.Elapsed(s, DateTime.UtcNow)), amount = s.Amount, active = s.EndedAt == null }).ToList();
            var topups = (await db.Txns.Where(t => t.Customer == uname && t.Type == "TopUp").OrderByDescending(t => t.Id).Take(20).ToListAsync())
                .Select(t => new { at = Engine.Iso(t.CreatedAt), t.Amount }).ToList();
            return Results.Ok(new { u.Username, u.FullName, u.Email, u.Tier, u.Wallet, minutesLeft = Engine.Affordable(rules, u.Tier, u.Wallet), google, sessions, topups });
        });

        app.MapPost("/api/portal/change-password", async (HttpContext c, AppDb db, PPw q) =>
        {
            var (u, google) = await Auth(c.Request, db);
            if (u == null) return Results.Unauthorized();
            if ((q.NewPassword ?? "").Length < 4) return Err(400, "New password min 4 chars.");
            // Google-created accounts have no known password yet, so they may set one without the old one.
            if (!(google && string.IsNullOrEmpty(q.OldPassword)))
            {
                if (Throttled(c)) return Err(429, "Too many attempts. Try again in a minute.");
                if (!BCrypt.Net.BCrypt.Verify(q.OldPassword ?? "", u.PasswordHash)) { Fail(c); return Err(401, "Old password is wrong."); }
            }
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(q.NewPassword);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}

record ProductReq(int Id, string? Name, string? Category, decimal Price, int ReorderLevel, int Stock);
record StockReq(int ProductId, int Qty, decimal UnitCost, int? SupplierId);
record SupplierReq(int Id, string? Name, string? Phone);
record CartItem(int ProductId, int Qty);
record SaleReq(List<CartItem>? Items, string? Payment, string? Username);
record ChatSend(string? Pc, string? Text);
record ClientChat(string? PcName, string? Text);
record ActReq(string? PcName, string? App, string? Title);
record PLogin(string? Username, string? Password);
record PGoogle(string? Credential);
record PPw(string? OldPassword, string? NewPassword);


