@echo off
setlocal

if not exist build mkdir build
if not exist build\intermediate mkdir build\intermediate

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

cl ^
  /LD ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Fobuild\intermediate\ ^
  src\CinematicShadersNative.cpp ^
  /link ^
  d3d11.lib dxgi.lib ole32.lib ^
  /OUT:build\CinematicShadersNative.dll ^
  /IMPLIB:build\intermediate\CinematicShadersNative.lib

if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

echo Release build successful: build\CinematicShadersNative.dll

REM Deploy to KSP test installation
set DEPLOY_PATH=C:\SSDGames\KSPReleaseTest\GameData\CinematicShaders\PluginData
if not exist "%DEPLOY_PATH%" mkdir "%DEPLOY_PATH%"
copy /Y "build\CinematicShadersNative.dll" "%DEPLOY_PATH%\"
if errorlevel 1 (
    echo Deploy failed!
    exit /b 1
)
echo Deployed to: %DEPLOY_PATH%

endlocal