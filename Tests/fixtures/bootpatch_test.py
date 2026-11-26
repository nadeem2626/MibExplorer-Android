# Runs the REAL startup.sh patch loop (extracted from the shipped InstallSshScript) on fixture startup.sh files with /bin/sh.
import re, subprocess, tempfile, os, sys
src = open(os.path.join(os.path.dirname(__file__), '../../MibExplorer.Core/Services/Packages/SdUpdatePackageBuilder.cs')).read()
i = src.index('private const string InstallSshScript ='); t = re.search(r'@"(.*?)";\n', src[i:], re.S).group(1).replace('""', '"')
a = t.index('            : > "$TMP_PATCH"'); b = t.index('done < "$TMP_ORIG"') + len('done < "$TMP_ORIG"')
loop = t[a:b]; fails = 0
def run(name, content, expect_hook, expect_count=None):
    global fails
    d = tempfile.mkdtemp(); orig = d + '/orig'; patch = d + '/patch'
    open(orig, 'wb').write(content)
    sh = f'TMP_ORIG={orig}\nTMP_PATCH={patch}\nHOOK_INSERTED=0\nIN_DCIVIDEO_BLOCK=0\n{loop}\necho HOOK=$HOOK_INSERTED\n'
    out = subprocess.run(['/bin/sh', '-c', sh], capture_output=True, text=True).stdout
    got = int(re.search(r'HOOK=(\d)', out).group(1)); body = open(patch, 'rb').read()
    n = body.count(b'# MIBEXPLORER SSH FINISHER'); lines_kept = all(l in body for l in content.split(b'\n') if l.strip())
    ok = (got == expect_hook) and (n == (expect_count if expect_count is not None else (1 if expect_hook else 0))) and lines_kept
    print(('PASS ' if ok else 'FAIL ') + name + f' (hook={got}, markers={n}, all original lines preserved={lines_kept})'); fails += (not ok)
run('QNX VNC marker present: hook inserted once before it', b'echo a\n    # QNX VNC CLIENT\n    foo\necho z\n', 1)
run('DCIVIDEO block, no VNC marker: hook inserted after block end', b'x\n    # DCIVIDEO: Kombi Map\n    if true; then\n    echo k\n    fi\nafter\n', 1)
run('neither anchor: no hook (installer then reports error, file untouched)', b'echo a\necho b\n', 0)
run('CRLF startup.sh: anchors not matched, no hook (fail safe)', b'echo a\r\n    # QNX VNC CLIENT\r\n    foo\r\n', 0)
run('last line without newline preserved', b'echo a\n    # QNX VNC CLIENT\n    foo', 1)
sys.exit(1 if fails else 0)
