using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

/// <summary>Strategy.Run with partial buys: cash and shares held at the same time.</summary>
[TestFixture]
public class StrategyPartialPositionTests
{
	[Test]
	public void Run_PartialBuy_HoldsCashAndShares()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 1 }) { BuyAmount = 400m };

		var r = strategy.Run(Daily(10m, 10m, 20m), buyOnFirstDay: false);

		Assert.Multiple(() =>
		{
			Assert.That(r.FinalCash,       Is.EqualTo(600m));
			Assert.That(r.FinalShares,     Is.EqualTo(40m));
			Assert.That(r.FinalInvestment, Is.EqualTo(1400m));
			Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		});
	}

	[Test]
	public void Run_SellPnlUsesCostOfAllBuys()
	{
		// 50 shares @10 + 100 shares @5 = cost 1000; sell 150 @8 = 1200 -> +200
		var strategy = new ScriptedStrategy(enterDays: new[] { 0, 1 }, exitDays: new[] { 2 }) { BuyAmount = 500m };

		var r = strategy.Run(Daily(10m, 5m, 8m), buyOnFirstDay: false);

		Assert.Multiple(() =>
		{
			Assert.That(r.FinalInvestment,      Is.EqualTo(1200m));
			Assert.That(r.TotalTradeCount,      Is.EqualTo(3));
			Assert.That(strategy.GrossProfit,   Is.EqualTo(200m));
			Assert.That(strategy.GrossLoss,     Is.EqualTo(0m));
			Assert.That(r.FinalCostBasis,       Is.EqualTo(0m));
		});
	}

	[Test]
	public void Run_HoldingPeriodDelaysSellingButNotBuying()
	{
		// Buys on day 0 and day 2 (inside the holding period of the first buy); sell on day 3 is blocked (1 day after last buy)
		var strategy = new ScriptedStrategy(enterDays: new[] { 0, 2 }, exitDays: new[] { 3 }) { BuyAmount = 300m, HoldingPeriodDays = 5 };

		var r = strategy.Run(Daily(10m, 10m, 10m, 10m, 10m), buyOnFirstDay: false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(2));
		Assert.That(r.FinalShares,     Is.EqualTo(60m));
		Assert.That(r.FinalCash,       Is.EqualTo(400m));
	}

	[Test]
	public void Run_DrawdownIsMeasuredOnCashPlusShares()
	{
		// 500 cash + 50 shares @10 -> 500 + 50*5 = 750 -> 25% drawdown
		var strategy = new ScriptedStrategy(enterDays: new[] { 0 }) { BuyAmount = 500m };

		var r = strategy.Run(Daily(10m, 5m), buyOnFirstDay: false);

		Assert.That(r.MaxDrawdownPercent, Is.EqualTo(25m));
	}

	[Test]
	public void Run_BuyAmountIsCappedAtCash()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 0 }) { BuyAmount = 5000m };

		var r = strategy.Run(Daily(10m, 20m), buyOnFirstDay: false);

		Assert.That(r.FinalCash,       Is.EqualTo(0m));
		Assert.That(r.FinalInvestment, Is.EqualTo(2000m));
	}

	[Test]
	public void Run_ReportsCostBasisForContinuation()
	{
		var strategy = new ScriptedStrategy(enterDays: new[] { 1 }) { BuyAmount = 300m };

		var r = strategy.Run(Daily(10m, 15m), false, initialInvestment: 1000m, initialCash: 500m, initialShares: 50m, lastBuy: null, initialCostBasis: 400m);

		Assert.That(r.InitialCostBasis, Is.EqualTo(400m));
		Assert.That(r.FinalCostBasis,   Is.EqualTo(700m));
	}
}
