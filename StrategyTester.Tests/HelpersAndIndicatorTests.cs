using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class HelpersTests
{
	[Test]
	public void GetYears_IsCalendarDaysBetweenFirstAndLastOver365()
	{
		var data = new List<StockPrice>
		{
			new(new DateTime(2020, 1, 1),   1m),
			new(new DateTime(2020, 7, 1),   1m),
			new(new DateTime(2021, 12, 31), 1m), // 730 days (2020 is a leap year)
		};

		Assert.That(Helpers.GetYears(data), Is.EqualTo(2.0).Within(1e-12));
	}

	[TestCase(1000, 1210, 2, 10)]
	[TestCase(1000, 1000, 5, 0)]
	[TestCase(1000, 500,  1, -50)]
	[TestCase(1000, 4000, 2, 100)]
	public void GetAvgPercentPerYear_IsCagr(double initial, double final, double years, double expectedPercent)
	{
		var cagr = Helpers.GetAvgPercentPerYear((decimal)initial, (decimal)final, years);

		Assert.That(cagr, Is.EqualTo(expectedPercent).Within(1e-9));
	}
}

[TestFixture]
public class IndicatorFunctionsTests
{
	private static readonly decimal[] OneToTen = Enumerable.Range(1, 10).Select(i => (decimal)i).ToArray();

	[SetUp]
	public void SetUp() => ClearStaticCaches();

	[Test]
	public void MovingAverage_AveragesTrailingWindowIncludingCurrentDay()
	{
		var data = Daily(OneToTen);

		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, 5), Is.EqualTo(5m)); // 4,5,6
		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, 3), Is.EqualTo(3m)); // 2,3,4
	}

	[Test]
	public void MovingAverage_ReturnsNullWithoutEnoughHistory()
	{
		var data = Daily(OneToTen);

		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, 1),  Is.Null);
		Assert.That(IndicatorFunctions.MovingAverage(UniqueTicker(), data, 3, -1), Is.Null);
	}

	[Test]
	public void MovingAverage_DifferentTickersDoNotShareCache()
	{
		var a = Daily(1m, 1m, 1m, 1m);
		var b = Daily(5m, 5m, 5m, 5m);

		Assert.That(IndicatorFunctions.MovingAverage("A_" + UniqueTicker(), a, 3, 3), Is.EqualTo(1m));
		Assert.That(IndicatorFunctions.MovingAverage("B_" + UniqueTicker(), b, 3, 3), Is.EqualTo(5m));
	}

	[Test]
	public void ExponentialMovingAverage_ReturnsNullBeforeSeed()
	{
		Assert.That(IndicatorFunctions.ExponentialMovingAverage(UniqueTicker(), Daily(2m, 4m, 6m, 8m), 3, 1), Is.Null);
		Assert.That(IndicatorFunctions.ExponentialMovingAverage(UniqueTicker(), Daily(2m, 4m, 6m, 8m), 3, -1), Is.Null);
	}

	[Test]
	public void ExponentialMovingAverage_IsSeededWithSmaThenRecursive()
	{
		// periods = 3 -> alpha = 0.5, seed at index 2 = (2+4+6)/3 = 4
		var data = Daily(2m, 4m, 6m, 8m, 4m);
		var t    = UniqueTicker();

		Assert.That(IndicatorFunctions.ExponentialMovingAverage(t, data, 3, 2), Is.EqualTo(4m));
		Assert.That(IndicatorFunctions.ExponentialMovingAverage(t, data, 3, 3), Is.EqualTo(6m)); // .5*8 + .5*4
		Assert.That(IndicatorFunctions.ExponentialMovingAverage(t, data, 3, 4), Is.EqualTo(5m)); // .5*4 + .5*6
	}

	[Test]
	public void ExponentialMovingAverage_OfConstantSeriesIsConstant()
	{
		var data = Daily(Enumerable.Repeat(42m, 30).ToArray());

		Assert.That(IndicatorFunctions.ExponentialMovingAverage(UniqueTicker(), data, 5, 29), Is.EqualTo(42m).Within(0.0000000001m));
	}
}
