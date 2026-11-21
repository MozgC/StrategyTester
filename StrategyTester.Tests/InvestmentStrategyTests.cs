using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class InvestmentStrategyTests
{
	[Test]
	public void GetMinAndMaxPriceForPeriod_LooksBackStrictlyLessThanPeriodDays()
	{
		var data = Daily(5m, 3m, 8m, 6m, 7m);

		// Period 3 at index 4: days 2..4 are < 3 days back -> {8, 6, 7}
		Assert.That(InvestmentStrategy.GetMinAndMaxPriceForPeriod(data, 4, 3), Is.EqualTo((6m, 8m)));
	}

	[Test]
	public void GetMinAndMaxPriceForPeriod_StopsAtStartOfData()
	{
		var data = Daily(5m, 3m, 8m, 6m, 7m);

		Assert.That(InvestmentStrategy.GetMinAndMaxPriceForPeriod(data, 4, 10), Is.EqualTo((3m, 8m)));
		Assert.That(InvestmentStrategy.GetMinAndMaxPriceForPeriod(data, 0, 10), Is.EqualTo((5m, 5m)));
	}

	[Test]
	public void GetFirstBusinessDatesOfEachMonth_ReturnsFirstPointOfEachNewMonth()
	{
		var data = new List<StockPrice>
		{
			new(new DateTime(2024, 1, 30), 1m),
			new(new DateTime(2024, 1, 31), 1m),
			new(new DateTime(2024, 2, 5),  1m),
			new(new DateTime(2024, 2, 6),  1m),
			new(new DateTime(2024, 3, 1),  1m),
		};

		var withFirst    = InvestmentStrategy.GetFirstBusinessDatesOfEachMonth(data, true) .Select(x => x.index).ToList();
		var withoutFirst = InvestmentStrategy.GetFirstBusinessDatesOfEachMonth(data, false).Select(x => x.index).ToList();

		Assert.That(withFirst,    Is.EqualTo(new[] { 0, 2, 4 }));
		Assert.That(withoutFirst, Is.EqualTo(new[] { 2, 4 }));
	}

	[Test]
	public void BuyAndHold_ReturnsInitialSharesTimesLastPrice()
	{
		var (result, log) = new BuyAndHoldStrategy(1000m).Execute(Daily(10m, 20m, 15m));

		Assert.That(result, Is.EqualTo(1500m));
		Assert.That(log,    Is.Null);
	}

	[Test]
	public void Dca_BuysOnFirstDataPointOfEveryFollowingMonth()
	{
		var data = new List<StockPrice>
		{
			new(new DateTime(2024, 1, 15), 10m), // initial 1000 -> 100 shares
			new(new DateTime(2024, 1, 20), 10m),
			new(new DateTime(2024, 2, 3),  20m), // +100 -> 5 shares
			new(new DateTime(2024, 3, 2),  25m), // +100 -> 4 shares
		};

		var (result, _) = new DcaStrategy(1000m, 100m).Execute(data);

		Assert.That(result, Is.EqualTo(109m * 25m));
	}

	[Test]
	public void BuyLowSellHigh_SellsAtTargetAndRebuysNearPeriodLow()
	{
		// day0 buy 100 @10; day1 11 >= 10*1.1 -> sell 1100; day2 12 not near low;
		// day3 10.1 <= min + (max-min)/10 = 10.2 -> buy; day4 20 -> sell
		var (result, _) = new BuyLowSellHighStrategy(1000m, 10, 10m).Execute(Daily(10m, 11m, 12m, 10.1m, 20m));

		Assert.That(result, Is.EqualTo(1100m / 10.1m * 20m).Within(0.0000001m));
	}

	[Test]
	public void BuyLowSellHigh_HoldsWhenTargetNeverReached()
	{
		var (result, _) = new BuyLowSellHighStrategy(1000m, 10, 10m).Execute(Daily(10m, 10.5m, 9m));

		Assert.That(result, Is.EqualTo(900m));
	}

	[Test]
	public void SaveCashInvestWhenDrop_InvestsAccumulatedCashOnlyAfterDrop()
	{
		var data = new List<StockPrice>
		{
			new(new DateTime(2024, 1, 1), 100m), // initial 1000 -> 10 shares
			new(new DateTime(2024, 2, 1), 100m), // save 100, no drop
			new(new DateTime(2024, 3, 1),  85m), // save 100, 85 <= 90% of 30-day max (100) -> invest 200
			new(new DateTime(2024, 4, 1), 100m), // save 100, no drop -> stays cash
		};

		var (result, _) = new SaveCashInvestWhenDropStrategy(1000m, 100m, 10, 30).Execute(data);

		Assert.That(result, Is.EqualTo(100m * (10m + 200m / 85m) + 100m).Within(0.0000001m));
	}

	[Test]
	public void SaveCashInvestWhenDrop_KeepsCashWhenNoDrop()
	{
		var data = Enumerable.Range(0, 4).Select(i => new StockPrice(new DateTime(2024, 1, 1).AddMonths(i), 100m)).ToList();

		var (result, _) = new SaveCashInvestWhenDropStrategy(1000m, 100m, 10, 30).Execute(data);

		Assert.That(result, Is.EqualTo(1300m));
	}
}
