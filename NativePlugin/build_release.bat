@echo off
setlocal

if not exist build mkdir build
if not exist build\intermediate mkdir build\intermediate

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

:: Compile resource file (version info)
rc.exe /fo build\intermediate\CinematicShadersNative.res src\CinematicShadersNative.rc
if errorlevel 1 (
    echo Resource compilation failed!
    exit /b 1
)

:: Compile CinematicShadersNative.cpp to object file
cl ^
  /c ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Fobuild\intermediate\CinematicShadersNative.obj ^
  src\CinematicShadersNative.cpp
if errorlevel 1 (
    echo Compilation of CinematicShadersNative.cpp failed!
    exit /b 1
)

:: Compile StarfieldNative.cpp to object file
cl ^
  /c ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Fobuild\intermediate\StarfieldNative.obj ^
  src\StarfieldNative.cpp
if errorlevel 1 (
    echo Compilation of StarfieldNative.cpp failed!
    exit /b 1
)

:: Link objects + resources into DLL
link ^
  build\intermediate\CinematicShadersNative.obj ^
  build\intermediate\StarfieldNative.obj ^
  build\intermediate\CinematicShadersNative.res ^
  d3d11.lib dxgi.lib ole32.lib ^
  /DLL ^
  /OUT:build\CinematicShadersNative.dll ^
  /IMPLIB:build\intermediate\CinematicShadersNative.lib
if errorlevel 1 (
    echo Link failed!
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