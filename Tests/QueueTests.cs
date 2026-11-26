using MibExplorer.Services;

public static class QueueTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    public static async Task<int> RunAsync()
    {
        var q = new TransferQueue(); int running = 0, maxRun = 0; var order = new List<string>();
        Func<string, Func<CancellationToken, Task<string>>> job = n => async ct => { int r = Interlocked.Increment(ref running); maxRun = Math.Max(maxRun, r); order.Add(n); await Task.Delay(40, ct); Interlocked.Decrement(ref running); return n + " ok"; };
        var t = new[] { q.RunAsync("Download", "a", job("a")), q.RunAsync("Download", "b", job("b")), q.RunAsync("Upload", "c", job("c")) };
        var jobs = await Task.WhenAll(t);
        Check("jobs run one at a time", maxRun == 1);
        Check("all three finish Done", jobs.All(j => j.State == TransferState.Done));
        Check("detail is kept", jobs[0].Detail == "a ok");
        // cancel while queued
        q = new TransferQueue(); order.Clear(); running = 0;
        var first = q.RunAsync("Download", "slow", async ct => { await Task.Delay(200, ct); return "x"; });
        await Task.Delay(30);
        var secondTask = q.RunAsync("Download", "queued", job("queued")); await Task.Delay(20);
        var queuedJob = q.Items.First(i => i.Name == "queued"); Check("second is Queued while first runs", queuedJob.State == TransferState.Queued);
        q.Cancel(queuedJob); var res2 = await secondTask; await first;
        Check("queued job cancelled before start", res2.State == TransferState.Cancelled && !order.Contains("queued"));
        // cancel while running
        q = new TransferQueue(); bool sawCancel = false;
        var rt = q.RunAsync("Folder download", "big", async ct => { try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { sawCancel = true; throw; } return "x"; });
        await Task.Delay(30); q.Cancel(q.Items[0]); var rj = await rt;
        Check("running job cancelled through its token", rj.State == TransferState.Cancelled && sawCancel);
        Check("cancel text does not claim a rollback", rj.Detail.Contains("may be left behind"));
        Check("finished job cannot be cancelled", !rj.CanCancel);
        // failure does not block the queue
        q = new TransferQueue();
        var f = await q.RunAsync("Upload", "bad", ct => throw new IOException("boom"));
        var ok = await q.RunAsync("Upload", "good", ct => Task.FromResult("fine"));
        Check("failed job reports its error", f.State == TransferState.Failed && f.Detail == "boom");
        Check("queue continues after a failure", ok.State == TransferState.Done);
        q.ClearFinished(); Check("clear removes finished jobs", q.Items.Count == 0);
        // progress
        q = new TransferQueue(); double? seenPct = -1; ulong seenBytes = 0; TransferJob? pj = null;
        var pt = q.RunAsync("Download", "p", async (ct, pr) =>
        {
            pj = q.Items.First(); pr.Report(new MibExplorer.Models.FileTransferProgressInfo { BytesTransferred = 50, TotalBytes = 200 }); seenPct = pj.Percent; seenBytes = pj.BytesDone;
            pr.Report(new MibExplorer.Models.FileTransferProgressInfo { BytesTransferred = 80 }); return "ok";
        });
        var pdone = await pt;
        Check("progress with known total sets percentage and bytes", seenPct == 25 && seenBytes == 50);
        Check("progress without total gives no percentage (no made-up number)", pdone.Percent is null && pdone.BytesDone == 80);
        Console.WriteLine($"queue: {pass} pass, {fail} fail"); return fail == 0 ? 0 : 1;
    }
}
