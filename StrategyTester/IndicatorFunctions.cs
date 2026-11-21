using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace StrategyTester
{
	public static class IndicatorFunctions
	{
		// Caches are scoped to the data series instance (by reference), not to ticker/date: Monte Carlo shuffles keep the
		// ticker and dates but change prices, and walk-forward windows change where the EMA seed starts.
		// ConditionalWeakTable lets a series' cache be collected together with the series.
		// NOTE: a series must not be mutated after indicators have been calculated on it.
		private static readonly ConditionalWeakTable<IList<StockPrice>, ConcurrentDictionary<(int index, int periods), decimal>> _maCache  = new();
		private static readonly ConditionalWeakTable<IList<StockPrice>, ConcurrentDictionary<int, decimal[]>>                    _emaCache = new();

		/// <param name="ticker">Not used; kept for call-site compatibility.</param>
		public static decimal? MovingAverage(string ticker, IList<StockPrice> dataPoints, int periods, int currentDayIndex)
		{
			if (currentDayIndex < periods - 1) return null;  // Need 'periods' points: indexes currentDayIndex-periods+1 .. currentDayIndex

			var cache = _maCache.GetValue(dataPoints, _ => new ConcurrentDictionary<(int index, int periods), decimal>());

			return cache.GetOrAdd((currentDayIndex, periods), key =>
			{
				decimal sum = 0;

				for (int i = key.index; i > key.index - key.periods; i--)
				{
					sum += dataPoints[i].ClosingPrice;
				}

				return sum / key.periods;
			});
		}

		/// <param name="ticker">Not used; kept for call-site compatibility.</param>
		public static decimal? ExponentialMovingAverage(string ticker, IList<StockPrice> dataPoints, int periods, int currentDayIndex)
		{
			if (currentDayIndex < periods - 1) return null;  // Need at least 'periods' points for SMA seed

			var cache = _emaCache.GetValue(dataPoints, _ => new ConcurrentDictionary<int, decimal[]>());
			var emas  = cache.GetOrAdd(periods, p => CalculateEmaSeries(dataPoints, p));

			return emas[currentDayIndex];
		}

		// EMA for the whole series in one pass, seeded with the SMA of the first 'periods' prices.
		// Entries before index periods-1 are unused (callers get null for them).
		private static decimal[] CalculateEmaSeries(IList<StockPrice> dataPoints, int periods)
		{
			var emas = new decimal[dataPoints.Count];

			if (dataPoints.Count < periods)
				return emas;

			var alpha = 2m / (periods + 1m);

			decimal sma = 0m;
			for (int i = 0; i < periods; i++)
			{
				sma += dataPoints[i].ClosingPrice;
			}
			emas[periods - 1] = sma / periods;

			for (int i = periods; i < dataPoints.Count; i++)
			{
				emas[i] = alpha * dataPoints[i].ClosingPrice + (1 - alpha) * emas[i - 1];
			}

			return emas;
		}
	}
}
