"""Compares framework-dependent publish options that could improve startup.

Startup is dominated by JIT-compiling WPF's cold paths. `PublishReadyToRun` precompiles to
native code, which normally buys faster startup at the cost of a larger file. This measures
whether that trade is worth taking for a 0.17 MB app whose startup target is < 1 s.
"""

import ctypes
import statistics
import subprocess
import sys
import time
from ctypes import wintypes
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "src" / "MiniClip" / "MiniClip.csproj"
STAGE = ROOT / "artifacts" / "publish-sizes"
LOCALAPPDATA = Path.home() / "AppData" / "Local" / "MiniClip"

VARIANTS = {
    "fd single-file (current)": ["-p:SelfContained=false", "-p:PublishSingleFile=true"],
    "fd + ReadyToRun": [
        "-p:SelfContained=false", "-p:PublishSingleFile=true", "-p:PublishReadyToRun=true",
    ],
}

RUNS = 7

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


def sample_once(exe: Path, run_seconds: int = 4) -> tuple[float, float | None]:
    """Returns (peak working set MB, startup ms)."""
    for f in list((exe.parent / "data").glob("miniclip-*.log")) + list(LOCALAPPDATA.glob("miniclip-*.log")):
        f.unlink(missing_ok=True)

    created = datetime.now()
    proc = subprocess.Popen(
        [str(exe), "--run", str(run_seconds)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )

    k32 = ctypes.windll.kernel32
    psapi = ctypes.windll.psapi
    k32.OpenProcess.restype = wintypes.HANDLE
    k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]

    peak = 0.0
    deadline = time.time() + (run_seconds - 1.5)
    while time.time() < deadline and proc.poll() is None:
        handle = k32.OpenProcess(0x1000, False, proc.pid)
        if handle:
            counters = PROCESS_MEMORY_COUNTERS()
            counters.cb = ctypes.sizeof(counters)
            if psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
                peak = max(peak, counters.WorkingSetSize / 1024 / 1024)
            k32.CloseHandle(handle)
        time.sleep(0.1)

    proc.wait(timeout=40)

    startup_ms = None
    logs = logs_for(exe)
    if logs:
        first = logs[0].read_text(encoding="utf-8", errors="replace").splitlines()
        if first:
            stamp = first[0].split()[0]
            try:
                hh, mm, rest = stamp.split(":")
                ss, ms = rest.split(".")
                logged = created.replace(
                    hour=int(hh), minute=int(mm), second=int(ss), microsecond=int(ms) * 1000
                )
                startup_ms = (logged - created).total_seconds() * 1000
            except (ValueError, IndexError):
                pass

    return peak, startup_ms


def main() -> None:
    print(f"{'变体':<28}{'exe':>10}{'常驻(中位)':>13}{'启动(中位)':>13}{'启动(最慢)':>13}")
    for name, props in VARIANTS.items():
        out = STAGE / ("r2r" if "ReadyToRun" in name else "fd-baseline")
        subprocess.run(["dotnet", "publish", str(CSPROJ), "-c", "Release", "-r", "win-x64",
                        *props, "-o", str(out), "-v", "q", "--nologo"],
                       capture_output=True, text=True, cwd=ROOT)

        exe = out / "MiniClip.exe"
        if not exe.exists():
            print(f"{name:<28} publish failed")
            continue

        ws_samples, st_samples = [], []
        for _ in range(RUNS):
            ws, st = sample_once(exe)
            ws_samples.append(ws)
            if st is not None:
                st_samples.append(st)

        exe_mb = exe.stat().st_size / 1024 / 1024
        print(
            f"{name:<28}{exe_mb:>8.2f} MB"
            f"{statistics.median(ws_samples):>10.1f} MB"
            f"{statistics.median(st_samples):>10.0f} ms"
            f"{max(st_samples):>10.0f} ms"
        )

    print(f"\n每组 {RUNS} 次采样。启动时间取自应用自身写入的首行时间戳与进程创建的差值。")


if __name__ == "__main__":
    sys.exit(main())
