# Quick Start Guide

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11 or Windows Server 2016+ (for Windows Service deployment)
- Administrator privileges (for service installation)

## Quick Development Start

### 1. Clone and Build

```powershell
git clone <repository-url>
cd NetOrchestrator
dotnet build
```

### 2. Configure Services

Edit `Orchestrator.json` to add your services:

```json
{
  "Services": {
    "MyApp": {
      "Name": "MyApp",
  "ExecutablePath": "path/to/MyApp.dll",
      "WorkingDirectory": "path/to/workdir",
 "Arguments": "",
  "MinInstances": 1,
      "MaxInstances": 1,
      "SchedulePolicy": {
 "Type": "steady"
 },
 "Dependencies": []
    }
  }
}
```

### 3. Run in Development Mode

Open 3 terminal windows:

**Terminal 1 - Core Orchestrator:**
```powershell
cd Orchestrator
dotnet run
```

**Terminal 2 - Web API:**
```powershell
cd Orchestrator.WebApi
dotnet run
```

**Terminal 3 - Web UI:**
```powershell
cd Orchestrator.WebUI
dotnet run
```

### 4. Access the Application

- Open browser to http://localhost:5080
- You should see the Orchestrator dashboard
- Click "Services" to manage your configured services

## Quick Production Deployment (Windows Services)

### 1. Build and Deploy

```powershell
# Run deployment script (as Administrator)
.\deploy-windows.ps1
```

### 2. Install Services

```powershell
# Navigate to publish directory
cd publish

# Install services (as Administrator)
.\install-services.ps1
```

### 3. Start Services

```powershell
# Start all services
.\start-services.ps1
```

### 4. Verify

Open browser to http://localhost:5080 and verify the dashboard loads.

## Managing Services

### Start a Service
1. Go to http://localhost:5080/services
2. Find your service in the list
3. Click the green "Start" button

### Stop a Service
1. Go to http://localhost:5080/services
2. Find your service in the list
3. Click the red "Stop" button

### View Logs
1. Go to http://localhost:5080/services
2. Find your service in the list
3. Click the blue "Logs" button
4. Logs will stream in real-time below the table

## Troubleshooting

### Issue: Services won't start

**Solution:**
- Check `orchestrator.json` paths are correct
- Verify the executable exists at the specified path
- Check Windows Event Viewer for error details

### Issue: Web UI shows "Disconnected"

**Solution:**
- Ensure all three components are running:
  - Orchestrator.exe
  - Orchestrator.WebApi.exe
  - Orchestrator.WebUI.exe
- Check Windows Firewall isn't blocking ports 5001, 5080, 6000

### Issue: No logs appearing

**Solution:**
- Ensure your managed services write to stdout/stderr
- Check the Orchestrator console for IPC connection errors
- Verify `Ipc.LogPort` is consistent in all `orchestrator.json` files

## Common Tasks

### View Service Status via API
```powershell
Invoke-RestMethod -Uri "http://localhost:5001/api/services"
```

### Start a Service via API
```powershell
Invoke-RestMethod -Uri "http://localhost:5001/api/services/MyApp/start" -Method Post
```

### Stop a Service via API
```powershell
Invoke-RestMethod -Uri "http://localhost:5001/api/services/MyApp/stop" -Method Post
```

## Next Steps

- Read the full [README.md](README.md) for detailed documentation
- Explore the [Web UI](http://localhost:5080) features
- Review [API documentation](http://localhost:5001/swagger)
- Configure scheduling policies for your services
- Set up health monitoring

## Getting Help

- Check the logs in the Web UI Dashboard
- Review Windows Event Viewer (for services)
- Check console output (for development mode)
- Review `orchestrator.json` configuration

## Stopping the System

### Development Mode
Press `Ctrl+C` in each terminal window

### Windows Services
```powershell
cd publish
.\stop-services.ps1
```

### Uninstall Services
```powershell
cd publish
.\uninstall-services.ps1
```

## Default Ports

- **Web UI**: 5080
- **Web API**: 5001
- **IPC Log Port**: 6000
- **IPC Status Port**: 6001

These can be changed in `orchestrator.json`.
