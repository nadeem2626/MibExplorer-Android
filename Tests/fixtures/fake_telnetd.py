#!/usr/bin/env python3
"""Local fixture: Telnet server with IAC negotiation, login/password, and a real /bin/sh confined to a start dir. Test use only."""
import socket, subprocess, sys, threading, os, select
PORT=int(sys.argv[1]); ROOT=sys.argv[2]; USER="root"; PW="secret"
IAC=bytes([255])
def handle(c):
    c.sendall(IAC+bytes([253,31])+IAC+bytes([251,1])+IAC+bytes([251,3]))  # DO NAWS, WILL ECHO, WILL SGA
    c.sendall(b"MHI2 fixture\r\nlogin: ")
    buf=b""
    def readline():
        nonlocal buf
        while b"\n" not in buf:
            d=c.recv(1024)
            if not d: return None
            # strip IAC sequences
            out=bytearray(); i=0
            while i<len(d):
                if d[i]==255 and i+1<len(d):
                    if d[i+1] in (251,252,253,254): i+=3; continue
                    if d[i+1]==255: out.append(255); i+=2; continue
                    i+=2; continue
                out.append(d[i]); i+=1
            buf+=bytes(out)
        l,buf=buf.split(b"\n",1); return l.strip(b"\r").decode()
    u=readline(); c.sendall(b"Password: ")
    p=readline()
    if u!=USER or p!=PW:
        c.sendall(b"\r\nLogin incorrect\r\nlogin: "); 
        try: c.recv(1024)
        except: pass
        c.close(); return
    c.sendall(b"\r\n# ")
    sh=subprocess.Popen(["/bin/sh"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,cwd=ROOT,bufsize=0)
    def pump():
        while True:
            d=os.read(sh.stdout.fileno(),4096)
            if not d: break
            c.sendall(d.replace(b"\xff",b"\xff\xff").replace(b"\n",b"\r\n"))
    threading.Thread(target=pump,daemon=True).start()
    try:
        while True:
            d=c.recv(4096)
            if not d: break
            out=bytearray(); i=0
            while i<len(d):
                if d[i]==255 and i+1<len(d):
                    if d[i+1] in (251,252,253,254): i+=3; continue
                    if d[i+1]==255: out.append(255); i+=2; continue
                    i+=2; continue
                out.append(d[i]); i+=1
            sh.stdin.write(bytes(out).replace(b"\r\n",b"\n").replace(b"\r",b"\n"))
    except Exception: pass
    sh.kill(); c.close()
s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(("127.0.0.1",PORT)); s.listen(5)
while True:
    c,_=s.accept(); threading.Thread(target=handle,args=(c,),daemon=True).start()
