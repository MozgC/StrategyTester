using System.Collections.Concurrent;
using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

/// <summary>
/// Tests asserting the intended behaviour where the current code is wrong. They FAIL until the bug is fixed.
/// Exclude them with:  dotnet test --filter "TestCategory!=KnownBug"
/// </summary>
[TestFixture, Category("KnownBug")]
public class KnownBugTests
{
	[SetUp]
	public void SetUp() => ClearStaticCaches();

	/// <summary>
	/// _minMaxPrice is keyed by (index, period) only, so a second dataset gets the first one's min/max.
	/// Affects BuyLowSellHigh / SaveCashInvestWhenDrop whenever more than one series (ticker, IS window,
	/// Monte Carlo shuffle) is evaluated in the same process.
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
	/// MA/EMA caches are keyed by (ticker, date, periods). Monte Carlo shuffles keep the ticker and dates but change
	/// prices, so MACrossOver on a shuffled path reads the real series' indicators. Walk-forward IS windows have the
	/// same issue for EMA, whose value depends on where the window (and therefore the SMA seed) starts.
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
	/// MovingAverage returns null at index periods-1 although the window is full (guard should be
	/// currentDayIndex &lt; periods - 1, as in ExponentialMovingAverage). Delays MACrossOver signals by a day.
	/// </summary>
	[Test]
	public void MovingAverage_IsAvailableOnceWindowIsFull()
	{
		var data = Daily(1m, 2m, 3m, 4m);

		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, 2), Is.EqualTo(2m));
	}

	/// <summary>
	/// BuyLowSellHigh never updates paidPrice after a re-buy, so the sell target stays relative to day 0's price.
	/// Here: re-buy @5, price goes to 6 (+20%) but the code waits for 11.
	/// </summary>
	[Test]
	public void BuyLowSellHigh_SellTargetMustBeRelativeToLastBuyPrice()
	{
		// day0 buy 100 @10; day1 sell @11 -> 1100; day2 buy 220 @5; day3 6 >= 5*1.1 -> sell 1320; day4 buy 330 @4
		var (result, _) = new BuyLowSellHighStrategy(1000m, 10, 10m).Execute(Daily(10m, 11m, 5m, 6m, 4m));

		Assert.That(result, Is.EqualTo(1320m).Within(0.0000001m));
	}

	/// <summary>int / int: returns 0 unless every run passed.</summary>
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
