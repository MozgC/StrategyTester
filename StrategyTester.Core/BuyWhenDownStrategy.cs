namespace StrategyTester
{
	/// <summary>
	/// Starts in cash and never sells. Buys in parts as the price falls from its peak (highest close since the start of the run's data):
	/// each time the drawdown from the peak reaches another multiple of PctDownFromPeakToBuy, invest
	/// PctDownFromPeakToBuy / PctDownToInvestAllCash of the cash we had at the peak (several levels at once if the price gapped),
	/// and invest all remaining cash once the drawdown reaches PctDownToInvestAllCash.
	/// A buy also requires the price to have stabilized: no lower close than the previous day for DaysOfNonGoingDownToBuy days in a row.
	/// A level counts as reached once the lowest close since the peak has touched it, even if the price has bounced back since.
	/// Each level is bought once per peak; a new peak resets the levels.
	/// </summary>
	public class BuyWhenDownStrategy : Strategy
	{
		public readonly int PctDownFromPeakToBuy;
		public readonly int DaysOfNonGoingDownToBuy;
		public readonly int PctDownToInvestAllCash;

		private decimal _pricePeak;
		private decimal _lowestSincePeak;
		private decimal _cashAtPeak;
		private int     _nonDownDays;
		private int     _levelsBought;    // levels already bought since the current peak
		private int     _levelsReached;   // levels reached since the current peak (int.MaxValue = invest-all level)
		private decimal _amountToBuy;

		public BuyWhenDownStrategy(string ticker, int pctDownFromPeakToBuy, int daysOfNonGoingDownToBuy, int pctDownToInvestAllCash = 35)
			: base(
				ticker,
				"Buy in parts when the price is down from its peak and has stabilized",
				ValidateAndDescribe(pctDownFromPeakToBuy, daysOfNonGoingDownToBuy, pctDownToInvestAllCash),
				$"Down % to buy = {pctDownFromPeakToBuy}, Non-down days = {daysOfNonGoingDownToBuy}, Down % to invest all = {pctDownToInvestAllCash}")
		{
			PctDownFromPeakToBuy    = pctDownFromPeakToBuy;
			DaysOfNonGoingDownToBuy = daysOfNonGoingDownToBuy;
			PctDownToInvestAllCash  = pctDownToInvestAllCash;
		}

		// Runs inside the base constructor call, i.e. before the constructor body, so arguments are validated before they're used
		private static string ValidateAndDescribe(int pctDownFromPeakToBuy, int daysOfNonGoingDownToBuy, int pctDownToInvestAllCash)
		{
			if (pctDownFromPeakToBuy    <= 0) throw new ArgumentOutOfRangeException(nameof(pctDownFromPeakToBuy));
			if (daysOfNonGoingDownToBuy <  0) throw new ArgumentOutOfRangeException(nameof(daysOfNonGoingDownToBuy));
			if (pctDownToInvestAllCash  <= 0) throw new ArgumentOutOfRangeException(nameof(pctDownToInvestAllCash));

			return $"For every {pctDownFromPeakToBuy}% down from the peak, after the price hasn't gone down for {daysOfNonGoingDownToBuy} days in a row, " +
			       $"invest {(decimal)pctDownFromPeakToBuy / pctDownToInvestAllCash:P} of the cash we had at the peak; invest all cash at {pctDownToInvestAllCash}% down. Never sells.";
		}

		public override bool BuysOnFirstDay => false;

		public override (decimal x, decimal y) GetHeatmapKey()
		{
			return (PctDownFromPeakToBuy, DaysOfNonGoingDownToBuy);
		}

		public override (string x, string y) HeatmapAxisLabels => ("Down % from peak to buy", "Non-down days before buying");

		protected override void OnRunStarting()
		{
			_pricePeak       = 0;
			_lowestSincePeak = 0;
			_cashAtPeak      = 0;
			_nonDownDays     = 0;
			_levelsBought    = 0;
			_levelsReached   = 0;
			_amountToBuy     = 0;
		}

		protected override bool CalcDailyParametersAndDecideIfCanBuyOrSell(int currentIndex)
		{
			decimal price = DataPoints[currentIndex].ClosingPrice;

			_nonDownDays = currentIndex > 0 && price >= DataPoints[currentIndex - 1].ClosingPrice
				? _nonDownDays + 1
				: 0;

			if (currentIndex == 0 || price > _pricePeak)
			{
				// New peak: levels reset, sizing is based on the cash we have now
				_pricePeak       = price;
				_lowestSincePeak = price;
				_cashAtPeak      = Cash;
				_levelsBought    = 0;
				_levelsReached   = 0;
				_amountToBuy     = 0;
				return true;
			}

			if (price < _lowestSincePeak)
				_lowestSincePeak = price;

			decimal maxDownPercent = (_pricePeak - _lowestSincePeak) / _pricePeak * 100;

			_levelsReached = maxDownPercent >= PctDownToInvestAllCash
				? int.MaxValue
				: (int)(maxDownPercent / PctDownFromPeakToBuy);

			_amountToBuy = 0;

			if (_levelsReached > _levelsBought && _nonDownDays >= DaysOfNonGoingDownToBuy)
			{
				_amountToBuy = _levelsReached == int.MaxValue
					? Cash
					: (_levelsReached - _levelsBought) * PctDownFromPeakToBuy * _cashAtPeak / PctDownToInvestAllCash;
			}

			return true;
		}

		protected override bool ShouldEnter() => _amountToBuy > 0;

		protected override bool ShouldExit() => false;

		protected override decimal GetAmountToBuy() => _amountToBuy;

		protected override void OnBought(StockPrice dp, decimal amount)
		{
			_levelsBought = _levelsReached;
			_amountToBuy  = 0;
		}

		public override IEnumerable<Strategy> GenerateStrategies(string ticker) => ParameterGrid(ticker);

		// Down % to buy 3..17, non-down days 0..10, invest all cash at 35% down
		public static IEnumerable<Strategy> ParameterGrid(string ticker)
		{
			const int pctDownToInvestAllCash = 35;

			for (int pctDown = 3; pctDown <= 17; pctDown++)
			{
				for (int days = 0; days <= 10; days++)
				{
					yield return new BuyWhenDownStrategy(ticker, pctDown, days, pctDownToInvestAllCash);
				}
			}
		}

		// Few trades per window and never sells, so no trade-count filters; optimize for return relative to drawdown,
		// since FinalProfitRatio would favour parameters that rarely buy in a rising market.
		// Equal return/drawdown (e.g. several runs with no drawdown, which all get the 9999 sentinel) -> higher profit wins.
		public static WalkForwardSetup WalkForwardSetup => new(
			StrategyName:       "Buy when down",
			GenerateStrategies: ParameterGrid,
			InSampleFilter:     _ => true,
			OutOfSampleFilter:  r => r.FinalProfitRatio > 1,
			IsLastBetter:       x => x.last.ReturnToDrawdownRatio != x.best.ReturnToDrawdownRatio
				? x.last.ReturnToDrawdownRatio > x.best.ReturnToDrawdownRatio
				: x.last.FinalProfitRatio      > x.best.FinalProfitRatio);
	}
}
