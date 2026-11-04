@echo off
setlocal
echo Removing RealmeBudsPopup from Windows Startup Registry...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RealmeBudsPopup" /f

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] Application removed from startup registry!
) else (
    echo [INFO] Registry entry was not present or already removed.
)

pause
