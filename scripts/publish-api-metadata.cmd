@echo off
rem Double-clickable wrapper for publish-api-metadata.sh.
rem
rem Windows has several bash.exe on PATH - the one under System32 is WSL, where dotnet and
rem docfx do not exist, so the script would fail instantly and the window would vanish.
rem This picks Git Bash explicitly and keeps the window open so you can read the output.

setlocal

set "GITBASH="
if exist "%ProgramFiles%\Git\bin\bash.exe" set "GITBASH=%ProgramFiles%\Git\bin\bash.exe"
if not defined GITBASH if exist "%ProgramFiles(x86)%\Git\bin\bash.exe" set "GITBASH=%ProgramFiles(x86)%\Git\bin\bash.exe"
if not defined GITBASH if exist "%LocalAppData%\Programs\Git\bin\bash.exe" set "GITBASH=%LocalAppData%\Programs\Git\bin\bash.exe"

if not defined GITBASH (
  echo Could not find Git Bash. Install Git for Windows, or run the script yourself:
  echo     bash scripts/publish-api-metadata.sh
  echo.
  pause
  exit /b 1
)

"%GITBASH%" "%~dp0publish-api-metadata.sh" %*
set "RESULT=%ERRORLEVEL%"

echo.
echo ---- finished (exit code %RESULT%) ----
pause
exit /b %RESULT%
