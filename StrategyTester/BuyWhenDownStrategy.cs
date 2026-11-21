using System;
using System.Collections.Generic;
using System.Text;

namespace StrategyTester
{
	/*
	internal class BuyWhenDownStrategy : Strategy
	{
		private readonly bool     _allowSellingAtLoss;
		private          decimal  _price;

		public BuyWhenDownStrategy(string ticker, int pctDownFromPeakToBuy, int daysOfNonGoingDownToBuy, bool allowSellingAtLoss = true) 
			: base(
				ticker, 
				"We buy in parts when price is X% down from peak and have been stable (non going down) for Y days.", // Name
				$"If we're {pctDownFromPeakToBuy}% down from the peak and have been ", // Desc
				$"EMA Days = {emaDays}, MA Days = {maDays}") // Params description
		{
			EmaDays = emaDays;
			MaDays  = maDays;
			_allowSellingAtLoss = allowSellingAtLoss;
		}
		
		public override (decimal x, decimal y) GetHeatmapKey()
		{
			return (MaDays, EmaDays);
		}

		protected override bool CalcDailyParametersAndDecideIfCanBuyOrSell(int currentIndex)
		{
			var dp = DataPoints[currentIndex];
			_price = dp.ClosingPrice;

			_currentMA  = IndicatorFunctions.MovingAverage(Ticker, DataPoints, MaDays, currentIndex);
			_maPrev     = IndicatorFunctions.MovingAverage(Ticker, DataPoints, MaDays, currentIndex - 1);
			_currentEMA = IndicatorFunctions.ExponentialMovingAverage(Ticker, DataPoints, EmaDays, currentIndex);
			_emaPrev    = IndicatorFunctions.ExponentialMovingAverage(Ticker, DataPoints, EmaDays, currentIndex - 1);

			if (_currentMA == null || _currentEMA == null)
				return false;

			return true;
		}

		protected override bool ShouldEnter()
		{
			return _emaPrev < _maPrev && _currentEMA > _currentMA;
		}
		
		protected override bool ShouldExit()
		{
			if (!_allowSellingAtLoss && LastBuy != null && _price < LastBuy.ClosingPrice)
				return false;
				
			return _emaPrev > _maPrev && _currentEMA < _currentMA;
		}

		public override IEnumerable<Strategy> GenerateStrategies(string ticker)
		{
			int minMA   = 20;
			int maCount = 35;

			for (int maDays = minMA; maDays < minMA + maCount; maDays++)
			{
				for (int emaDays = 7; emaDays <= (maDays + 1) / 2; emaDays += 1)
				{
					yield return new MACrossOverStrategy(ticker, emaDays, maDays);
				}
			}
		}
	}
	*/
}
