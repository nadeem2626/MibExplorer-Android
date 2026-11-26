using System.Text.Json;

namespace MibExplorer.Android;

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string Protocol { get; set; } = "SSH";
    public bool RememberPassword { get; set; }
    public override string ToString() => $"{Name}  ({Host})";
}

/// <summary>Saved connection profiles. Passwords (only if the user opts in) go to the Android Keystore-backed SecureStorage, never to the profile JSON.</summary>
public static class Profiles
{
    public static readonly string[] All = ["SSH (SCP transfers)", "Telnet", "FTP"];
    public static int DefaultPort(string p) => p.StartsWith("SSH") ? 22 : p == "Telnet" ? 23 : 21;
    public static string DefaultUser(string p) => p == "FTP" ? "anonymous" : "root";

    public static List<Profile> Load()
    {
        var json = Preferences.Default.Get("profiles_v1", "");
        if (string.IsNullOrEmpty(json) && !Preferences.Default.Get("profiles_seeded", false))
        {
            var seed = new List<Profile>
            {
                new() { Name = "mhi2 hotspot", Host = "10.173.189.1", Port = 22, Protocol = "SSH (SCP transfers)" },
                new() { Name = "mhi2 lan", Host = "172.16.250.248", Port = 22, Protocol = "SSH (SCP transfers)" },
                new() { Name = "mst2 ftp", Host = "192.168.1.4", Port = 21, Protocol = "FTP" },
            };
            Save(seed);
            Preferences.Default.Set("profiles_seeded", true);
            return seed;
        }
        try { return JsonSerializer.Deserialize<List<Profile>>(json) ?? []; } catch { return []; }
    }

    public static void Save(List<Profile> list) => Preferences.Default.Set("profiles_v1", JsonSerializer.Serialize(list));

    public static Task<string?> GetPasswordAsync(Profile p) => SecureStorage.Default.GetAsync("pw_" + p.Id);
    public static Task SetPasswordAsync(Profile p, string pw) => SecureStorage.Default.SetAsync("pw_" + p.Id, pw);
    public static void ClearPassword(Profile p) => SecureStorage.Default.Remove("pw_" + p.Id);
}
