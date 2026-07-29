@echo off
REM ==============================================
REM Asset Deployment Script
REM ==============================================
REM Deploys mod assets (navball icons, fonts, catalogs, sounds)
REM to both local packaging GameData and test installation.

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"

REM Define mod root destinations
set "DEST1=%SCRIPT_DIR%GameData\CinematicShaders"
set "DEST2=C:\SSDGames\KSPReleaseTest\GameData\CinematicShaders"
set "DEST3=C:\SSDGames\ReformTestInstance\GameData\CinematicShaders"

set "DEPLOY_COUNT=0"

echo ============================================
echo CinematicShaders Asset Deployment
echo ============================================
echo.

for %%D in ("%DEST1%" "%DEST2%" "%DEST3%") do (
    set /a DEPLOY_COUNT+=1
    echo --- Deploying to: %%~D ---
    
    REM Navball Icons (source of truth is repo GameData)
    if not exist "%%~D\PluginData\NavballIcons" mkdir "%%~D\PluginData\NavballIcons"
    copy /Y "%SCRIPT_DIR%GameData\CinematicShaders\PluginData\NavballIcons\*.png" "%%~D\PluginData\NavballIcons\" >nul
    if errorlevel 1 (
        echo   WARNING: Failed to copy navball icons.
    ) else (
        echo   Navball icons copied.
    )
    
    REM Font
    if not exist "%%~D\PluginData\Fonts" mkdir "%%~D\PluginData\Fonts"
    copy /Y "%SCRIPT_DIR%Fonts\AcPlus_Rainbow100_re_66.ttf" "%%~D\PluginData\Fonts\" >nul
    if errorlevel 1 (
        echo   WARNING: Failed to copy font.
    ) else (
        echo   Font copied.
    )
    
    REM Star Catalogs
    if not exist "%%~D\PluginData\StarCatalogs" mkdir "%%~D\PluginData\StarCatalogs"
    
    for %%F in ("%SCRIPT_DIR%HipparcosData\hyg_v42*.bin" "%SCRIPT_DIR%HipparcosData\hyg_v42*.json") do (
        set "FNAME=%%~nxF"
        set "SKIP=0"
        if not "!FNAME:guides=!"=="!FNAME!" set "SKIP=1"
        if not "!FNAME:polaris=!"=="!FNAME!" set "SKIP=1"
        if not "!FNAME:debug=!"=="!FNAME!" set "SKIP=1"
        if !SKIP! equ 0 (
            copy /Y "%%~F" "%%~D\PluginData\StarCatalogs\" >nul
        )
    )
    echo   Star catalogs copied.
    
    REM Sounds
    if not exist "%%~D\Sounds" mkdir "%%~D\Sounds"
    copy /Y "%SCRIPT_DIR%Sounds\*.ogg" "%%~D\Sounds\" >nul
    if errorlevel 1 (
        echo   WARNING: Failed to copy sounds.
    ) else (
        echo   Sounds copied.
    )
    
    echo.
)

echo ============================================
echo Asset deployment complete for !DEPLOY_COUNT! locations.
echo ============================================

endlocal
pause
