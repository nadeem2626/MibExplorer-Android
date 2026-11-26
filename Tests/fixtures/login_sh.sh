#!/bin/sh
# Prompts like an embedded login, then starts an interactive shell on the pty (echo ON).
# The pty is raw in this server, so CR/LF are stripped and empty lines skipped here.
rd() { v=""; while [ -z "$v" ]; do read v; v=$(printf %s "$v" | tr -d '\r'); done; }
printf 'login: '; rd; u=$v; printf 'Password: '; stty -echo; rd; p=$v; stty echo; echo
if [ "$u" = root ] && [ "$p" = secret ]; then cd "$MXROOT"; exec /bin/sh -i; else echo 'Login incorrect'; exit 1; fi
