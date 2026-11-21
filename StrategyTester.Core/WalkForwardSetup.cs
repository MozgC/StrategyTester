namespace StrategyTester;

/// <summary>
/// Everything needed to walk-forward test one kind of strategy: the parameter grid to optimize over, which in-sample runs
/// are eligible, how to pick the best one, which out-of-sample runs count as "passed", and the window sizes.
/// Each strategy class exposes one as a static WalkForwardSetup property, so Program can switch strategies with one line.
/// </summary>
public record WalkForwardSetup(
	string                                        StrategyName,
	Func<string, IEnumerable<Strategy>>           GenerateStrategies,   // ticker -> strategies with every parameter combination
	Func<RunReport, bool>                         InSampleFilter,       // in-sample runs failing this are not eligible to be "best"
	Func<RunReport, bool>                         OutOfSampleFilter,    // OOS runs passing this count towards "passed OOS filter"
	Func<(RunReport best, RunReport last), bool>  IsLastBetter,         // optimization criterion
	int                                           InSampleMonths    = 48,
	int                                           OutOfSampleMonths = 12);
