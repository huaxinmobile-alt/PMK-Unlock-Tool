@echo off
REM ============================================================
REM  PMK Unlock Tool — Release builder
REM  တစ်ဆင့်ချင်း: build → zip → latest.json → (option) GitHub release
REM
REM  Usage:
REM    release.bat                    -> zip + manifest ပဲ လုပ်
REM    release.bat --notes "..."      -> zip + manifest
REM    release.bat --git              -> zip + manifest + commit/push
REM    release.bat --release          -> အကုန် + GitHub release vX upload (public!)
REM ============================================================
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python မတွေ့ဘူး — python.org ကနေ install ပြီး "Add to PATH" စစ်ပါ
    pause
    exit /b 1
)

echo [1/2] Building portable package (dotnet publish)...
dotnet publish WinFormsApp1\WinFormsApp1.csproj -c Release -r win-x64 --self-contained true -o Publish\win-x64
if errorlevel 1 (
    echo [ERROR] Build မအောင်ဘူး
    pause
    exit /b 1
)

echo [2/2] Packaging zip + manifest...
python tools\make_release.py %*

echo.
echo Done. Releases\ folder ထဲက zip ကို ဖြန့်လို့ရပြီ။
pause
