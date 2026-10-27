@echo off
echo ===================================================
echo  Publishing Better Bluetooth (Single-File .EXE)
echo ===================================================
echo.
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
echo.
echo Single-file executable ready at: .\publish\BetterBluetooth.exe
echo ===================================================
pause
