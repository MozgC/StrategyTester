using System.Collections.Concurrent;
using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

/// <summary>
/// Synthetic prices built so that one specific (down % from peak, non-down days) pair is the best BuyWhenDown setting;
/// the heatmap must then peak exactly at that cell.
/// </summary>
[TestFixture]
public class BuyWhenDownHeatmapTests
{
	private static readonly Func<(RunReport best, RunReport last), bool> HigherProfit =
		x => x.last.FinalProfitRatio > x.best.FinalProfitRatio;

	/// <summary>
	/// One crash cycle of <paramref name="n"/> prices starting at a new <paramref name="peak"/>, in which (step, days) is the best setting:
	///  1. a shallow dip to -0.6*step%, flat for 12 days: smaller steps spend part of their cash here, at a worse price;
	///  2. a touch of -step%: days = 0 buys here;
	///  3. (days >= 1) one more small drop to -(step+0.3)%, flat so that days 1..days-1 buy here, still too high;
	///  4. (days >= 1) a drop to the low, -(step+0.6)%, then exactly `days` flat days: (step, days) buys at the low;
	///  5. a slow rise: variants needing more days buy later and higher.
	/// The deepest drawdown is below (step+1)%, so larger steps never buy, and below 35% so nobody goes all-in.
	/// All buys are below the next peak, so investing more at a lower price is always better.
	/// </summary>
	private static List<decimal> Cycle(decimal peak, int step, int days, int n)
	{
		decimal Down(decimal pct) => peak * (1 - pct / 100);

		var prices = new List<decimal> { peak };
		prices.AddRange(Enumerable.Repeat(Down(step * 0.6m), 12));
		prices.Add(Down(step));

		decimal low = Down(step);

		if (days >= 1)
		{
			prices.AddRange(Enumerable.Repeat(Down(step + 0.3m), days));
			low = Down(step + 0.6m);
			prices.AddRange(Enumerable.Repeat(low, days + 1));
		}

		for (int k = 1; prices.Count < n; k++)
			prices.Add(low * (1 + k / 1000m));

		return prices;
	}

	private static List<StockPrice> SingleCycle(int step, int days) =>
		Daily(Cycle(100m, step, days, 60).Append(130m).ToArray());

	// 2-month cycles, each starting on the 1st of Jan/Mar/May/... at a 30% higher peak
	private static List<StockPrice> RepeatedCycles(int step, int days, int months)
	{
		var data  = new List<StockPrice>();
		var peak  = 100m;

		for (var from = Start; from < Start.AddMonths(months); from = from.AddMonths(2), peak *= 1.3m)
		{
			int n = (from.AddMonths(2) - from).Days;
			data.AddRange(Cycle(peak, step, days, n).Select((p, i) => new StockPrice(from.AddDays(i), p)));
		}

		return data;
	}

	private static (decimal x, decimal y) BestCell(IDictionary<(decimal x, decimal y), List<decimal>> heatmap, out int cellsWithBestValue)
	{
		var averages = heatmap.ToDictionary(kv => kv.Key, kv => kv.Value.Average());
		var best     = averages.Values.Max();

		cellsWithBestValue = averages.Count(kv => kv.Value == best);
		return averages.First(kv => kv.Value == best).Key;
	}

	private static IEnumerable<TestCaseData> AllGridPairs() =>
		from step in Enumerable.Range(3, 15)
		from days in Enumerable.Range(0, 11)
		select new TestCaseData(step, days);

	[TestCaseSource(nameof(AllGridPairs))]
	public void InSampleHeatmapPeaksAtTargetPair(int step, int days)
	{
		var tester  = new WalkForwardTester(() => BuyWhenDownStrategy.ParameterGrid("X"), HigherProfit);
		var heatmap = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		var (best, _) = tester.FindBestStrategyOnInSample(SingleCycle(step, days), _ => true, heatmap);

		Assert.That(BestCell(heatmap, out int ties), Is.EqualTo(((decimal)step, (decimal)days)));
		Assert.That(ties, Is.EqualTo(1), "best cell must be unique");

		var b = (BuyWhenDownStrategy)best!;
		Assert.That((b.PctDownFromPeakToBuy, b.DaysOfNonGoingDownToBuy), Is.EqualTo((step, days)));
	}

	[TestCaseSource(nameof(AllGridPairs))]
	public void StrategySetupPicksTargetPair(int step, int days)
	{
		// The real BuyWhenDown selection: return/drawdown, ties broken by profit ratio
		var setup   = BuyWhenDownStrategy.WalkForwardSetup;
		var tester  = new WalkForwardTester(() => setup.GenerateStrategies("X"), setup.IsLastBetter);
		var heatmap = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		var (best, _) = tester.FindBestStrategyOnInSample(SingleCycle(step, days), setup.InSampleFilter, heatmap);

		var b = (BuyWhenDownStrategy)best!;
		Assert.That((b.PctDownFromPeakToBuy, b.DaysOfNonGoingDownToBuy), Is.EqualTo((step, days)));
	}

	[TestCase(10, 2)]
	[TestCase(5, 0)]
	[TestCase(15, 7)]
	[TestCase(3, 10)]
	[TestCase(17, 4)]
	public void WalkForwardHeatmapAndOosPicksMatchTargetPair(int step, int days)
	{
		var tester = new WalkForwardTester(() => BuyWhenDownStrategy.ParameterGrid("X"), HigherProfit);

		var result = tester.Run(RepeatedCycles(step, days, 48), 1000m, 12, 12, _ => true);

		Assert.That(BestCell(result.InSampleHeatMap, out int ties), Is.EqualTo(((decimal)step, (decimal)days)));
		Assert.That(ties, Is.EqualTo(1));
		Assert.That(result.InSampleHeatMap.Values.All(v => v.Count == 3), Is.True, "3 IS windows");
		Assert.That(result.OOSRunReports.Select(r => r.HeatmapKey), Is.All.EqualTo(((decimal)step, (decimal)days)));
	}

	[Test]
	public void HeatmapCellValuesForTarget10Pct2Days()
	{
		// Prices: 100, 94 x12, 90, 89.7 x2, 89.4 x3, rising from 89.4 ..., final 130
		var tester  = new WalkForwardTester(() => BuyWhenDownStrategy.ParameterGrid("X"), HigherProfit);
		var heatmap = new ConcurrentDictionary<(decimal x, decimal y), List<decimal>>();

		tester.FindBestStrategyOnInSample(SingleCycle(10, 2), _ => true, heatmap);

		decimal Cell(int step, int days) => heatmap[(step, days)].Single();
		decimal Ratio(params (decimal amount, decimal price)[] buys) =>
			(1000m - buys.Sum(b => b.amount) + buys.Sum(b => b.amount / b.price) * 130m) / 1000m;

		var level10 = 1000m * 10 / 35;
		var level5  = 1000m * 5 / 35;

		Assert.Multiple(() =>
		{
			Assert.That(Cell(10, 2), Is.EqualTo(Ratio((level10, 89.4m))).Within(0.000000001m),                   "buys at the low");
			Assert.That(Cell(10, 0), Is.EqualTo(Ratio((level10, 90m))).Within(0.000000001m),                     "buys at the -10% touch");
			Assert.That(Cell(10, 1), Is.EqualTo(Ratio((level10, 89.7m))).Within(0.000000001m),                   "buys on the plateau");
			Assert.That(Cell(10, 3), Is.EqualTo(Ratio((level10, 89.4m * 1.001m))).Within(0.000000001m),         "buys on the first rising day");
			Assert.That(Cell(5, 2),  Is.EqualTo(Ratio((level5, 94m), (level5, 89.4m))).Within(0.000000001m),   "half at the shallow dip");

			for (int step = 11; step <= 17; step++)
			for (int days = 0; days <= 10; days++)
				Assert.That(Cell(step, days), Is.EqualTo(1m), $"step {step} never reaches its first level");
		});
	}
}
