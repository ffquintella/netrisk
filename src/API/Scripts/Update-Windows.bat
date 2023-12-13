@echo off
set pid=%1
set app=%2

taskkill /IM %pid% /F 
taskkill /IM GUIClient.exe /F 

timeout /t 5 /nobreak

start "installer" "%app%"