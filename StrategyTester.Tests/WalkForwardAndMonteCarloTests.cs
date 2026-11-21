using System.Collections.Concurrent;
using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class WalkForwardTesterTests
{
	private static readonly Func<(RunReport best, RunReport last), bool> HigherFinalInvestment =
		r => r.last.FinalInvestment > r.best.FinalInvestment;

	[SetUp]
	public void SetUp() => ClearStaticCaches();

	[Test]
	public void FindBestStrategyOnInSample_PicksBestAndFillsHeatmap()
	{
		var hold      = new ScriptedStrategy();                                       // 10 -> 13: 1300
		var sellEarly = new ScriptedStrategy(exitDays: new[] { 1 }) { HeatmapX = 1 }; // sells @11: 1100
		var tester    = new WalkForwardTester(() => new Strategy[] { sellEarly, hold }, HigherFinalInvestment);
		var heatmap   = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		var (best, report) = tester.FindBestStrategyOnInSample(Daily(10m, 11m, 12m, 13m), _ => true, heatmap);

		Assert.That(best,                    Is.SameAs(hold));
		Assert.That(report!.FinalInvestment, Is.EqualTo(1300m));
		Assert.That(heatmap.Keys,            Is.EquivalentTo(new[] { (0m, 0m), (1m, 0m) }));
		Assert.That(heatmap[(0m, 0m)],       Is.EqualTo(new[] { 1.3m }));
	}

	[Test]
	public void FindBestStrategyOnInSample_ReturnsNullWhenFilterRejectsEverything()
	{
		var tester  = new WalkForwardTester(() => new Strategy[] { new ScriptedStrategy() }, HigherFinalInvestment);
		var heatmap = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		var (best, report) = tester.FindBestStrategyOnInSample(Daily(10m, 11m), _ => false, heatmap);

		Assert.That(best,    Is.Null);
		Assert.That(report,  Is.Null);
		Assert.That(heatmap, Is.Empty);
	}

	[Test]
	public void Run_ChainsOutOfSampleRunsIntoOneEquityCurve()
	{
		// Daily prices 2020-01-01 .. 2022-12-31, rising by 1/day. IS = 12 months, OOS = 12 months -> 2 OOS windows (2021, 2022).
		var data = Enumerable.Range(0, 1096).Select(i => new StockPrice(Start.AddDays(i), 100m + i)).ToList();
		Assert.That(data.Last().Date, Is.EqualTo(new DateTime(2022, 12, 31)));

		var tester = new WalkForwardTester(() => new Strategy[] { new ScriptedStrategy() }, HigherFinalInvestment);

		var result = tester.Run(data, 1000m, 12, 12, _ => true, r => r.FinalInvestment > r.InitialInvestment);

		var firstOosPrice = data.Single(x => x.Date == new DateTime(2021, 1, 1)).ClosingPrice;
		var expectedFinal = 1000m / firstOosPrice * data.Last().ClosingPrice;

		Assert.Multiple(() =>
		{
			Assert.That(result.OOSRunReports,               Has.Count.EqualTo(2));
			Assert.That(result.OOSEquityCurve,              Has.Count.EqualTo(3));
			Assert.That(result.OOSEquityCurve[0],           Is.EqualTo((new DateTime(2021, 1, 1), 1000m)));
			Assert.That(result.OOSEquityCurve[2].Item1,     Is.EqualTo(new DateTime(2022, 12, 31)));
			Assert.That(result.FinalInvestment,             Is.EqualTo(expectedFinal).Within(0.0000001m));
			Assert.That(result.OOSRunReports[1].InitialInvestment, Is.EqualTo(result.OOSRunReports[0].FinalInvestment));
			Assert.That(result.OOSRunsPassedOOSFilterCount, Is.EqualTo(2));
		});
	}

	[Test]
	public void Run_ThrowsWhenNoStrategyPassesInSampleFilter()
	{
		var data   = Enumerable.Range(0, 800).Select(i => new StockPrice(Start.AddDays(i), 100m + i)).ToList();
		var tester = new WalkForwardTester(() => new Strategy[] { new ScriptedStrategy() }, HigherFinalInvestment);

		Assert.That(() => tester.Run(data, 1000m, 12, 12, _ => false),
			Throws.Exception.With.Message.Contains("No best strategy"));
	}
}

[TestFixture]
public class MonteCarloTesterTests
{
	private double _total;

	[SetUp]
	public void SetUp() => ClearStaticCaches();

	private static List<StockPrice> Wiggly(int count) =>
		Daily(Enumerable.Range(0, count).Select(i => 100m + (i % 7) * 3m - (i % 4) * 2m).ToArray());

	[Test]
	public void GenerateMonteCarloShuffles_KeepsDatesAndEndPrice()
	{
		var data  = Wiggly(30);
		var shuffles = MonteCarloTester.GenerateMonteCarloShuffles(data, 50).ToList();

		Assert.That(shuffles, Has.Count.EqualTo(50));

		foreach (var s in shuffles)
		{
			Assert.That(s.Select(x => x.Date), Is.EqualTo(data.Select(x => x.Date)));
			// Product of all daily returns is unchanged by shuffling, so the path ends where the original did.
			Assert.That(s.Last().ClosingPrice, Is.EqualTo(data.Last().ClosingPrice).Within(0.000000001m));
		}
	}

	[Test]
	public void GenerateMonteCarloShuffles_IsAPermutationOfDailyReturns()
	{
		var data     = Wiggly(20);
		var original = new List<decimal> { 1m };
		for (int i = 1; i < data.Count; i++)
			original.Add(data[i].ClosingPrice / data[i - 1].ClosingPrice);

		var s        = MonteCarloTester.GenerateMonteCarloShuffles(data, 1).Single();
		var shuffled = new List<decimal> { s[0].ClosingPrice / data[0].ClosingPrice };
		for (int i = 1; i < s.Count; i++)
			shuffled.Add(s[i].ClosingPrice / s[i - 1].ClosingPrice);

		original.Sort();
		shuffled.Sort();

		for (int i = 0; i < original.Count; i++)
			Assert.That(shuffled[i], Is.EqualTo(original[i]).Within(0.000000001m), $"return #{i}");
	}

	[Test]
	public void Run_OnStrictlyRisingDataAlwaysMakesMoneyWithNoDrawdown()
	{
		// Every daily return >= 1, so every shuffled path is non-decreasing.
		var data     = Daily(Enumerable.Range(0, 60).Select(i => 100m + i).ToArray());
		var strategy = new ScriptedStrategy { EnterAlways = true };
		var report   = strategy.Run(data, false);

		var wfr = new WalkForwardResult(
			new List<RunReport> { report },
			new List<(DateTime, decimal)>(),
			new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>(),
			1);

		var results = MonteCarloTester.Run(wfr, data, 200);

		Assert.That(results, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(results[0].OriginalOOSReport,        Is.SameAs(report));
			Assert.That(results[0].ProbabilityOfMakingMoney, Is.EqualTo(1m));
			Assert.That(results[0].MedianDrawdown,           Is.EqualTo(0m));
			Assert.That(results[0].MedianProfitRatio,        Is.GreaterThan(1m));
		});
	}

	[Test]
	public void Add_IsAtomicUnderContention()
	{
		_total = 0;

		Parallel.For(0, 100_000, _ => MonteCarloTester.Add(1.0, ref _total));

		Assert.That(_total, Is.EqualTo(100_000.0));
	}
}
