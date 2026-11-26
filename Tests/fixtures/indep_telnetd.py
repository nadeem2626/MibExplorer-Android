# Independent Telnet server: telnetlib3 (third party) with a pty login shell, used only to check interoperability.
import sys, os, subprocess
os.environ["MXROOT"]=os.path.realpath(sys.argv[2])
here=os.path.dirname(os.path.abspath(__file__))
os.execv(os.path.expanduser("~/.local/bin/telnetlib3-server"),["telnetlib3-server","--pty-exec",os.path.join(here,"login_sh.sh"),"--force-binary","127.0.0.1",sys.argv[1]])
