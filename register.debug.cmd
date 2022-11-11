@echo off

echo Registering RTD Server
#%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe %~dp0\rtd-client\bin\Debug\RedisRTD.dll /codebase
cd C:\Users\navni\source\repos\nsharmanahellc\nme-rtd\rtd-client\bin\Debug
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe RedisRtd.dll /codebase
if errorlevel 1 pause
