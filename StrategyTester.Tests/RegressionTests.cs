using System.Collections.Concurrent;
using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

/// <summary>Regression tests for bugs fixed in Nov 2026.</summary>
[TestFixture]
public class RegressionTests
{
	/// <summary>
	/// _minMaxPrice used to be keyed by (index, period) only, so a second dataset got the first one's min/max.
	/// </summary>
	[Test]
	public void GetMinAndMaxPriceForPeriod_CacheMustNotLeakAcrossDatasets()
	{
		var a = Daily(1m, 2m, 3m, 4m, 5m);
		var b = Daily(10m, 20m, 30m, 40m, 50m);

		Assert.That(InvestmentStrategy.GetMinAndMaxPriceForPeriod(a, 4, 30), Is.EqualTo((1m, 5m)));
		Assert.That(InvestmentStrategy.GetMinAndMaxPriceForPeriod(b, 4, 30), Is.EqualTo((10m, 50m)));
	}

	/// <summary>
	/// MA/EMA caches used to be keyed by (ticker, date, periods), so Monte Carlo shuffles (same ticker and dates,
	/// different prices) and walk-forward windows (different EMA seed) read another series' indicators.
	/// </summary>
	[Test]
	public void ExponentialMovingAverage_CacheMustNotLeakAcrossDatasetsWithSameTickerAndDates()
	{
		var ticker   = UniqueTicker();
		var real     = Daily(10m, 10m, 10m, 10m);
		var shuffled = Daily(20m, 20m, 20m, 20m);

		Assert.That(IndicatorFunctions.ExponentialMovingAverage(ticker, real,     3, 3), Is.EqualTo(10m));
		Assert.That(IndicatorFunctions.ExponentialMovingAverage(ticker, shuffled, 3, 3), Is.EqualTo(20m));
	}

	/// <summary>
	/// MovingAverage used to return null at index periods-1 although the window is full.
	/// </summary>
	[Test]
	public void MovingAverage_IsAvailableOnceWindowIsFull()
	{
		var data = Daily(1m, 2m, 3m, 4m);

		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, 2), Is.EqualTo(2m));
	}

	/// <summary>
	/// BuyLowSellHigh used to keep the sell target relative to day 0's price after a re-buy.
	/// Here: re-buy @5, price goes to 6 (+20%) - must sell, not wait for 11.
	/// </summary>
	[Test]
	public void BuyLowSellHigh_SellTargetMustBeRelativeToLastBuyPrice()
	{
		// day0 buy 100 @10; day1 sell @11 -> 1100; day2 buy 220 @5; day3 6 >= 5*1.1 -> sell 1320; day4 buy 330 @4
		var (result, _) = new BuyLowSellHighStrategy(1000m, 10, 10m).Execute(Daily(10m, 11m, 5m, 6m, 4m));

		Assert.That(result, Is.EqualTo(1320m).Within(0.0000001m));
	}

	/// <summary>Used int / int, returning 0 unless every run passed.</summary>
	[Test]
	public void PercentOfOOSRunsPassedFilter_MustNotUseIntegerDivision()
	{
		var wfr = new WalkForwardResult(
			new List<RunReport> { new(), new() },
			new List<(DateTime, decimal)>(),
			new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>(),
			1);

		Assert.That(wfr.PercentOfOOSRunsPassedFilter, Is.EqualTo(0.5m));
	}
}
