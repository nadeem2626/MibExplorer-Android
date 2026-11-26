using Android.Content;
using Android.Net;
using System.Text;

namespace MibExplorer.Android;

/// <summary>Facts about the device's networks at the moment of a failed download, plus the full exception chain. .NET Android reports every transport failure as the generic "Connection failure"; the real cause (connect refused, unknown host, TLS trust) is in the inner exceptions.</summary>
public static class NetDiag
{
    public static string Chain(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException) sb.Append(sb.Length == 0 ? "" : " <- ").Append(e.GetType().Name).Append(": ").Append(e.Message);
        return sb.ToString();
    }

    public static string Snapshot()
    {
        try
        {
            var cm = (ConnectivityManager)global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService)!;
            var sb = new StringBuilder();
            var bound = cm.BoundNetworkForProcess; sb.Append("process bound to a network: ").Append(bound != null ? "YES" : "no").Append("; ");
            var act = cm.ActiveNetwork; var ac = act == null ? null : cm.GetNetworkCapabilities(act);
            sb.Append("default network: ").Append(Describe(ac)).Append("; ");
            int n = 0, validated = 0;
            foreach (var net in cm.GetAllNetworks()) { var c = cm.GetNetworkCapabilities(net); n++; if (c != null && c.HasCapability(NetCapability.Internet) && c.HasCapability(NetCapability.Validated)) validated++; }
            sb.Append(n).Append(" networks, ").Append(validated).Append(" with validated internet");
            return sb.ToString();
        }
        catch (Exception e) { return "network state unavailable: " + e.Message; }
    }

    private static string Describe(NetworkCapabilities? c)
    {
        if (c == null) return "none";
        string t = c.HasTransport(TransportType.Wifi) ? "Wi-Fi" : c.HasTransport(TransportType.Cellular) ? "mobile data" : c.HasTransport(TransportType.Ethernet) ? "ethernet" : "other";
        return t + (c.HasCapability(NetCapability.Internet) ? ", internet-capable" : ", no internet capability") + (c.HasCapability(NetCapability.Validated) ? ", validated" : ", NOT validated");
    }
}
