# Independent FTP server: pyftpdlib (third party), used only to check interoperability.
import sys, os
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler
from pyftpdlib.servers import FTPServer
a=DummyAuthorizer(); a.add_user("admin","pw",os.path.realpath(sys.argv[2]),perm="elradfmwMT")
H=FTPHandler; H.authorizer=a; H.passive_ports=range(32140,32180)
FTPServer(("127.0.0.1",int(sys.argv[1])),H).serve_forever()
