@echo off
rem Double-click to build and start Open Corneroids. Arguments are passed to the game, for example:
rem   Play.cmd --creative --seed=42
rem Godot .NET is looked up in OPEN_CORNEROIDS_GODOT (full path to the exe), then in a Godot_v*_mono_win64
rem folder in this repository. The .NET SDK is the repository's .dotnet folder if present, otherwise dotnet on PATH.
setlocal
cd /d "%~dp0"

set "GODOT=%OPEN_CORNEROIDS_GODOT%"
if not defined GODOT (
    for /d %%D in ("Godot_v*_mono_win64") do (
        for %%F in ("%%D\Godot_v*_mono_win64.exe") do set "GODOT=%%~fF"
    )
)
if not defined GODOT goto :no_godot
if not exist "%GODOT%" goto :no_godot

set "DOTNET=dotnet"
if exist ".dotnet\dotnet.exe" set "DOTNET=%~dp0.dotnet\dotnet.exe"

echo Building the game...
"%DOTNET%" build "game\OpenCorneroids.csproj" -nologo -v quiet -clp:ErrorsOnly
if errorlevel 1 (
    echo.
    echo The build failed; see the errors above. Is the .NET SDK installed?
    pause
    exit /b 1
)

start "" "%GODOT%" --path game -- %*
exit /b 0

:no_godot
echo Godot .NET 4.7.2 was not found.
echo Put the Godot_v4.7.2-stable_mono_win64 folder in this repository, or set OPEN_CORNEROIDS_GODOT
echo to the full path of Godot_v4.7.2-stable_mono_win64.exe.
pause
exit /b 1
