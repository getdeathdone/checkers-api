@echo off
setlocal enabledelayedexpansion

:: Switch working directory immediately to the folder where this batch script lives
cd /d "%~dp0"
set "SCRIPT_DIR=%~dp0"

:: Ensure logs directory exists
if not exist "!SCRIPT_DIR!logs" mkdir "!SCRIPT_DIR!logs" 2>nul
set "DEPLOY_LOG=!SCRIPT_DIR!logs\deploy.log"

:: Default arguments
set MODE=iis
set PORT=5000
set PUBLISH_PATH=C:\inetpub\CheckersApi
set SITE_NAME=CheckersApi
set APP_POOL=CheckersApiPool
set NO_BROWSER=0
set NO_PAUSE=0

:: Parse CLI arguments
:PARSE_ARGS
if "%~1"=="" goto ARGS_DONE

if /i "%~1"=="-h" goto SHOW_HELP
if /i "%~1"=="--help" goto SHOW_HELP
if /i "%~1"=="/?" goto SHOW_HELP

if /i "%~1"=="-m" (
    set MODE=%~2
    shift
    shift
    goto PARSE_ARGS
)
if /i "%~1"=="--mode" (
    set MODE=%~2
    shift
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="-p" (
    set PORT=%~2
    shift
    shift
    goto PARSE_ARGS
)
if /i "%~1"=="--port" (
    set PORT=%~2
    shift
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="--path" (
    set PUBLISH_PATH=%~2
    shift
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="--site-name" (
    set SITE_NAME=%~2
    shift
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="--app-pool" (
    set APP_POOL=%~2
    shift
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="--no-browser" (
    set NO_BROWSER=1
    shift
    goto PARSE_ARGS
)

if /i "%~1"=="--no-pause" (
    set NO_PAUSE=1
    shift
    goto PARSE_ARGS
)
if /i "%~1"=="-y" (
    set NO_PAUSE=1
    shift
    goto PARSE_ARGS
)

:: Unknown argument
echo [ERROR] Unknown option: %~1
call :LOG "ERROR: Unknown option %~1"
echo Use 'deploy.bat --help' for usage information.
if %NO_PAUSE% equ 0 pause
exit /b 1

:ARGS_DONE
call :LOG "Starting execution: MODE=%MODE% PORT=%PORT% PATH=%PUBLISH_PATH%"

:: Execute selected mode
if /i "%MODE%"=="iis" goto EXEC_IIS
if /i "%MODE%"=="standalone" goto EXEC_STANDALONE
if /i "%MODE%"=="test" goto EXEC_TEST
if /i "%MODE%"=="tests" goto EXEC_TEST
if /i "%MODE%"=="install-bundle" goto EXEC_BUNDLE

echo [ERROR] Invalid mode: '%MODE%'. Supported modes: iis, standalone, test, install-bundle
call :LOG "ERROR: Invalid mode %MODE%"
if %NO_PAUSE% equ 0 pause
exit /b 1

:: --------------------------------------------------------
:: MODE 1: IIS Deployment
:: --------------------------------------------------------
:EXEC_IIS
echo ========================================================
echo   Deploying Checkers Web API to IIS
echo   Mode: IIS ^| Port: %PORT% ^| Path: %PUBLISH_PATH%
echo ========================================================
echo Deployment log: !DEPLOY_LOG!
echo.

:: Check Admin privileges
net session >nul 2>&1
if !errorLevel! equ 0 goto IIS_ADMIN_OK

echo [WARNING] Administrator privileges are required to configure IIS.
call :LOG "Requesting UAC elevation to Administrator..."
set ARGS_PASSTHROUGH=--mode iis --port %PORT% --path %PUBLISH_PATH% --site-name %SITE_NAME% --app-pool %APP_POOL%
if %NO_BROWSER% equ 1 set ARGS_PASSTHROUGH=!ARGS_PASSTHROUGH! --no-browser
if %NO_PAUSE% equ 1 set ARGS_PASSTHROUGH=!ARGS_PASSTHROUGH! --no-pause

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '!ARGS_PASSTHROUGH!' -Verb RunAs"
exit /b

:IIS_ADMIN_OK
:: Check if deploy-iis.ps1 exists in current directory
if not exist "deploy-iis.ps1" (
    echo [ERROR] 'deploy-iis.ps1' was not found in:
    echo   !SCRIPT_DIR!
    echo Please ensure the entire zip archive was extracted before running deploy.bat!
    call :LOG "ERROR: deploy-iis.ps1 not found in !SCRIPT_DIR!"
    if %NO_PAUSE% equ 0 pause
    exit /b 1
)

:: Auto-detect dotnet in Program Files if not currently in PATH
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles%\dotnet;!PATH!"
)
if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles(x86)%\dotnet;!PATH!"
)

where dotnet >nul 2>&1
if !errorLevel! equ 0 goto DOTNET_OK

echo [NOTICE] 'dotnet' not found in PATH.
call :LOG "'dotnet' not found in PATH. Starting auto-download..."
echo Auto-downloading .NET 8 SDK from Microsoft...
echo (Please do NOT click inside the console window to avoid pausing)
echo.
set "INSTALLER=%TEMP%\dotnet-sdk-8.0-win-x64.exe"
where curl >nul 2>&1
if !errorLevel! equ 0 (
    echo Downloading via curl with progress bar:
    curl.exe -fSL --progress-bar -o "!INSTALLER!" "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
) else (
    echo Downloading via WebClient...
    powershell -NoProfile -Command "$ProgressPreference = 'SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile('https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe', '$env:TEMP\dotnet-sdk-8.0-win-x64.exe')"
)

echo Installing .NET 8 SDK silently (please wait ~1 minute)...
start /wait "" "!INSTALLER!" /install /quiet /norestart

if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles%\dotnet;!PATH!"
)

where dotnet >nul 2>&1
if !errorLevel! equ 0 goto DOTNET_INSTALLED_OK

echo [ERROR] Could not auto-install .NET 8 SDK.
call :LOG "ERROR: Could not auto-install .NET 8 SDK"
echo Please manually install .NET 8 SDK from:
echo https://dotnet.microsoft.com/download/dotnet/8.0
if %NO_PAUSE% equ 0 pause
exit /b 1

:DOTNET_INSTALLED_OK
echo [OK] .NET 8 SDK installed successfully!
call :LOG ".NET 8 SDK installed successfully"

:DOTNET_OK
:: Prepare PowerShell arguments
set PS_ARGS=-Port %PORT% -PublishPath "%PUBLISH_PATH%" -SiteName "%SITE_NAME%" -AppPoolName "%APP_POOL%"
if %NO_BROWSER% equ 1 set PS_ARGS=%PS_ARGS% -NoBrowser

call :LOG "Invoking .\deploy-iis.ps1 %PS_ARGS%"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\deploy-iis.ps1" %PS_ARGS%
set DEPLOY_STATUS=%errorlevel%
call :LOG "deploy-iis.ps1 completed with status !DEPLOY_STATUS!"

if !DEPLOY_STATUS! equ 0 goto IIS_SUCCESS
if !DEPLOY_STATUS! equ 2 goto EXEC_STANDALONE
goto IIS_ERROR

:IIS_SUCCESS
echo.
echo ========================================================
echo   [SUCCESS] IIS deployment completed successfully!
echo ========================================================
echo Web UI:         http://localhost:%PORT%/index.html
echo Health check:   http://localhost:%PORT%/healthz
echo Deployment log: !DEPLOY_LOG!
echo App logs:       %PUBLISH_PATH%\logs
if %NO_PAUSE% equ 0 pause
exit /b 0

:IIS_ERROR
echo.
echo ========================================================
echo   [ERROR] IIS deployment encountered an error (!DEPLOY_STATUS!)
echo ========================================================
echo Detailed deployment log saved to:
echo   !DEPLOY_LOG!
if exist "%PUBLISH_PATH%\logs\deploy.log" echo   %PUBLISH_PATH%\logs\deploy.log
echo.
if %NO_PAUSE% equ 0 pause
exit /b !DEPLOY_STATUS!

:: --------------------------------------------------------
:: MODE 2: Standalone Kestrel Server
:: --------------------------------------------------------
:EXEC_STANDALONE
echo ========================================================
echo   Starting Checkers Web API in Standalone Kestrel mode
echo   Port: %PORT% (No IIS required)
echo ========================================================
echo Deployment log: !DEPLOY_LOG!
echo.

:: Auto-detect dotnet
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles%\dotnet;!PATH!"
)
if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles(x86)%\dotnet;!PATH!"
)

where dotnet >nul 2>&1
if !errorLevel! equ 0 goto STANDALONE_DOTNET_OK

echo [ERROR] 'dotnet' not found in PATH!
call :LOG "Standalone ERROR: dotnet not found"
echo Please install .NET 8 SDK from:
echo https://dotnet.microsoft.com/download/dotnet/8.0
if %NO_PAUSE% equ 0 pause
exit /b 1

:STANDALONE_DOTNET_OK
:: Check if port is already listening
netstat -ano | findstr /R ":%PORT% " >nul 2>&1
if !errorLevel! neq 0 goto PORT_OK

echo [WARNING] Port %PORT% appears to already be in use by another process.
call :LOG "Standalone WARNING: Port %PORT% is in use"
echo If IIS or another instance of CheckersApi is running, you can:
echo   1. Stop the conflicting process
echo   2. Run standalone on a different port, e.g.:
echo      deploy.bat --mode standalone --port 5001
echo.

:PORT_OK
if %NO_BROWSER% equ 0 (
    start "" cmd /c "timeout /t 3 >nul && start http://localhost:%PORT%/index.html"
)

echo Listening on http://localhost:%PORT% ...
echo Web UI:       http://localhost:%PORT%/index.html
echo Health check: http://localhost:%PORT%/healthz
echo Log files:    %SCRIPT_DIR%logs\app-*.log
echo Press Ctrl+C to stop the server.
echo.

call :LOG "Starting dotnet run on port %PORT%"
dotnet run --project "%SCRIPT_DIR%src\CheckersApi.Web\CheckersApi.Web.csproj" -c Release --urls "http://0.0.0.0:%PORT%"
set RUN_STATUS=%errorlevel%
call :LOG "dotnet run finished with exit code !RUN_STATUS!"

if !RUN_STATUS! equ 0 goto STANDALONE_DONE

echo.
echo [ERROR] Server exited with status code: !RUN_STATUS!
echo Detailed logs saved to:
echo   !DEPLOY_LOG!
echo   %SCRIPT_DIR%logs\app-*.log

:STANDALONE_DONE
if %NO_PAUSE% equ 0 pause
exit /b !RUN_STATUS!

:: --------------------------------------------------------
:: MODE 3: Run Unit Tests
:: --------------------------------------------------------
:EXEC_TEST
echo ========================================================
echo   Running 19 xUnit Unit Tests
echo ========================================================
echo.

if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles%\dotnet;!PATH!"
)
if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "PATH=%ProgramFiles(x86)%\dotnet;!PATH!"
)

call :LOG "Running dotnet test"
dotnet test "%SCRIPT_DIR%CheckersApi.sln" -c Release --nologo
set TEST_STATUS=%errorlevel%
call :LOG "dotnet test finished with status !TEST_STATUS!"

if !TEST_STATUS! equ 0 (
    echo [SUCCESS] All unit tests passed!
) else (
    echo [ERROR] Tests failed with status !TEST_STATUS!.
    echo Check logs in: !DEPLOY_LOG!
)

if %NO_PAUSE% equ 0 pause
exit /b !TEST_STATUS!

:: --------------------------------------------------------
:: MODE 4: Install Hosting Bundle
:: --------------------------------------------------------
:EXEC_BUNDLE
echo ========================================================
echo   Installing .NET 8 Hosting Bundle for IIS
echo   (Fixes IIS Error 500.19 / 0x8007000d)
echo ========================================================
echo.

net session >nul 2>&1
if !errorLevel! equ 0 goto BUNDLE_IS_ADMIN

echo Elevating privileges to Administrator...
call :LOG "Elevating for hosting bundle install..."
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--mode install-bundle' -Verb RunAs"
exit /b

:BUNDLE_IS_ADMIN
echo 1. Downloading .NET 8 Hosting Bundle from Microsoft...
call :LOG "Downloading Hosting Bundle..."
set "INSTALLER=%TEMP%\dotnet-hosting-8.0-win.exe"

where curl >nul 2>&1
if !errorLevel! equ 0 (
    curl.exe -fSL --progress-bar -o "!INSTALLER!" "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe"
) else (
    powershell -NoProfile -Command "$ProgressPreference = 'SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile('https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe', '$env:TEMP\dotnet-hosting-8.0-win.exe')"
)

if exist "!INSTALLER!" goto BUNDLE_DOWNLOADED_OK

echo [ERROR] Failed to download installer.
call :LOG "ERROR: Failed to download Hosting Bundle"
echo Please manually download and install from:
echo https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe
if %NO_PAUSE% equ 0 pause
exit /b 1

:BUNDLE_DOWNLOADED_OK
echo 2. Installing .NET 8 Hosting Bundle (this takes ~30 seconds)...
call :LOG "Running Hosting Bundle installer..."
start /wait "" "!INSTALLER!" /install /quiet /norestart

echo 3. Restarting IIS...
call :LOG "Restarting IIS (iisreset)..."
iisreset

echo.
echo ========================================================
echo   Hosting Bundle installed successfully!
echo ========================================================
call :LOG "Hosting Bundle installed successfully"
if %NO_PAUSE% equ 0 pause
exit /b 0

:: --------------------------------------------------------
:: HELP MESSAGE
:: --------------------------------------------------------
:SHOW_HELP
echo Checkers REST Web API - Command Line Deployment Tool
echo.
echo Usage:
echo   deploy.bat [options]
echo.
echo Options:
echo   -m, --mode ^<mode^>        Deployment mode: iis, standalone, test, install-bundle
echo                            (Default: iis)
echo   -p, --port ^<number^>      HTTP port to bind (Default: 5000)
echo   --path ^<dir^>             Publish directory for IIS (Default: C:\inetpub\CheckersApi)
echo   --site-name ^<name^>       IIS Website name (Default: CheckersApi)
echo   --app-pool ^<name^>        IIS AppPool name (Default: CheckersApiPool)
echo   --no-browser             Do not auto-open browser after deployment
echo   -y, --no-pause           Do not wait for keypress at exit (for CI/CD and scripts)
echo   -h, --help               Show this help message
echo.
echo Examples:
echo   deploy.bat                                    Deploy to IIS on port 5000 (Default)
echo   deploy.bat --mode standalone                  Run without IIS on port 5000
echo   deploy.bat --mode standalone --port 8080      Run standalone on port 8080
echo   deploy.bat --mode iis --port 80 --no-pause    Unattended IIS deploy on port 80
echo   deploy.bat --mode test                        Run all 19 unit tests
echo   deploy.bat --mode install-bundle              Install .NET 8 Hosting Bundle for IIS
echo.
if %NO_PAUSE% equ 0 pause
exit /b 0

:: --------------------------------------------------------
:: LOG HELPER SUBROUTINE
:: --------------------------------------------------------
:LOG
if not exist "!SCRIPT_DIR!logs" mkdir "!SCRIPT_DIR!logs" 2>nul
echo [%DATE% %TIME%] [deploy.bat] %~1 >> "!DEPLOY_LOG!" 2>nul
goto :eof
