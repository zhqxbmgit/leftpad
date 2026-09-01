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
    [switch]$NoExit
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

function Test-Simulation {
    param([string]$Name)
    return $Simulate -contains $Name
}

function Resolve-PythonInterpreter {
    param([string]$ExplicitExecutable)

    $candidates = New-Object System.Collections.Generic.List[object]
    if (-not [string]::IsNullOrWhiteSpace($ExplicitExecutable)) {
        $candidates.Add([pscustomobject]@{Kind='Explicit'; Command=$ExplicitExecutable; Arguments=@()})
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:LEFTPAD_PYTHON)) {
        $candidates.Add([pscustomobject]@{Kind='LEFTPAD_PYTHON'; Command=$env:LEFTPAD_PYTHON; Arguments=@()})
    }

    $pathCommandsAvailable = $false
    if (-not (Test-Simulation 'PythonNotOnPath')) {
        $py = Get-Command py -ErrorAction SilentlyContinue
        if ($null -ne $py) {
            $pathCommandsAvailable = $true
            $candidates.Add([pscustomobject]@{Kind='PyLauncher314'; Command=$py.Source; Arguments=@('-3.14')})
            $candidates.Add([pscustomobject]@{Kind='PyLauncher3'; Command=$py.Source; Arguments=@('-3')})
        }

        foreach ($name in @('python', 'python3')) {
            $command = Get-Command $name -ErrorAction SilentlyContinue
            if ($null -ne $command) {
                $pathCommandsAvailable = $true
                $candidates.Add([pscustomobject]@{Kind="Path:$name"; Command=$command.Source; Arguments=@()})
            }
        }
    }

    $localRoots = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python'),
        (Join-Path $env:LOCALAPPDATA 'Python')
    )
    foreach ($root in $localRoots) {
        if (Test-Path -LiteralPath $root -PathType Container) {
            $localExecutables = @(Get-ChildItem -LiteralPath $root -Filter python.exe -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending)
            foreach ($item in $localExecutables) {
                $candidates.Add([pscustomobject]@{Kind='LocalAppData'; Command=$item.FullName; Arguments=@()})
            }
        }
    }

    $seen = @{}
    $incompatibleFallback = $null
    foreach ($candidate in $candidates) {
        $key = "$($candidate.Command)|$($candidate.Arguments -join ' ')"
        if ($seen.ContainsKey($key)) {
            continue
        }
        $seen[$key] = $true

        $probeArguments = @($candidate.Arguments) + @(
            '-c',
            'import json, platform, struct, sys; print(json.dumps({"executable": sys.executable, "version": platform.python_version(), "major": sys.version_info.major, "minor": sys.version_info.minor, "architecture": str(struct.calcsize("P") * 8) + "bit"}))'
        )
        $probe = Invoke-CapturedCommand -FilePath $candidate.Command -Arguments $probeArguments -WorkingDirectory $script:RepoRoot
        if ($probe.ExitCode -ne 0) {
            continue
        }

        try {
            $data = $probe.Output.Trim() | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($data.executable)) {
                continue
            }
            $source = $candidate.Kind
            if ((Test-Simulation 'PythonNotOnPath') -and $source -eq 'Explicit') {
                $source = 'SimulatedLocalAppData'
            }
            $result = [ordered]@{
                Found = $true
                Executable = $data.executable
                Version = $data.version
                Architecture = $data.architecture
                Major = [int]$data.major
                Minor = [int]$data.minor
                CompatibleWith314Scripts = ([int]$data.major -eq 3 -and [int]$data.minor -ge 10)
                DiscoverySource = $source
                PathCommandsAvailable = $pathCommandsAvailable
                Error = $null
            }
            if ($result.CompatibleWith314Scripts) {
                return $result
            }
            if ($null -eq $incompatibleFallback) {
                $incompatibleFallback = $result
            }
        }
        catch {
            continue
        }
    }

    if ($null -ne $incompatibleFallback) {
        return $incompatibleFallback
    }

    return [ordered]@{
        Found = $false
        Executable = $null
        Version = $null
        Architecture = $null
        Major = $null
        Minor = $null
        CompatibleWith314Scripts = $false
        DiscoverySource = $null
        PathCommandsAvailable = $pathCommandsAvailable
        Error = 'No working Python interpreter was found through -PythonExecutable, LEFTPAD_PYTHON, py launcher, PATH, or common LocalAppData installations.'
    }
}

function Test-PythonPackage {
    param(
        [hashtable]$Python,
        [string]$ImportName,
        [string]$DistributionName,
        [string]$SimulationName
    )

    if (-not $Python.Found) {
        return [ordered]@{Installed=$false; Version=$null; ImportVerified=$false; Error='Python interpreter unavailable.'}
    }

    $code = "import importlib, importlib.metadata, json; importlib.import_module('$ImportName'); print(json.dumps({'version': importlib.metadata.version('$DistributionName')}))"
    $probe = Invoke-CapturedCommand -FilePath $Python.Executable -Arguments @('-B', '-c', $code) -WorkingDirectory $script:RepoRoot
    if ($probe.ExitCode -ne 0) {
        return [ordered]@{Installed=$false; Version=$null; ImportVerified=$false; Error=$probe.Output.Trim()}
    }

    try {
        $version = ($probe.Output.Trim() | ConvertFrom-Json).version
        $installed = $true
        $error = $null
    }
    catch {
        $version = $null
        $installed = $false
        $error = "Package imported but its version probe was not valid JSON: $($_.Exception.Message)"
    }

    if (Test-Simulation $SimulationName) {
        $installed = $false
        $version = $null
        $error = "Simulated missing package: $DistributionName"
    }

    return [ordered]@{Installed=$installed; Version=$version; ImportVerified=$installed; Error=$error}
}

function Test-NuGetAssetsFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{Path=$Path; Exists=$false; Valid=$false; Status='RESTORE REQUIRED'; Error='project.assets.json is missing.'}
    }

    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        $targetCount = @($json.targets.PSObject.Properties).Count
        $valid = ($null -ne $json.version -and $targetCount -gt 0 -and $null -ne $json.project.restore.projectPath)
        return [ordered]@{
            Path = $Path
            Exists = $true
            Valid = $valid
            Status = $(if ($valid) { 'READY' } else { 'RESTORE REQUIRED' })
            Error = $(if ($valid) { $null } else { 'project.assets.json is present but incomplete or invalid.' })
        }
    }
    catch {
        return [ordered]@{Path=$Path; Exists=$true; Valid=$false; Status='RESTORE REQUIRED'; Error=$_.Exception.Message}
    }
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$productionProject = Join-Path $script:RepoRoot 'pc_ds4_server\PcDs4Server\PcDs4Server.csproj'
$testProject = Join-Path $script:RepoRoot 'pc_ds4_server\PcDs4Server.Tests\PcDs4Server.Tests.csproj'
$attributesPath = Join-Path $script:RepoRoot '.gitattributes'
$productionAssetsPath = Join-Path $script:RepoRoot 'pc_ds4_server\PcDs4Server\obj\project.assets.json'
$testAssetsPath = Join-Path $script:RepoRoot 'pc_ds4_server\PcDs4Server.Tests\obj\project.assets.json'

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$git = [ordered]@{
    Available = ($null -ne $gitCommand)
    Executable = $(if ($null -ne $gitCommand) { $gitCommand.Source } else { $null })
    RepositoryValid = $false
    TopLevel = $null
    Branch = $null
    HEAD = $null
    WorkingTree = 'UNKNOWN'
    WorkingEntries = @()
    StagedEntries = @()
    UntrackedEntries = @()
    LocalAutocrlf = $null
    AutocrlfReady = $false
    Error = $null
}

if ($git.Available) {
    try {
        $topLevel = (& $git.Executable -C $script:RepoRoot rev-parse --show-toplevel 2>$null).Trim()
        $git.TopLevel = $topLevel
        $git.RepositoryValid = (-not [string]::IsNullOrWhiteSpace($topLevel) -and
            [System.IO.Path]::GetFullPath($topLevel).TrimEnd('\') -ieq $script:RepoRoot.TrimEnd('\') -and
            (Test-Path -LiteralPath $productionProject -PathType Leaf) -and
            (Test-Path -LiteralPath $testProject -PathType Leaf))
        if ($git.RepositoryValid) {
            $git.Branch = (& $git.Executable -C $script:RepoRoot branch --show-current).Trim()
            $git.HEAD = (& $git.Executable -C $script:RepoRoot rev-parse HEAD).Trim()
            $git.WorkingEntries = @(& $git.Executable -C $script:RepoRoot diff --name-only)
            $git.StagedEntries = @(& $git.Executable -C $script:RepoRoot diff --cached --name-only)
            $git.UntrackedEntries = @(& $git.Executable -C $script:RepoRoot ls-files --others --exclude-standard)
            $git.WorkingTree = $(if (($git.WorkingEntries.Count + $git.StagedEntries.Count + $git.UntrackedEntries.Count) -eq 0) { 'CLEAN' } else { 'MODIFIED' })
            $git.LocalAutocrlf = (& $git.Executable -C $script:RepoRoot config --local --get core.autocrlf 2>$null).Trim()
            $git.AutocrlfReady = ($git.LocalAutocrlf -eq 'false')
        }
    }
    catch {
        $git.Error = $_.Exception.Message
    }
}
else {
    $git.Error = 'git executable was not found.'
}

if (Test-Simulation 'AutocrlfWrong') {
    $git.LocalAutocrlf = 'true'
    $git.AutocrlfReady = $false
}

$requiredLfRules = @(
    'pc_ds4_server/PcDs4Server/Assets/UIVisualPacks/*/*.json text eol=lf',
    'pc_ds4_server/PcDs4Server/Assets/UIThemes/*/*.json text eol=lf'
)
$attributesLines = $(if (Test-Path -LiteralPath $attributesPath -PathType Leaf) { @(Get-Content -LiteralPath $attributesPath) } else { @() })
$missingLfRules = @($requiredLfRules | Where-Object { $attributesLines -notcontains $_ })
$gitAttributes = [ordered]@{
    Path = $attributesPath
    Exists = (Test-Path -LiteralPath $attributesPath -PathType Leaf)
    RequiredRules = $requiredLfRules
    MissingRules = $missingLfRules
    Ready = ((Test-Path -LiteralPath $attributesPath -PathType Leaf) -and $missingLfRules.Count -eq 0)
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = [ordered]@{
    Available = ($null -ne $dotnetCommand)
    Executable = $(if ($null -ne $dotnetCommand) { $dotnetCommand.Source } else { $null })
    Version = $null
    Info = $null
    InstalledSdks = @()
    Net8SdkAvailable = $false
    Error = $null
}
if ($dotnet.Available) {
    $versionProbe = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('--version') -WorkingDirectory $script:RepoRoot
    $infoProbe = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('--info') -WorkingDirectory $script:RepoRoot
    $sdkProbe = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('--list-sdks') -WorkingDirectory $script:RepoRoot
    if ($versionProbe.ExitCode -eq 0 -and $infoProbe.ExitCode -eq 0 -and $sdkProbe.ExitCode -eq 0) {
        $dotnet.Version = $versionProbe.Output.Trim()
        $dotnet.Info = $infoProbe.Output.Trim()
        $dotnet.InstalledSdks = @($sdkProbe.Output -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $dotnet.Net8SdkAvailable = (@($dotnet.InstalledSdks | Where-Object { $_ -match '^8\.' }).Count -gt 0)
    }
    else {
        $dotnet.Error = "dotnet probe failed. Version: $($versionProbe.Output) Info: $($infoProbe.Output) SDKs: $($sdkProbe.Output)"
    }
}
else {
    $dotnet.Error = '.NET SDK executable was not found.'
}

$python = Resolve-PythonInterpreter -ExplicitExecutable $PythonExecutable
$pillow = Test-PythonPackage -Python $python -ImportName 'PIL' -DistributionName 'Pillow' -SimulationName 'PillowMissing'
$numpy = Test-PythonPackage -Python $python -ImportName 'numpy' -DistributionName 'numpy' -SimulationName 'NumPyMissing'

$productionAssets = Test-NuGetAssetsFile -Path $productionAssetsPath
$testAssets = Test-NuGetAssetsFile -Path $testAssetsPath
if (Test-Simulation 'NuGetAssetsMissing') {
    foreach ($asset in @($productionAssets, $testAssets)) {
        $asset.Exists = $false
        $asset.Valid = $false
        $asset.Status = 'RESTORE REQUIRED'
        $asset.Error = 'Simulated missing project.assets.json.'
    }
}
$nugetAssets = [ordered]@{
    Production = $productionAssets
    Tests = $testAssets
    Ready = ($productionAssets.Valid -and $testAssets.Valid)
    Status = $(if ($productionAssets.Valid -and $testAssets.Valid) { 'READY' } else { 'RESTORE REQUIRED' })
}

$projectText = $(if (Test-Path -LiteralPath $productionProject -PathType Leaf) { [System.IO.File]::ReadAllText($productionProject) } else { '' })
$webView2References = @([regex]::Matches($projectText, 'Microsoft\.Web\.WebView2', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase))
$webView2Absent = ($webView2References.Count -eq 0)
if (Test-Simulation 'WebView2Present') {
    $webView2Absent = $false
}

$buildResults = [ordered]@{
    Requested = [bool]$Build
    Production = [ordered]@{Ran=$false; Passed=$null; ExitCode=$null; Output=$null}
    Tests = [ordered]@{Ran=$false; Passed=$null; ExitCode=$null; Output=$null}
}
if ($Build) {
    if ($dotnet.Available) {
        $productionBuild = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('build', $productionProject, '-c', 'Release', '--no-restore') -WorkingDirectory $script:RepoRoot
        $testBuild = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('build', $testProject, '-c', 'Release', '--no-restore') -WorkingDirectory $script:RepoRoot
        $buildResults.Production = [ordered]@{Ran=$true; Passed=($productionBuild.ExitCode -eq 0); ExitCode=$productionBuild.ExitCode; Output=$productionBuild.Output}
        $buildResults.Tests = [ordered]@{Ran=$true; Passed=($testBuild.ExitCode -eq 0); ExitCode=$testBuild.ExitCode; Output=$testBuild.Output}
    }
    else {
        $buildResults.Production = [ordered]@{Ran=$false; Passed=$false; ExitCode=$null; Output='.NET SDK unavailable.'}
        $buildResults.Tests = [ordered]@{Ran=$false; Passed=$false; ExitCode=$null; Output='.NET SDK unavailable.'}
    }
}

$testResults = [ordered]@{
    Requested = [bool]$Test
    CSharp = [ordered]@{Ran=$false; Passed=$null; ExitCode=$null; Output=$null}
    Python = [ordered]@{Ran=$false; Passed=$null; ExitCode=$null; Output=$null; Executable=$python.Executable}
}
if ($Test) {
    if ($dotnet.Available) {
        $csharpTest = Invoke-CapturedCommand -FilePath $dotnet.Executable -Arguments @('test', $testProject, '-c', 'Release', '--no-restore') -WorkingDirectory $script:RepoRoot
        $testResults.CSharp = [ordered]@{Ran=$true; Passed=($csharpTest.ExitCode -eq 0); ExitCode=$csharpTest.ExitCode; Output=$csharpTest.Output}
    }
    else {
        $testResults.CSharp = [ordered]@{Ran=$false; Passed=$false; ExitCode=$null; Output='.NET SDK unavailable.'}
    }

    if ($python.Found) {
        $pythonTest = Invoke-CapturedCommand -FilePath $python.Executable -Arguments @('-B', '-m', 'unittest', 'discover', '-s', 'tests', '-v') -WorkingDirectory $script:RepoRoot
        $testResults.Python = [ordered]@{Ran=$true; Passed=($pythonTest.ExitCode -eq 0); ExitCode=$pythonTest.ExitCode; Output=$pythonTest.Output; Executable=$python.Executable}
    }
    else {
        $testResults.Python = [ordered]@{Ran=$false; Passed=$false; ExitCode=$null; Output='Python interpreter unavailable.'; Executable=$null}
    }
}

$buildReady = ($dotnet.Net8SdkAvailable -and $nugetAssets.Ready)
if ($Build) {
    $buildReady = ($buildReady -and $buildResults.Production.Passed -and $buildResults.Tests.Passed)
}
$testReady = ($buildReady -and $python.Found -and $python.CompatibleWith314Scripts -and $pillow.Installed -and $numpy.Installed)
if ($Test) {
    $testReady = ($testReady -and $testResults.CSharp.Passed -and $testResults.Python.Passed)
}

$blockingReasons = New-Object System.Collections.Generic.List[string]
if (-not $git.Available) { $blockingReasons.Add('git executable is unavailable.') }
if (-not $git.RepositoryValid) { $blockingReasons.Add('The script is not anchored to a valid leftpad repository checkout.') }
if (-not $git.AutocrlfReady) { $blockingReasons.Add("Repo-local core.autocrlf must be false; actual: '$($git.LocalAutocrlf)'.") }
if (-not $gitAttributes.Ready) { $blockingReasons.Add(".gitattributes is missing required LF rules: $($gitAttributes.MissingRules -join ', ')") }
if (-not $dotnet.Net8SdkAvailable) { $blockingReasons.Add('.NET 8 SDK is unavailable.') }
if (-not $python.Found) { $blockingReasons.Add($python.Error) }
elseif (-not $python.CompatibleWith314Scripts) { $blockingReasons.Add("Python $($python.Version) does not meet the supported Python 3.10+ contract used for Python 3.14-compatible scripts.") }
if ($python.Found -and -not $pillow.Installed) { $blockingReasons.Add("Pillow import failed: $($pillow.Error)") }
if ($python.Found -and -not $numpy.Installed) { $blockingReasons.Add("NumPy import failed: $($numpy.Error)") }
if (-not $nugetAssets.Ready) { $blockingReasons.Add('NuGet project assets are missing or invalid: RESTORE REQUIRED.') }
if (-not $webView2Absent) { $blockingReasons.Add('PcDs4Server.csproj contains Microsoft.Web.WebView2; WebView2 must remain absent.') }
if ($Build -and -not $buildReady) { $blockingReasons.Add('One or more requested Release builds failed.') }
if ($Test -and -not $testReady) { $blockingReasons.Add('One or more requested C# or Python tests failed.') }

$summary = [ordered]@{
    SchemaVersion = 1
    Mode = 'Verify'
    Repository = [ordered]@{Root=$script:RepoRoot; Valid=$git.RepositoryValid}
    Git = $git
    Branch = $git.Branch
    HEAD = $git.HEAD
    WorkingTree = $git.WorkingTree
    Autocrlf = [ordered]@{Scope='repo-local'; Actual=$git.LocalAutocrlf; Ready=$git.AutocrlfReady}
    GitAttributes = $gitAttributes
    DotNet = $dotnet
    Python = $python
    Pillow = $pillow
    NumPy = $numpy
    NuGetAssets = $nugetAssets
    WebView2Absent = $webView2Absent
    Build = $buildResults
    Tests = $testResults
    BuildReady = $buildReady
    TestReady = $testReady
    Simulation = @($Simulate)
    BlockingReasons = @($blockingReasons)
    Verdict = $(if ($blockingReasons.Count -eq 0) { 'READY' } else { 'BLOCKED' })
}

if ($Json) {
    $summary | ConvertTo-Json -Depth 12
}
else {
    Write-Output 'TWO-PC DEV ENVIRONMENT VERIFY'
    Write-Output "Repository : $($summary.Repository.Root)"
    Write-Output "Git        : $($summary.Git.Executable)"
    Write-Output "Branch     : $($summary.Branch)"
    Write-Output "HEAD       : $($summary.HEAD)"
    Write-Output "Working    : $($summary.WorkingTree)"
    Write-Output "EOL        : repo-local core.autocrlf=$($summary.Autocrlf.Actual); ready=$($summary.Autocrlf.Ready)"
    Write-Output ".NET       : $($summary.DotNet.Version); .NET 8 SDK=$($summary.DotNet.Net8SdkAvailable)"
    Write-Output "Python     : $($summary.Python.Executable); $($summary.Python.Version); $($summary.Python.Architecture)"
    Write-Output "Pillow     : imported=$($summary.Pillow.ImportVerified); version=$($summary.Pillow.Version)"
    Write-Output "NumPy      : imported=$($summary.NumPy.ImportVerified); version=$($summary.NumPy.Version)"
    Write-Output "NuGet      : $($summary.NuGetAssets.Status)"
    Write-Output "WebView2   : absent=$($summary.WebView2Absent); runtime not required"
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

if (-not $NoExit) {
    if ($summary.Verdict -eq 'READY') { exit 0 } else { exit 1 }
}
