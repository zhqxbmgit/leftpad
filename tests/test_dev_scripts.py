import json
import shutil
import subprocess
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
VERIFY_SCRIPT = REPO_ROOT / "scripts" / "verify-dev.ps1"
BOOTSTRAP_SCRIPT = REPO_ROOT / "scripts" / "bootstrap-dev.ps1"


def find_powershell() -> str:
    for name in ("pwsh", "powershell.exe", "powershell"):
        executable = shutil.which(name)
        if executable:
            return executable
    raise RuntimeError("PowerShell was not found.")


POWERSHELL = find_powershell()


def run_json(script: Path, *arguments: str) -> tuple[subprocess.CompletedProcess[str], dict]:
    command = [
        POWERSHELL,
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script),
        "-Json",
        "-PythonExecutable",
        sys.executable,
        *arguments,
    ]
    completed = subprocess.run(
        command,
        cwd=REPO_ROOT,
        text=True,
        encoding="utf-8-sig",
        capture_output=True,
        check=False,
    )
    try:
        payload = json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        raise AssertionError(
            f"{script.name} did not emit valid JSON.\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        ) from exc
    return completed, payload


class VerifyDevTests(unittest.TestCase):
    def test_current_environment_json_has_required_authority(self) -> None:
        completed, payload = run_json(VERIFY_SCRIPT)

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("Verify", payload["Mode"])
        expected_branch = subprocess.check_output(
            ["git", "branch", "--show-current"], cwd=REPO_ROOT, text=True
        ).strip()
        expected_head = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, text=True
        ).strip()
        self.assertEqual(expected_branch, payload["Branch"])
        self.assertEqual(expected_head, payload["HEAD"])
        self.assertTrue(payload["Repository"]["Valid"])
        self.assertEqual("false", payload["Autocrlf"]["Actual"])
        self.assertTrue(payload["GitAttributes"]["Ready"])
        self.assertTrue(payload["DotNet"]["Net8SdkAvailable"])
        self.assertEqual(str(Path(sys.executable)), str(Path(payload["Python"]["Executable"])))
        self.assertTrue(payload["Python"]["CompatibleWith314Scripts"])
        self.assertTrue(payload["Pillow"]["ImportVerified"])
        self.assertTrue(payload["NumPy"]["ImportVerified"])
        self.assertEqual("READY", payload["NuGetAssets"]["Status"])
        self.assertTrue(payload["WebView2Absent"])
        self.assertEqual("READY", payload["Verdict"])

    def test_python_not_on_path_uses_non_path_discovery_seam(self) -> None:
        completed, payload = run_json(VERIFY_SCRIPT, "-Simulate", "PythonNotOnPath")

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertFalse(payload["Python"]["PathCommandsAvailable"])
        self.assertEqual("SimulatedLocalAppData", payload["Python"]["DiscoverySource"])
        self.assertTrue(payload["Python"]["Found"])
        self.assertEqual("READY", payload["Verdict"])

    def test_missing_nuget_assets_reports_restore_required_without_restoring(self) -> None:
        before = subprocess.check_output(
            ["git", "status", "--porcelain=v1", "--untracked-files=all"],
            cwd=REPO_ROOT,
            text=True,
        )
        completed, payload = run_json(VERIFY_SCRIPT, "-Simulate", "NuGetAssetsMissing")
        after = subprocess.check_output(
            ["git", "status", "--porcelain=v1", "--untracked-files=all"],
            cwd=REPO_ROOT,
            text=True,
        )

        self.assertEqual(1, completed.returncode)
        self.assertEqual("RESTORE REQUIRED", payload["NuGetAssets"]["Status"])
        self.assertEqual("BLOCKED", payload["Verdict"])
        self.assertEqual(before, after, "verify mode changed the repository")

    def test_wrong_autocrlf_is_blocking_and_repo_local(self) -> None:
        completed, payload = run_json(VERIFY_SCRIPT, "-Simulate", "AutocrlfWrong")

        self.assertEqual(1, completed.returncode)
        self.assertEqual("repo-local", payload["Autocrlf"]["Scope"])
        self.assertEqual("true", payload["Autocrlf"]["Actual"])
        self.assertFalse(payload["Autocrlf"]["Ready"])
        self.assertEqual("BLOCKED", payload["Verdict"])

    def test_missing_python_packages_are_distinct_blockers(self) -> None:
        for simulation, field in (("PillowMissing", "Pillow"), ("NumPyMissing", "NumPy")):
            with self.subTest(simulation=simulation):
                completed, payload = run_json(VERIFY_SCRIPT, "-Simulate", simulation)
                self.assertEqual(1, completed.returncode)
                self.assertFalse(payload[field]["ImportVerified"])
                self.assertEqual("BLOCKED", payload["Verdict"])

    def test_webview2_is_absent_and_not_a_requirement(self) -> None:
        completed, payload = run_json(VERIFY_SCRIPT)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertTrue(payload["WebView2Absent"])

        simulated, simulated_payload = run_json(VERIFY_SCRIPT, "-Simulate", "WebView2Present")
        self.assertEqual(1, simulated.returncode)
        self.assertFalse(simulated_payload["WebView2Absent"])
        self.assertEqual("BLOCKED", simulated_payload["Verdict"])


class BootstrapDevTests(unittest.TestCase):
    def action(self, payload: dict, name: str) -> dict:
        return next(action for action in payload["Actions"] if action["Name"] == name)

    def test_dry_run_plans_only_repo_local_git_config(self) -> None:
        completed, payload = run_json(
            BOOTSTRAP_SCRIPT, "-DryRun", "-Simulate", "AutocrlfWrong"
        )

        self.assertEqual(1, completed.returncode)
        action = self.action(payload, "SetRepoLocalAutocrlfFalse")
        self.assertFalse(action["Executed"])
        self.assertEqual(
            ["-C", str(REPO_ROOT), "config", "--local", "core.autocrlf", "false"],
            action["Arguments"],
        )
        self.assertNotIn("--global", action["Arguments"])

    def test_dry_run_uses_discovered_interpreter_for_each_missing_package(self) -> None:
        for simulation, package in (("PillowMissing", "Pillow"), ("NumPyMissing", "NumPy")):
            with self.subTest(simulation=simulation):
                completed, payload = run_json(
                    BOOTSTRAP_SCRIPT, "-DryRun", "-Simulate", simulation
                )
                self.assertEqual(1, completed.returncode)
                action = self.action(payload, "InstallPythonPackages")
                self.assertEqual(str(Path(sys.executable)), str(Path(action["FilePath"])))
                self.assertEqual(["-m", "pip", "install", package], action["Arguments"])
                self.assertFalse(action["Executed"])

    def test_dry_run_plans_test_project_restore_for_missing_assets(self) -> None:
        completed, payload = run_json(
            BOOTSTRAP_SCRIPT, "-DryRun", "-Simulate", "NuGetAssetsMissing"
        )

        self.assertEqual(1, completed.returncode)
        action = self.action(payload, "RestoreNuGetAssets")
        self.assertEqual("restore", action["Arguments"][0])
        self.assertEqual(
            str(REPO_ROOT / "pc_ds4_server" / "PcDs4Server.Tests" / "PcDs4Server.Tests.csproj"),
            str(Path(action["Arguments"][1])),
        )
        self.assertFalse(action["Executed"])

    def test_bootstrap_contains_no_destructive_or_global_git_commands(self) -> None:
        source = BOOTSTRAP_SCRIPT.read_text(encoding="utf-8")
        lowered = source.lower()

        self.assertNotIn("c:\\users\\zhq", lowered)
        self.assertNotIn("config', '--global", lowered)
        self.assertNotIn('reset --hard', lowered)
        self.assertNotIn('git add', lowered)


if __name__ == "__main__":
    unittest.main()
