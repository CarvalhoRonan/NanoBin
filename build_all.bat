@echo off
echo ============================================
echo    NanoBin v3.0 - Build Automatico
echo ============================================
echo.

cd /d "%~dp0"

:: ===== 1. BUILD PORTABLE =====
echo [1/5] Gerando versao PORTABLE...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ".\output\portable"
if errorlevel 1 goto :error

copy /Y icon*.ico ".\output\portable\" >nul 2>&1
copy /Y qrcode_donation.png ".\output\portable\" >nul 2>&1
echo       OK: output\portable\NanoBin.exe
echo.

:: ===== 2. BUILD PARA INSTALADOR =====
echo [2/5] Gerando build para instalador...
dotnet publish -c Release -r win-x64 --self-contained true -o ".\publish"
if errorlevel 1 goto :error

copy /Y icon*.ico ".\publish\" >nul 2>&1
copy /Y qrcode_donation.png ".\publish\" >nul 2>&1
echo       OK: pasta publish\ pronta
echo.

:: ===== 3. GERAR INSTALADOR .EXE (Inno Setup) =====
echo [3/5] Gerando instalador .EXE com Inno Setup...
set INNO_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set INNO_PATH=C:\Program Files\Inno Setup 6\ISCC.exe

if "%INNO_PATH%"=="" (
    echo       AVISO: Inno Setup nao encontrado! Instale de https://jrsoftware.org/isinfo.php
) else (
    "%INNO_PATH%" NanoBin_Installer.iss
    if errorlevel 1 (
        echo       ERRO ao gerar instalador .exe
    ) else (
        move /Y "Output\NanoBin_v3.0_Setup.exe" ".\output\" >nul 2>&1
        echo       OK: output\NanoBin_v3.0_Setup.exe
    )
)
echo.

:: ===== 4. GERAR INSTALADOR AUTONOMO (silencioso em 2o plano) =====
echo [4/5] Gerando instalador autonomo silencioso...

:: Criar _install.ps1 usando echo
call :criar_install_ps1

:: Criar _install.bat usando echo
call :criar_install_bat

:: Criar SED para IExpress
call :criar_sed

:: Executar IExpress
iexpress /N /Q ".\NanoBin_SFX.sed"
if errorlevel 1 (
    echo       AVISO: IExpress falhou ao gerar o autonomo.
) else (
    echo       OK: output\NanoBin_V3.0_Autonomo.exe
)

:: Limpar temporarios do autonomo
del ".\publish\_install.bat" >nul 2>&1
del ".\publish\_install.ps1" >nul 2>&1
echo.

:: ===== 5. GERAR PACOTE MSIX (Microsoft Store) =====
echo [5/5] Gerando pacote MSIX para Microsoft Store...

set MAKEAPPX=
for /f "delims=" %%i in ('where makeappx.exe 2^>nul') do set MAKEAPPX=%%i
if "%MAKEAPPX%"=="" (
    for /f "delims=" %%i in ('dir /s /b "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe" 2^>nul') do set MAKEAPPX=%%i
)

mkdir ".\output\msix_content" >nul 2>&1
xcopy /E /Y /Q ".\publish\*" ".\output\msix_content\" >nul

:: Criar AppxManifest.xml
call :criar_manifest

if "%MAKEAPPX%"=="" (
    echo       AVISO: makeappx.exe nao encontrado. Instale o Windows 10 SDK.
    echo       Download: https://developer.microsoft.com/windows/downloads/windows-sdk/
    echo.
    echo       OK: output\msix_content\ (estrutura pronta para empacotamento)
    echo       Para gerar o .msix manualmente, execute:
    echo         makeappx pack /d output\msix_content /p output\NanoBin_V3.0.msix
) else (
    "%MAKEAPPX%" pack /d ".\output\msix_content" /p ".\output\NanoBin_V3.0.msix" /o
    if errorlevel 1 (
        echo       ERRO ao gerar pacote MSIX.
    ) else (
        echo       OK: output\NanoBin_V3.0.msix
    )
)

:: Limpar temporarios gerais
del ".\NanoBin_SFX.sed" >nul 2>&1

echo.
echo ============================================
echo    Build concluido! Verifique a pasta output\
echo ============================================
echo.
echo    output\portable\NanoBin.exe            = Versao portable
echo    output\NanoBin_V3.0_Setup.exe          = Instalador (Inno Setup)
echo    output\NanoBin_V3.0_Autonomo.exe       = Instalador autonomo (silencioso)
echo    output\NanoBin_V3.0.msix               = Pacote Microsoft Store
echo    output\msix_content\                    = Estrutura MSIX (se makeappx ausente)
echo.
pause
exit /b 0

:: ===== SUB-ROTINAS =====

:criar_install_ps1
> ".\publish\_install.ps1" (
    echo $ErrorActionPreference = 'SilentlyContinue'
    echo $dest = "$env:ProgramFiles\NanoBin"
    echo Stop-Process -Name 'NanoBin' -Force 2^>$null
    echo Start-Sleep -Seconds 1
    echo New-Item -ItemType Directory -Path $dest -Force ^| Out-Null
    echo $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    echo Get-ChildItem -Path $scriptDir -Exclude '_install.ps1','_install.bat' ^| Copy-Item -Destination $dest -Recurse -Force
    echo $ws = New-Object -ComObject WScript.Shell
    echo $s = $ws.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'^), 'NanoBin.lnk'^)^)
    echo $s.TargetPath = "$dest\NanoBin.exe"
    echo $s.Save(^)
    echo Start-Process -FilePath "$dest\NanoBin.exe"
)
goto :eof

:criar_install_bat
> ".\publish\_install.bat" (
    echo @echo off
    echo powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%%~dp0_install.ps1"
)
goto :eof

:criar_sed
> ".\NanoBin_SFX.sed" (
    echo [Version]
    echo Class=IEXPRESS
    echo SEDVersion=3
    echo [Options]
    echo PackagePurpose=InstallApp
    echo ShowInstallProgramWindow=0
    echo HideExtractAnimation=1
    echo UseLongFileName=1
    echo InsideCompressed=0
    echo CAB_FixedSize=0
    echo CAB_ResvCodeSigning=0
    echo RebootMode=N
    echo InstallPrompt=
    echo DisplayLicense=
    echo FinishMessage=
    echo TargetName=.\output\NanoBin_V3.0_Autonomo.exe
    echo FriendlyName=NanoBin V3.0
    echo AppLaunched=_install.bat
    echo PostInstallCmd=^<None^>
    echo AdminQuietInstCmd=
    echo UserQuietInstCmd=
    echo SourceFiles=SourceFiles
    echo [Strings]
    echo [SourceFiles]
    echo SourceFiles0=.\publish\
    echo [SourceFiles0]
)
powershell -NoProfile -Command "Get-ChildItem '.\publish' -File | ForEach-Object { Add-Content '.\NanoBin_SFX.sed' ('%%FILE%%=' + $_.Name) }"
goto :eof

:criar_manifest
> ".\output\msix_content\AppxManifest.xml" (
    echo ^<?xml version="1.0" encoding="utf-8"?^>
    echo ^<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
    echo          xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
    echo          xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
    echo          IgnorableNamespaces="uap rescap"^>
    echo   ^<Identity Name="OverclockInformatica.NanoBin"
    echo             Version="3.0.0.0"
    echo             Publisher="CN=Overclock Informatica"
    echo             ProcessorArchitecture="x64" /^>
    echo   ^<Properties^>
    echo     ^<DisplayName^>NanoBin^</DisplayName^>
    echo     ^<PublisherDisplayName^>Overclock Informatica^</PublisherDisplayName^>
    echo     ^<Logo^>icon.ico^</Logo^>
    echo   ^</Properties^>
    echo   ^<Dependencies^>
    echo     ^<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" /^>
    echo   ^</Dependencies^>
    echo   ^<Resources^>
    echo     ^<Resource Language="pt-BR" /^>
    echo   ^</Resources^>
    echo   ^<Applications^>
    echo     ^<Application Id="NanoBin" Executable="NanoBin.exe" EntryPoint="Windows.FullTrustApplication"^>
    echo       ^<uap:VisualElements DisplayName="NanoBin"
    echo                          Description="Gerenciador da lixeira do Windows"
    echo                          BackgroundColor="transparent"
    echo                          Square150x150Logo="icon.ico"
    echo                          Square44x44Logo="icon.ico" /^>
    echo     ^</Application^>
    echo   ^</Applications^>
    echo   ^<Capabilities^>
    echo     ^<rescap:Capability Name="runFullTrust" /^>
    echo   ^</Capabilities^>
    echo ^</Package^>
)
goto :eof

:error
echo.
echo ERRO durante o build! Verifique se o .NET SDK esta instalado.
echo Download: https://dotnet.microsoft.com/download
pause
exit /b 1
