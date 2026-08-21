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

:: Compile GalaxyCamCompositor.cpp to object file
cl ^
  /c ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Fobuild\intermediate\GalaxyCamCompositor.obj ^
  src\GalaxyCamCompositor.cpp
if errorlevel 1 (
    echo Compilation of GalaxyCamCompositor.cpp failed!
    exit /b 1
)

:: Compile TextSystem.cpp to object file
cl ^
  /c ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Fobuild\intermediate\TextSystem.obj ^
  src\TextSystem.cpp
if errorlevel 1 (
    echo Compilation of TextSystem.cpp failed!
    exit /b 1
)

:: Link objects + resources into DLL
link ^
  build\intermediate\CinematicShadersNative.obj ^
  build\intermediate\StarfieldNative.obj ^
  build\intermediate\GalaxyCamCompositor.obj ^
  build\intermediate\TextSystem.obj ^
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

REM Deploy to local repo GameData
set "DEPLOY_PATH1=C:\Users\Matt\source\repos\CinematicShaders\GameData\CinematicShaders\PluginData"
if not exist "%DEPLOY_PATH1%" mkdir "%DEPLOY_PATH1%"
copy /Y "build\CinematicShadersNative.dll" "%DEPLOY_PATH1%\"
if errorlevel 1 (
    echo Deploy failed to local repo!
    exit /b 1
)
echo Deployed to: %DEPLOY_PATH1%

REM Deploy to KSP test installation
set "DEPLOY_PATH2=C:\SSDGames\KSPReleaseTest\GameData\CinematicShaders\PluginData"
if not exist "%DEPLOY_PATH2%" mkdir "%DEPLOY_PATH2%"
copy /Y "build\CinematicShadersNative.dll" "%DEPLOY_PATH2%\"
if errorlevel 1 (
    echo Deploy failed to test install!
    exit /b 1
)
echo Deployed to: %DEPLOY_PATH2%

REM Deploy to Reform test installation
set "DEPLOY_PATH3=C:\SSDGames\ReformTestInstance\GameData\CinematicShaders\PluginData"
if not exist "%DEPLOY_PATH3%" mkdir "%DEPLOY_PATH3%"
copy /Y "build\CinematicShadersNative.dll" "%DEPLOY_PATH3%\"
if errorlevel 1 (
    echo Deploy failed to Reform test install!
    exit /b 1
)
echo Deployed to: %DEPLOY_PATH3%

endlocal
