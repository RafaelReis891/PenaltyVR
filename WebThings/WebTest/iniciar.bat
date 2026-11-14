
@echo off
title Football VR - Painel
color 0B

cd /d "%~dp0"

echo.
echo ==========================================
echo       FOOTBALL VR - PAINEL
echo ==========================================
echo.

:: Verifica se Node.js esta instalado
where node >nul 2>nul

if errorlevel 1 (
    echo ERRO: Node.js nao encontrado!
    echo.
    echo Instale o Node.js para executar o servidor.
    echo https://nodejs.org/
    echo.
    pause
    exit /b 1
)

:: Verifica se o servidor existe
if not exist "server.js" (
    echo ERRO: server.js nao encontrado!
    echo.
    pause
    exit /b 1
)

echo Iniciando servidor Football VR...
echo.

:: Inicia o servidor em uma nova janela
start "Football VR Server" cmd /k "cd /d ""%~dp0"" && node server.js"

:: Aguarda o servidor iniciar
timeout /t 2 /nobreak >nul

:: Descobre o IP local
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "$c = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1; if ($c) { $c.IPv4Address[0].IPAddress }"`) do set "LAN_IP=%%I"

if "%LAN_IP%"=="" (
    set "LAN_IP=127.0.0.1"
)

echo.
echo ==========================================
echo SERVIDOR INICIADO
echo ==========================================
echo.
echo Computador:
echo http://localhost:5500/
echo.
echo Rede local:
echo http://%LAN_IP%:5500/
echo.
echo Abra o QR Code no painel para conectar o celular.
echo.

start "" "http://%LAN_IP%:5500/"

echo.
echo Esta janela pode ser fechada.
echo A janela do servidor deve permanecer aberta.
echo.
pause
