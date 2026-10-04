@echo off
rem usage: run.cmd <experiment> [seeds] [maxDays]   (results: out\<experiment>.txt)
cd /d "%~dp0"
dotnet run -c Release -- %* > "out\%1.console.txt" 2>&1
