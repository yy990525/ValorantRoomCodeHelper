param(
    [string]$SdkPath = 'dotnet',
    [string]$CachePath = (Join-Path $PSScriptRoot 'build-cache'),
    [string]$FeedPath = '',
    [ValidateSet('Restore','Build','Test','Publish')][string]$Action='Publish',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'publish')
)
$ErrorActionPreference='Stop'
$env:APPDATA=Join-Path $CachePath 'profile\Roaming'
$env:LOCALAPPDATA=Join-Path $CachePath 'profile\Local'
$env:DOTNET_CLI_HOME=Join-Path $CachePath 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:NUGET_PACKAGES=Join-Path $CachePath 'packages'
$env:NUGET_HTTP_CACHE_PATH=Join-Path $CachePath 'http-cache'
$project=Join-Path $PSScriptRoot 'App\ValorantRoomCodeHelper.csproj'
$restoreOptions=@('--configfile',(Join-Path $PSScriptRoot 'NuGet.Config'))
if($FeedPath){$restoreOptions+=@('--source',$FeedPath)}
if($Action -eq 'Restore') { & $SdkPath restore $project @restoreOptions --verbosity minimal }
elseif($Action -eq 'Build') { & $SdkPath build $project -c Release --no-restore --verbosity minimal }
elseif($Action -eq 'Test') {
    $testProject=Join-Path $PSScriptRoot 'Tests\Tests.csproj'
    & $SdkPath restore $testProject @restoreOptions --verbosity minimal
    if($LASTEXITCODE -ne 0){throw 'Test restore failed'}
    & $SdkPath run --project $testProject -c Release --no-restore
}
else {
    & $SdkPath publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false --no-restore -o $OutputPath --verbosity minimal
}
if($LASTEXITCODE -ne 0){throw "$Action failed with exit code $LASTEXITCODE"}
