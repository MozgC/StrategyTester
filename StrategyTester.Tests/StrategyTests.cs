using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class StrategyRunTests
{
	[SetUp]
	public void SetUp() => ClearStaticCaches();

	[Test]
	public void Run_TracksTradesProfitFactorAndDrawdown()
	{
		// buy @10 (day1), sell @20 (day2, +1000), buy @10 (day3), dip to 5 (-50% from 2000 peak), sell @8 (day5, -400)
		var strategy = new ScriptedStrategy(enterDays: new[] { 1, 3 }, exitDays: new[] { 2, 5 });

		var r = strategy.Run(Daily(10m, 10m, 20m, 10m, 5m, 8m), buyOnFirstDay: false);

		Assert.Multiple(() =>
		{
			Assert.That(r.FinalInvestment,    Is.EqualTo(1600m));
			Assert.That(r.FinalCash,          Is.EqualTo(1600m));
			Assert.That(r.FinalShares,        Is.EqualTo(0m));
			Assert.That(r.FinalProfitRatio,   Is.EqualTo(1.6m));
			Assert.That(r.TotalTradeCount,    Is.EqualTo(4));
			Assert.That(r.ProfitFactor,       Is.EqualTo(2.5m));
			Assert.That(r.MaxDrawdownPercent, Is.EqualTo(50m));
		});
	}

	[Test]
	public void Run_BuyOnFirstDay_HoldsWithoutCountingATrade()
	{
		var r = new ScriptedStrategy().Run(Daily(10m, 12m, 15m), buyOnFirstDay: true);

		Assert.Multiple(() =>
		{
			Assert.That(r.FinalInvestment,  Is.EqualTo(1500m));
			Assert.That(r.FinalShares,      Is.EqualTo(100m));
			Assert.That(r.TotalTradeCount,  Is.EqualTo(0));
			Assert.That(r.LastBuy.Date,     Is.EqualTo(Start));
			Assert.That(r.ProfitFactor,     Is.EqualTo(9999m), "no losing trades -> sentinel");
		});
	}

	[Test]
	public void Run_WithoutSignals_StaysInCash()
	{
		var r = new ScriptedStrategy().Run(Daily(10m, 20m, 5m), buyOnFirstDay: false);

		Assert.That(r.FinalInvestment,    Is.EqualTo(1000m));
		Assert.That(r.MaxDrawdownPercent, Is.EqualTo(0m));
	}

	[Test]
	public void Run_RespectsHoldingPeriod()
	{
		var strategy = new ScriptedStrategy { ExitAlways = true, HoldingPeriodDays = 5 };

		// Bought day0; exit is blocked until 5 full days have passed -> sells day5 @15
		var r = strategy.Run(Daily(10m, 11m, 12m, 13m, 14m, 15m, 16m, 17m), buyOnFirstDay: true);

		Assert.That(r.FinalInvestment, Is.EqualTo(1500m));
		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
	}

	[Test]
	public void Run_OverrideEntryLogicReplacesShouldEnter()
	{
		var strategy = new ScriptedStrategy();
		strategy.OverrideEntryLogic(() => true);

		var r = strategy.Run(Daily(10m, 20m), buyOnFirstDay: false);

		Assert.That(r.FinalInvestment, Is.EqualTo(2000m));
		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
	}

	[Test]
	public void Run_ContinuesFromGivenSharesAndCash()
	{
		var lastBuy = new StockPrice(Start.AddDays(-10), 8m);

		var r = new ScriptedStrategy().Run(Daily(10m, 12m), false, initialInvestment: 1000m, initialCash: 0m, initialShares: 100m, lastBuy: lastBuy);

		Assert.That(r.FinalInvestment,  Is.EqualTo(1200m));
		Assert.That(r.FinalProfitRatio, Is.EqualTo(1.2m));
		Assert.That(r.LastBuy,          Is.SameAs(lastBuy));
	}

	[Test]
	public void Run_IsRepeatableOnSameInstance()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 1, 3 }, exitDays: new[] { 2, 5 });
		var data     = Daily(10m, 10m, 20m, 10m, 5m, 8m);

		var first  = strategy.Run(data, false);
		var second = strategy.Run(data, false);

		Assert.That(second.FinalInvestment,    Is.EqualTo(first.FinalInvestment));
		Assert.That(second.TotalTradeCount,    Is.EqualTo(first.TotalTradeCount));
		Assert.That(second.MaxDrawdownPercent, Is.EqualTo(first.MaxDrawdownPercent));
	}
}

[TestFixture]
public class MACrossOverStrategyTests
{
	[SetUp]
	public void SetUp() => ClearStaticCaches();

	[Test]
	public void Run_BuysOnceWhenEmaCrossesAboveMaAfterBottom()
	{
		// 20 -> 10 linearly, then 12, 14, ... 50. EMA(3) crosses above MA(6) on day 12 (price 14), never crosses back.
		var prices = Enumerable.Range(0, 11).Select(i => 20m - i)
			.Concat(Enumerable.Range(0, 20).Select(i => 12m + 2 * i))
			.ToArray();

		var strategy = new MACrossOverStrategy(UniqueTicker(), emaDays: 3, maDays: 6) { HoldingPeriodDays = 0 };

		var r = strategy.Run(Daily(prices), buyOnFirstDay: false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(14m));
		Assert.That(r.FinalInvestment, Is.EqualTo(1000m / 14m * 50m).Within(0.0000001m));
	}

	[Test]
	public void Run_DoesNotTradeOnMonotonicDecline()
	{
		var prices   = Enumerable.Range(0, 40).Select(i => 100m - i).ToArray();
		var strategy = new MACrossOverStrategy(UniqueTicker(), 3, 6) { HoldingPeriodDays = 0 };

		var r = strategy.Run(Daily(prices), buyOnFirstDay: false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(0));
		Assert.That(r.FinalInvestment, Is.EqualTo(1000m));
	}

	[Test]
	public void GenerateStrategies_ProducesExpectedParameterGrid()
	{
		var strategies = new MACrossOverStrategy("X", 1, 2).GenerateStrategies("X").Cast<MACrossOverStrategy>().ToList();

		// MA 20..54, EMA 7..(MA+1)/2
		Assert.That(strategies, Has.Count.EqualTo(446));
		Assert.That(strategies.All(s => s.EmaDays >= 7 && s.EmaDays * 2 <= s.MaDays + 1), Is.True);
		Assert.That(strategies.Select(s => (s.EmaDays, s.MaDays)).Distinct().Count(), Is.EqualTo(446));
		Assert.That(strategies.All(s => s.Ticker == "X"), Is.True);
	}
}
