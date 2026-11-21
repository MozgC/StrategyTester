using static StrategyTester.Tests.TestUtil;

namespace StrategyTester.Tests;

[TestFixture]
public class BuyWhenDownStrategyTests
{
	// step 10%, all-in at 35% -> each level invests 10/35 of the cash at the peak
	private const decimal Level = 1000m * 10m / 35m;

	[Test]
	public void Run_BuysEachLevelThenAllCashAtInvestAllLevel()
	{
		var data     = Daily(100m, 95m, 90m, 85m, 80m, 75m, 70m, 65m); // -10% @90, -20% @80, -30% @70, -35% @65
		var strategy = new BuyWhenDownStrategy("X", 10, 0, 35);

		var r = strategy.Run(data, buyOnFirstDay: false);

		var expectedShares = Level / 90m + Level / 80m + Level / 70m + (1000m - 3 * Level) / 65m;

		Assert.Multiple(() =>
		{
			Assert.That(r.TotalTradeCount, Is.EqualTo(4));
			Assert.That(r.FinalCash,       Is.EqualTo(0m).Within(0.0000001m));
			Assert.That(r.FinalShares,     Is.EqualTo(expectedShares).Within(0.0000001m));
			Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(65m));
		});

		// state is reset between runs
		var again = strategy.Run(data, buyOnFirstDay: false);
		Assert.That(again.FinalShares, Is.EqualTo(r.FinalShares));
	}

	[Test]
	public void Run_GapThroughSeveralLevelsBuysThemAllAtOnce()
	{
		var r = new BuyWhenDownStrategy("X", 10, 0, 35).Run(Daily(100m, 92m, 78m), false); // -8% -> -22%

		Assert.That(r.TotalTradeCount,      Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(78m));
		Assert.That(r.FinalCash,            Is.EqualTo(1000m - 2 * Level).Within(0.0000001m));
	}

	[Test]
	public void Run_WaitsForStabilizationAndBuysEvenIfPriceBouncedAboveLevel()
	{
		// -10% @90, still falling @89, then up 2 days in a row -> buy @92 although only -8% now
		var r = new BuyWhenDownStrategy("X", 10, 2, 35).Run(Daily(100m, 90m, 89m, 91m, 92m), false);

		Assert.That(r.TotalTradeCount,      Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(92m));
		Assert.That(r.FinalCash,            Is.EqualTo(1000m - Level).Within(0.0000001m));
	}

	[Test]
	public void Run_InvestAllLevelAlsoWaitsForStabilization()
	{
		var r = new BuyWhenDownStrategy("X", 10, 1, 35).Run(Daily(100m, 60m, 59m, 60m), false);

		Assert.That(r.TotalTradeCount,      Is.EqualTo(1));
		Assert.That(r.LastBuy.ClosingPrice, Is.EqualTo(60m));
		Assert.That(r.FinalCash,            Is.EqualTo(0m));
	}

	[Test]
	public void Run_LevelIsBoughtOncePerPeak()
	{
		// -10% @90 buy, recovers to 95 (no new peak), back to 90 -> no buy, -20% @80 -> buy
		var r = new BuyWhenDownStrategy("X", 10, 0, 35).Run(Daily(100m, 90m, 95m, 90m, 80m), false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(2));
		Assert.That(r.FinalCash,       Is.EqualTo(1000m - 2 * Level).Within(0.0000001m));
	}

	[Test]
	public void Run_NewPeakResetsLevelsAndUsesCashAtNewPeak()
	{
		// buy level 1 @90, new peak 110 with cash 1000-Level, -10% @99 -> invest 10/35 of that cash
		var r = new BuyWhenDownStrategy("X", 10, 0, 35).Run(Daily(100m, 90m, 110m, 99m), false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(2));
		Assert.That(r.FinalCash,       Is.EqualTo((1000m - Level) * 25m / 35m).Within(0.0000001m));
	}

	[Test]
	public void Run_SizesLevelsFromCashNotFromTotalInvestment()
	{
		// continuing with 500 cash + 5 shares: level = 10/35 of 500
		var r = new BuyWhenDownStrategy("X", 10, 0, 35).Run(Daily(100m, 90m), false, initialInvestment: 1000m, initialCash: 500m, initialShares: 5m);

		Assert.That(r.FinalCash, Is.EqualTo(500m - 500m * 10m / 35m).Within(0.0000001m));
	}

	[Test]
	public void Run_IgnoresBuyOnFirstDayAndNeverSells()
	{
		var r = new BuyWhenDownStrategy("X", 10, 0, 35).Run(Daily(100m, 101m, 102m), buyOnFirstDay: true);

		Assert.That(r.FinalShares,     Is.EqualTo(0m));
		Assert.That(r.FinalInvestment, Is.EqualTo(1000m));
		Assert.That(r.TotalTradeCount, Is.EqualTo(0));
	}

	[Test]
	public void Run_EntryOverrideOnNonLevelDayBuysNothing()
	{
		var strategy = new BuyWhenDownStrategy("X", 10, 0, 35);
		strategy.OverrideEntryLogic(() => true);

		var r = strategy.Run(Daily(100m, 99m, 98m), false);

		Assert.That(r.TotalTradeCount, Is.EqualTo(0));
	}

	[Test]
	public void WalkForward_DoesNotInvestOnFirstDayForStrategiesThatDecideEntries()
	{
		var data   = Enumerable.Range(0, 1096).Select(i => new StockPrice(Start.AddDays(i), 100m + i)).ToList(); // never down
		var tester = new WalkForwardTester(
			() => new BuyWhenDownStrategy("X", 10, 0).GenerateStrategies("X"),
			x => x.last.FinalProfitRatio > x.best.FinalProfitRatio);

		var result = tester.Run(data, 1000m, 12, 12, _ => true);

		Assert.That(result.FinalInvestment,                       Is.EqualTo(1000m));
		Assert.That(result.OOSRunReports.All(r => r.FinalShares == 0), Is.True);
	}

	[Test]
	public void WalkForwardSetup_RunsEndToEnd()
	{
		// 3 years of a choppy series: 120-day cycles between 100 and ~70, so there are drawdowns to buy into
		var data  = Enumerable.Range(0, 1096)
			.Select(i => new StockPrice(Start.AddDays(i), 85m + 15m * (decimal)Math.Cos(i * 2 * Math.PI / 120)))
			.ToList();
		var setup = BuyWhenDownStrategy.WalkForwardSetup;

		var tester = new WalkForwardTester(() => setup.GenerateStrategies("X"), setup.IsLastBetter);
		var result = tester.Run(data, 1000m, 12, 12, setup.InSampleFilter, setup.OutOfSampleFilter);

		Assert.That(result.OOSRunReports, Has.Count.EqualTo(2));
		Assert.That(result.OOSRunReports.All(r => r.Strategy is BuyWhenDownStrategy), Is.True);
		Assert.That(result.OOSRunReports.Sum(r => r.TotalTradeCount), Is.GreaterThan(0));
	}

	[Test]
	public void GenerateStrategies_ProducesExpectedParameterGrid()
	{
		var strategies = new BuyWhenDownStrategy("X", 10, 0).GenerateStrategies("X").Cast<BuyWhenDownStrategy>().ToList();

		Assert.That(strategies, Has.Count.EqualTo(15 * 11));
		Assert.That(strategies.Select(s => s.PctDownFromPeakToBuy).Distinct(),    Is.EquivalentTo(Enumerable.Range(3, 15)));
		Assert.That(strategies.Select(s => s.DaysOfNonGoingDownToBuy).Distinct(), Is.EquivalentTo(Enumerable.Range(0, 11)));
		Assert.That(strategies.All(s => s.PctDownToInvestAllCash == 35),          Is.True);
	}

	[Test]
	public void Constructor_RejectsNonPositivePercentages()
	{
		Assert.That(() => new BuyWhenDownStrategy("X", 0, 0),       Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => new BuyWhenDownStrategy("X", 10, -1),     Throws.InstanceOf<ArgumentOutOfRangeException>());
		Assert.That(() => new BuyWhenDownStrategy("X", 10, 0, 0),   Throws.InstanceOf<ArgumentOutOfRangeException>());
	}
}
