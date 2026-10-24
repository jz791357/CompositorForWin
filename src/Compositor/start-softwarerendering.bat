@echo off
set COMPOSITOR_SOFTWARE_RENDER=1
Compositor.exe
if errorlevel 1 pause
