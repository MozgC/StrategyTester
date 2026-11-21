using System.Collections.Concurrent;
using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class WalkForwardMoreTests
{
	private static readonly Func<(RunReport best, RunReport last), bool> HigherProfit =
		x => x.last.FinalProfitRatio > x.best.FinalProfitRatio;

	// Daily prices for 48 months from 2020-01: rising in months 0-11, falling 12-23, rising 24-35, flat 36-47
	private static List<StockPrice> UpDownUpFlat()
	{
		var data  = new List<StockPrice>();
		decimal p = 100m;

		for (var d = Start; d < Start.AddMonths(48); d = d.AddDays(1))
		{
			int month = (d.Year - Start.Year) * 12 + d.Month - 1;
			p *= month < 12 ? 1.002m : month < 24 ? 0.998m : month < 36 ? 1.002m : 1m;
			data.Add(new StockPrice(d, p));
		}

		return data;
	}

	private static IEnumerable<Strategy> HoldAndCash() => new Strategy[]
	{
		new ScriptedStrategy { HeatmapX = 1 },                             // buys on day one, holds
		new ScriptedStrategy { HeatmapX = 2, AllowBuyOnFirstDay = false }, // stays in cash
	};

	[Test]
	public void Run_PicksBestStrategyPerWindowAndCarriesPositionBetweenWindows()
	{
		var tester = new WalkForwardTester(HoldAndCash, HigherProfit);

		var result = tester.Run(UpDownUpFlat(), 1000m, 12, 12, _ => true);

		var picked = result.OOSRunReports.Select(r => ((ScriptedStrategy)r.Strategy).HeatmapX).ToList();
		Assert.That(picked, Is.EqualTo(new[] { 1, 2, 1 }), "rising IS -> hold, falling IS -> cash");

		for (int i = 1; i < result.OOSRunReports.Count; i++)
		{
			var prev = result.OOSRunReports[i - 1];
			var cur  = result.OOSRunReports[i];

			Assert.That(cur.InitialInvestment, Is.EqualTo(prev.FinalInvestment), $"window {i}");
			Assert.That(cur.InitialCash,       Is.EqualTo(prev.FinalCash),       $"window {i}");
			Assert.That(cur.InitialShares,     Is.EqualTo(prev.FinalShares),     $"window {i}");
			Assert.That(cur.InitialCostBasis,  Is.EqualTo(prev.FinalCostBasis),  $"window {i}");
		}

		Assert.That(result.OOSEquityCurve.Last().Item2, Is.EqualTo(result.FinalInvestment));
		Assert.That(result.InSampleHeatMap.Keys,         Is.EquivalentTo(new[] { (1m, 0m), (2m, 0m) }));
		Assert.That(result.InSampleHeatMap.Values.All(v => v.Count == 3), Is.True, "one value per IS window");
	}

	[Test]
	public void FindBestStrategyOnInSample_TieKeepsFirstStrategy()
	{
		var first  = new ScriptedStrategy { HeatmapX = 1 };
		var second = new ScriptedStrategy { HeatmapX = 2 };
		var tester = new WalkForwardTester(() => new Strategy[] { first, second }, HigherProfit);

		var (best, _) = tester.FindBestStrategyOnInSample(Daily(10m, 11m, 12m), _ => true,
			new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>());

		Assert.That(best, Is.SameAs(first));
	}

	[Test]
	public void FindBestStrategyOnInSample_FilteredRunsAreNotSelectedNorInHeatmap()
	{
		var tester  = new WalkForwardTester(HoldAndCash, HigherProfit);
		var heatmap = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		var (best, _) = tester.FindBestStrategyOnInSample(Daily(10m, 11m, 12m), r => r.HeatmapKey.x != 1, heatmap);

		Assert.That(((ScriptedStrategy)best!).HeatmapX, Is.EqualTo(2), "hold is better but filtered out");
		Assert.That(heatmap.Keys, Is.EquivalentTo(new[] { (2m, 0m) }));
	}

	[Test]
	public void Run_WithLessDataThanOneWindowThrowsClearError()
	{
		var data   = Enumerable.Range(0, 600).Select(i => new StockPrice(Start.AddDays(i), 100m)).ToList(); // 2020-01 .. 2021-08 = 20 months
		var tester = new WalkForwardTester(HoldAndCash, HigherProfit);

		Assert.That(() => tester.Run(data, 1000m, 12, 12, _ => true),
			Throws.ArgumentException.With.Message.Contains("need at least 24 months").And.Message.Contains("got 20 (2020-01 - 2021-08)"));
	}

	[Test]
	public void Run_WithNoDataThrowsClearError()
	{
		var tester = new WalkForwardTester(HoldAndCash, HigherProfit);

		Assert.That(() => tester.Run(new List<StockPrice>(), 1000m, 12, 12, _ => true),
			Throws.ArgumentException.With.Message.Contains("no data"));
	}
}
