using System.Diagnostics;

namespace StrategyTester
{
	partial class Program
	{
		// ======== Choose what to test ========
		private const  string           Security = "SOXL";
		//private static WalkForwardSetup Setup    => MACrossOverStrategy.WalkForwardSetup;  // or BuyWhenDownStrategy.WalkForwardSetup
		private static WalkForwardSetup Setup    => BuyWhenDownStrategy.WalkForwardSetup;
		// =====================================

		private const int                  _holdingPeriodDays = 30; // used by Program.OldAndDirty.cs
		private static IStockPriceProvider _client;

		[STAThread]
		static void Main(string[] args)
		{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			Console.InputEncoding  = System.Text.Encoding.UTF8;

			string security = Security;
			var    setup    = Setup;

			Console.WriteLine("Security: " + security);
			Console.WriteLine("Strategy: " + setup.StrategyName);
			decimal initialInvestment = 1000;
			Console.WriteLine($"Initial investment: {initialInvestment:C}");

			_client = new YahooFinanceProvider();
			var dataPoints = _client.GetLast10YearsOfPrices(security);

			DateTime? fromDate = new DateTime(2014, 5, 22);
			Console.WriteLine("Starting from: " + fromDate);
			DateTime? toDate = new DateTime(2029, 1, 1);

			dataPoints = dataPoints.SkipWhile(dp => dp.Date < fromDate).Where(dp => dp.Date < toDate).ToList();

			var walkForwardResult = RunWalkForward(setup, security, initialInvestment, dataPoints);
			
			Console.WriteLine(Environment.NewLine + "Buy & Hold:");
			decimal buyAndHold = CalcBuyAndHold(initialInvestment, dataPoints.SkipWhile(x => x.Date <= walkForwardResult.OOSRunReports[0].StartDate).ToList());
			Console.WriteLine($"{buyAndHold:C}");

			Console.WriteLine(Environment.NewLine + "Starting monkey tests..." + Environment.NewLine);
			
			var monkeyTester = new MonkeyTester();
			var monkeyTestResults = monkeyTester.Run(dataPoints.SkipWhile(x => x.Date < walkForwardResult.OOSRunReports[0].StartDate).ToList(), walkForwardResult.OOSRunReports.Last().Strategy);
			
			Console.WriteLine("Monkey test results:" + Environment.NewLine);
			Console.WriteLine($"90% of monkey tests with random entry returned below {monkeyTestResults.RandomEnter90PercentFinalInvestment:C}");
			Console.WriteLine($"90% of monkey tests with random exit returned below {monkeyTestResults.RandomExit90PercentFinalInvestment:C}");
			Console.WriteLine($"90% of monkey tests with random entry and exit returned below {monkeyTestResults.RandomEnterAndExit90PercentFinalInvestment:C}");
			
			Console.WriteLine();
			
			Console.WriteLine($"Is our strategy better than 90% of monkey test runs with random entry? - {walkForwardResult.FinalInvestment > monkeyTestResults.RandomEnter90PercentFinalInvestment}");
			Console.WriteLine($"Is our strategy better than 90% of monkey test runs with random exit? - {walkForwardResult.FinalInvestment > monkeyTestResults.RandomExit90PercentFinalInvestment}");
			Console.WriteLine($"Is our strategy better than 90% of monkey test runs with random entry and exit? - {walkForwardResult.FinalInvestment > monkeyTestResults.RandomEnterAndExit90PercentFinalInvestment}");
			
			const int monteCarloSimulations = 2500;
			Console.Write(Environment.NewLine + $"Starting Monte Carlo Simulations ({monteCarloSimulations} permutations)..." + Environment.NewLine);

			var mctResults = MonteCarloTester.Run(walkForwardResult, dataPoints, monteCarloSimulations);

			Console.Write(Environment.NewLine + "Results:" + Environment.NewLine);

			foreach (var mctResult in mctResults)
			{
				Console.WriteLine($"OOS Period: {mctResult.OriginalOOSReport.StartDate:Y} - {mctResult.OriginalOOSReport.EndDate:Y}");
				Console.WriteLine($"Median Drawdown: {mctResult.MedianDrawdown/100:P}");
				Console.WriteLine($"Median Profit Ratio: {mctResult.MedianProfitRatio:N}");
				Console.WriteLine($"Median Return to Drawdown ratio: {mctResult.MedianReturnToDrawdownRatio:N}");
				Console.WriteLine($"Probability of making money: {mctResult.ProbabilityOfMakingMoney:P}");
				
				Console.WriteLine();
			}

			ScottPlotHelper.DrawEquityCurve(security, walkForwardResult.OOSRunsPassedOOSFilterCount / (decimal)walkForwardResult.OOSRunReports.Count, walkForwardResult.OOSEquityCurve);
			var heatmapStrategy = walkForwardResult.OOSRunReports[0].Strategy;
			ScottPlotHelper.ShowHeatMap(security, walkForwardResult.InSampleHeatMap, heatmapStrategy.HeatmapAxisLabels, heatmapStrategy.HeatmapValueLabel, false);

			Console.Read();
		}

		private static WalkForwardResult RunWalkForward(WalkForwardSetup setup, string ticker, decimal initialInvestment, IList<StockPrice> dataPoints)
		{
			var walkForwardTester = new WalkForwardTester(() => setup.GenerateStrategies(ticker), setup.IsLastBetter);

			return walkForwardTester.Run(
				dataPoints, 
				initialInvestment, 
				setup.InSampleMonths, 
				setup.OutOfSampleMonths, 
				setup.InSampleFilter, 
				setup.OutOfSampleFilter);
		}
	}
}