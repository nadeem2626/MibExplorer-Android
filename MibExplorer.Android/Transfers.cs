using MibExplorer.Services;

namespace MibExplorer.Android;

/// <summary>All Files and Editor transfers run through one serial queue with cancel (logic in Core, Linux-tested).</summary>
public static class Transfers
{
    public static readonly TransferQueue Queue = new();
    static Transfers()
    {
        Queue.Changed += () =>
        {
            bool busy; lock (Queue.Items) busy = Queue.Items.Any(x => x.CanCancel);
            TransferService.Sync(busy);
        };
    }
}
