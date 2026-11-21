namespace StrategyTester.Tests;

internal static class TestUtil
{
	public static readonly DateTime Start = new(2020, 1, 1);

	/// <summary>One price per calendar day starting at <see cref="Start"/>.</summary>
	public static List<StockPrice> Daily(params decimal[] prices) => Daily(Start, prices);

	public static List<StockPrice> Daily(DateTime start, params decimal[] prices) =>
		prices.Select((p, i) => new StockPrice(start.AddDays(i), p)).ToList();

	public static string UniqueTicker() => "T_" + Guid.NewGuid().ToString("N");
}

/// <summary>
/// Strategy whose entry/exit signals are scripted by day index, so Strategy.Run bookkeeping can be tested in isolation.
/// </summary>
internal sealed class ScriptedStrategy : Strategy
{
	private readonly HashSet<int> _enterDays;
	private readonly HashSet<int> _exitDays;
	private          int          _currentIndex;

	public bool EnterAlways { get; init; }
	public bool ExitAlways  { get; init; }
	public int  HeatmapX    { get; init; }

	/// <summary>Amount to invest on an entry signal; null = all cash (the base default).</summary>
	public decimal? BuyAmount { get; init; }

	public ScriptedStrategy(IEnumerable<int>? enterDays = null, IEnumerable<int>? exitDays = null)
		: base("TEST", "Scripted", "Scripted", "Scripted")
	{
		_enterDays        = new HashSet<int>(enterDays ?? Enumerable.Empty<int>());
		_exitDays         = new HashSet<int>(exitDays  ?? Enumerable.Empty<int>());
		HoldingPeriodDays = 0;
	}

	public override (decimal x, decimal y) GetHeatmapKey() => (HeatmapX, 0);

	protected override decimal GetAmountToBuy() => BuyAmount ?? base.GetAmountToBuy();

	protected override bool CalcDailyParametersAndDecideIfCanBuyOrSell(int currentIndex)
	{
		_currentIndex = currentIndex;
		return true;
	}

	protected override bool ShouldEnter() => EnterAlways || _enterDays.Contains(_currentIndex);
	protected override bool ShouldExit()  => ExitAlways  || _exitDays .Contains(_currentIndex);
}
