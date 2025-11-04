# Deployment Checklist

## Pre-Deployment

- [ ] All code builds successfully in Release mode
- [ ] No build warnings in Release configuration
- [ ] Configuration file (`orchestrator.json`) is properly configured
- [ ] Service executable paths are correct and accessible
- [ ] Working directories exist
- [ ] Firewall rules allow ports 5001, 5080, 6000, 6001
- [ ] .NET 8.0 Runtime is installed on target machine
- [ ] Administrator privileges available for service installation

## Configuration Review

- [ ] Verify `Services` section lists all managed services
- [ ] Check `ExecutablePath` for each service
- [ ] Confirm `WorkingDirectory` exists
- [ ] Review `MinInstances` and `MaxInstances` settings
- [ ] Validate `SchedulePolicy` configuration
- [ ] Confirm `Web.UiPort` and `Web.ApiPort` are available
- [ ] Verify `Ipc.LogPort` and `Ipc.StatusPort` are available
- [ ] Check `Global.HealthCheckInterval` is appropriate

## Build and Publish

- [ ] Clean solution: `dotnet clean`
- [ ] Build in Release mode: `dotnet build --configuration Release`
- [ ] Run deployment script: `.\deploy-windows.ps1`
- [ ] Verify publish directory contents:
  - [ ] `publish/Orchestrator/` exists with exe and DLLs
  - [ ] `publish/Orchestrator.WebApi/` exists with exe and DLLs
  - [ ] `publish/Orchestrator.WebUI/` exists with exe and DLLs
  - [ ] `orchestrator.json` copied to all three directories
  - [ ] Installation scripts created in `publish/`

## Installation

- [ ] Open PowerShell as Administrator
- [ ] Navigate to `publish` directory
- [ ] Run `.\install-services.ps1`
- [ ] Verify services installed:
  ```powershell
  Get-Service Orchestrator*
  ```
- [ ] Check services are set to "Automatic" startup
- [ ] Verify recovery options configured

## Service Start

- [ ] Run `.\start-services.ps1`
- [ ] Verify all services running:
  ```powershell
  Get-Service Orchestrator* | Format-Table Name, Status
  ```
- [ ] Check Windows Event Viewer for startup errors
  - Application log
  - Look for "Orchestrator" source

## Verification

### Web UI
- [ ] Navigate to http://localhost:5080
- [ ] Dashboard loads successfully
- [ ] Status stream shows "Live" (green)
- [ ] Service count displays correctly
- [ ] Navigate to `/services` page
- [ ] Navigate to `/health` page
- [ ] All pages load without errors

### Web API
- [ ] Navigate to http://localhost:5001/swagger
- [ ] Swagger UI loads
- [ ] Test `GET /api/services` endpoint
- [ ] Verify service list returns correctly
- [ ] Test `GET /api/services/internal` endpoint
- [ ] Internal health components display

### Service Management
- [ ] Start a test service via Web UI
- [ ] Verify service appears as "Running"
- [ ] View logs for the service
- [ ] Logs stream in real-time
- [ ] Stop the service
- [ ] Verify service appears as "Stopped"

### Health Monitoring
- [ ] Navigate to `/health` page
- [ ] Verify "ProcessSupervisor" shows as Healthy
- [ ] Verify "ProcessScheduler" shows as Healthy
- [ ] Refresh button works

## Performance Check

- [ ] Monitor CPU usage (should be low when idle)
- [ ] Monitor memory usage
- [ ] Check log file sizes
- [ ] Verify no memory leaks after 1 hour

## Security Review

- [ ] Services running under appropriate account
- [ ] File permissions set correctly
- [ ] Network ports properly secured
- [ ] HTTPS configured if exposing externally (optional)
- [ ] API authentication configured if needed (optional)

## Logging

- [ ] Check Windows Event Viewer for errors
- [ ] Verify application logs are being written
- [ ] Confirm log levels are appropriate for production
- [ ] Set up log rotation if needed

## Backup

- [ ] Backup `orchestrator.json` configuration
- [ ] Document custom settings
- [ ] Save deployment scripts
- [ ] Record service installation paths

## Documentation

- [ ] Update deployment date
- [ ] Document any configuration changes
- [ ] Note any issues encountered
- [ ] Record resolution steps

## Post-Deployment Monitoring

### First Hour
- [ ] Check all services remain running
- [ ] Monitor for errors in Event Viewer
- [ ] Verify managed services start/stop correctly
- [ ] Check real-time updates in Web UI

### First Day
- [ ] Review accumulated logs
- [ ] Check for any crashes or restarts
- [ ] Verify scheduled tasks executing
- [ ] Monitor resource usage

### First Week
- [ ] Review overall system stability
- [ ] Check for memory leaks
- [ ] Analyze log patterns
- [ ] Gather user feedback

## Rollback Plan

If issues occur:
- [ ] Stop all services: `.\stop-services.ps1`
- [ ] Uninstall services: `.\uninstall-services.ps1`
- [ ] Remove publish directory
- [ ] Restore previous version
- [ ] Document issues for troubleshooting

## Success Criteria

- [ ] All three services running
- [ ] Web UI accessible and responsive
- [ ] API endpoints responding correctly
- [ ] Services can be started and stopped
- [ ] Logs streaming correctly
- [ ] Health monitoring operational
- [ ] No errors in Event Viewer
- [ ] Resource usage acceptable
- [ ] System stable for 24 hours

## Sign-Off

- **Deployed by:** _______________
- **Date:** _______________
- **Version:** _______________
- **Environment:** Production / Staging / Test
- **Approved by:** _______________

## Notes

_Add any deployment-specific notes here:_

---

**Deployment Status:** [ ] Success  [ ] Issues  [ ] Rolled Back
