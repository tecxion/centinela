namespace Centinela.Media.Tests;

public class RecorderFinalizationTests
{
    static (Task, Func<string?>) Pending(TaskCompletionSource done, Func<string?> error) => (done.Task, error);

    [Fact]
    public async Task Completes_when_every_recorder_finished_without_error()
    {
        var a = new TaskCompletionSource();
        var task = RecorderFinalization.WhenAllFinalizedAsync([Pending(a, () => null)], () => []);
        Assert.False(task.IsCompleted);

        a.SetResult();
        await task;
    }

    [Fact]
    public async Task Faults_with_every_recorder_error_after_all_of_them_finished()
    {
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();
        string? errorA = null, errorB = null;
        var task = RecorderFinalization.WhenAllFinalizedAsync(
            [Pending(a, () => errorA), Pending(b, () => errorB)], () => []);

        errorA = "Error al finalizar la grabación: disco lleno";
        a.SetResult();
        await Task.Delay(50);
        Assert.False(task.IsCompleted); // b is still draining

        errorB = "Error al cerrar el archivo: acceso denegado";
        b.SetResult();
        var ex = await Assert.ThrowsAsync<RecordingException>(() => task);
        Assert.Contains("disco lleno", ex.Message);
        Assert.Contains("acceso denegado", ex.Message);
    }

    [Fact]
    public async Task Waits_for_recorders_that_register_while_draining_and_reports_their_errors()
    {
        var first = new TaskCompletionSource();
        var late = new TaskCompletionSource();
        var lateRegistered = false;
        var task = RecorderFinalization.WhenAllFinalizedAsync(
            [Pending(first, () => null)],
            () => lateRegistered && !late.Task.IsCompleted ? [Pending(late, () => "Error al escribir: E/S")] : []);

        lateRegistered = true;
        first.SetResult();
        await Task.Delay(50);
        Assert.False(task.IsCompleted);

        late.SetResult();
        var ex = await Assert.ThrowsAsync<RecordingException>(() => task);
        Assert.Equal("Error al escribir: E/S", ex.Message);
    }

    [Fact]
    public async Task Same_error_is_reported_once()
    {
        var a = new TaskCompletionSource();
        a.SetResult();
        var ex = await Assert.ThrowsAsync<RecordingException>(() =>
            RecorderFinalization.WhenAllFinalizedAsync([Pending(a, () => "x"), Pending(a, () => "x")], () => []));
        Assert.Equal("x", ex.Message);
    }
}
