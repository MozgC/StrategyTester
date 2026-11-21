namespace StrategyTester
{
	public abstract class Strategy
	{
		public    string      Name;
		public    string      Description;
		public    string      ParametersDescription;

		public    string      Ticker;
		public    decimal     InitialCash       = 1000;
		public    decimal     InitialInvestment = 1000;

		public    decimal     FinalInvestment;

		// Minimum number of days between the last buy and a sell (a sell is never on the day of a buy, even with 0). Does not restrict buying.
		public    int         HoldingPeriodDays = 30;

		public    int         TotalTradeCount;
		public    decimal     MaxDrawdownPercent;

		public    string      Debug = "";

		// A strategy may hold cash and shares at the same time; the investment value is Cash + Shares * price.
		protected decimal     Cash;
		protected decimal     Shares;
		protected StockPrice? LastBuy;

		// Purchase cost of the shares currently held (sum of shares bought * buy price). Used for per-trade P&L.
		protected decimal     CostBasis;

		// Peak investment value (Cash + Shares * price), used for drawdown
		public    decimal     Peak;
		public    decimal     GrossProfit;
		public    decimal     GrossLoss;

		public IList<StockPrice> DataPoints;

		private Func<bool>? _overrideEntryLogic;
		private Func<bool>? _overrideExitLogic;

		public void OverrideEntryLogic(Func<bool>? logic)
		{
			_overrideEntryLogic = logic;
		}

		public void OverrideExitLogic(Func<bool>? logic)
		{
			_overrideExitLogic = logic;
		}

		public virtual (decimal x, decimal y) GetHeatmapKey()
		{
			return (0, 0);
		}

		public virtual Func<RunReport, decimal> GetHeatmapValue()
		{
			return r => r.FinalProfitRatio;
		}

		// Heatmap labels: what the two GetHeatmapKey() parameters are, and what GetHeatmapValue() measures
		public virtual (string x, string y) HeatmapAxisLabels => ("X", "Y");
		public virtual string               HeatmapValueLabel => "Profit ratio";

		/// <summary>
		/// Whether Run(buyOnFirstDay: true) invests all cash on the first day.
		/// Strategies that decide all entries themselves override this with false, and callers like WalkForwardTester then
		/// leave the money in cash until the strategy says to buy.
		/// </summary>
		public virtual bool BuysOnFirstDay => true;

		protected Strategy(string ticker, string name, string description, string parametersDescription)
		{
			Ticker                   = ticker;
			Name                     = name;
			Description              = description;
			ParametersDescription    = parametersDescription;
		}

		// Optionally in the derived class, propose how to generate multiple strategies with different parameters
		public virtual IEnumerable<Strategy> GenerateStrategies(string ticker)
		{
			return [];
		}

		private void Initialize(IList<StockPrice> dataPoints, bool buyOnFirstDay, decimal initialInvestment, decimal initialCash, decimal initialShares, StockPrice? lastBuy, decimal? initialCostBasis)
		{
			DataPoints        = dataPoints;
			InitialCash       = initialCash;
			InitialInvestment = initialInvestment;
			Shares            = initialShares;
			LastBuy           = lastBuy;
			CostBasis         = initialCostBasis ?? initialShares * (lastBuy ?? dataPoints[0]).ClosingPrice;

			Cash               = InitialCash;
			Peak               = InitialInvestment;
			Debug              = "";
			TotalTradeCount    = 0;
			MaxDrawdownPercent = 0;
			GrossProfit        = 0;
			GrossLoss          = 0;

			OnRunStarting();

			if (Cash > 0 && buyOnFirstDay && BuysOnFirstDay)
			{
				Debug = InvestmentStrategy.GetBuyingSharesAtForString(DataPoints[0], Cash);
				AddShares(DataPoints[0], Cash); // not counted as a trade
			}
		}

		/// <summary>Called at the start of every Run, before the first day. Reset per-run state here.</summary>
		protected virtual void OnRunStarting() { }

		/// <summary>Called after every buy (including the first-day buy).</summary>
		protected virtual void OnBought(StockPrice dp, decimal amount) { }

		/// <summary>How much cash to invest when ShouldEnter (or the entry override) says to buy. Capped at Cash; 0 means don't buy.</summary>
		protected virtual decimal GetAmountToBuy() => Cash;

		protected abstract bool CalcDailyParametersAndDecideIfCanBuyOrSell(int currentIndex);

		public RunReport Run(
			IList<StockPrice> dataPoints,
			bool              buyOnFirstDay,
			decimal           initialInvestment = 1000, // cash + shares value at start
			decimal           initialCash       = 1000,
			decimal           initialShares     = 0,
			StockPrice?       lastBuy           = null,
			decimal?          initialCostBasis  = null) // default: initialShares bought at lastBuy's price (or the first price)
		{
			Initialize(dataPoints, buyOnFirstDay, initialInvestment, initialCash, initialShares, lastBuy, initialCostBasis);

			decimal initialCostBasisUsed = CostBasis;

			for (int i = 0; i < DataPoints.Count; i++)
			{
				var dp = DataPoints[i];

				decimal currentInvestment = Cash + Shares * dp.ClosingPrice;

				if (currentInvestment > Peak)
					Peak = currentInvestment;

				decimal currentDrawdownFromPeak = (Peak - currentInvestment) / Peak * 100;

				if (currentDrawdownFromPeak > MaxDrawdownPercent)
					MaxDrawdownPercent = currentDrawdownFromPeak;

				if (!CalcDailyParametersAndDecideIfCanBuyOrSell(i))
					continue;

				if (Cash > 0 && EffectiveShouldEnter())
				{
					decimal amount = Math.Min(GetAmountToBuy(), Cash);

					if (amount > 0)
					{
						Buy(dp, amount);
						continue;
					}
				}

				if (Shares > 0 && !BoughtToday(dp) && !IsInHoldingPeriod(dp) && EffectiveShouldExit())
				{
					Sell(dp);
				}
			}

			var lastPrice = DataPoints.Last().ClosingPrice;

			FinalInvestment = Cash + Shares * lastPrice;

			var avgPercent = Helpers.GetAvgPercentPerYear(initialInvestment, FinalInvestment, Helpers.GetYears(DataPoints));
			Debug += $"Avg year %: {avgPercent:N}";

			decimal profitFactor = GrossLoss == 0 ? 9999m : GrossProfit / GrossLoss;

			var returnToDrawdownRatio = MaxDrawdownPercent <= 0.0001m  // treat near-zero as zero
				? avgPercent > 0
					? 9999m
					: 0m
				: (decimal) avgPercent / MaxDrawdownPercent;

			decimal years = (decimal) (DataPoints.Last().Date - DataPoints[0].Date).TotalDays / 365;

			var runReport = new RunReport(
				this,
				Name,
				Description,
				DataPoints[0].Date,
				DataPoints.Last().Date,
				initialInvestment,
				initialCash,
				initialShares,
				FinalInvestment,
				Cash,
				Shares,
				FinalInvestment / InitialInvestment,
				LastBuy,
				TotalTradeCount,
				TotalTradeCount / years,
				MaxDrawdownPercent,
				returnToDrawdownRatio,
				profitFactor,
				Debug);

			runReport.InitialCostBasis = initialCostBasisUsed;
			runReport.FinalCostBasis   = CostBasis;

			runReport.SetHeatmapKeyAndValue(GetHeatmapKey(), GetHeatmapValue()(runReport));

			return runReport;
		}

		// Never buy and sell on the same day (e.g. the first-day buy followed by an exit signal on day 0)
		private bool BoughtToday(StockPrice dp) => LastBuy != null && LastBuy.Date.Date == dp.Date.Date;

		private bool IsInHoldingPeriod(StockPrice dp) =>
			HoldingPeriodDays > 0 && LastBuy != null && dp.Date.Date - LastBuy.Date.Date < TimeSpan.FromDays(HoldingPeriodDays);

		protected void Buy(StockPrice dp, decimal amount)
		{
			Debug += $"{dp.Date:d} Buying {amount / dp.ClosingPrice:N} shares at {dp.ClosingPrice:C} for  {amount:C}\n";

			AddShares(dp, amount);

			TotalTradeCount++;
		}

		private void AddShares(StockPrice dp, decimal amount)
		{
			decimal shares = amount / dp.ClosingPrice;

			Shares    += shares;
			CostBasis += shares * dp.ClosingPrice;
			Cash      -= amount;
			LastBuy    = dp;

			OnBought(dp, amount);
		}

		// Sells all shares
		protected void Sell(StockPrice dp)
		{
			decimal proceeds = Shares * dp.ClosingPrice;

			Debug += $"{dp.Date:d} Selling {Shares:N} shares at {dp.ClosingPrice:C} for  {proceeds:C}\n";

			decimal tradeProfitAndLoss = proceeds - CostBasis;

			if (tradeProfitAndLoss > 0)
				GrossProfit += tradeProfitAndLoss;
			else if (tradeProfitAndLoss < 0)
				GrossLoss -= tradeProfitAndLoss; // grossLoss increases by absolute loss

			Cash     += proceeds;
			Shares    = 0;
			CostBasis = 0;

			TotalTradeCount++;
		}

		// Small helper to centralize “effective” logic
		private bool EffectiveShouldEnter() => _overrideEntryLogic?.Invoke() ?? ShouldEnter();

		private bool EffectiveShouldExit() => _overrideExitLogic?.Invoke() ?? ShouldExit();

		protected abstract bool ShouldEnter();
		protected abstract bool ShouldExit();

		public Strategy ShallowClone()
		{
			return FastCloner.FastCloner.ShallowClone(this);
		}
	}
}
