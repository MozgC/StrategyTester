using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class BuyWhenDownEdgeCaseTests
{
	private static RunReport Run(int step, int days, params decimal[] prices) =>
		new BuyWhenDownStrategy("X", step, days, 35).Run(Daily(prices), false);

	[Test]
	public void PriceEqualToPeakIsNotANewPeak()
	{
		// level 1 bought @90; back to exactly 100 does not reset levels, so 90 again doesn't buy
		Assert.That(Run(10, 0, 100m, 90m, 100m, 90m).TotalTradeCount, Is.EqualTo(1));
	}

	[Test]
	public void DownDayResetsStabilizationCounter()
	{
		// needs 2 non-down days: 91 (1), 90.5 down (0), 91 (1), 92 (2) -> buy @92
		var r = Run(10, 2, 100m, 90m, 91m, 90.5m, 91m, 92m);

		Assert.That(r.TotalTradeCount,      Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(92m));
	}

	[Test]
	public void UnchangedCloseCountsAsNonDownDay()
	{
		var r = Run(10, 1, 100m, 90m, 90m);

		Assert.That(r.TotalTradeCount, Is.EqualTo(1));
		Assert.That(r.LastBuy.Date,    Is.EqualTo(Start.AddDays(2)));
	}

	[Test]
	public void StepThatDoesNotDivideInvestAllLevel()
	{
		// step 12: levels at -12%, -24%, then all remaining cash at -35%
		var r = Run(12, 0, 100m, 88m, 76m, 65m);
		var a = 1000m * 12m / 35m;

		Assert.That(r.TotalTradeCount, Is.EqualTo(3));
		Assert.That(r.FinalShares,     Is.EqualTo(a / 88m + a / 76m + (1000m - 2 * a) / 65m).Within(0.0000001m));
	}

	[Test]
	public void StepLargerThanInvestAllLevelOnlyBuysAtInvestAllLevel()
	{
		var r = Run(40, 0, 100m, 70m, 64m);

		Assert.That(r.TotalTradeCount,      Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(64m));
		Assert.That(r.FinalCash,            Is.EqualTo(0m));
	}

	[Test]
	public void NoMoreBuysAfterInvestingAllCash()
	{
		Assert.That(Run(10, 0, 100m, 65m, 120m, 100m).TotalTradeCount, Is.EqualTo(1));
	}

	[Test]
	public void ReportsHeatmapKeyAndLabels()
	{
		var strategy = new BuyWhenDownStrategy("X", 10, 2, 35);

		var r = strategy.Run(Daily(100m, 101m), false);

		Assert.That(r.HeatmapKey,                 Is.EqualTo((10m, 2m)));
		Assert.That(strategy.HeatmapAxisLabels.x, Does.Contain("Down %"));
		Assert.That(strategy.HeatmapAxisLabels.y, Does.Contain("days"));
	}

	[Test]
	public void ShallowCloneRunsIndependentlyOfOriginal()
	{
		// Monte Carlo runs clones of the winning strategy in parallel
		var a = Daily(100m, 90m, 80m, 70m, 60m);
		var b = Daily(100m, 95m, 89m, 99m, 110m);

		var original = new BuyWhenDownStrategy("X", 10, 0, 35);
		var clone    = original.ShallowClone();

		var expected = new BuyWhenDownStrategy("X", 10, 0, 35).Run(b, false);
		clone.Run(a, false);
		var actual   = original.Run(b, false);

		Assert.That(actual.FinalShares,     Is.EqualTo(expected.FinalShares));
		Assert.That(actual.TotalTradeCount, Is.EqualTo(expected.TotalTradeCount));
	}
}
