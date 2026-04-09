@echo off
echo ========================================
echo Panwar Portals API - Azure Functions Deploy
echo ========================================
echo.

REM Build the project
echo [1/3] Building project...
dotnet build --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)
echo Build successful!
echo.

REM Publish the project
echo [2/3] Publishing project...
dotnet publish --configuration Release --output ./publish
if %ERRORLEVEL% NEQ 0 (
    echo Publish failed!
    exit /b %ERRORLEVEL%
)
echo Publish successful!
echo.

REM Deploy to Azure
echo [3/3] Deploying to Azure Functions...
cd publish
func azure functionapp publish panwar-api --dotnet-isolated
if %ERRORLEVEL% NEQ 0 (
    echo Deploy failed!
    cd ..
    exit /b %ERRORLEVEL%
)
cd ..
echo.

echo ========================================
echo Deployment successful!
echo ========================================
echo.
echo Your API is now live at:
echo https://api.panwarhealth.com.au
echo (until DNS is set up, also reachable at https://panwar-api.azurewebsites.net)
echo.
pause
