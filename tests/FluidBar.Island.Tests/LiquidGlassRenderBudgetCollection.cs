namespace FluidBar.Island.Tests;

/// <summary>
/// Wall-clock raster budgets need an idle test runner: concurrent golden-image rendering and font
/// shaping measure resource contention rather than the settled renderer. Keep the 8 ms budget intact.
/// </summary>
[CollectionDefinition("Liquid glass render budget", DisableParallelization = true)]
public sealed class LiquidGlassRenderBudgetCollection;
