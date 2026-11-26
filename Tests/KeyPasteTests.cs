using MibExplorer.Services;

public static class KeyPasteTests
{
    sealed class Mem : IKeyStore
    {
        public Dictionary<string, string> D = new();
        public Task<string?> GetAsync(string k) => Task.FromResult(D.TryGetValue(k, out var v) ? v : null);
        public Task SetAsync(string k, string v) { D[k] = v; return Task.CompletedTask; }
        public void Remove(string k) => D.Remove(k);
    }
    public static async Task<int> RunAsync()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var s = new Mem();
        await KeySlots.RotateAsync(s, "A", "pubA");
        Check("first key: stored, no previous", s.D["ssh_key_pem"] == "A" && !await KeySlots.HasPreviousAsync(s) && !await KeySlots.RotationDiscardsPreviousAsync(s));
        s.D["ssh_key_pass"] = "pwA";
        Check("with one key and no previous, rotating discards nothing", !await KeySlots.RotationDiscardsPreviousAsync(s));
        await KeySlots.RotateAsync(s, "B", "pubB");
        Check("rotate: A becomes previous with its pub and passphrase", s.D["ssh_key_pem_prev"] == "A" && s.D["ssh_key_pub_prev"] == "pubA" && s.D["ssh_key_pass_prev"] == "pwA");
        Check("rotate: B active, passphrase cleared", s.D["ssh_key_pem"] == "B" && s.D["ssh_key_pub"] == "pubB" && !s.D.ContainsKey("ssh_key_pass"));
        Check("a second rotation WOULD discard A (caller must warn)", await KeySlots.RotationDiscardsPreviousAsync(s));
        Check("swap succeeds", await KeySlots.SwapWithPreviousAsync(s));
        Check("swap: A active with its passphrase, B previous", s.D["ssh_key_pem"] == "A" && s.D["ssh_key_pass"] == "pwA" && s.D["ssh_key_pem_prev"] == "B" && s.D["ssh_key_pub_prev"] == "pubB" && !s.D.ContainsKey("ssh_key_pass_prev"));
        await KeySlots.SwapWithPreviousAsync(s);
        Check("swap twice returns to the start, nothing lost", s.D["ssh_key_pem"] == "B" && s.D["ssh_key_pem_prev"] == "A" && s.D["ssh_key_pass_prev"] == "pwA");
        var e = new Mem(); Check("swap with no previous does nothing", !await KeySlots.SwapWithPreviousAsync(e) && e.D.Count == 0);
        bool threw = false; try { await KeySlots.RotateAsync(e, "", "x"); } catch (ArgumentException) { threw = true; }
        Check("empty new key refused, store untouched", threw && e.D.Count == 0);

        Check("paste: empty", PasteGuard.Classify("", out _) == PasteVerdict.Empty && PasteGuard.Classify(null, out _) == PasteVerdict.Empty);
        Check("paste: single line sends without confirmation", PasteGuard.Classify("ls -la", out int l1) == PasteVerdict.Send && l1 == 1);
        Check("paste: two lines needs confirmation", PasteGuard.Classify("a\nb", out int l2) == PasteVerdict.Confirm && l2 == 2);
        Check("paste: trailing newline needs confirmation", PasteGuard.Classify("reboot\n", out _) == PasteVerdict.Confirm);
        Check("paste: CR only needs confirmation", PasteGuard.Classify("a\rb", out _) == PasteVerdict.Confirm);
        Check("paste: over the cap refused", PasteGuard.Classify(new string('x', 20001), out _) == PasteVerdict.TooLarge && PasteGuard.Classify(new string('x', 20000), out _) == PasteVerdict.Send);
        Check("chunk: multi-char with line break is a paste", PasteGuard.IsPasteChunk("ls\nrm x") && PasteGuard.IsPasteChunk("ab\r"));
        Check("chunk: Enter key, escape sequences and plain text are typing", !PasteGuard.IsPasteChunk("\r") && !PasteGuard.IsPasteChunk("\u001b[A") && !PasteGuard.IsPasteChunk("\u001b[200~x\n") && !PasteGuard.IsPasteChunk("ls"));
        Check("ctrl: c -> 0x03, C -> 0x03, d -> 0x04, z -> 0x1a, l -> 0x0c", CtrlModifier.Apply("c") == "\u0003" && CtrlModifier.Apply("C") == "\u0003" && CtrlModifier.Apply("d") == "\u0004" && CtrlModifier.Apply("z") == "\u001a" && CtrlModifier.Apply("l") == "\u000c");
        Check("ctrl: [ -> ESC, \\ -> 0x1c, @ -> NUL", CtrlModifier.Apply("[") == "\u001b" && CtrlModifier.Apply("\\") == "\u001c" && CtrlModifier.Apply("@") == "\0");
        Check("ctrl: digits, space, multi-char and empty are not mapped", CtrlModifier.Apply("1") == null && CtrlModifier.Apply(" ") == null && CtrlModifier.Apply("ab") == null && CtrlModifier.Apply("") == null && CtrlModifier.Apply("\u001b[A") == null);
        return fail == 0 ? 0 : 1;
    }
}
