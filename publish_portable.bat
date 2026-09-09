@echo off
REM ============================================================
REM  Portable build — .NET runtime မလိုဘဲ ဘယ် PC မှာမဆို run လို့ရတဲ့ folder
REM  (Loaders / edl / mtkclient အကုန် ပါပါတယ်)
REM  Result: Publish\win-x64\WinFormsApp1.exe
REM ============================================================
cd /d "%~dp0"
dotnet publish WinFormsApp1\WinFormsApp1.csproj -c Release -r win-x64 --self-contained true -o Publish\win-x64
echo.
echo Done: Publish\win-x64\WinFormsApp1.exe
pause
