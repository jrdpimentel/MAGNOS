using Xunit;
namespace MAGNOS.Server.Tests;

public class SessionMathTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    static Session S(Action<Session>? tweak = null)
    {
        var s = new Session { StartedAt = T0, Tier = "WalkIn", Mode = "Open" };
        tweak?.Invoke(s);
        return s;
    }

    [Fact]
    public void Elapsed_is_the_time_since_the_start()
        => Assert.Equal(90, Engine.Elapsed(S(), T0.AddMinutes(90)), 3);

    [Fact]
    public void Paused_seconds_are_not_counted()
        => Assert.Equal(80, Engine.Elapsed(S(s => s.PausedSeconds = 600), T0.AddMinutes(90)), 3);

    [Fact]
    public void An_ended_session_stops_counting_at_its_end_time()
        => Assert.Equal(30, Engine.Elapsed(S(s => s.EndedAt = T0.AddMinutes(30)), T0.AddMinutes(90)), 3);

    [Fact]
    public void A_paused_session_is_frozen_at_the_moment_it_was_paused()
        => Assert.Equal(20, Engine.Elapsed(S(s => s.PausedAt = T0.AddMinutes(20)), T0.AddMinutes(90)), 3);

    [Fact]
    public void Earlier_pauses_and_a_current_pause_combine()
        => Assert.Equal(15, Engine.Elapsed(S(s => { s.PausedSeconds = 300; s.PausedAt = T0.AddMinutes(20); }), T0.AddMinutes(90)), 3);

    [Fact]
    public void Elapsed_is_never_negative()
        => Assert.Equal(0, Engine.Elapsed(S(), T0.AddMinutes(-5)));

    [Fact]
    public void Prepaid_time_counts_down()
    {
        var s = S(x => { x.Mode = "Fixed"; x.PurchasedMinutes = 60; });
        Assert.Equal(15, Engine.Remaining(s, null, Harness.DefaultRules(), T0.AddMinutes(45))!.Value, 3);
        Assert.Equal(0, Engine.Remaining(s, null, Harness.DefaultRules(), T0.AddMinutes(90))!.Value, 3);
    }

    [Fact]
    public void Wallet_time_is_what_the_balance_can_buy_minus_time_used()
    {
        var s = S(x => x.Mode = "Wallet");
        var user = new User { Wallet = 25m };    // 25 pesos buys 60 minutes at WalkIn rates
        Assert.Equal(40, Engine.Remaining(s, user, Harness.DefaultRules(), T0.AddMinutes(20))!.Value, 3);
    }

    [Fact]
    public void Open_time_sessions_have_no_remaining_time()
        => Assert.Null(Engine.Remaining(S(), null, Harness.DefaultRules(), T0.AddMinutes(20)));

    [Fact]
    public void Wallet_session_without_a_user_has_no_remaining_time()
        => Assert.Null(Engine.Remaining(S(x => x.Mode = "Wallet"), null, Harness.DefaultRules(), T0.AddMinutes(20)));

    [Fact]
    public void Timestamps_are_sent_as_utc()
    {
        Assert.EndsWith("Z", Engine.Iso(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified)));
        Assert.Null(Engine.Iso((DateTime?)null));
    }
}

