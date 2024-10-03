param (
    [switch] $install,
    [switch] $start,
    [switch] $restart,
    [switch] $stop,
    [switch] $update,
    [switch] $rebuild,
    [switch] $updateconf,
    [switch] $renewcert,
    [switch] $updatedb,
    [switch] $updaterun,
    [switch] $updateself,
    [switch] $uninstall,
    [switch] $help,
    [string] $output = ""
)

# Setup

$scriptPath = $MyInvocation.MyCommand.Path
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($output -eq "") {
    $output = "${dir}\bwdata"
}

$scriptsDir = "${output}\scripts"
$bitwardenScriptUrl = "https://func.bitwarden.com/api/dl/?app=self-host&platform=windows"
$runScriptUrl = "https://func.bitwarden.com/api/dl/?app=self-host&platform=windows&variant=run"

# Please do not create pull requests modifying the version numbers.
$coreVersion = "2024.9.2"
$webVersion = "2024.10.0"
$keyConnectorVersion = "2024.8.0"

# Functions

function Get-Self {
    Invoke-RestMethod -OutFile $scriptPath -Uri $bitwardenScriptUrl
}

function Get-Run-File {
    if (!(Test-Path -Path $scriptsDir)) {
        New-Item -ItemType directory -Path $scriptsDir | Out-Null
    }
    Invoke-RestMethod -OutFile $scriptsDir\run.ps1 -Uri $runScriptUrl
}

function Test-Output-Dir-Exists {
    if (!(Test-Path -Path $output)) {
        throw "Cannot find a Bitwarden installation at $output."
    }
}

function Test-Output-Dir-Not-Exists {
    if (Test-Path -Path "$output\docker") {
        throw "Looks like Bitwarden is already installed at $output."
    }
}

function Show-Commands {
    Write-Line "
Available commands:

-install
-start
-restart
-stop
-update
-updatedb
-updaterun
-updateself
-updateconf
-uninstall
-renewcert
-rebuild
-help

See more at https://bitwarden.com/help/article/install-on-premise/#script-commands-reference
"
}

function Write-Line($str) {
    if ($env:BITWARDEN_QUIET -ne "true") {
        Write-Host $str
    }
}

# Intro

$year = (Get-Date).year

Write-Line @'
 _     _ _                         _            
| |__ (_) |___      ____ _ _ __ __| | ___ _ __  
| '_ \| | __\ \ /\ / / _` | '__/ _` |/ _ \ '_ \ 
| |_) | | |_ \ V  V / (_| | | | (_| |  __/ | | |
|_.__/|_|\__| \_/\_/ \__,_|_|  \__,_|\___|_| |_|
'@

Write-Line "
Open source password management solutions
Copyright 2015-${year}, 8bit Solutions LLC
https://bitwarden.com, https://github.com/bitwarden

===================================================
"

if ($env:BITWARDEN_QUIET -ne "true") {
    Write-Line "bitwarden.ps1 version ${coreVersion}"
    docker --version
    docker-compose --version
}

Write-Line ""

# Commands

if ($install) {
    Test-Output-Dir-Not-Exists
    New-Item -ItemType directory -Path $output -ErrorAction Ignore | Out-Null
    Get-Run-File
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -install -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($start -Or $restart) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -restart -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($update) {
    Test-Output-Dir-Exists
    Get-Run-File
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -update -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($rebuild) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -rebuild -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($updateconf) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -updateconf -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($updatedb) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -updatedb -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($stop) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -stop -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($renewcert) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -renewcert -outputDir `"$output`" -coreVersion $coreVersion -webVersion $webVersion -keyConnectorVersion $keyConnectorVersion"
}
elseif ($updaterun) {
    Test-Output-Dir-Exists
    Get-Run-File
}
elseif ($updateself) {
    Get-Self
    Write-Line "Updated self."
}
elseif ($uninstall) {
    Test-Output-Dir-Exists
    Invoke-Expression "& `"$scriptsDir\run.ps1`" -uninstall -outputDir `"$output`" "
}
elseif ($help) {
    Show-Commands
}
else {
    Write-Line "No command found."
    Write-Line ""
    Show-Commands
}
