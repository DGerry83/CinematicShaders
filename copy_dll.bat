@echo off
REM ==============================================
REM DLL Copy Script - For Post-Build Events
REM ==============================================
REM Reads target folder from dll_copy_config.txt
REM Config file should contain: TARGET_FOLDER=YourPathHere

setlocal

REM Get the directory where this batch file is located
set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%dll_copy_config.txt"

REM Check if config file exists
if not exist "%CONFIG_FILE%" (
    echo ERROR: Configuration file not found.
    echo.
    echo Please create: dll_copy_config.txt
    echo With this line: TARGET_FOLDER=C:\Your\Path\Here
    echo.
    pause
    exit /b 1
)

REM Read target folder from config file
set "TARGET_FOLDER="
for /f "usebackq tokens=1,* delims==" %%A in ("%CONFIG_FILE%") do (
    if /i "%%A"=="TARGET_FOLDER" set "TARGET_FOLDER=%%B"
)

REM Check if target folder was found in config
if not defined TARGET_FOLDER (
    echo ERROR: TARGET_FOLDER not found in config file.
    echo Please add this line to %CONFIG_FILE%:
    echo TARGET_FOLDER=C:\Your\Path\Here
    echo.
    pause
    exit /b 1
)

REM Check if source file was provided
if "%~1"=="" (
    echo ERROR: No source DLL provided.
    echo Usage: %~nx0 "C:\Path\To\Your.dll"
    echo.
    pause
    exit /b 1
)

REM Create target folder if it doesn't exist
if not exist "%TARGET_FOLDER%" (
    echo Creating folder: %TARGET_FOLDER%
    mkdir "%TARGET_FOLDER%"
    if errorlevel 1 (
        echo ERROR: Could not create target folder.
        echo.
        pause
        exit /b 1
    )
)

REM Extract filename from source path
for %%I in ("%~1") do set "FILENAME=%%~nxI"

echo Copying %FILENAME%...
echo From: %~1
echo To: %TARGET_FOLDER%
echo.

copy /Y "%~1" "%TARGET_FOLDER%\%FILENAME%"

if errorlevel 1 (
    echo ERROR: Failed to copy DLL.
    echo.
    pause
    exit /b 1
) else (
    echo Successfully copied %FILENAME%
)

endlocal