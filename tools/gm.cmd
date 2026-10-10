@echo off
rem `gm` from PowerShell or cmd - see tools/gm.py. Goes through tools/gm so platform.sh picks the Python that runs.
sh "%~dp0gm" %*
