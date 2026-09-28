"""Measures every plausible MiniClip publish configuration and runs the resulting exe.

Every option is measured on disk AND executed through the real self-test, because an
optimisation that ships a broken tray app is not an optimisation. Trimmed WPF is the risky
one: WPF is not trim-compatible, and the failure mode is a runtime crash on a code path the
self-test may not reach.

Run from the repository root:  python tools/measure-publish.py
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "src" / "MiniClip" / "MiniClip.csproj"
STAGE = ROOT / "artifacts" / "publish-sizes"

# name -> extra MSBuild properties
CONFIGS = {
    "framework-dependent": [
        "-p:SelfContained=false",
        "-p:PublishSingleFile=false",
    ],
    "framework-dep + single file": [
        "-p:SelfContained=false",
        "-p:PublishSingleFile=true",
    ],
    "self-contained (current)": [
        "-p:SelfContained=true",
        "-p:PublishSingleFile=true",
    ],
    "self-contained + compression": [
        "-p:SelfContained=true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
    ],
    "framework-dep + compression": [
        "-p:SelfContained=false",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
    ],
}


def dir_size(path: Path) -> int:
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file())


def run(cmd: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT)


def main() -> None:
    STAGE.mkdir(parents=True, exist_ok=True)
    rows = []

    for name, props in CONFIGS.items():
        out = STAGE / name.replace(" ", "_").replace("(", "").replace(")", "").replace("+", "and")
        if out.exists():
            shutil.rmtree(out, ignore_errors=True)

        print(f"\n=== {name} ===", flush=True)
        publish = run([
            "dotnet", "publish", str(CSPROJ),
            "-c", "Release", "-r", "win-x64",
            *props,
            "-o", str(out),
            "-v", "q", "--nologo",
        ])

        errors = [l for l in publish.stdout.splitlines() if ": error" in l]
        warnings = [l for l in publish.stdout.splitlines() if ": warning" in l]
        if errors:
            print("  BUILD FAILED:")
            for line in errors[:4]:
                print("   ", line.strip())
            rows.append({"config": name, "size": None, "note": "build failed"})
            continue

        # The largest executable is the shipping artifact; measure it and the whole folder.
        exes = sorted(out.glob("*.exe"), key=lambda p: p.stat().st_size, reverse=True)
        main_exe = exes[0] if exes else None
        total = dir_size(out)
        files = len([f for f in out.rglob("*") if f.is_file()])

        exe_mb = main_exe.stat().st_size / 1024 / 1024 if main_exe else 0
        total_mb = total / 1024 / 1024
        print(f"  MiniClip.exe : {exe_mb:.1f} MB")
        print(f"  whole folder : {total_mb:.1f} MB ({files} files)")
        if warnings:
            print(f"  warnings     : {len(warnings)}")

        # Execute it. This is the part that matters.
        test_dir = STAGE / f"selftest-{out.name}"
        if test_dir.exists():
            shutil.rmtree(test_dir, ignore_errors=True)
        proc = run([str(main_exe), "--selftest", str(test_dir)])
        passed = proc.stdout.count("[PASS]")
        failed = proc.stdout.count("[FAIL]")
        print(f"  self-test    : exit={proc.returncode} PASS={passed} FAIL={failed}")

        note = "ok" if proc.returncode == 0 and passed >= 26 else f"SELF-TEST FAILED exit={proc.returncode}"
        if proc.stderr.strip():
            note += f" | stderr: {proc.stderr.strip().splitlines()[-1][:90]}"

        rows.append({
            "config": name,
            "exe_mb": round(exe_mb, 2),
            "folder_mb": round(total_mb, 2),
            "files": files,
            "warnings": len(warnings),
            "selftest_exit": proc.returncode,
            "pass": passed,
            "fail": failed,
            "note": note,
        })

    print("\n\n================ 汇总 ================")
    print(f"{'配置':<32}{'exe':>9}{'目录':>9}{'文件':>6}{'自检':>16}")
    for r in rows:
        if r.get("size") is None and "exe_mb" not in r:
            print(f"{r['config']:<32}{'—':>9}{'—':>9}{'—':>6}{r['note']:>16}")
            continue
        status = f"{r['pass']}/{r['pass'] + r['fail']} exit{r['selftest_exit']}"
        print(f"{r['config']:<32}{r['exe_mb']:>8.1f}M{r['folder_mb']:>8.1f}M{r['files']:>6}{status:>16}")

    (STAGE / "results.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n结果已写入 {STAGE / 'results.json'}")


if __name__ == "__main__":
    main()
