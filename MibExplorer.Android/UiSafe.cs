namespace MibExplorer.Android;

/// <summary>An exception escaping an async-void UI handler crashes the app. Handlers that are not already fully wrapped run through this and report the error to the user instead.</summary>
public static class UiSafe
{
    public static async Task Run(Func<Task> f, Func<string, Task> report)
    {
        try { await f(); }
        catch (OperationCanceledException) { try { await report("Cancelled. If this was a change on the unit, check the current state before repeating it."); } catch { } }
        catch (Exception ex) { try { await report(ex.Message); } catch (Exception inner) { System.Diagnostics.Debug.WriteLine("UiSafe report failed: " + inner.Message); } }
    }
}
