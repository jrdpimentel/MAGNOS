using Xunit;
namespace MAGNOS.Server.Tests;

public class PricingTests
{
    static readonly List<PricingRule> Rules = Harness.DefaultRules();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(30, 12.5)]
    [InlineData(60, 25)]
    [InlineData(90, 37.5)]
    [InlineData(120, 50)]
    public void Whole_blocks_use_the_rule_prices(double minutes, double expected)
        => Assert.Equal((decimal)expected, Engine.Price(Rules, "WalkIn", minutes));

    [Theory]
    [InlineData(1, 0.42)]
    [InlineData(10, 4.17)]
    [InlineData(45, 18.75)]
    [InlineData(59, 24.58)]
    [InlineData(61, 25.42)]
    public void Leftover_time_is_charged_pro_rata_at_the_smallest_rule_rate(double minutes, double expected)
        => Assert.Equal((decimal)expected, Engine.Price(Rules, "WalkIn", minutes));

    [Fact]
    public void Fractional_minutes_are_floored_to_whole_minutes()
        => Assert.Equal(Engine.Price(Rules, "WalkIn", 59), Engine.Price(Rules, "WalkIn", 59.9));

    [Fact]
    public void Each_tier_uses_its_own_rules()
    {
        Assert.Equal(25m, Engine.Price(Rules, "WalkIn", 60));
        Assert.Equal(20m, Engine.Price(Rules, "Member", 60));
        Assert.Equal(10m, Engine.Price(Rules, "VIP", 60));
    }

    [Fact]
    public void An_unknown_tier_costs_nothing()
        => Assert.Equal(0m, Engine.Price(Rules, "Gold", 60));

    [Fact]
    public void The_largest_rule_is_applied_first()
    {
        var rules = new List<PricingRule>
        {
            Engine.Rule("WalkIn", 30, 12.5m), Engine.Rule("WalkIn", 60, 25m), Engine.Rule("WalkIn", 120, 40m),
        };
        Assert.Equal(40m, Engine.Price(rules, "WalkIn", 120));   // cheaper than 2 x 60
        Assert.Equal(52.5m, Engine.Price(rules, "WalkIn", 150));
        Assert.Equal(65m, Engine.Price(rules, "WalkIn", 180));
    }

    [Fact]
    public void Price_never_goes_down_as_time_goes_up()
    {
        decimal last = 0;
        for (var m = 0; m <= 600; m++)
        {
            var p = Engine.Price(Rules, "WalkIn", m);
            Assert.True(p >= last, $"price dropped at {m} minutes");
            last = p;
        }
    }

    [Fact]
    public void An_empty_rule_list_costs_nothing()
        => Assert.Equal(0m, Engine.Price(new List<PricingRule>(), "WalkIn", 60));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.4, 0)]
    [InlineData(0.42, 1)]
    [InlineData(12.5, 30)]
    [InlineData(25, 60)]
    public void Affordable_minutes_match_what_the_wallet_can_pay(double wallet, int expectedMinutes)
        => Assert.Equal(expectedMinutes, Engine.Affordable(Rules, "WalkIn", (decimal)wallet));

    [Fact]
    public void Affordable_minutes_are_capped_at_24_hours()
        => Assert.Equal(1440, Engine.Affordable(Rules, "WalkIn", 100000m));
}

