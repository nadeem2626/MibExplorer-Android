#!/usr/bin/env python3
"""Local fixture: minimal FTP server (PASV, LIST via ls -la, RETR/STOR/DELE/MKD/RMD/RNFR/RNTO/SITE CHMOD/NOOP). Test use only."""
import socket, sys, threading, os, subprocess
PORT=int(sys.argv[1]); ROOT=os.path.realpath(sys.argv[2]); ALLOW_CHMOD=(len(sys.argv)<4 or sys.argv[3]!="nochmod")
def real(p,cwd):
    full=os.path.realpath(os.path.join(ROOT,(p if p.startswith("/") else os.path.join(cwd,p)).lstrip("/")))
    if not full.startswith(ROOT): raise PermissionError
    return full
def handle(c):
    f=c.makefile("rwb",buffering=0); cwd="/"; ps=None; rn=None
    def w(s): f.write((s+"\r\n").encode())
    w("220 fixture ftpd"); user=None
    def data():
        d,_=ps.accept(); return d
    while True:
        l=f.readline()
        if not l: break
        l=l.decode().rstrip("\r\n"); cmd,_,a=l.partition(" ")
        cmd=cmd.upper()
        try:
            if cmd=="USER": user=a; w("331 pw")
            elif cmd=="PASS": w("230 ok") if (user=="admin" and a=="pw") else w("530 bad login")
            elif cmd in("TYPE","NOOP"): w("200 ok")
            elif cmd=="PASV":
                if ps: ps.close()
                ps=socket.socket(); ps.bind(("127.0.0.1",0)); ps.listen(1); p=ps.getsockname()[1]
                w("227 Entering Passive Mode (127,0,0,1,%d,%d)"%(p>>8,p&255))
            elif cmd=="LIST":
                w("150 here"); d=data()
                out=subprocess.run(["ls","-la",real(a,cwd)],capture_output=True,text=True).stdout
                d.sendall(out.replace("\n","\r\n").encode()); d.close(); w("226 done")
            elif cmd=="RETR":
                fp=real(a,cwd); w("150 go"); d=data(); d.sendall(open(fp,"rb").read()); d.close(); w("226 done")
            elif cmd=="STOR":
                fp=real(a,cwd); w("150 go"); d=data(); b=b""
                while True:
                    x=d.recv(8192)
                    if not x: break
                    b+=x
                open(fp,"wb").write(b); d.close(); w("226 done")
            elif cmd=="DELE": os.remove(real(a,cwd)); w("250 ok")
            elif cmd=="RMD": os.rmdir(real(a,cwd)); w("250 ok")
            elif cmd=="MKD": os.mkdir(real(a,cwd)); w('257 "%s" created'%a)
            elif cmd=="RNFR": rn=real(a,cwd); w("350 ok")
            elif cmd=="RNTO": os.rename(rn,real(a,cwd)); w("250 ok")
            elif cmd=="SITE":
                parts=a.split(" ",2)
                if ALLOW_CHMOD and parts[0].upper()=="CHMOD": os.chmod(real(parts[2],cwd),int(parts[1],8)); w("200 ok")
                else: w("502 not implemented")
            elif cmd=="QUIT": w("221 bye"); break
            else: w("502 unknown")
        except FileNotFoundError: w("550 not found")
        except PermissionError: w("550 denied")
        except OSError as e: w("550 "+str(e))
    c.close()
s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(("127.0.0.1",PORT)); s.listen(5)
while True:
    c,_=s.accept(); threading.Thread(target=handle,args=(c,),daemon=True).start()
