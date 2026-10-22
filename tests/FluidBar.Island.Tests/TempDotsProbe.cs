using FluidBar.Island;

namespace FluidBar.Island.Tests;

public sealed class TempDotsProbe
{
    [Fact]
    public void Dump()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var report = new System.Text.StringBuilder();
        for (var page = 0; page < 6; page++)
        {
            var state = IslandTestFixtures.Expanded(page, strip: true);
            var layout = renderer.MeasureStrip(state);
            report.AppendLine($"page={page} state.PageIndex={state.PageIndex} layout.Page={layout.Page} left={layout.DotsLeft.Length} right={layout.DotsRight.Length}");
        }

        File.WriteAllText(@"C:\Users\Doulor\AppData\Local\Temp\dots-probe.txt", report.ToString());
    }
}
