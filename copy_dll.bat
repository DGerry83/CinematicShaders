@echo off
REM ==============================================
REM DLL Copy Script - For Post-Build Events
REM ==============================================
REM Reads target folders from dll_copy_config.txt
REM Config file can contain multiple lines: TARGET_FOLDER=YourPathHere

setlocal enabledelayedexpansion

REM Get the directory where this batch file is located
set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%dll_copy_config.txt"

REM Check if config file exists
if not exist "%CONFIG_FILE%" (
    echo ERROR: Configuration file not found.
    echo.
    echo Please create: dll_copy_config.txt
    echo With lines like: TARGET_FOLDER=C:\Your\Path\Here
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

set "FOLDER_COUNT=0"

REM Read target folders from config file and copy to each
for /f "usebackq tokens=1,* delims==" %%A in ("%CONFIG_FILE%") do (
    if /i "%%A"=="TARGET_FOLDER" (
        set /a FOLDER_COUNT+=1
        
        REM Create target folder if it doesn't exist
        if not exist "%%B" (
            echo Creating folder: %%B
            mkdir "%%B"
            if errorlevel 1 (
                echo ERROR: Could not create target folder: %%B
                echo.
                pause
                exit /b 1
            )
        )
        
        echo Copying %~nx1...
        echo From: %~1
        echo To: %%B
        echo.
        
        copy /Y "%~1" "%%B\%~nx1"
        
        if errorlevel 1 (
            echo ERROR: Failed to copy DLL to %%B
            echo.
            pause
            exit /b 1
        ) else (
            echo Successfully copied %~nx1 to %%B
            echo.
        )
    )
)

if !FOLDER_COUNT! equ 0 (
    echo ERROR: No TARGET_FOLDER entries found in config file.
    echo Please add lines to %CONFIG_FILE%:
    echo TARGET_FOLDER=C:\Your\Path\Here
    echo.
    pause
    exit /b 1
)

echo.
echo Copied DLL to !FOLDER_COUNT! location(s).
echo.

endlocal
