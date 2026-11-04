@echo off
setlocal
set "EXE_PATH=%~dp0RealmeBudsPopup.exe"

if not exist "%EXE_PATH%" (
    echo Error: RealmeBudsPopup.exe not found!
    pause
    exit /b 1
)

echo Adding RealmeBudsPopup to Windows Startup Registry...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "RealmeBudsPopup" /t REG_SZ /d "\"%EXE_PATH%\"" /f

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] Application added to startup registry!
) else (
    echo [FAIL] Could not write to registry.
)

pause
