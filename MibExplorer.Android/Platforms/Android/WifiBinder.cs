using Android.Content;
using Android.Net;

namespace MibExplorer.Android;

/// <summary>
/// Finds the currently connected Wi-Fi network and binds the whole process to it (the plan's fallback mode:
/// simple, but cuts mobile-data internet for this app while bound). Socket-level binding is a later, tested option.
/// Does NOT join or switch Wi-Fi networks: the user connects to the MIB hotspot manually in Android settings.
/// </summary>
public static class WifiBinder
{
    public static string Bind()
    {
        var cm = (ConnectivityManager)global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService)!;
        foreach (var n in cm.GetAllNetworks())
        {
            var caps = cm.GetNetworkCapabilities(n);
            if (caps != null && caps.HasTransport(TransportType.Wifi))
                return cm.BindProcessToNetwork(n) ? "Process bound to Wi-Fi network" : "Bind refused by Android";
        }
        return "No Wi-Fi network found (connect to the unit's hotspot in Android settings first)";
    }

    public static void Unbind()
    {
        var cm = (ConnectivityManager)global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService)!;
        cm.BindProcessToNetwork(null);
    }
}
