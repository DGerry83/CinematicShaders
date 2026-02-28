@echo off
setlocal enabledelayedexpansion

echo ============================================
echo Compiling GTAO_Output.hlsl
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

REM Check if fxc is in PATH
where fxc >nul 2>&1
if %errorlevel% == 0 (
    set "FXC=fxc"
    goto :found
)

echo ERROR: Could not find fxc.exe
exit /b 1

:found
echo Found fxc.exe at:
echo   %FXC%
echo.

REM Compile vertex shader
"%FXC%" /T vs_5_0 /E VSMain /Fh "..\include\GTAO_Output_VS.h" /Vn "g_GTAOOutputVS" "GTAO_Output.hlsl"
if %errorlevel% neq 0 (
    echo ERROR: Vertex shader compilation failed!
    exit /b %errorlevel%
)

REM Compile pixel shader
"%FXC%" /T ps_5_0 /E PSMain /Fh "..\include\GTAO_Output_PS.h" /Vn "g_GTAOOutputPS" "GTAO_Output.hlsl"
if %errorlevel% neq 0 (
    echo ERROR: Pixel shader compilation failed!
    exit /b %errorlevel%
)

echo.
echo ============================================
echo SUCCESS: Output shaders compiled to:
echo   ..\include\GTAO_Output_VS.h
echo   ..\include\GTAO_Output_PS.h
echo ============================================

endlocal
