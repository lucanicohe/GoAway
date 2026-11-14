@echo off

if exist "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" set unityPath="C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe"
if exist "D:\Program Files\Unity\Editor\6000.3.8f1\Editor\Unity.exe" set unityPath="D:\Program Files\Unity\Editor\6000.3.8f1\Editor\Unity.exe"


start "" %unityPath% -projectPath %cd%