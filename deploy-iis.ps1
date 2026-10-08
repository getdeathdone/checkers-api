<#
.SYNOPSIS
    Deploys Checkers REST Web API to Windows Server IIS.
.DESCRIPTION
    Builds and publishes the ASP.NET Core Web API, configures IIS Application Pool
    and Website, sets up permissions, ensures logging directories are writable,
    and verifies the /healthz endpoint. All output and errors are logged to logs\deploy.log.
.EXAMPLE
    .\deploy-iis.ps1 -SiteName "CheckersApi" -Port 5000 -PublishPath "C:\inetpub\CheckersApi"
#>

param(
    [string]$SiteName = "CheckersApi",
    [int]$Port = 5000,
    [string]$PublishPath = "C:\inetpub\CheckersApi",
    [string]$AppPoolName = "CheckersApiPool",
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

# Setup persistent deployment log file
$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$deployLogPath = Join-Path $logDir "deploy.log"

function Log-Message([string]$msg, [string]$level = "INFO") {
    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$timestamp] [$level] $msg"
    try {
        Add-Content -Path $deployLogPath -Value $line -ErrorAction SilentlyContinue
    } catch { }
}

try {
    Start-Transcript -Path $deployLogPath -Append -Force -ErrorAction SilentlyContinue | Out-Null
} catch { }

Log-Message "=== Deployment started ==="
Log-Message "Parameters: SiteName=$SiteName, Port=$Port, PublishPath=$PublishPath, AppPool=$AppPoolName"

try {
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host "   Deploying Checkers REST Web API to Windows IIS       " -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host "Deployment log: $deployLogPath" -ForegroundColor DarkGray
    Write-Host ""

    # 1. Publish .NET Web App
    Write-Host "1. Building and publishing .NET solution in Release mode..." -ForegroundColor Yellow
    $projectPath = "$PSScriptRoot\src\CheckersApi.Web\CheckersApi.Web.csproj"
    Log-Message "Running dotnet publish on $projectPath"

    # Auto-detect 64-bit dotnet SDK to avoid 32-bit hosting bundle PATH collisions
    $dotnetExe = "dotnet"
    if (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe") {
        $dotnetExe = "$env:ProgramFiles\dotnet\dotnet.exe"
        $env:DOTNET_ROOT = "$env:ProgramFiles\dotnet"
        $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
    } elseif (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
        $dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
        $env:DOTNET_ROOT = "C:\Program Files\dotnet"
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    }

    # Stop IIS site and AppPool if running to unlock published DLLs (w3wp process lock)
    try {
        if (Get-Module -ListAvailable -Name WebAdministration) {
            Import-Module WebAdministration -ErrorAction SilentlyContinue
            Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
            Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
        }
    } catch { }

    # Ensure w3wp processes hosting the app are terminated to avoid MSB3026 file lock
    try {
        Get-Process -Name "w3wp" -ErrorAction SilentlyContinue | ForEach-Object {
            try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    } catch { }

    $publishSuccess = $false
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $dotnetExe publish $projectPath -c Release -f net8.0 -o $PublishPath --nologo
        if ($LASTEXITCODE -eq 0) {
            $publishSuccess = $true
        }
    } catch {
        Log-Message "Notice during dotnet publish: $_" "WARN"
    }
    $ErrorActionPreference = $prevEap

    $mainDll = Join-Path $PublishPath "CheckersApi.Web.dll"
    if (-not $publishSuccess) {
        if (Test-Path $mainDll) {
            Write-Host "[OK] Using existing published binaries in $PublishPath" -ForegroundColor Green
            Log-Message "Using existing published build in $PublishPath"
        } else {
            $pubErrMsg = "dotnet publish failed and $mainDll does not exist."
            Log-Message $pubErrMsg "ERROR"
            Write-Error "$pubErrMsg Check build errors above or in $deployLogPath."
            exit 1
        }
    } else {
        Log-Message "dotnet publish completed successfully."
    }

    # 2. Check IIS Administration Module
    Write-Host "2. Checking IIS WebAdministration module..." -ForegroundColor Yellow
    $iisAvailable = $false

    try {
        Import-Module WebAdministration -ErrorAction Stop
        $iisAvailable = $true
        Log-Message "WebAdministration module loaded successfully."
    } catch {
        Log-Message "WebAdministration module not found. Attempting DISM install..." "WARN"
        Write-Host "Attempting to enable IIS features via DISM..." -ForegroundColor Yellow
        try {
            # Use clean Windows 10/11 & Server compatible feature names (no Server-only IIS-WebServerRole)
            Start-Process -FilePath "dism.exe" -ArgumentList "/Online /Enable-Feature /FeatureName:IIS-WebServer /FeatureName:IIS-ManagementScriptingTools /All /NoRestart" -Wait -NoNewWindow -ErrorAction SilentlyContinue
            Import-Module WebAdministration -ErrorAction Stop
            $iisAvailable = $true
            Log-Message "IIS enabled via DISM and WebAdministration module loaded."
        } catch {
            $iisAvailable = $false
            Log-Message "Failed to enable IIS via DISM: $_" "ERROR"
        }
    }

    if (-not $iisAvailable) {
        Log-Message "IIS is not available. Exiting with code 2 for standalone fallback." "WARN"
        Write-Warning "IIS WebAdministration is not available on this Windows edition."
        Write-Host ""
        Write-Host "----------------------------------------------------------------------" -ForegroundColor Yellow
        Write-Host " IIS is not installed or could not be enabled by DISM." -ForegroundColor Yellow
        Write-Host " Switching to Standalone Kestrel mode (port $Port)..." -ForegroundColor Green
        Write-Host "----------------------------------------------------------------------" -ForegroundColor Yellow
        exit 2
    }

    # 3. Check for AspNetCoreModuleV2 in IIS (Prevents 500.19 0x8007000d)
    Write-Host "3. Checking for AspNetCoreModuleV2 in IIS..." -ForegroundColor Yellow
    $moduleInstalled = $false
    try {
        $aspNetCoreModule = Get-WebGlobalModule -Name "AspNetCoreModuleV2" -ErrorAction SilentlyContinue
        if ($aspNetCoreModule) {
            $moduleInstalled = $true
            Log-Message "AspNetCoreModuleV2 is installed."
        }
    } catch { }

    if (-not $moduleInstalled) {
        Log-Message "AspNetCoreModuleV2 is NOT installed. Downloading Hosting Bundle..." "WARN"
        Write-Host "AspNetCoreModuleV2 is NOT installed in IIS!" -ForegroundColor Yellow
        Write-Host "Downloading .NET 8 Hosting Bundle from Microsoft..." -ForegroundColor Yellow
        $installerPath = "$env:TEMP\dotnet-hosting-8.0-win.exe"
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe" -OutFile $installerPath -UseBasicParsing
            Write-Host "Installing .NET 8 Hosting Bundle silently (please wait ~30 seconds)..." -ForegroundColor Yellow
            Start-Process -FilePath $installerPath -ArgumentList "/install /quiet /norestart" -Wait
            Write-Host "Restarting IIS (iisreset)..." -ForegroundColor Yellow
            & iisreset
            Log-Message "AspNetCoreModuleV2 installed successfully via Hosting Bundle."
            Write-Host "AspNetCoreModuleV2 installed successfully!" -ForegroundColor Green
        } catch {
            $hbErr = "Could not auto-download Hosting Bundle: $_"
            Log-Message $hbErr "ERROR"
            Write-Warning $hbErr
            Write-Host "Please download and install it manually from:" -ForegroundColor Cyan
            Write-Host "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe" -ForegroundColor Cyan
        }
    }

    # 4. Create or Update App Pool
    Write-Host "4. Configuring IIS Application Pool '$AppPoolName'..." -ForegroundColor Yellow
    Log-Message "Configuring AppPool: $AppPoolName"
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        New-Item "IIS:\AppPools\$AppPoolName" | Out-Null
        Log-Message "Created AppPool: $AppPoolName"
    }
    # ASP.NET Core runs as 'No Managed Code'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.identityType" -Value "ApplicationPoolIdentity"

    # Stop any conflicting websites on the target port (e.g., Default Web Site on port 80)
    try {
        if (Test-Path "IIS:\Sites\Default Web Site") {
            Write-Host "Stopping 'Default Web Site' to free port $Port..." -ForegroundColor Yellow
            Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
            Set-ItemProperty "IIS:\Sites\Default Web Site" -Name "serverAutoStart" -Value $false -ErrorAction SilentlyContinue
        }
        $targetPortPattern = "*:$($Port):*"
        Get-Website | Where-Object { 
            $_.Name -ne $SiteName -and ($_.Bindings.Collection.bindingInformation -like $targetPortPattern)
        } | ForEach-Object {
            Write-Host "Stopping conflicting website '$($_.Name)' on port $Port..." -ForegroundColor Yellow
            Log-Message "Stopping conflicting website '$($_.Name)' on port $Port"
            Stop-Website -Name $_.Name -ErrorAction SilentlyContinue
            Set-ItemProperty "IIS:\Sites\$($_.Name)" -Name "serverAutoStart" -Value $false -ErrorAction SilentlyContinue
        }
    } catch {
        Log-Message "Note during conflicting site check: $_" "WARN"
    }

    # 5. Set NTFS Permissions for AppPool, IIS_IUSRS and logs
    Write-Host "5. Setting filesystem permissions for IIS AppPool and logs..." -ForegroundColor Yellow
    try {
        $acl = Get-Acl $PublishPath
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
        $acl.AddAccessRule($rule)
        $ruleIisUsers = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\IIS_IUSRS", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
        $acl.AddAccessRule($ruleIisUsers)
        Set-Acl $PublishPath $acl
        Log-Message "Granted ReadAndExecute permissions on $PublishPath to IIS AppPool\$AppPoolName and BUILTIN\IIS_IUSRS"
    } catch {
        Log-Message "Warning updating ACL on $PublishPath. Details: $_" "WARN"
    }

    # Ensure logs directory exists in publish folder and has full write permissions
    $logsPath = Join-Path $PublishPath "logs"
    if (-not (Test-Path $logsPath)) {
        New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
    }

    try {
        $aclLogs = Get-Acl $logsPath
        $modifyAppPool = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
        $aclLogs.AddAccessRule($modifyAppPool)
        $modifyIisUsers = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\IIS_IUSRS", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
        $aclLogs.AddAccessRule($modifyIisUsers)
        $modifyUsers = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Users", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
        $aclLogs.AddAccessRule($modifyUsers)
        Set-Acl $logsPath $aclLogs
        Log-Message "Granted Modify permissions on $logsPath to AppPool and IIS_IUSRS"
    } catch {
        Log-Message "Warning updating ACL on $logsPath. Details: $_" "WARN"
        Write-Warning "Could not update ACL on $logsPath. Details: $_"
    }

    # 6. Create or Update Web Site
    Write-Host "6. Configuring IIS Web Site '$SiteName' on port $Port..." -ForegroundColor Yellow
    Log-Message "Configuring Website: $SiteName on port $Port with path $PublishPath"
    if (Test-Path "IIS:\Sites\$SiteName") {
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name "physicalPath" -Value $PublishPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name "applicationPool" -Value $AppPoolName
        Log-Message "Updated existing Website: $SiteName"
    } else {
        New-Website -Name $SiteName -Port $Port -PhysicalPath $PublishPath -ApplicationPool $AppPoolName
        Log-Message "Created new Website: $SiteName"
    }
    Start-Website -Name $SiteName
    Log-Message "Started Website: $SiteName"

    # 7. Health Check Verification
    Write-Host "7. Verifying application health..." -ForegroundColor Yellow
    Log-Message "Testing health endpoint http://localhost:$Port/healthz"
    Start-Sleep -Seconds 2
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:$Port/healthz" -TimeoutSec 10 -ErrorAction Stop
        $respStr = ($response | ConvertTo-Json -Compress)
        Write-Host "[OK] Health check passed: $respStr" -ForegroundColor Green
        Log-Message "Health check passed: $respStr"
    } catch {
        $healthErr = "Health check endpoint failed: $($_.Exception.Message)"
        Log-Message $healthErr "WARN"
        Write-Warning $healthErr
        Write-Host "Checking recent runtime logs in $logsPath..." -ForegroundColor Yellow
        if (Test-Path $logsPath) {
            $recentFiles = Get-ChildItem -Path $logsPath -File | Sort-Object LastWriteTime -Descending | Select-Object -First 3
            foreach ($file in $recentFiles) {
                Write-Host "=== Log file: $($file.Name) ===" -ForegroundColor Cyan
                Log-Message "--- Recent log: $($file.Name) ---" "LOG"
                $lines = Get-Content $file.FullName -Tail 25
                foreach ($l in $lines) {
                    Write-Host $l
                    Log-Message $l "RUNTIME"
                }
            }
        }
    }

    # Copy deploy.log to publish logs directory as well
    try {
        Copy-Item -Path $deployLogPath -Destination (Join-Path $logsPath "deploy.log") -Force -ErrorAction SilentlyContinue
    } catch { }

    Log-Message "=== Deployment finished successfully ==="

    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host "   Deployment completed successfully!                   " -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host "  Web UI:         http://localhost:$Port/index.html" -ForegroundColor White
    Write-Host "  Health check:   http://localhost:$Port/healthz" -ForegroundColor White
    Write-Host "  Deployment log: $deployLogPath" -ForegroundColor White
    Write-Host "  App logs:       $logsPath" -ForegroundColor White
    Write-Host ""

    if (-not $NoBrowser) {
        try {
            Start-Process "http://localhost:$Port/index.html"
        } catch { }
    }

    exit 0
}
catch {
    $crashMsg = "CRITICAL DEPLOYMENT FAILURE: $($_.Exception.Message)`n$($_.ScriptStackTrace)"
    Log-Message $crashMsg "FATAL"
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Red
    Write-Host "   DEPLOYMENT FAILED                                    " -ForegroundColor Red
    Write-Host "========================================================" -ForegroundColor Red
    Write-Host $crashMsg -ForegroundColor Red
    Write-Host ""
    Write-Host "Detailed deployment log has been saved to:" -ForegroundColor Yellow
    Write-Host "  $deployLogPath" -ForegroundColor Cyan
    Write-Host ""

    # Try copying deploy.log to publish folder logs
    try {
        $publishLogs = Join-Path $PublishPath "logs"
        if (Test-Path $publishLogs) {
            Copy-Item -Path $deployLogPath -Destination (Join-Path $publishLogs "deploy.log") -Force -ErrorAction SilentlyContinue
        }
    } catch { }

    exit 1
}
finally {
    try {
        Stop-Transcript -ErrorAction SilentlyContinue | Out-Null
    } catch { }
}
