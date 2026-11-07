using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

public sealed class FileDragFeedbackTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shell_bitmap_is_attached_only_after_independent_preview_initialization_fails(bool ready)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Marshal.ThrowExceptionForHR(NativeMethods.Ole32.OleInitialize(IntPtr.Zero));
                try
                {
                    using var data = ShellFileDragData.Create([Path.Combine(Path.GetTempPath(), "fallback.txt")]);
                    var initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var feedback = new FileDragFeedback(data, new FileDragImage(64, 32,
                        Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray(), -16, -16), initialized.Task);
                    var format = new FORMATETC { cfFormat = unchecked((short)NativeMethods.User32.RegisterClipboardFormatW("DragImageBits")),
                        dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
                    feedback.Update(1);
                    Assert.NotEqual(0, data.Data.QueryGetData(ref format));
                    initialized.SetResult(ready);
                    feedback.Update(1);
                    Assert.Equal(!ready, data.Data.QueryGetData(ref format) == 0);
                    feedback.Dispose();
                    feedback.Update(1); // OLE may deliver a final callback after cancellation.
                }
                finally { NativeMethods.Ole32.OleUninitialize(); }
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
