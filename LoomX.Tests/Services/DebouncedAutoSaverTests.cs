using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class DebouncedAutoSaverTests
{
    [Fact]
    public async Task TriggerCoalescesRapidCallsIntoSingleSave()
    {
        var saves = 0;
        using var saver = new DebouncedAutoSaver(() => { Interlocked.Increment(ref saves); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(40));

        saver.Trigger();
        saver.Trigger();
        saver.Trigger();
        await Task.Delay(200);

        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task TriggerDuringSaveSchedulesFollowUpSaveWithLatestState()
    {
        var saves = 0;
        var firstSaveStarted = new TaskCompletionSource();
        var releaseFirstSave = new TaskCompletionSource();
        using var saver = new DebouncedAutoSaver(async () =>
        {
            if (Interlocked.Increment(ref saves) == 1)
            {
                firstSaveStarted.TrySetResult();
                await releaseFirstSave.Task;
            }
        }, TimeSpan.FromMilliseconds(10));

        saver.Trigger();
        await firstSaveStarted.Task;
        saver.Trigger();
        await Task.Delay(60); // 等防抖到期并登记补存
        releaseFirstSave.TrySetResult();
        await WaitForConditionAsync(() => Volatile.Read(ref saves) >= 2);

        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task FlushAsyncSavesImmediatelyAndCancelsPendingDebounce()
    {
        var saves = 0;
        using var saver = new DebouncedAutoSaver(() => { Interlocked.Increment(ref saves); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(10_000));

        saver.Trigger();
        await saver.FlushAsync();

        Assert.Equal(1, saves);
        await Task.Delay(50);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task SaveFaultedEventRaisedAndSaverKeepsWorking()
    {
        var saves = 0;
        var failures = 0;
        using var saver = new DebouncedAutoSaver(() =>
        {
            Interlocked.Increment(ref saves);
            if (saves == 1) throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(10));
        saver.SaveFaulted += (_, _) => Interlocked.Increment(ref failures);

        saver.Trigger();
        await WaitForConditionAsync(() => Volatile.Read(ref saves) >= 1 && Volatile.Read(ref failures) >= 1);

        saver.Trigger();
        await WaitForConditionAsync(() => Volatile.Read(ref saves) >= 2);

        Assert.Equal(1, failures);
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task DisposeCancelsPendingDebounce()
    {
        var saves = 0;
        var saver = new DebouncedAutoSaver(() => { Interlocked.Increment(ref saves); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50));

        saver.Trigger();
        saver.Dispose();
        await Task.Delay(150);

        Assert.Equal(0, saves);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        for (var elapsed = 0; elapsed < timeoutMs && !condition(); elapsed += 10)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
