@echo off
rem SafeEye 실행 파일: 앱이 아직 빌드되지 않았으면 먼저 빌드한다.
set "APP=%~dp0app\SafeEye\bin\Release\net10.0-windows\SafeEye.exe"
if not exist "%APP%" (
    echo 처음 실행이라 앱을 빌드합니다. 잠시만 기다려 주세요...
    dotnet build -c Release "%~dp0app\SafeEye"
    if errorlevel 1 (
        echo 빌드에 실패했습니다. .NET 10 SDK 가 설치되어 있는지 확인하세요.
        pause
        exit /b 1
    )
)
start "" "%APP%"