import ctypes
import shutil
import subprocess
import time
from ctypes import wintypes
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TMP = Path.home() / "AppData" / "Local" / "Temp"
LOCALAPPDATA = Path.home() / "AppData" / "Local" / "MiniClip"

VARIANTS = {
    "框架依赖（5 个文件）": ROOT / "artifacts/publish-sizes/framework-dependent/MiniClip.exe",
    "框架依赖 + 单文件": ROOT / "artifacts/publish-sizes/fd-baseline/MiniClip.exe",
}


def _bind_apis():
    """Binds argtypes/restypes explicitly. Without this, ctypes assumes 32-bit c_int for
    every pointer, and HMODULE handles truncate with an OverflowError on x64."""
    k32 = ctypes.windll.kernel32
    psapi = ctypes.windll.psapi

    k32.OpenProcess.restype = wintypes.HANDLE
    k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    k32.CloseHandle.argtypes = [wintypes.HANDLE]

    psapi.EnumProcessModulesEx.restype = wintypes.BOOL
    psapi.EnumProcessModulesEx.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(wintypes.HMODULE),
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
        wintypes.DWORD,
    ]

    psapi.GetModuleFileNameExW.restype = wintypes.DWORD
    psapi.GetModuleFileNameExW.argtypes = [
        wintypes.HANDLE,
        wintypes.HMODULE,
        wintypes.LPWSTR,
        wintypes.DWORD,
    ]

    return k32, psapi


def list_modules(pid: int) -> list[str]:
    """Reads the loaded module list straight from the process, without spawning a shell."""
    k32, psapi = _bind_apis()

    handle = k32.OpenProcess(0x0410, False, pid)  # QUERY_INFORMATION | VM_READ
    if not handle:
        return []

    try:
        needed = wintypes.DWORD()
        if not psapi.EnumProcessModulesEx(handle, None, 0, ctypes.byref(needed), 0x03):
            return []

        count = needed.value // ctypes.sizeof(wintypes.HMODULE)
        if count <= 0:
            return []

        modules = (wintypes.HMODULE * count)()
        if not psapi.EnumProcessModulesEx(
            handle, modules, needed.value, ctypes.byref(needed), 0x03
        ):
            return []

        names = []
        for i in range(count):
            buf = ctypes.create_unicode_buffer(1024)
            if psapi.GetModuleFileNameExW(handle, modules[i], buf, 1024):
                names.append(buf.value)
        return names
    finally:
        k32.CloseHandle(handle)


def probe(label: str, exe: Path) -> None:
    print(f"\n=== {label} ===")
    print(f"exe: {exe.relative_to(ROOT)}")

    # Start with a visible window so the process stays alive long enough to inspect.
    proc = subprocess.Popen([str(exe), "--run", "6"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(2.5)

    modules = list_modules(proc.pid)
    mine = [m for m in modules if "MiniClip" in m]
    print(f"进程内 MiniClip 相关模块 ({len(mine)}):")
    for m in mine:
        print(f"   {m}")

    # .NET 8+ extracts side-by-side files a single-file app still needs (the app's own
    # .dll is one) into a per-user directory under the temp path.
    extracted = [
        p for p in TMP.glob(".net/MiniClip/*") if p.is_dir()
    ]
    print(f"%TEMP%\\.net\\MiniClip 下的解包目录: {len(extracted)}")
    for d in sorted(extracted, key=lambda p: p.stat().st_mtime, reverse=True)[:1]:
        files = sorted(d.rglob("*"))
        print(f"   最新: {d.name}  ({len([f for f in files if f.is_file()])} 个文件)")
        for f in files[:8]:
            if f.is_file():
                print(f"      {f.stat().st_size/1024:8.1f} KB  {f.relative_to(d)}")

    proc.wait(timeout=30)


def main() -> None:
    for label, exe in VARIANTS.items():
        if exe.exists():
            probe(label, exe)
        else:
            print(f"{label}: 未找到 {exe}")

    print("\n注：实测两者都不会往 %TEMP%\\.net\\ 解包——这只发生在自包含单文件上")
    print("    （它要把内嵌的原生库落盘）。纯框架依赖的单文件把托管程序集留在内存里。")


if __name__ == "__main__":
    main()
