# Two-PC development bootstrap

These scripts prepare and verify a Windows checkout without changing product code, the Git index, tracked files, global Git configuration, or user settings.

For a new clone, set the EOL policy before checkout:

```powershell
git clone -c core.autocrlf=false https://github.com/zhqxbmgit/leftpad.git
```

For an existing checkout, run the bootstrap from PowerShell:

```powershell
.\scripts\bootstrap-dev.ps1
```

Bootstrap performs only these environment actions:

1. Sets `core.autocrlf=false` in this repository's local Git config.
2. Discovers Python through an explicit `-PythonExecutable`, `LEFTPAD_PYTHON`, the `py` launcher, `python`/`python3` on `PATH`, or common `%LOCALAPPDATA%` Python installations.
3. Imports Pillow and NumPy. Missing packages are installed with the discovered interpreter itself: `<python> -m pip install ...`.
4. Restores `PcDs4Server.Tests.csproj`; its project reference also prepares Production NuGet assets.
5. Calls `verify-dev.ps1` and returns `READY` or `BLOCKED` with reasons.

Bootstrap does not install Python or .NET. Install a Python interpreter that can run the repository's Python tools if discovery reports no interpreter. The scripts use language features supported by Python 3.10 and later, including Python 3.14. Package compatibility is verified by importing Pillow and NumPy with the exact discovered interpreter.

If Python is installed in a nonstandard location, use either portable override:

```powershell
$env:LEFTPAD_PYTHON = 'D:\Tools\Python314\python.exe'
.\scripts\bootstrap-dev.ps1
```

or:

```powershell
.\scripts\bootstrap-dev.ps1 -PythonExecutable 'D:\Tools\Python314\python.exe'
```

Neither path is written to the repository.

Verification is read-only:

```powershell
.\scripts\verify-dev.ps1
.\scripts\verify-dev.ps1 -Json
.\scripts\verify-dev.ps1 -Build
.\scripts\verify-dev.ps1 -Build -Test
```

`-Build` runs both Release builds with `--no-restore`. `-Test` runs the complete C# project and then uses the discovered Python interpreter for:

```text
-B -m unittest discover -s tests -v
```

`verify-dev.ps1` reports a modified working tree but never resets, restores, stages, or cleans it. `bootstrap-dev.ps1` has the same preservation rule. Existing changes remain untouched.

Both scripts support `-Json`. The output includes Git, branch, HEAD, working-tree state, repo-local EOL policy, `.NET`, Python executable/version/architecture, Pillow, NumPy, NuGet assets, WebView2 absence, build readiness, test readiness, and the final verdict.

WebView2 is intentionally not a prerequisite. Verification instead asserts that `PcDs4Server.csproj` does not reference `Microsoft.Web.WebView2`.

The `-Simulate` and bootstrap `-DryRun` parameters are test seams for deterministic second-PC failure coverage. `-DryRun` lists planned actions without modifying Git config, installing packages, or restoring NuGet assets.
