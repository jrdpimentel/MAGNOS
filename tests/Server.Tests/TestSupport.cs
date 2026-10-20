using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace MAGNOS.Server.Tests;

public sealed class SentMessage
{
    public string Target = "";
    public string Method = "";
    public object?[] Args = Array.Empty<object?>();
}

// A stand-in for SignalR that records what the server would have sent to the PCs.
public sealed class FakeHub : IHubContext<CafeHub>
{
    public readonly List<SentMessage> Sent = new();
    public IHubClients Clients { get; }
    public IGroupManager Groups { get; } = new NoGroups();
    public FakeHub() { Clients = new FakeClients(this); }

    sealed class NoGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    sealed class Proxy : IClientProxy
    {
        readonly FakeHub _hub; readonly string _target;
        public Proxy(FakeHub hub, string target) { _hub = hub; _target = target; }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _hub.Sent.Add(new SentMessage { Target = _target, Method = method, Args = args });
            return Task.CompletedTask;
        }
    }

    sealed class FakeClients : IHubClients
    {
        readonly FakeHub _hub;
        public FakeClients(FakeHub hub) { _hub = hub; }
        IClientProxy P(string t) => new Proxy(_hub, t);
        public IClientProxy All => P("*");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => P("*");
        public IClientProxy Client(string connectionId) => P(connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => P("*");
        public IClientProxy Group(string groupName) => P(groupName);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => P(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => P("*");
        public IClientProxy User(string userId) => P(userId);
        public IClientProxy Users(IReadOnlyList<string> userIds) => P("*");
    }
}

// A fresh in-memory database, the default price rules, and a Billing service wired to a FakeHub.
public sealed class Harness : IDisposable
{
    readonly SqliteConnection _conn;
    public AppDb Db { get; }
    public FakeHub Hub { get; }
    public Billing Billing { get; }

    public Harness()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        Schema.Upgrade(Db);
        Db.Rules.AddRange(DefaultRules());
        Db.SaveChanges();
        Hub = new FakeHub();
        Billing = new Billing(Hub);
    }

    public static List<PricingRule> DefaultRules() => new()
    {
        Engine.Rule("WalkIn", 30, 12.5m), Engine.Rule("WalkIn", 60, 25m),
        Engine.Rule("Member", 30, 10m),  Engine.Rule("Member", 60, 20m),
        Engine.Rule("VIP", 30, 5m),      Engine.Rule("VIP", 60, 10m),
    };

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}

