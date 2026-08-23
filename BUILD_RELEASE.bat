@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title ServiceKiller V1.1.3.01 - Compilar
color 0B

echo.
echo ================================================================
echo   SERVICEKILLER V1.1.3.01 - COMPILACION (.NET Framework 4.8)
echo ================================================================
echo.

set "PROJECT=src\ServiceKiller"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [ERROR] No se encontro el compilador C# de .NET Framework.
    echo Instala o activa .NET Framework 4.8, o abre %PROJECT%\ServiceKillerV1.sln en Visual Studio.
    pause
    exit /b 1
)

if not exist "artifacts" mkdir "artifacts"
if exist "artifacts\ServiceKiller.exe" del /q "artifacts\ServiceKiller.exe" >nul 2>&1
if exist "artifacts\ServiceKiller.exe.config" del /q "artifacts\ServiceKiller.exe.config" >nul 2>&1

echo Compilando ServiceKiller V1.1.3.01...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
 /win32manifest:"%PROJECT%\app.manifest" /win32icon:"%PROJECT%\Properties\ServiceKiller.ico" ^
 /reference:System.dll /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
 /reference:System.Runtime.Serialization.dll /reference:System.ServiceProcess.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll ^
 /out:"artifacts\ServiceKiller.exe" ^
 "%PROJECT%\Program.cs" "%PROJECT%\BuildInfo.cs" ^
 "%PROJECT%\Properties\AssemblyInfo.cs" ^
 "%PROJECT%\Core\AppPaths.cs" "%PROJECT%\Core\MachineDataSecurity.cs" "%PROJECT%\Core\MachineOperationLock.cs" "%PROJECT%\Core\JournalValidator.cs" "%PROJECT%\Core\TaskSchedulerInterop.cs" "%PROJECT%\Core\WindowsCompatibility.cs" "%PROJECT%\Core\ApplicationDetector.cs" "%PROJECT%\Core\BootManager.cs" "%PROJECT%\Core\CommandRunner.cs" "%PROJECT%\Core\CustomAppStore.cs" "%PROJECT%\Core\Logger.cs" ^
 "%PROJECT%\Core\StartupDiagnostics.cs" "%PROJECT%\Core\PrivilegeHelper.cs" "%PROJECT%\Core\ElevationManager.cs" "%PROJECT%\Core\WorkerRunner.cs" ^
 "%PROJECT%\Core\ProcessManager.cs" "%PROJECT%\Core\ProfileStore.cs" "%PROJECT%\Core\RegistryManager.cs" "%PROJECT%\Core\RestorationVerifier.cs" "%PROJECT%\Core\DiagnosticReportBuilder.cs" "%PROJECT%\Core\ShortcutResolver.cs" "%PROJECT%\Core\StartupManager.cs" "%PROJECT%\Core\StateStore.cs" ^
 "%PROJECT%\Core\SessionRestoreManager.cs" "%PROJECT%\Core\SessionApplyCoordinator.cs" "%PROJECT%\Core\SystemMetricsReader.cs" "%PROJECT%\Core\TweakEngine.cs" "%PROJECT%\Core\WindowsServiceManager.cs" ^
 "%PROJECT%\Data\TweakCatalog.cs" ^
 "%PROJECT%\Models\Enums.cs" "%PROJECT%\Models\CustomApplicationInfo.cs" "%PROJECT%\Models\SystemState.cs" "%PROJECT%\Models\TweakDefinition.cs" "%PROJECT%\Models\UserProfileInfo.cs" ^
 "%PROJECT%\UI\MainForm.cs" "%PROJECT%\UI\DiagnosticForm.cs" "%PROJECT%\UI\BoostSummaryForm.cs" "%PROJECT%\UI\PreviewForm.cs" "%PROJECT%\UI\ProcessAnalyzerForm.cs" "%PROJECT%\UI\TextPromptForm.cs" "%PROJECT%\UI\Theme.cs" "%PROJECT%\UI\TweakRowControl.cs"

if errorlevel 1 (
    echo.
    echo [ERROR] La compilacion ha fallado. No se ejecutara nada.
    pause
    exit /b 1
)

if exist "%PROJECT%\ServiceKiller.exe.config" copy /y "%PROJECT%\ServiceKiller.exe.config" "artifacts\ServiceKiller.exe.config" >nul

for /f "tokens=*" %%H in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 'artifacts\ServiceKiller.exe').Hash.ToLower()"') do set "SHA256=%%H"

echo.
echo [OK] Ejecutable generado:
echo   %CD%\artifacts\ServiceKiller.exe
echo SHA-256:
echo   %SHA256%
echo.
echo El build NO ejecuta automaticamente el EXE.
echo.

if /I "%~1"=="--run" (
    echo Abriendo ServiceKiller por solicitud explicita --run...
    start "" "artifacts\ServiceKiller.exe"
)

exit /b 0
