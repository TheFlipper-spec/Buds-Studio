@echo off
chcp 65001 > nul
echo Запуск демонстрации всплывающего окна realme Buds T500 Pro...
start "" "%~dp0RealmeBudsPopup.exe" --test
