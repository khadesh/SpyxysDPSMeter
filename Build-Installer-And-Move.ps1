param(
    # When omitted, the version is read from the <Version> property in
    # SpyxysDPSMeter.csproj (the single source of truth, also used by CI).
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = "",

    [string]$InnoCompilerPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Repository layout expected:
#
# SpyxysDPSMeter\
# ├─ Build-Installer-And-Move.ps1
# ├─ Build-Release.cmd
# ├─ SpyxysDPSMeterInstaller.iss
# └─ SpyxysDPSMeter\
#    ├─ SpyxysDPSMeter.csproj
#    ├─ Assets\
#    └─ publish\
#
# The final installer is placed in the OUTER repository root.

$RepositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepositoryRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $CsprojPath = Join-Path `
        $RepositoryRoot `
        "SpyxysDPSMeter\SpyxysDPSMeter.csproj"

    [xml]$CsprojXml = Get-Content -Path $CsprojPath

    $Version = ($CsprojXml.Project.PropertyGroup.Version |
        Where-Object { $_ } |
        Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "No <Version> property found in $CsprojPath."
    }

    $Version = $Version.Trim()

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid <Version> '$Version' in $CsprojPath."
    }

    Write-Host "Version read from csproj: $Version" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Building Spyxy's DPS Meter v$Version" -ForegroundColor Cyan
Write-Host "Repository root: $RepositoryRoot"
Write-Host ""

# ------------------------------------------------------------
# Locate the nested project and root installer script
# ------------------------------------------------------------

$ProjectFile = Get-ChildItem `
    -Path $RepositoryRoot `
    -Filter "SpyxysDPSMeter.csproj" `
    -File `
    -Recurse |
    Where-Object {
        $_.FullName -notmatch '\\(bin|obj|publish|artifacts)\\'
    } |
    Select-Object -First 1

if (-not $ProjectFile) {
    throw "SpyxysDPSMeter.csproj was not found beneath $RepositoryRoot."
}

$ProjectDirectory = $ProjectFile.DirectoryName

$InstallerScript = Join-Path `
    $RepositoryRoot `
    "SpyxysDPSMeterInstaller.iss"

if (-not (Test-Path $InstallerScript)) {
    throw "SpyxysDPSMeterInstaller.iss was not found in $RepositoryRoot."
}

Write-Host "Project directory: $ProjectDirectory"
Write-Host "Installer script: $InstallerScript"
Write-Host ""

$PublishDirectory = Join-Path `
    $ProjectDirectory `
    "publish\win-x64"

$InstallerOutputDirectory = Join-Path `
    $RepositoryRoot `
    "InstallerOutput"

$InstallerFileName =
    "SpyxysDPSMeter-Setup-$Version-win-x64.exe"

$RootInstallerPath = Join-Path `
    $RepositoryRoot `
    $InstallerFileName

# ------------------------------------------------------------
# Update the installer version
# ------------------------------------------------------------

Write-Host "Updating installer version..." -ForegroundColor Yellow

$InstallerScriptContent = Get-Content `
    -Path $InstallerScript `
    -Raw

$VersionPattern =
    '(?m)^#define\s+MyAppVersion\s+"[^"]+"'

if ($InstallerScriptContent -notmatch $VersionPattern) {
    throw @"
The installer script does not contain a line like:

#define MyAppVersion "1.0.0"
"@
}

$UpdatedInstallerScriptContent = [regex]::Replace(
    $InstallerScriptContent,
    $VersionPattern,
    "#define MyAppVersion `"$Version`""
)

Set-Content `
    -Path $InstallerScript `
    -Value $UpdatedInstallerScriptContent `
    -Encoding UTF8

# ------------------------------------------------------------
# Clean old output
# ------------------------------------------------------------

Write-Host "Cleaning previous output..." -ForegroundColor Yellow

Remove-Item `
    -Path $PublishDirectory `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item `
    -Path $InstallerOutputDirectory `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

Remove-Item `
    -Path $RootInstallerPath `
    -Force `
    -ErrorAction SilentlyContinue

New-Item `
    -Path $PublishDirectory `
    -ItemType Directory `
    -Force |
    Out-Null

New-Item `
    -Path $InstallerOutputDirectory `
    -ItemType Directory `
    -Force |
    Out-Null

# ------------------------------------------------------------
# Restore and publish the nested WPF project
# ------------------------------------------------------------

Write-Host "Restoring packages..." -ForegroundColor Yellow

dotnet restore $ProjectFile.FullName

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

Write-Host "Publishing self-contained win-x64 build..." -ForegroundColor Yellow

dotnet publish $ProjectFile.FullName `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -o $PublishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$PublishedExe = Join-Path `
    $PublishDirectory `
    "SpyxysDPSMeter.exe"

if (-not (Test-Path $PublishedExe)) {
    throw "Published executable was not found at $PublishedExe."
}

# ------------------------------------------------------------
# Locate the Inno Setup command-line compiler
# ------------------------------------------------------------

function Add-InnoCandidate {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path $Path -PathType Leaf)) {
        $Candidates.Add(
            (Resolve-Path $Path).Path)
    }
}

$InnoCompilerCandidates =
    New-Object System.Collections.Generic.List[string]

# Optional explicit override:
# .\Build-Installer-And-Move.ps1 -Version 1.0.0 `
#     -InnoCompilerPath "C:\...\ISCC.exe"
Add-InnoCandidate `
    -Candidates $InnoCompilerCandidates `
    -Path $InnoCompilerPath

$ProgramFiles32 =
    [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)

$ProgramFiles64 =
    [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)

$KnownPaths = @(
    (Join-Path $ProgramFiles32 "Inno Setup 6\ISCC.exe"),
    (Join-Path $ProgramFiles64 "Inno Setup 6\ISCC.exe"),
    (Join-Path $ProgramFiles32 "Inno Setup 7\ISCC.exe"),
    (Join-Path $ProgramFiles64 "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Inno Setup 7\ISCC.exe")
)

foreach ($KnownPath in $KnownPaths) {
    Add-InnoCandidate `
        -Candidates $InnoCompilerCandidates `
        -Path $KnownPath
}

# Registry lookup. Some uninstall records do not contain DisplayName or
# InstallLocation, so properties are read safely under Strict Mode.
$UninstallRegistryPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

foreach ($RegistryPath in $UninstallRegistryPaths) {
    $RegistryEntries = Get-ItemProperty `
        -Path $RegistryPath `
        -ErrorAction SilentlyContinue

    foreach ($RegistryEntry in $RegistryEntries) {
        $DisplayNameProperty =
            $RegistryEntry.PSObject.Properties["DisplayName"]

        if ($null -eq $DisplayNameProperty) {
            continue
        }

        $DisplayName =
            [string]$DisplayNameProperty.Value

        if ($DisplayName -notlike "Inno Setup*") {
            continue
        }

        $InstallLocationProperty =
            $RegistryEntry.PSObject.Properties["InstallLocation"]

        if ($null -ne $InstallLocationProperty) {
            $InstallLocation =
                [string]$InstallLocationProperty.Value

            if (-not [string]::IsNullOrWhiteSpace($InstallLocation)) {
                Add-InnoCandidate `
                    -Candidates $InnoCompilerCandidates `
                    -Path (Join-Path $InstallLocation "ISCC.exe")
            }
        }

        $UninstallStringProperty =
            $RegistryEntry.PSObject.Properties["UninstallString"]

        if ($null -ne $UninstallStringProperty) {
            $UninstallString =
                [string]$UninstallStringProperty.Value

            if (-not [string]::IsNullOrWhiteSpace($UninstallString)) {
                $UninstallerPath =
                    $UninstallString.Trim().Trim('"')

                $UninstallerDirectory =
                    Split-Path `
                        -Parent `
                        $UninstallerPath `
                        -ErrorAction SilentlyContinue

                if (-not [string]::IsNullOrWhiteSpace(
                        $UninstallerDirectory)) {
                    Add-InnoCandidate `
                        -Candidates $InnoCompilerCandidates `
                        -Path (Join-Path $UninstallerDirectory "ISCC.exe")
                }
            }
        }
    }
}

# Resolve Start Menu shortcuts. Your reported folder is covered here:
# %APPDATA%\Microsoft\Windows\Start Menu\Programs\Inno Setup 6
$StartMenuFolders = @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Inno Setup 6"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Inno Setup 7"),
    (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Inno Setup 6"),
    (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Inno Setup 7")
)

$WshShell = New-Object -ComObject WScript.Shell

foreach ($StartMenuFolder in $StartMenuFolders) {
    if (-not (Test-Path $StartMenuFolder -PathType Container)) {
        continue
    }

    $ShortcutFiles = Get-ChildItem `
        -Path $StartMenuFolder `
        -Filter "*.lnk" `
        -File `
        -ErrorAction SilentlyContinue

    foreach ($ShortcutFile in $ShortcutFiles) {
        $Shortcut =
            $WshShell.CreateShortcut(
                $ShortcutFile.FullName)

        $ShortcutTarget =
            [string]$Shortcut.TargetPath

        if ([string]::IsNullOrWhiteSpace($ShortcutTarget)) {
            continue
        }

        if ((Split-Path -Leaf $ShortcutTarget) -ieq "ISCC.exe") {
            Add-InnoCandidate `
                -Candidates $InnoCompilerCandidates `
                -Path $ShortcutTarget
        }

        $ShortcutDirectory =
            Split-Path `
                -Parent `
                $ShortcutTarget `
                -ErrorAction SilentlyContinue

        if (-not [string]::IsNullOrWhiteSpace(
                $ShortcutDirectory)) {
            Add-InnoCandidate `
                -Candidates $InnoCompilerCandidates `
                -Path (Join-Path $ShortcutDirectory "ISCC.exe")
        }
    }
}

$InnoCompiler = $InnoCompilerCandidates |
    Select-Object -Unique |
    Select-Object -First 1

if (-not $InnoCompiler) {
    throw @"
Inno Setup is installed, but ISCC.exe could not be located automatically.

Run this command to locate it:

Get-ChildItem `
  "C:\Program Files", `
  "C:\Program Files (x86)", `
  "`$env:LOCALAPPDATA" `
  -Filter ISCC.exe `
  -File `
  -Recurse `
  -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty FullName

Then pass the returned path explicitly:

.\Build-Installer-And-Move.ps1 `
  -Version $Version `
  -InnoCompilerPath "FULL\PATH\TO\ISCC.exe"
"@
}

Write-Host "Inno Setup compiler: $InnoCompiler" -ForegroundColor Green

# ------------------------------------------------------------
# Compile the ROOT-LEVEL Inno Setup script
# ------------------------------------------------------------

Write-Host "Compiling installer..." -ForegroundColor Yellow

Push-Location $RepositoryRoot

try {
    & $InnoCompiler $InstallerScript

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed."
    }
}
finally {
    Pop-Location
}

$ExpectedInstallerPath = Join-Path `
    $InstallerOutputDirectory `
    $InstallerFileName

if (-not (Test-Path $ExpectedInstallerPath)) {
    $NewestInstaller = Get-ChildItem `
        -Path $InstallerOutputDirectory `
        -Filter "*.exe" `
        -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $NewestInstaller) {
        throw "No installer executable was created in $InstallerOutputDirectory."
    }

    $ExpectedInstallerPath =
        $NewestInstaller.FullName
}

# ------------------------------------------------------------
# Move installer from InstallerOutput into repository root
# ------------------------------------------------------------

Write-Host "Moving installer to repository root..." -ForegroundColor Yellow

Move-Item `
    -Path $ExpectedInstallerPath `
    -Destination $RootInstallerPath `
    -Force

if (-not (Test-Path $RootInstallerPath)) {
    throw "The installer was not moved successfully."
}

Write-Host ""
Write-Host "Installer created successfully:" -ForegroundColor Green
Write-Host $RootInstallerPath
Write-Host ""
Write-Host "Run the commands in GitBash-Commit-Commands.txt next."
