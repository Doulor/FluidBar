@echo off
rem FluidBar agent-emit wrapper (Windows PowerShell 5.1 compatible).
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0agent-emit.ps1" %*
exit /b %errorlevel%
