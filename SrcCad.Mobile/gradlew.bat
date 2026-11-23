@echo off
setlocal
set "APP_HOME=%~dp0"
set "WRAPPER_DIR=%APP_HOME%gradle\wrapper"
set "WRAPPER_JAR=%WRAPPER_DIR%\gradle-wrapper.jar"
set "WRAPPER_B64=%WRAPPER_DIR%\gradle-wrapper.jar.b64"

if not exist "%WRAPPER_JAR%" (
  if not exist "%WRAPPER_B64%" (
    echo [ERROR] Gradle wrapper payload is missing.
    exit /b 1
  )
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$b64 = Get-Content -Raw -LiteralPath '%WRAPPER_B64%'; [IO.File]::WriteAllBytes('%WRAPPER_JAR%', [Convert]::FromBase64String($b64))"
  if errorlevel 1 (
    echo [ERROR] Could not unpack Gradle wrapper JAR.
    exit /b 1
  )
)

if not defined JAVA_HOME (
  where java >nul 2>nul
  if errorlevel 1 (
    echo [ERROR] Java was not found. Install JDK 17 and set JAVA_HOME.
    exit /b 1
  )
  set "JAVA_EXE=java"
) else (
  set "JAVA_EXE=%JAVA_HOME%\bin\java.exe"
)

"%JAVA_EXE%" -classpath "%WRAPPER_JAR%" org.gradle.wrapper.GradleWrapperMain %*
exit /b %ERRORLEVEL%
