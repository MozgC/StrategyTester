using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class StrategyEdgeCaseTests
{
	[Test]
	public void Run_BuyOnFirstDayAddsToSharesAlreadyHeld()
	{
		var r = new ScriptedStrategy().Run(Daily(10m, 10m), true, initialInvestment: 1500m, initialCash: 1000m, initialShares: 50m);

		Assert.That(r.FinalShares,    Is.EqualTo(150m));
		Assert.That(r.FinalCash,      Is.EqualTo(0m));
		Assert.That(r.FinalCostBasis, Is.EqualTo(1500m));
	}

	[Test]
	public void Run_WithoutLastBuy_CostBasisOfInitialSharesIsFirstPrice()
	{
		var strategy = new ScriptedStrategy(exitDays: new[] { 1 });

		var r = strategy.Run(Daily(10m, 12m), false, initialInvestment: 1000m, initialCash: 0m, initialShares: 100m);

		Assert.That(r.InitialCostBasis,   Is.EqualTo(1000m));
		Assert.That(strategy.GrossProfit, Is.EqualTo(200m));
	}

	[Test]
	public void Run_LosingTradeGoesToGrossLoss()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 0 }, exitDays: new[] { 1 });

		var r = strategy.Run(Daily(10m, 8m), false);

		Assert.That(strategy.GrossLoss,   Is.EqualTo(200m));
		Assert.That(strategy.GrossProfit, Is.EqualTo(0m));
		Assert.That(r.ProfitFactor,       Is.EqualTo(0m));
	}

	[Test]
	public void Run_HoldingPeriodRestartsOnEveryBuy()
	{
		// Buys day0 and day2; with a 3-day holding period measured from the LAST buy, the first allowed sell is day5 (@30)
		var strategy = new ScriptedStrategy(enterDays: new[] { 0, 2 }, exitDays: new[] { 3, 4, 5 }) { BuyAmount = 300m, HoldingPeriodDays = 3 };

		var r = strategy.Run(Daily(10m, 10m, 10m, 20m, 20m, 30m), false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(3));
		Assert.That(r.FinalInvestment, Is.EqualTo(400m + 60m * 30m));
	}

	[Test]
	public void Run_ExitOverrideReplacesShouldExitAndNullRestoresIt()
	{
		var strategy = new ScriptedStrategy();
		var data     = Daily(10m, 20m, 30m);

		strategy.OverrideExitLogic(() => true);
		Assert.That(strategy.Run(data, true).FinalInvestment, Is.EqualTo(2000m)); // sold @20

		strategy.OverrideExitLogic(null);
		Assert.That(strategy.Run(data, true).FinalInvestment, Is.EqualTo(3000m)); // held
	}

	[Test]
	public void Run_NeverSellsOnTheDayItBought()
	{
		// No holding period, exit signal every day: the first-day buy must not be sold on day 0
		var strategy = new ScriptedStrategy { ExitAlways = true };

		var r = strategy.Run(Daily(10m, 20m), buyOnFirstDay: true);

		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		Assert.That(r.FinalInvestment, Is.EqualTo(2000m)); // sold on day 1 @20
	}

	[Test]
	public void Run_EntryTakesPrecedenceOverExitOnSameDay()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 1 }, exitDays: new[] { 1 });

		var r = strategy.Run(Daily(10m, 12m), false, initialInvestment: 1000m, initialCash: 500m, initialShares: 50m);

		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		Assert.That(r.FinalCash,       Is.EqualTo(0m));
		Assert.That(r.FinalShares,     Is.EqualTo(50m + 500m / 12m));
	}

	[Test]
	public void Run_EntryThatBuysNothingStillAllowsExitOnSameDay()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 1 }, exitDays: new[] { 1 }) { BuyAmount = 0m };

		var r = strategy.Run(Daily(10m, 12m), false, initialInvestment: 1000m, initialCash: 500m, initialShares: 50m);

		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		Assert.That(r.FinalCash,       Is.EqualTo(1100m));
	}

	[Test]
	public void Run_ReportsDatesTradesPerYearAndHeatmap()
	{
		var data     = Daily(Enumerable.Repeat(10m, 366).ToArray()); // exactly 365 days -> 1 year
		var strategy = new ScriptedStrategy(enterDays: new[] { 0 }, exitDays: new[] { 365 }) { HeatmapX = 7 };

		var r = strategy.Run(data, false);

		Assert.Multiple(() =>
		{
			Assert.That(r.StartDate,            Is.EqualTo(Start));
			Assert.That(r.EndDate,              Is.EqualTo(Start.AddDays(365)));
			Assert.That(r.TotalTradeCount,      Is.EqualTo(2));
			Assert.That(r.AvgTradeCountPerYear, Is.EqualTo(2m));
			Assert.That(r.HeatmapKey,           Is.EqualTo((7m, 0m)));
			Assert.That(r.HeatmapValue,         Is.EqualTo(r.FinalProfitRatio));
			Assert.That(r.Strategy,             Is.SameAs(strategy));
		});
	}

	[Test]
	public void Run_ReturnToDrawdownSentinels()
	{
		var rising = Daily(Enumerable.Range(0, 30).Select(i => 10m + i).ToArray());

		Assert.That(new ScriptedStrategy().Run(rising, true).ReturnToDrawdownRatio,  Is.EqualTo(9999m), "profit without drawdown");
		Assert.That(new ScriptedStrategy().Run(rising, false).ReturnToDrawdownRatio, Is.EqualTo(0m),    "no profit, no drawdown");
	}
}
