using FluidBar.Core.Layout;

namespace FluidBar.Core.Tests;

/// <summary>Rectangle union used by the rail damage path (content + strip bands).</summary>
public sealed class VisualRectUnionTests
{
    [Fact]
    public void Union_contains_both_inputs_and_handles_empty()
    {
        var a = new VisualRect(10, 20, 10, 10);
        var b = new VisualRect(30, 50, 10, 10);

        Assert.Equal(new VisualRect(10, 20, 30, 40), a.Union(b));
        Assert.Equal(a, VisualRect.Empty.Union(a));
        Assert.Equal(a, a.Union(VisualRect.Empty));
        Assert.Equal(VisualRect.Empty, VisualRect.Empty.Union(VisualRect.Empty));
    }
}
