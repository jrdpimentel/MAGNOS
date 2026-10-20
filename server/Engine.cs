namespace MAGNOS.Server;

public static class Engine
{
    public static PricingRule Rule(string tier, int minutes, decimal price) => new() { Tier = tier, Minutes = minutes, Price = price };

    // Price for a duration: whole blocks use the biggest matching rules first;
    // any leftover time is charged pro-rata at the smallest rule's per-minute rate.
    public static decimal Price(IEnumerable<PricingRule> all, string tier, double minutes)
    {
        var rules = all.Where(r => r.Tier == tier && r.Minutes > 0).OrderByDescending(r => r.Minutes).ToList();
        if (rules.Count == 0 || minutes <= 0) return 0;
        decimal total = 0; double left = Math.Floor(minutes);
        foreach (var r in rules)
            while (left >= r.Minutes) { total += r.Price; left -= r.Minutes; }
        var s = rules[^1];
        total += (decimal)left * s.Price / s.Minutes;
        return Math.Round(total, 2);
    }

    // How many minutes a wallet balance can buy.
    public static int Affordable(IEnumerable<PricingRule> all, string tier, decimal money)
    {
        var list = all.ToList(); int m = 0;
        while (m < 1440 && Price(list, tier, m + 1) <= money) m++;
        return m;
    }

    public static double Elapsed(Session s, DateTime now)
    {
        var end = s.EndedAt ?? s.PausedAt ?? now;
        return Math.Max(0, (end - s.StartedAt).TotalMinutes - s.PausedSeconds / 60.0);
    }

    // Minutes left, or null for open-time sessions.
    public static double? Remaining(Session s, User? u, List<PricingRule> rules, DateTime now)
    {
        var e = Elapsed(s, now);
        if (s.Mode == "Fixed") return Math.Max(0, s.PurchasedMinutes - e);
        if (s.Mode == "Wallet" && u != null) return Math.Max(0, Affordable(rules, s.Tier, u.Wallet) - e);
        return null;
    }

    public static string Iso(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("o");
    public static string? Iso(DateTime? d) => d == null ? null : Iso(d.Value);
}

