# Independent SSH server: asyncssh (third party). Runs exec commands with /bin/sh in the fixture root and serves SCP/SFTP. Interop check only.
import asyncio, sys, os, asyncssh, subprocess
root=os.path.realpath(sys.argv[2]); port=int(sys.argv[1]); keyf=os.path.join(os.path.dirname(os.path.abspath(__file__)),"indep_hostkey")
AK=os.path.join(os.path.dirname(os.path.abspath(__file__)),"indep_authorized.pub")
class S(asyncssh.SSHServer):
    def begin_auth(self, u): return True
    def password_auth_supported(self): return True
    def public_key_auth_supported(self): return os.path.exists(AK)
    def validate_public_key(self, u, key): return u=="root" and os.path.exists(AK) and asyncssh.read_public_key(AK).public_data==key.public_data
    def validate_password(self, u, p): return u=="root" and p=="secret"
async def handle(proc):
    cmd=proc.command
    if cmd is None:
        p=await asyncio.create_subprocess_exec("/bin/sh","-i",cwd=root,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        async def pump_in():
            try:
                while True:
                    d=await proc.stdin.read(1024)
                    if not d: break
                    p.stdin.write(d.encode() if isinstance(d,str) else d); await p.stdin.drain()
            except Exception: pass
        async def pump_out():
            try:
                while True:
                    d=await p.stdout.read(1024)
                    if not d: break
                    proc.stdout.write(d.decode(errors="replace"))
            except Exception: pass  # channel already closed by the client
            try: proc.exit(0)
            except Exception: pass
        ti=asyncio.ensure_future(pump_in()); to=asyncio.ensure_future(pump_out())
        await asyncio.wait([ti,to],return_when=asyncio.FIRST_COMPLETED)  # client closed (stdin EOF) or the shell exited
        try: p.kill()
        except Exception: pass
        ti.cancel(); to.cancel(); return
    r=await asyncio.create_subprocess_exec("/bin/sh","-c",cmd,cwd=root,stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    o,e=await r.communicate()
    proc.stdout.write(o.decode(errors="replace")); proc.stderr.write(e.decode(errors="replace")); proc.exit(r.returncode)
async def main():
    if not os.path.exists(keyf): asyncssh.generate_private_key("ssh-rsa").write_private_key(keyf)
    await asyncssh.create_server(S,"127.0.0.1",port,server_host_keys=[keyf],process_factory=handle,sftp_factory=True,allow_scp=True,encoding=None if False else "utf-8")
    await asyncio.Event().wait()
asyncio.run(main())
