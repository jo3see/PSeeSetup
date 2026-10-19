[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'outputs\PSeeSetup 1.2.7'),
    [string]$DotnetPath = (Join-Path $PSScriptRoot 'work\sdk\dotnet.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $DotnetPath)) { $DotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source }
$buildEnvironmentNames = @('DOTNET_CLI_HOME','NUGET_PACKAGES','APPDATA','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE')
$savedBuildEnvironment = @{}
foreach ($name in $buildEnvironmentNames) { $savedBuildEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '.packages') -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'work\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'work\nuget-packages'
$env:APPDATA = Join-Path $PSScriptRoot 'work\build-appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$config = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'work\OfflineNuGet.Config')) { Join-Path $PSScriptRoot 'work\OfflineNuGet.Config' } else { Join-Path $PSScriptRoot 'NuGet.Config' }
$project = Join-Path $PSScriptRoot 'src\PCSetup.App\PCSetup.App.csproj'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($architecture in @('x64', 'arm64')) {
    & $DotnetPath publish $project -c Release -r "win-$architecture" --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -p:RestoreConfigFile=$config -o (Join-Path $OutputDirectory $architecture) --nologo
    if ($LASTEXITCODE -ne 0) { throw "The $architecture publish failed." }
}
& $DotnetPath publish (Join-Path $PSScriptRoot 'src\PCSetup.Launcher\PCSetup.Launcher.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:RestoreConfigFile=$config `
    -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'The automatic launcher publish failed.' }
Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter '*.exe' |
    ForEach-Object { Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 }

}
finally {
    foreach ($name in $buildEnvironmentNames) { [Environment]::SetEnvironmentVariable($name, $savedBuildEnvironment[$name], 'Process') }
}
