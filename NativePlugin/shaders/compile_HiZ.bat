@echo off
setlocal enabledelayedexpansion

echo ============================================
echo Compiling HiZ.hlsl
echo ============================================

REM Try to find fxc.exe in common Windows SDK locations
set "FXC="

REM Check Windows 10/11 SDK paths (newest first)
for %%v in (10.0.22621.0 10.0.22000.0 10.0.20348.0 10.0.19041.0 10.0.18362.0 10.0.17763.0 10.0.17134.0 10.0.16299.0 10.0.15063.0 10.0.14393.0 10.0.10586.0 10.0.10240.0) do (
    if exist "C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\fxc.exe" (
        set "FXC=C:\Program Files (x86)\Windows Kits\10\bin\%%v\x64\fxc.exe"
        goto :found
    )
)

REM Check x86 variant
for %%v in (10.0.22621.0 10.0.22000.0 10.0.20348.0 10.0.19041.0 10.0.18362.0 10.0.17763.0 10.0.17134.0 10.0.16299.0 10.0.15063.0 10.0.14393.0 10.0.10586.0 10.0.10240.0) do (
    if exist "C:\Program Files (x86)\Windows Kits\10\bin\%%v\x86\fxc.exe" (
        set "FXC=C:\Program Files (x86)\Windows Kits\10\bin\%%v\x86\fxc.exe"
        goto :found
    )
)

REM Check Windows 8.1 SDK
if exist "C:\Program Files (x86)\Windows Kits\8.1\bin\x64\fxc.exe" (
    set "FXC=C:\Program Files (x86)\Windows Kits\8.1\bin\x64\fxc.exe"
    goto :found
)

if exist "C:\Program Files (x86)\Windows Kits\8.1\bin\x86\fxc.exe" (
    set "FXC=C:\Program Files (x86)\Windows Kits\8.1\bin\x86\fxc.exe"
    goto :found
)

REM Check if fxc is in PATH
where fxc >nul 2>&1
if %errorlevel% == 0 (
    set "FXC=fxc"
    goto :found
)

echo ERROR: Could not find fxc.exe
echo.
echo Please install the Windows SDK or ensure fxc.exe is in your PATH.
echo Common locations:
echo   - C:\Program Files (x86)\Windows Kits\10\bin\[version]\x64\fxc.exe
echo   - C:\Program Files (x86)\Windows Kits\8.1\bin\x64\fxc.exe
echo.
exit /b 1

:found
echo Found fxc.exe at:
echo   %FXC%
echo.

REM Compile the shader
"%FXC%" /T cs_5_0 /E CSMain /Fh "..\include\HiZ.h" /Vn "g_HiZCS" "HiZ.hlsl"

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Shader compilation failed!
    exit /b %errorlevel%
)

echo.
echo ============================================
echo SUCCESS: Shader compiled to:
echo   ..\include\HiZ.h
echo ============================================
