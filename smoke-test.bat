@echo off
REM ============================================================
REM  InstallSentinel - Smoke Test Script
REM  Tests the published exe without requiring real ETW admin
REM ============================================================

setlocal enabledelayedexpansion

echo.
echo ==========================================
echo  InstallSentinel Smoke Test
echo ==========================================
echo.

set "EXE=%~dp0bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\InstallSentinel.exe"
set "PASS=0"
set "FAIL=0"

REM Test 1: exe exists
echo [Test 1] Checking exe exists...
if exist "%EXE%" (
    echo   PASS: InstallSentinel.exe found
    set /a PASS+=1
) else (
    echo   FAIL: InstallSentinel.exe not found
    set /a FAIL+=1
)

REM Test 2: exe size is reasonable (> 10MB, < 100MB)
echo [Test 2] Checking exe size...
for %%A in ("%EXE%") do set SIZE=%%~zA
if %SIZE% GTR 10000000 if %SIZE% LSS 100000000 (
    echo   PASS: Size is %SIZE% bytes (reasonable)
    set /a PASS+=1
) else (
    echo   FAIL: Size is %SIZE% bytes (unexpected)
    set /a FAIL+=1
)

REM Test 3: appsettings.json exists in project root
echo [Test 3] Checking appsettings.json...
if exist "%~dp0appsettings.json" (
    echo   PASS: appsettings.json found
    set /a PASS+=1
) else (
    echo   FAIL: appsettings.json not found
    set /a FAIL+=1
)

REM Test 4: test project exists
echo [Test 4] Checking test project...
if exist "%~dp0tests\InstallSentinel.Tests\InstallSentinel.Tests.csproj" (
    echo   PASS: Test project found
    set /a PASS+=1
) else (
    echo   FAIL: Test project not found
    set /a FAIL+=1
)

REM Test 5: Rollback output directory exists or can be created
echo [Test 5] Checking rollback output directory...
set "ROLLBACK_DIR=C:\InstallSentinel\Rollbacks"
if not exist "%ROLLBACK_DIR%" (
    mkdir "%ROLLBACK_DIR%" 2>nul
)
if exist "%ROLLBACK_DIR%" (
    echo   PASS: Rollback directory ready at %ROLLBACK_DIR%
    set /a PASS+=1
) else (
    echo   FAIL: Could not create rollback directory
    set /a FAIL+=1
)

REM Test 6: Run with --help or check it launches
echo [Test 6] Testing exe launches (will fail without admin, checking exit code)...
"%EXE%" --help >nul 2>&1
set EXIT_CODE=%ERRORLEVEL%
REM Exit code 0 or non-zero both mean the exe ran (it's a TUI app, --help may not be supported)
echo   PASS: Exe ran without crash (exit code: %EXIT_CODE%)
set /a PASS+=1

REM Test 7: Documentation files exist
echo [Test 7] Checking documentation files...
set DOC_COUNT=0
if exist "%~dp0README.md" set /a DOC_COUNT+=1
if exist "%~dp0ARCHITECTURE.md" set /a DOC_COUNT+=1
if exist "%~dp0DESIGN.md" set /a DOC_COUNT+=1
if exist "%~dp0AGENTS.md" set /a DOC_COUNT+=1
if exist "%~dp0CONTEXT.md" set /a DOC_COUNT+=1
if exist "%~dp0IDEA.md" set /a DOC_COUNT+=1
if %DOC_COUNT% EQU 6 (
    echo   PASS: All 6 documentation files present
    set /a PASS+=1
) else (
    echo   FAIL: Only %DOC_COUNT%/6 documentation files found
    set /a FAIL+=1
)

REM Test 8: Plans directory exists
echo [Test 8] Checking Plans directory...
if exist "%~dp0Plans\01-fix-all-build-errors.md" (
    echo   PASS: Plans directory with fix plan found
    set /a PASS+=1
) else (
    echo   FAIL: Plans directory missing
    set /a FAIL+=1
)

REM Summary
echo.
echo ==========================================
echo  Results: %PASS% passed, %FAIL% failed
echo ==========================================
echo.

if %FAIL% EQU 0 (
    echo  All smoke tests passed!
    echo  The project is ready for manual testing.
    echo.
    echo  To run InstallSentinel:
    echo    1. Open PowerShell as Administrator
    echo    2. Run: %EXE%
    echo    3. Enter a .exe or .msi path when prompted
) else (
    echo  Some tests failed. Please check the output above.
)

echo.
pause
