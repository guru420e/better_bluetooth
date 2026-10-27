@echo off
echo ===================================================
echo  Publishing Bluetooth Rectifier (Single-File .EXE)
echo ===================================================
echo.
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
echo.
echo Single-file executable ready at: .\publish\BluetoothRectifier.exe
echo ===================================================
pause
