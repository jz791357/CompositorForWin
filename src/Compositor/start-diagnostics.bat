@echo off
echo Starting Compositor with console output...
echo Log file: %%LOCALAPPDATA%%\Compositor\startup.log
echo.
Compositor.exe
echo.
echo Exit code: %ERRORLEVEL%
pause
