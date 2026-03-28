@echo off
setlocal

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

:: Compile vertex shader
fxc /T vs_5_0 /E Main /Fh "..\include\KartographerVS.h" /Vn "g_KartographerVS" "KartographerVS.hlsl"
if errorlevel 1 (
    echo Vertex shader compilation failed!
    exit /b 1
)

:: Compile pixel shader
fxc /T ps_5_0 /E PSMain /Fh "..\include\KartographerPS.h" /Vn "g_KartographerPS" "KartographerPS.hlsl"
if errorlevel 1 (
    echo Pixel shader compilation failed!
    exit /b 1
)

echo Kartographer shaders compiled successfully!

endlocal
