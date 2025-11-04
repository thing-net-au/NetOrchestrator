# Deployment Script for Windows

Write-Host "Building .NET Orchestrator for deployment..." -ForegroundColor Green

# Configuration
$publishDir = ".\publish"
$configuration = "Release"

# Clean previous publish
if (Test-Path $publishDir) {
    Write-Host "Cleaning previous publish directory..." -ForegroundColor Yellow
  Remove-Item -Path $publishDir -Recurse -Force
}

# Create publish directory
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# Publish Orchestrator (Main Service)
Write-Host "`nPublishing Orchestrator..." -ForegroundColor Cyan
dotnet publish .\Orchestrator\Orchestrator.csproj `
    --configuration $configuration `
    --output "$publishDir\Orchestrator" `
    --self-contained false

# Publish Web API
Write-Host "`nPublishing Orchestrator.WebApi..." -ForegroundColor Cyan
dotnet publish .\Orchestrator.WebApi\Orchestrator.WebApi.csproj `
    --configuration $configuration `
    --output "$publishDir\Orchestrator.WebApi" `
    --self-contained false

# Publish Web UI
Write-Host "`nPublishing Orchestrator.WebUI..." -ForegroundColor Cyan
dotnet publish .\Orchestrator.WebUI\Orchestrator.WebUI.csproj `
--configuration $configuration `
    --output "$publishDir\Orchestrator.WebUI" `
    --self-contained false

# Copy configuration file
Write-Host "`nCopying configuration files..." -ForegroundColor Cyan
Copy-Item -Path ".\Orchestrator.json" -Destination "$publishDir\Orchestrator\" -Force
Copy-Item -Path ".\Orchestrator.json" -Destination "$publishDir\Orchestrator.WebApi\" -Force
Copy-Item -Path ".\Orchestrator.json" -Destination "$publishDir\Orchestrator.WebUI\" -Force

# Create installation scripts
Write-Host "`nCreating installation scripts..." -ForegroundColor Cyan

# Install script
$installScript = @"
# Install Orchestrator as Windows Services
`$services = @(
    @{Name='OrchestratorCore'; Path='$((Get-Location).Path)\publish\Orchestrator\Orchestrator.exe'; Display='Orchestrator Core Service'},
    @{Name='OrchestratorApi'; Path='$((Get-Location).Path)\publish\Orchestrator.WebApi\Orchestrator.WebApi.exe'; Display='Orchestrator API Service'},
    @{Name='OrchestratorUI'; Path='$((Get-Location).Path)\publish\Orchestrator.WebUI\Orchestrator.WebUI.exe'; Display='Orchestrator Web UI Service'}
)

foreach (`$svc in `$services) {
    Write-Host "Installing `$(`$svc.Display)..." -ForegroundColor Green
    
    # Stop and remove if exists
    `$existing = Get-Service -Name `$svc.Name -ErrorAction SilentlyContinue
    if (`$existing) {
        Write-Host "  Stopping existing service..." -ForegroundColor Yellow
        Stop-Service -Name `$svc.Name -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe delete `$svc.Name
  Start-Sleep -Seconds 1
    }
    
    # Create service
 Write-Host "  Creating service..." -ForegroundColor Cyan
    sc.exe create `$svc.Name binPath= `$svc.Path DisplayName= `$svc.Display start= auto
 
    # Set description
    sc.exe description `$svc.Name ".NET Orchestrator - `$(`$svc.Display)"
  
    # Configure recovery options
    sc.exe failure `$svc.Name reset= 86400 actions= restart/5000/restart/10000/restart/20000
}

Write-Host "`nAll services installed successfully!" -ForegroundColor Green
Write-Host "Start services with: .\start-services.ps1" -ForegroundColor Cyan
"@

$installScript | Out-File -FilePath "$publishDir\install-services.ps1" -Encoding UTF8

# Start script
$startScript = @"
# Start all Orchestrator services
Write-Host "Starting Orchestrator services..." -ForegroundColor Green

`$services = @('OrchestratorCore', 'OrchestratorApi', 'OrchestratorUI')

foreach (`$svc in `$services) {
    Write-Host "Starting `$svc..." -ForegroundColor Cyan
    Start-Service -Name `$svc
 `$status = (Get-Service -Name `$svc).Status
    Write-Host "  Status: `$status" -ForegroundColor $(if (`$status -eq 'Running') {'Green'} else {'Red'})
}

Write-Host "`nAll services started!" -ForegroundColor Green
Write-Host "Access the Web UI at: http://localhost:5080" -ForegroundColor Cyan
Write-Host "Access the API at: http://localhost:5001" -ForegroundColor Cyan
"@

$startScript | Out-File -FilePath "$publishDir\start-services.ps1" -Encoding UTF8

# Stop script
$stopScript = @"
# Stop all Orchestrator services
Write-Host "Stopping Orchestrator services..." -ForegroundColor Yellow

`$services = @('OrchestratorUI', 'OrchestratorApi', 'OrchestratorCore')

foreach (`$svc in `$services) {
    Write-Host "Stopping `$svc..." -ForegroundColor Cyan
    Stop-Service -Name `$svc -Force -ErrorAction SilentlyContinue
 `$status = (Get-Service -Name `$svc).Status
    Write-Host "  Status: `$status" -ForegroundColor $(if (`$status -eq 'Stopped') {'Green'} else {'Yellow'})
}

Write-Host "`nAll services stopped!" -ForegroundColor Green
"@

$stopScript | Out-File -FilePath "$publishDir\stop-services.ps1" -Encoding UTF8

# Uninstall script
$uninstallScript = @"
# Uninstall Orchestrator Windows Services
Write-Host "Uninstalling Orchestrator services..." -ForegroundColor Yellow

`$services = @('OrchestratorUI', 'OrchestratorApi', 'OrchestratorCore')

foreach (`$svc in `$services) {
 Write-Host "Uninstalling `$svc..." -ForegroundColor Cyan
    
  # Stop service
    Stop-Service -Name `$svc -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
 
    # Delete service
    sc.exe delete `$svc
    Write-Host "  `$svc uninstalled" -ForegroundColor Green
}

Write-Host "`nAll services uninstalled!" -ForegroundColor Green
"@

$uninstallScript | Out-File -FilePath "$publishDir\uninstall-services.ps1" -Encoding UTF8

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nPublished to: $publishDir" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. cd publish" -ForegroundColor White
Write-Host "2. .\install-services.ps1 (run as Administrator)" -ForegroundColor White
Write-Host "3. .\start-services.ps1" -ForegroundColor White
Write-Host "`nAccess the application:" -ForegroundColor Yellow
Write-Host "  Web UI: http://localhost:5080" -ForegroundColor White
Write-Host "  API: http://localhost:5001" -ForegroundColor White
Write-Host "  Swagger: http://localhost:5001/swagger" -ForegroundColor White
