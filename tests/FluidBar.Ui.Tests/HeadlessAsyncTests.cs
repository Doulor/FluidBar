namespace FluidBar.Ui.Tests;

public sealed class HeadlessAsyncTests
{
    [Fact]
    public async Task Async_ui_failures_are_awaited_and_propagated_to_the_test()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => AvaloniaTest.RunAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("Expected asynchronous test failure.");
        }));
    }
}
