@echo off
REM cs.cmd — Windows shim so `cs ...` runs the Python tool. Put this dir (or a
REM bin dir containing a copy) on %PATH%. Requires python + git + fzf on PATH.
setlocal
set "CS_HOME=%~dp0"
python "%~dp0cs" %*
