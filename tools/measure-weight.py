"""Measures startup latency and resident memory for each publish flavour.

File size alone does not answer "is this lightweight". A 63 MB compressed single file has to
decompress itself before the first frame appears, and the project's plan sets < 1 s startup
and < 50 MB resident as the targets (sections 26). This measures exactly those two, on the
real application, several times, and reports the median.

Run from the repository root:  python tools/measure-weight.py
"""

import json
import shutil
import statistics
import subprocess
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LOCALAPPDATA = Path.home() / "AppData" / "Local" / "MiniClip"

# Only flavours that actually exist on disk; missing ones are skipped.
CANDIDATES = {
    "framework-dependent": ROOT / "artifacts/publish-sizes/framework-dependent/MiniClip.exe",
    "framework-dep single-file": ROOT / "artifacts/publish-sizes/framework-dep_and_single_file/MiniClip.exe",
    "self-contained 141 MB": ROOT / "artifacts/publish-sizes/self-contained_current/MiniClip.exe",
    "self-contained compressed 63 MB": ROOT / "artifacts/publish-sizes/self-contained_and_compression/MiniClip.exe",
    "dev Release build (0.16 MB)": ROOT / "src/MiniClip/bin/Release/net10.0-windows/MiniClip.exe",
}

RUNS = 5

def _data_dir_for(exe: Path) -> Path:
    """返回该可执行文件实际使用的数据目录。

    与 src/MiniClip/Settings/AppPaths.cs 的规则一致：
      1. 程序目录可写 -> <程序目录>/data
      2. 否则        -> %LOCALAPPDATA%/MiniClip
    这里按"哪个目录里已经有日志"来判断，因为测量脚本是在事后读日志。
    """
    candidates = [exe.parent / "data", Path.home() / "AppData" / "Local" / "MiniClip"]
    existing = [c for c in candidates if c.is_dir() and any(c.glob("miniclip-*.log"))]
    if existing:
        return max(existing, key=lambda c: max(f.stat().st_mtime for f in c.glob("miniclip-*.log")))
    return candidates[0]


def logs_for(exe: Path) -> list[Path]:
    """该可执行文件最近一次运行写下的日志，按时间倒序。"""
    data_dir = _data_dir_for(exe)
    return sorted(data_dir.glob("miniclip-*.log"), key=lambda p: p.stat().st_mtime, reverse=True)


def measure(exe: Path, run_seconds: int = 4) -> dict:
    """Starts the app, samples its memory from WMI, and reads its own startup timestamp."""
    data_dir = exe.parent / "data"
    for f in list(data_dir.glob("miniclip-*.log")) + list(LOCALAPPDATA.glob("miniclip-*.log")):
        f.unlink(missing_ok=True)

    created = datetime.now()
    exe_mb = exe.stat().st_size / 1024 / 1024

    proc = subprocess.Popen(
        [str(exe), "--run", str(run_seconds)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )

    # Sample memory without spawning a shell per sample: query the process directly.
    # A shell per sample would cost more than the process being measured.
    peak_ws = 0.0
    deadline = time.time() + (run_seconds - 1.5)
    while time.time() < deadline and proc.poll() is None:
        try:
            import ctypes
            from ctypes import wintypes

            PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
            k32 = ctypes.windll.kernel32
            k32.OpenProcess.restype = wintypes.HANDLE
            k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]

            class PROCESS_MEMORY_COUNTERS(ctypes.Structure):
                _fields_ = [
                    ("cb", wintypes.DWORD),
                    ("PageFaultCount", wintypes.DWORD),
                    ("PeakWorkingSetSize", ctypes.c_size_t),
                    ("WorkingSetSize", ctypes.c_size_t),
                    ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                    ("PagefileUsage", ctypes.c_size_t),
                    ("PeakPagefileUsage", ctypes.c_size_t),
                ]

            handle = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, proc.pid)
            if handle:
                counters = PROCESS_MEMORY_COUNTERS()
                counters.cb = ctypes.sizeof(counters)
                if ctypes.windll.psapi.GetProcessMemoryInfo(
                    handle, ctypes.byref(counters), counters.cb
                ):
                    peak_ws = max(peak_ws, counters.WorkingSetSize / 1024 / 1024)
                k32.CloseHandle(handle)
        except Exception:
            pass
        time.sleep(0.12)

    proc.wait(timeout=40)

    # The app timestamps its own first log line; the gap to process creation is the
    # startup latency, which is the number the plan actually targets.
    startup_ms = None
    logs = logs_for(exe)
    if logs:
        first = logs[0].read_text(encoding="utf-8", errors="replace").splitlines()
        if first:
            stamp = first[0].split()[0]
            try:
                hh, mm, ss = stamp.split(":")
                logged = created.replace(
                    hour=int(hh), minute=int(mm),
                    second=int(ss.split(".")[0]), microsecond=int(ss.split(".")[1]) * 1000,
                )
                startup_ms = (logged - created).total_seconds() * 1000
            except (ValueError, IndexError):
                pass

    return {
        "exe_mb": round(exe_mb, 2),
        "peak_working_set_mb": peak_ws,
        "startup_ms": round(startup_ms, 0) if startup_ms is not None else None,
    }


def main() -> None:
    rows = []
    for name, exe in CANDIDATES.items():
        if not exe.exists():
            print(f"{name:<34} (未找到，跳过)")
            continue

        samples = []
        for _ in range(RUNS):
            samples.append(measure(exe))

        exe_mb = samples[0]["exe_mb"]
        ws = [s["peak_working_set_mb"] for s in samples if s["peak_working_set_mb"]]
        st = [s["startup_ms"] for s in samples if s["startup_ms"] is not None]

        row = {
            "flavour": name,
            "exe_mb": exe_mb,
            "working_set_mb": round(statistics.median(ws), 1) if ws else None,
            "startup_ms_median": round(statistics.median(st)) if st else None,
            "startup_ms_samples": st,
        }
        rows.append(row)

        print(
            f"{name:<34} exe={exe_mb:>7.2f} MB  "
            f"常驻={row['working_set_mb']} MB  "
            f"启动中位={row['startup_ms_median']} ms  "
            f"(样本 {st})"
        )

    out = ROOT / "artifacts" / "publish-sizes" / "weight.json"
    out.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n================ 结论 ================")
    print(f"{'发行方式':<34}{'文件':>10}{'常驻内存':>12}{'启动':>12}")
    for r in rows:
        print(
            f"{r['flavour']:<34}{r['exe_mb']:>8.2f} MB"
            f"{(str(r['working_set_mb']) + ' MB'):>12}"
            f"{(str(r['startup_ms_median']) + ' ms'):>12}"
        )
    print(f"\n详细数据: {out}")


if __name__ == "__main__":
    main()
