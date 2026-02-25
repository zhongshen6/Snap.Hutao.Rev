import os
import ctypes
import struct
import tkinter as tk
from tkinter import filedialog, messagebox
from ctypes import wintypes

# ------------------------------
# Constants / protocol
# ------------------------------
MAPPING_NAME = "4F3E8543-40F7-4808-82DC-21E48A6037A7"
MAPPING_SIZE = 1024

MAGIC_DWORDS = [
    0x1560EC0, 0x15B73330, 0x106A3C0, 0x106A3B0, 0x0C835B70,
    0x608D620, 0x406330, 0x71A6EE0, 0xE47E1B0, 0xE4851E0,
    0xFEAFC10, 0x69EA500, 0x9199950, 0xA98F410, 0x1063C50,
    0x1063450, 0xFA87490, 0x1084E9E0, 0x105C2C10,
]

CREATE_SUSPENDED = 0x00000004
MEM_COMMIT = 0x00001000
MEM_RESERVE = 0x00002000
MEM_RELEASE = 0x00008000
PAGE_READWRITE = 0x04
PAGE_EXECUTE_READWRITE = 0x40
FILE_MAP_READ = 0x0004
FILE_MAP_WRITE = 0x0002
FILE_MAP_ALL_ACCESS = 0x001F0000 | FILE_MAP_READ | FILE_MAP_WRITE
INFINITE = 0xFFFFFFFF
WAIT_OBJECT_0 = 0x00000000

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
LPVOID = ctypes.c_void_p
SIZE_T = ctypes.c_size_t


class STARTUPINFOW(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("lpReserved", wintypes.LPWSTR),
        ("lpDesktop", wintypes.LPWSTR),
        ("lpTitle", wintypes.LPWSTR),
        ("dwX", wintypes.DWORD),
        ("dwY", wintypes.DWORD),
        ("dwXSize", wintypes.DWORD),
        ("dwYSize", wintypes.DWORD),
        ("dwXCountChars", wintypes.DWORD),
        ("dwYCountChars", wintypes.DWORD),
        ("dwFillAttribute", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("wShowWindow", wintypes.WORD),
        ("cbReserved2", wintypes.WORD),
        ("lpReserved2", ctypes.POINTER(ctypes.c_ubyte)),
        ("hStdInput", wintypes.HANDLE),
        ("hStdOutput", wintypes.HANDLE),
        ("hStdError", wintypes.HANDLE),
    ]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("hProcess", wintypes.HANDLE),
        ("hThread", wintypes.HANDLE),
        ("dwProcessId", wintypes.DWORD),
        ("dwThreadId", wintypes.DWORD),
    ]


class IslandEnvironment(ctypes.Structure):
    _fields_ = [
        ("Reserved", ctypes.c_ubyte * 76),
        ("EnableSetFieldOfView", wintypes.BOOL),
        ("FieldOfView", ctypes.c_float),
        ("FixLowFovScene", wintypes.BOOL),
        ("DisableFog", wintypes.BOOL),
        ("EnableSetTargetFrameRate", wintypes.BOOL),
        ("TargetFrameRate", wintypes.DWORD),
        ("RemoveOpenTeamProgress", wintypes.BOOL),
        ("HideQuestBanner", wintypes.BOOL),
        ("DisableEventCameraMove", wintypes.BOOL),
        ("DisableShowDamageText", wintypes.BOOL),
        ("UsingTouchScreen", wintypes.BOOL),
        ("RedirectCombineEntry", wintypes.BOOL),
        ("ResinListItemId000106Allowed", wintypes.BOOL),
        ("ResinListItemId000201Allowed", wintypes.BOOL),
        ("ResinListItemId107009Allowed", wintypes.BOOL),
        ("ResinListItemId107012Allowed", wintypes.BOOL),
        ("ResinListItemId220007Allowed", wintypes.BOOL),
        ("HideUid", wintypes.BOOL),
    ]


# WinAPI prototypes
kernel32.CreateFileMappingW.argtypes = [wintypes.HANDLE, LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.LPCWSTR]
kernel32.CreateFileMappingW.restype = wintypes.HANDLE
kernel32.OpenFileMappingW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.OpenFileMappingW.restype = wintypes.HANDLE
kernel32.MapViewOfFile.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, SIZE_T]
kernel32.MapViewOfFile.restype = LPVOID
kernel32.UnmapViewOfFile.argtypes = [LPVOID]
kernel32.UnmapViewOfFile.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL

kernel32.CreateProcessW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, LPVOID, LPVOID, wintypes.BOOL, wintypes.DWORD, LPVOID, wintypes.LPCWSTR, ctypes.POINTER(STARTUPINFOW), ctypes.POINTER(PROCESS_INFORMATION)]
kernel32.CreateProcessW.restype = wintypes.BOOL
kernel32.VirtualAllocEx.argtypes = [wintypes.HANDLE, LPVOID, SIZE_T, wintypes.DWORD, wintypes.DWORD]
kernel32.VirtualAllocEx.restype = LPVOID
kernel32.WriteProcessMemory.argtypes = [wintypes.HANDLE, LPVOID, LPVOID, SIZE_T, ctypes.POINTER(SIZE_T)]
kernel32.WriteProcessMemory.restype = wintypes.BOOL
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
kernel32.GetModuleHandleW.restype = wintypes.HMODULE
kernel32.GetProcAddress.argtypes = [wintypes.HMODULE, ctypes.c_char_p]
kernel32.GetProcAddress.restype = LPVOID
kernel32.CreateRemoteThread.argtypes = [wintypes.HANDLE, LPVOID, SIZE_T, LPVOID, LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
kernel32.CreateRemoteThread.restype = wintypes.HANDLE
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.GetExitCodeThread.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
kernel32.GetExitCodeThread.restype = wintypes.BOOL
kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
kernel32.ResumeThread.restype = wintypes.DWORD
kernel32.VirtualFreeEx.argtypes = [wintypes.HANDLE, LPVOID, SIZE_T, wintypes.DWORD]
kernel32.VirtualFreeEx.restype = wintypes.BOOL


def get_last_error() -> int:
    return ctypes.get_last_error()


def ensure(ok: bool, step: str):
    if not ok:
        raise RuntimeError(f"{step} failed, GetLastError={get_last_error()}")


class Nvd3UI:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("nvd3dump 全功能控制器")
        self.root.geometry("900x760")

        self.h_map = None
        self.view = None
        self.env = None

        self.game_path_var = tk.StringVar()
        self.dll_path_var = tk.StringVar(value=os.path.abspath("nvd3dump.dll"))

        self.bools = {
            "EnableSetFieldOfView": tk.BooleanVar(value=False),
            "FixLowFovScene": tk.BooleanVar(value=False),
            "DisableFog": tk.BooleanVar(value=False),
            "EnableSetTargetFrameRate": tk.BooleanVar(value=True),
            "RemoveOpenTeamProgress": tk.BooleanVar(value=False),
            "HideQuestBanner": tk.BooleanVar(value=False),
            "DisableEventCameraMove": tk.BooleanVar(value=False),
            "DisableShowDamageText": tk.BooleanVar(value=False),
            "UsingTouchScreen": tk.BooleanVar(value=False),
            "RedirectCombineEntry": tk.BooleanVar(value=False),
            "ResinListItemId000106Allowed": tk.BooleanVar(value=False),
            "ResinListItemId000201Allowed": tk.BooleanVar(value=False),
            "ResinListItemId107009Allowed": tk.BooleanVar(value=False),
            "ResinListItemId107012Allowed": tk.BooleanVar(value=False),
            "ResinListItemId220007Allowed": tk.BooleanVar(value=False),
            "HideUid": tk.BooleanVar(value=False),
        }

        self.fov_var = tk.StringVar(value="45")
        self.fps_var = tk.StringVar(value="60")

        self._build_ui()

    def _build_ui(self):
        top = tk.LabelFrame(self.root, text="路径")
        top.pack(fill="x", padx=8, pady=6)

        tk.Label(top, text="游戏 EXE").grid(row=0, column=0, sticky="w")
        tk.Entry(top, textvariable=self.game_path_var, width=90).grid(row=0, column=1, padx=6)
        tk.Button(top, text="选择", command=self.pick_game).grid(row=0, column=2)

        tk.Label(top, text="nvd3dump.dll").grid(row=1, column=0, sticky="w")
        tk.Entry(top, textvariable=self.dll_path_var, width=90).grid(row=1, column=1, padx=6)
        tk.Button(top, text="选择", command=self.pick_dll).grid(row=1, column=2)

        map_box = tk.LabelFrame(self.root, text="共享内存")
        map_box.pack(fill="x", padx=8, pady=6)
        tk.Button(map_box, text="创建/连接 Mapping", command=self.ensure_mapping).pack(side="left", padx=5, pady=5)
        tk.Button(map_box, text="写入 Reserved 魔数", command=self.write_reserved).pack(side="left", padx=5, pady=5)
        tk.Button(map_box, text="应用当前全部开关", command=self.apply_all).pack(side="left", padx=5, pady=5)

        feature = tk.LabelFrame(self.root, text="DLL 全部可控功能")
        feature.pack(fill="both", expand=True, padx=8, pady=6)

        row = 0
        for key in self.bools:
            tk.Checkbutton(feature, text=key, variable=self.bools[key]).grid(row=row // 2, column=(row % 2), sticky="w", padx=8, pady=2)
            row += 1

        value_box = tk.Frame(feature)
        value_box.grid(row=20, column=0, columnspan=2, sticky="w", padx=8, pady=8)
        tk.Label(value_box, text="FieldOfView").pack(side="left")
        tk.Entry(value_box, textvariable=self.fov_var, width=8).pack(side="left", padx=4)
        tk.Label(value_box, text="TargetFrameRate").pack(side="left", padx=(16, 0))
        tk.Entry(value_box, textvariable=self.fps_var, width=8).pack(side="left", padx=4)

        actions = tk.LabelFrame(self.root, text="启动")
        actions.pack(fill="x", padx=8, pady=6)
        tk.Button(actions, text="挂起启动并注入 DLL", command=self.launch_with_inject).pack(side="left", padx=5, pady=5)

        self.log = tk.Text(self.root, height=10)
        self.log.pack(fill="both", expand=False, padx=8, pady=6)

    def logln(self, msg: str):
        self.log.insert("end", msg + "\n")
        self.log.see("end")

    def pick_game(self):
        p = filedialog.askopenfilename(title="选择游戏 EXE", filetypes=[("Executable", "*.exe"), ("All", "*.*")])
        if p:
            self.game_path_var.set(p)

    def pick_dll(self):
        p = filedialog.askopenfilename(title="选择 nvd3dump.dll", filetypes=[("DLL", "*.dll"), ("All", "*.*")])
        if p:
            self.dll_path_var.set(p)

    def ensure_mapping(self):
        if self.env is not None:
            self.logln("Mapping 已连接")
            return

        h = kernel32.OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, False, MAPPING_NAME)
        if not h:
            h = kernel32.CreateFileMappingW(wintypes.HANDLE(-1), None, PAGE_EXECUTE_READWRITE, 0, MAPPING_SIZE, MAPPING_NAME)
            ensure(bool(h), "CreateFileMappingW")
            self.logln("创建新 Mapping")
        else:
            self.logln("连接现有 Mapping")

        view = kernel32.MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, 0)
        ensure(bool(view), "MapViewOfFile")

        self.h_map = h
        self.view = view
        self.env = ctypes.cast(view, ctypes.POINTER(IslandEnvironment)).contents

    def write_reserved(self):
        self.ensure_mapping()
        data = b"".join(struct.pack("<I", x) for x in MAGIC_DWORDS)
        ensure(len(data) == 76, "reserved size")
        ctypes.memmove(self.view, data, 76)
        self.logln("Reserved[76] 已写入 19 个 DWORD 魔数")

    def apply_all(self):
        try:
            self.ensure_mapping()
            self.write_reserved()

            for k, v in self.bools.items():
                setattr(self.env, k, 1 if v.get() else 0)

            self.env.FieldOfView = float(self.fov_var.get())
            self.env.TargetFrameRate = int(self.fps_var.get())
            self.logln("已写入全部 DLL 功能开关 + FOV/FPS")
        except Exception as e:
            self.logln(f"[ERROR] {e}")
            messagebox.showerror("错误", str(e))

    def launch_with_inject(self):
        try:
            self.apply_all()

            game = self.game_path_var.get().strip().strip('"')
            dll = self.dll_path_var.get().strip().strip('"')
            if not os.path.exists(game):
                raise RuntimeError("游戏 EXE 不存在")
            if not os.path.exists(dll):
                raise RuntimeError("DLL 不存在")

            si = STARTUPINFOW()
            si.cb = ctypes.sizeof(si)
            pi = PROCESS_INFORMATION()

            cmdline = ctypes.create_unicode_buffer(f'"{game}"')
            workdir = os.path.dirname(game)

            ok = kernel32.CreateProcessW(None, cmdline, None, None, False, CREATE_SUSPENDED, None, workdir, ctypes.byref(si), ctypes.byref(pi))
            ensure(bool(ok), "CreateProcessW")

            remote_mem = None
            h_thread = None
            try:
                path_bytes = dll.encode("utf-16le") + b"\x00\x00"
                path_buf = ctypes.create_string_buffer(path_bytes)

                remote_mem = kernel32.VirtualAllocEx(pi.hProcess, None, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE)
                ensure(bool(remote_mem), "VirtualAllocEx")

                written = SIZE_T(0)
                ok = kernel32.WriteProcessMemory(pi.hProcess, remote_mem, ctypes.cast(path_buf, LPVOID), len(path_bytes), ctypes.byref(written))
                ensure(bool(ok) and written.value == len(path_bytes), "WriteProcessMemory")

                h_kernel32 = kernel32.GetModuleHandleW("kernel32.dll")
                ensure(bool(h_kernel32), "GetModuleHandleW")
                p_load = kernel32.GetProcAddress(h_kernel32, b"LoadLibraryW")
                ensure(bool(p_load), "GetProcAddress")

                h_thread = kernel32.CreateRemoteThread(pi.hProcess, None, 0, p_load, remote_mem, 0, None)
                ensure(bool(h_thread), "CreateRemoteThread")

                wait_result = kernel32.WaitForSingleObject(h_thread, INFINITE)
                ensure(wait_result == WAIT_OBJECT_0, "WaitForSingleObject")

                exit_code = wintypes.DWORD(0)
                ok = kernel32.GetExitCodeThread(h_thread, ctypes.byref(exit_code))
                ensure(bool(ok), "GetExitCodeThread")
                if exit_code.value == 0:
                    raise RuntimeError("LoadLibraryW 返回 NULL，DLL 注入失败")

                resume = kernel32.ResumeThread(pi.hThread)
                if resume == 0xFFFFFFFF:
                    raise RuntimeError(f"ResumeThread failed, GetLastError={get_last_error()}")

                self.logln(f"注入成功，PID={pi.dwProcessId}, HMODULE=0x{exit_code.value:X}")
                messagebox.showinfo("成功", "游戏已启动并注入成功")
            finally:
                if h_thread:
                    kernel32.CloseHandle(h_thread)
                if remote_mem:
                    kernel32.VirtualFreeEx(pi.hProcess, remote_mem, 0, MEM_RELEASE)
                if pi.hThread:
                    kernel32.CloseHandle(pi.hThread)
                if pi.hProcess:
                    kernel32.CloseHandle(pi.hProcess)
        except Exception as e:
            self.logln(f"[ERROR] {e}")
            messagebox.showerror("错误", str(e))


def main():
    if os.name != "nt":
        raise SystemExit("This UI script is for Windows only.")

    root = tk.Tk()
    app = Nvd3UI(root)

    def on_close():
        if app.view:
            kernel32.UnmapViewOfFile(app.view)
        if app.h_map:
            kernel32.CloseHandle(app.h_map)
        root.destroy()

    root.protocol("WM_DELETE_WINDOW", on_close)
    root.mainloop()


if __name__ == "__main__":
    main()
