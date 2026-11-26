using MibExplorer.Services.Scripting; using MibExplorer.Settings;
public static class SeedTests
{
    public static Task<int> RunAsync()
    {
        int fail = 0; void C(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        string dir = Path.Combine(Path.GetTempPath(), "seed_" + Guid.NewGuid().ToString("N")[..6]); AppSettingsStore.FolderOverride = Path.Combine(dir, "settings"); AppSettingsStore.Current.ScriptsFolderPath = dir;
        var c = new ScriptCatalogService(); var items = c.GetScripts();
        C("fresh folder gets the bundled official set (>0 scripts)", items.Count > 0);
        C("manifest.json present", File.Exists(Path.Combine(dir, "Official", "manifest.json")));
        C("SvmFix run.sh present", File.Exists(Path.Combine(dir, "Official", "SvmFix", "run.sh")));
        File.WriteAllText(Path.Combine(dir, "Official", "ExtractSkin0ImageIdMapToSd.sh"), "changed");
        C("second seed does not overwrite", c.SeedBundledOfficial() == 0 && File.ReadAllText(Path.Combine(dir, "Official", "ExtractSkin0ImageIdMapToSd.sh")) == "changed");
        var up = new OfficialScriptUpdateService(); var m = up.LoadLocalManifest(Path.Combine(dir, "Official")); C("bundled manifest parses", m != null);
        Console.WriteLine("scripts listed: " + string.Join(", ", items.Select(i => i.Name)));
        return Task.FromResult(fail);
    }
}
