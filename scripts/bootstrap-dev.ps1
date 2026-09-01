[CmdletBinding()]
param(
    [switch]$Json,
    [switch]$Build,
    [switch]$Test,
    [string]$PythonExecutable,
    [ValidateSet(
        'PythonNotOnPath',
        'NuGetAssetsMissing',
        'AutocrlfWrong',
        'PillowMissing',
        'NumPyMissing',
        'WebView2Present'
    )]
    [string[]]$Simulate = @(),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Push-Location -LiteralPath $WorkingDirectory
    try {
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    catch {
        $output = @($_.Exception.Message)
        $exitCode = 1
    }
    finally {
        Pop-Location
    }

    return [ordered]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

function Invoke-Verify {
    param([switch]$RunBuild, [switch]$RunTest)

    $parameters = @{
        Json = $true
        NoExit = $true
    }
    if ($RunBuild) { $parameters.Build = $true }
    if ($RunTest) { $parameters.Test = $true }
    if (-not [string]::IsNullOrWhiteSpace($PythonExecutable)) { $parameters.PythonExecutable = $PythonExecutable }
    if ($Simulate.Count -gt 0) { $parameters.Simulate = $Simulate }

    $raw = @(& $script:VerifyScript @parameters) -join [Environment]::NewLine
    try {
        return ($raw | ConvertFrom-Json)
    }
    catch {
        throw "verify-dev.ps1 did not return valid JSON: $raw"
    }
}

function Add-Action {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [bool]$Required,
        [bool]$Execute
    )

    $action = [ordered]@{
        Name = $Name
        Required = $Required
        FilePath = $FilePath
        Arguments = $Arguments
        Executed = $false
        ExitCode = $null
        Passed = $null
        Output = $null
    }

    if ($Execute -and -not $DryRun) {
        $result = Invoke-CapturedCommand -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $script:RepoRoot
        $action.Executed = $true
        $action.ExitCode = $result.ExitCode
        $action.Passed = ($result.ExitCode -eq 0)
        $action.Output = $result.Output
    }
    elseif ($DryRun) {
        $action.Output = 'DRY RUN: action was planned but not executed.'
    }
    else {
        $action.Passed = $true
        $action.Output = 'Not required.'
    }

    $script:Actions.Add($action)
    return $action
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:VerifyScript = Join-Path $PSScriptRoot 'verify-dev.ps1'
$testProject = Join-Path $script:RepoRoot 'pc_ds4_server\PcDs4Server.Tests\PcDs4Server.Tests.csproj'
$script:Actions = New-Object System.Collections.Generic.List[object]

if (-not (Test-Path -LiteralPath $script:VerifyScript -PathType Leaf)) {
    throw "Required verify script is missing: $script:VerifyScript"
}

$initial = Invoke-Verify
$gitExecutable = $initial.Git.Executable
$dotnetExecutable = $initial.DotNet.Executable
$resolvedPython = $initial.Python.Executable

if ($initial.Repository.Valid -and -not [string]::IsNullOrWhiteSpace($gitExecutable)) {
    Add-Action -Name 'SetRepoLocalAutocrlfFalse' -FilePath $gitExecutable -Arguments @('-C', $script:RepoRoot, 'config', '--local', 'core.autocrlf', 'false') -Required $true -Execute $true | Out-Null
}
else {
    Add-Action -Name 'SetRepoLocalAutocrlfFalse' -FilePath $(if ($gitExecutable) { $gitExecutable } else { 'git' }) -Arguments @('-C', $script:RepoRoot, 'config', '--local', 'core.autocrlf', 'false') -Required $true -Execute $false | Out-Null
}

$missingPackages = New-Object System.Collections.Generic.List[string]
if (-not $initial.Pillow.Installed) { $missingPackages.Add('Pillow') }
if (-not $initial.NumPy.Installed) { $missingPackages.Add('NumPy') }
if ($missingPackages.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($resolvedPython)) {
    $pipArguments = @('-m', 'pip', 'install') + @($missingPackages)
    Add-Action -Name 'InstallPythonPackages' -FilePath $resolvedPython -Arguments $pipArguments -Required $true -Execute $true | Out-Null
}
else {
    Add-Action -Name 'InstallPythonPackages' -FilePath $(if ($resolvedPython) { $resolvedPython } else { '<python-unavailable>' }) -Arguments @('-m', 'pip', 'install') -Required ($missingPackages.Count -gt 0) -Execute $false | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($dotnetExecutable) -and $initial.Repository.Valid) {
    Add-Action -Name 'RestoreNuGetAssets' -FilePath $dotnetExecutable -Arguments @('restore', $testProject) -Required $true -Execute $true | Out-Null
}
else {
    Add-Action -Name 'RestoreNuGetAssets' -FilePath $(if ($dotnetExecutable) { $dotnetExecutable } else { 'dotnet' }) -Arguments @('restore', $testProject) -Required $true -Execute $false | Out-Null
}

$actionFailures = @($script:Actions | Where-Object { $_.Executed -and $_.Passed -eq $false })
if ($DryRun) {
    $final = $initial
}
else {
    $final = Invoke-Verify -RunBuild:$Build -RunTest:$Test
}

$blockingReasons = New-Object System.Collections.Generic.List[string]
foreach ($reason in @($final.BlockingReasons)) {
    if (-not [string]::IsNullOrWhiteSpace($reason)) { $blockingReasons.Add($reason) }
}
foreach ($failure in $actionFailures) {
    $blockingReasons.Add("Bootstrap action failed: $($failure.Name), exit $($failure.ExitCode).")
}
if ($DryRun) {
    $blockingReasons.Add('Dry-run mode does not mutate or restore the environment.')
}

$verdict = $(if ($blockingReasons.Count -eq 0) { 'READY' } else { 'BLOCKED' })
$summary = [ordered]@{
    SchemaVersion = 1
    Mode = 'Bootstrap'
    Repository = $final.Repository
    Git = $final.Git
    Branch = $final.Branch
    HEAD = $final.HEAD
    WorkingTree = $final.WorkingTree
    Autocrlf = $final.Autocrlf
    DotNet = $final.DotNet
    Python = $final.Python
    Pillow = $final.Pillow
    NumPy = $final.NumPy
    NuGetAssets = $final.NuGetAssets
    WebView2Absent = $final.WebView2Absent
    BuildReady = $final.BuildReady
    TestReady = $final.TestReady
    Build = $final.Build
    Tests = $final.Tests
    InitialVerify = [ordered]@{
        Verdict = $initial.Verdict
        WorkingTree = $initial.WorkingTree
        Autocrlf = $initial.Autocrlf
        PythonFound = $initial.Python.Found
        NuGetStatus = $initial.NuGetAssets.Status
    }
    Actions = @($script:Actions | ForEach-Object { $_ })
    Simulation = @($Simulate)
    DryRun = [bool]$DryRun
    BlockingReasons = @($blockingReasons)
    Verdict = $verdict
}

if ($Json) {
    $summary | ConvertTo-Json -Depth 14
}
else {
    Write-Output 'TWO-PC DEV ENVIRONMENT BOOTSTRAP'
    Write-Output "Repository : $($summary.Repository.Root)"
    Write-Output "Working    : $($summary.WorkingTree) (reported only; never cleaned automatically)"
    foreach ($action in $summary.Actions) {
        $state = if ($action.Executed) { "exit=$($action.ExitCode)" } elseif ($DryRun) { 'planned' } else { 'not required' }
        Write-Output "Action     : $($action.Name) - $state"
    }
    Write-Output "Local EOL  : core.autocrlf=$($summary.Autocrlf.Actual)"
    Write-Output "Python     : $($summary.Python.Executable); $($summary.Python.Version)"
    Write-Output "Pillow     : imported=$($summary.Pillow.ImportVerified); version=$($summary.Pillow.Version)"
    Write-Output "NumPy      : imported=$($summary.NumPy.ImportVerified); version=$($summary.NumPy.Version)"
    Write-Output "NuGet      : $($summary.NuGetAssets.Status)"
    Write-Output "BuildReady : $($summary.BuildReady)"
    Write-Output "TestReady  : $($summary.TestReady)"
    if ($summary.BlockingReasons.Count -gt 0) {
        Write-Output 'Blocking reasons:'
        foreach ($reason in $summary.BlockingReasons) {
            Write-Output "  - $reason"
        }
    }
    Write-Output "Verdict    : $($summary.Verdict)"
}

if ($summary.Verdict -eq 'READY') { exit 0 } else { exit 1 }
