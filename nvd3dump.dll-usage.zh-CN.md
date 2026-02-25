# `nvd3dump.dll` 完整使用手册（基于本仓库代码逆向出的“全量可见能力”）

> 目标读者：不看本项目源码，也要把这个 DLL 在本项目中的所有调用方式和可控功能完整复现出来。

---

## 0. 先给你一句“架构级结论”

这个项目对 `nvd3dump.dll` 的使用，不是“调用 DLL 导出函数”，而是两段式：

1. **启动时注入 DLL**：
   - 直接注入路径：`CreateProcessW(CREATE_SUSPENDED)` + 远程 `LoadLibraryW`（本文示例与最小注入器使用该路径）。
   - `sigewinne` 路径：先拉起 `toolkit_fulltrust.exe`，再由其内部完成后续动作（该 EXE 在本仓库中无源码）。
2. **通过共享内存实时传参**（命名 `FileMapping` + 结构体字段/位字段开关），让 DLL 在目标进程内读取并生效。

所以你真正要复刻的是：

- 注入链路（让 DLL 进进程）；
- 共享内存协议（告诉 DLL 要做什么功能）。

---

## 1. 证据链：为什么可以确认是“注入 + 共享内存协议”

### 1.1 注入证据（启动链路）

注入链路在不同版本中有两种实现：

- **直接注入实现**（本文示例/最小注入器）：`CreateProcessW(CREATE_SUSPENDED)` + 远程 `LoadLibraryW`。
- **`sigewinne` 启动器实现**：`LaunchGameImpl()` 启动 `toolkit_fulltrust.exe`，注入细节被封装在外部组件内。

这说明 DLL 仍被当成“加载后自运行模块”使用，而非显式 API SDK。 

### 1.2 协议证据（运行时控制）

`init_environment()` 里创建/打开命名共享内存：

- 名称：`4F3E8543-40F7-4808-82DC-21E48A6037A7`
- 映射数据类型：`IslandEnvironment`
- 启动器 UI 切换项会直接写 `penv->字段`

这说明 DLL 与启动器之间存在约定内存布局，DLL 在目标进程读取该共享区实现功能开关。 

---

## 2. 你要实现“全部使用方法”，最低必须实现的两个通道


### 通道 A：DLL 注入通道（一次性）

作用：把 `nvd3dump.dll` 装入游戏进程。

### 通道 B：环境控制通道（可持续）

作用：通过共享内存布尔/数值字段，实时或准实时驱动 DLL 功能。

如果只做 A，不做 B：DLL 可能加载了，但很多开关不会变。
如果只做 B，不做 A：共享内存有值，但 DLL 不在进程里，没消费者。

---

## 3. `IslandEnvironment` 协议全字段（即 DLL 可见的功能入口）

结构体如下（`sigewinne` 当前协议）：

```cpp
struct IslandEnvironment
{
    CHAR  Reserved[76];
    float FieldOfView;
    DWORD TargetFrameRate;
    uint32_t EnableSetFieldOfView : 1;
    uint32_t FixLowFovScene : 1;
    uint32_t DisableFog : 1;
    uint32_t EnableSetTargetFrameRate : 1;
    uint32_t RemoveOpenTeamProgress : 1;
    uint32_t HideQuestBanner : 1;
    uint32_t DisableEventCameraMove : 1;
    uint32_t DisableShowDamageText : 1;
    uint32_t UsingTouchScreen : 1;
    uint32_t RedirectCombineEntry : 1;
    uint32_t ResinListItemId000106Allowed : 1;
    uint32_t ResinListItemId000201Allowed : 1;
    uint32_t ResinListItemId107009Allowed : 1;
    uint32_t ResinListItemId107012Allowed : 1;
    uint32_t ResinListItemId220007Allowed : 1;
    uint32_t HideUid : 1;
    uint32_t ReservedBits : 16;
};
```

兼容说明：`Snap.Hutao-main` 主工程中仍有 `BOOL` 展开结构定义；字段语义一致，但内存布局不同。独立控制器请先确认你要兼容的版本。

**重点解释：**

- `Reserved[76]` 不是无意义填充。项目会把一组固定 `DWORD array[]` 拷贝到结构体头部（`memcpy(reinterpret_cast<char*>(penv), &array, sizeof(array));`），这通常用于 DLL 侧协议签名、版本/偏移指纹或校验标识。
- 也就是说，若你外部自己实现控制器，**建议保留这段初始化行为**，不要把 `Reserved` 当垃圾字节清零后不管。
- 若你使用 Python/C#/Rust 等不支持 C++ 位域映射的方式，需要自行把布尔开关打包为一个 `uint32 Flags`（bit0~bit15）。

---

## 4. 功能矩阵：DLL 能力、UI 映射、代码映射、调用方法

下面是“你关心的功能到底怎么调用”的核心表。

| 功能（中文） | UI/资源键 | 协议字段（共享内存） | Proto 持久化字段 | 调用方式 |
|---|---|---|---|---|
| 移除战斗伤害跳字 | `ViewPageIslandDisableShowDamageText` | `DisableShowDamageText` | `DisableShowDamageText` | 写 `penv->DisableShowDamageText = TRUE/FALSE` |
| 移除迷雾 | `ViewPageIslandDisableFog` | `DisableFog` | `DisableFog` | 写 `penv->DisableFog = TRUE/FALSE` |
| 移除元素爆发镜头特写 | `ViewPageIslandEventCameraMoveHotSwitch` | `DisableEventCameraMove` | `DisableEventCameraMove` | 写 `penv->DisableEventCameraMove` |
| 特殊界面修正 | `ViewPageIslandFixLowFovScene` | `FixLowFovScene` | `FixLowFovScene` | 写 `penv->FixLowFovScene` |
| 关闭地图横幅 | `ViewPageIslandHideQuestBannerHotSwitch` | `HideQuestBanner` | `HideQuestBanner` | 写 `penv->HideQuestBanner` |
| 重定向合成 | `ViewPageIslandRedirectCombineEntry` | `RedirectCombineEntry` | `RedirectCombineEntry` | 写 `penv->RedirectCombineEntry` |
| 设置视角(FOV)开关 | `ViewPageIslandTargetFovHotSwitch` | `EnableSetFieldOfView` | `EnableSetFieldOfView` | 写 `penv->EnableSetFieldOfView` |
| 目标视角值 | `ViewPageIslandTargetFov` | `FieldOfView` | `FieldOfView` | 写 `penv->FieldOfView = float` |
| 设置帧率开关 | `ViewPageIslandTargetFpsHotSwitch` | `EnableSetTargetFrameRate` | `EnableSetTargetFrameRate` | 写 `penv->EnableSetTargetFrameRate` |
| 目标帧率值 | `ViewPageIslandTargetFps` | `TargetFrameRate` | `TargetFrameRate` | 写 `penv->TargetFrameRate = DWORD` |
| 移除打开队伍进度 | `ViewPageIslandRemoveOpenTeamProgress` | `RemoveOpenTeamProgress` | `RemoveOpenTeamProgress` | 写 `penv->RemoveOpenTeamProgress` |
| 触屏模式 | `ViewPageIslandUsingTouchScreen` | `UsingTouchScreen` | `UsingTouchScreen` | 写 `penv->UsingTouchScreen` |
| 隐藏 UID | `ViewPageIslandHideUid` | `HideUid` | `HideUid` | 写 `penv->HideUid` |
| 树脂选项相关 5 项 | 对应 `ViewPageIslandResinListItem...` | `ResinListItemId...Allowed` | `items` map | `sigewinne` 页面层禁用；主工程 `Snap.Hutao-main` 已有对应选项写入 |

## 5. 两类状态：持久化设置 vs 实时环境

项目里有两套状态存储：

1. `config` 文件（protobuf `Settings`）用于**持久化**。
2. `IslandEnvironment` 共享内存用于**运行时生效**。

`IslandPage` 中每个 setter 都是“双写”：

- 先写 protobuf（保存偏好）；
- 再写 `penv`（让 DLL 立即看到）。

你自己复刻时也建议采用双写模型：

- 启动前从配置回填 env；
- 运行中改开关时直接改 env；
- 退出时把当前值存回配置。

---

## 6. 启动与注入完整流程（直接注入路径，可原样复刻）

1. 获取目标游戏命令行（可附参数）。
2. `CreateProcessW(..., CREATE_SUSPENDED, ...)`。
3. 计算 `nvd3dump.dll` 绝对路径（推荐与 launcher 同目录）。
4. `OpenProcess(PROCESS_ALL_ACCESS, ..., pid)`。
5. `VirtualAllocEx` 分配远程内存。
6. `WriteProcessMemory` 写入 DLL 宽字符串路径。
7. `GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW")`。
8. `CreateRemoteThread` 执行远程 `LoadLibraryW`。
9. `WaitForSingleObject` 等待线程。
10. `VirtualFreeEx`。
11. `ResumeThread`。

这就是本项目的“DLL 注入全部流程”。

补充：`sigewinne` 当前启动器会先启动 `toolkit_fulltrust.exe`，这是另一条封装链路；如果你要做独立最小运行器，直接注入路径仍可用。

---

## 7. 共享内存初始化流程（容易被忽略，但非常关键）

### 7.1 命名对象

- 名称固定：`4F3E8543-40F7-4808-82DC-21E48A6037A7`
- API：`OpenFileMapping` / `CreateFileMapping(INVALID_HANDLE_VALUE, ...)`
- 大小：项目创建时给了 1024 字节（足够容纳结构体）

### 7.2 首次创建时的初始化

- `ZeroMemory(penv, sizeof(IslandEnvironment));`
- `memcpy(penv, array, sizeof(array));`（写入 19 个 `DWORD` 到 `Reserved` 区域）

### 7.3 将持久化配置写入 env

`init_environment()` 会把 `pisland` 的值回填到 `penv`：

- FOV / FPS 数值与开关；
- 多个布尔功能位（雾、伤害数字、镜头、横幅、重定向等）。

> 更新：`sigewinne` 的 `init_environment()` 已回填 `HideUid`；5 个 `ResinListItemId...Allowed` 仍未在该函数内回填。

---

## 8. “功能是否真的可用”分级（按代码实现程度）

### A 级：已完整打通（有 UI、有 proto、有 penv 写入）

- EnableSetFieldOfView / FieldOfView
- FixLowFovScene
- DisableFog
- EnableSetTargetFrameRate / TargetFrameRate
- RemoveOpenTeamProgress
- HideQuestBanner
- DisableEventCameraMove
- DisableShowDamageText
- UsingTouchScreen
- RedirectCombineEntry
- HideUid

### B 级：分版本差异项

- 5 个 ResinListItem 开关：
  - `ResinListItemAllowOriginalResin`
  - `ResinListItemAllowPrimogem`
  - `ResinListItemAllowFragileResin`
  - `ResinListItemAllowTransientResin`
  - `ResinListItemAllowCondensedResin`

证据：

- `sigewinne`：XAML 里这些开关 `IsEnabled="False"`，getter 固定返回 `1`，setter 空实现。
- `Snap.Hutao-main`：这 5 项已连接到 `LaunchOptions` 并写入 `IslandEnvironment`。

结论：DLL 协议字段存在，具体是否能在 UI 控制取决于版本分支。

---

## 9. 可直接复用的“控制器级”示例（不依赖本项目 UI）

下面示例展示“仅通过协议控制 DLL 功能”。  
注意：该示例是 **`BOOL` 展开协议** 写法（用于旧布局兼容）；若目标是 `sigewinne` 当前位域协议，请改为 `Reserved + FieldOfView + TargetFrameRate + Flags(bitmask)` 布局。

```cpp
#include <Windows.h>
#include <stdexcept>
#include <string>

struct IslandEnvironment
{
    char  Reserved[76];
    BOOL  EnableSetFieldOfView;
    FLOAT FieldOfView;
    BOOL  FixLowFovScene;
    BOOL  DisableFog;
    BOOL  EnableSetTargetFrameRate;
    DWORD TargetFrameRate;
    BOOL  RemoveOpenTeamProgress;
    BOOL  HideQuestBanner;
    BOOL  DisableEventCameraMove;
    BOOL  DisableShowDamageText;
    BOOL  UsingTouchScreen;
    BOOL  RedirectCombineEntry;
    BOOL  ResinListItemId000106Allowed;
    BOOL  ResinListItemId000201Allowed;
    BOOL  ResinListItemId107009Allowed;
    BOOL  ResinListItemId107012Allowed;
    BOOL  ResinListItemId220007Allowed;
    BOOL  HideUid;
};

int main()
{
    HANDLE h = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE,
        L"4F3E8543-40F7-4808-82DC-21E48A6037A7");
    if (!h) return 1;

    auto env = reinterpret_cast<IslandEnvironment*>(
        MapViewOfFile(h, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0));
    if (!env) return 2;

    // 你示例里提到的开关：
    env->DisableShowDamageText = TRUE;   // 移除伤害跳字
    env->DisableFog = TRUE;              // 移除迷雾
    env->DisableEventCameraMove = TRUE;  // 移除元素爆发镜头
    env->FixLowFovScene = TRUE;          // 特殊界面修正
    env->HideQuestBanner = TRUE;         // 关闭地图横幅
    env->RedirectCombineEntry = TRUE;    // 重定向合成

    // 额外示例：FOV/FPS
    env->EnableSetFieldOfView = TRUE;
    env->FieldOfView = 75.0f;
    env->EnableSetTargetFrameRate = TRUE;
    env->TargetFrameRate = 120;

    UnmapViewOfFile(env);
    CloseHandle(h);
    return 0;
}
```

---

## 10. 端到端实现蓝图（你可以按这个自己重写一个完整程序）

### 第 1 步：初始化配置

- 准备一个本地配置文件（可 protobuf，也可 JSON）。
- 至少覆盖 A 级功能字段。

### 第 2 步：初始化共享内存

- `OpenFileMapping`；不存在则 `CreateFileMapping`。
- 首次创建时写 `Reserved` 签名字节。
- 从配置回填所有 env 字段。

### 第 3 步：注入 DLL 并启动游戏

- 挂起启动；
- 远程 `LoadLibraryW` 注入 `nvd3dump.dll`；
- 恢复主线程。

### 第 4 步：运行时热更新

- UI / 热键 / IPC 指令到达后，直接改 `env->字段`。
- 若需持久化，同步写回配置。

### 第 5 步：退出清理

- 保存配置；
- 关闭映射句柄。

---

## 11. 常见误区（会导致“看上去用了 DLL，实际上没生效”）

1. **只注入不写 env**：DLL 加载了，但开关都默认值。
2. **只写 config 不写 env**：配置变了，运行时不立即生效。
3. **宽窄字符写错**：`LoadLibraryW` 必须喂 UTF-16。
4. **路径非绝对**：目标进程工作目录变化导致找不到 DLL。
5. **位数不匹配**：32/64 位跨架构注入失败。
6. **忽略 Reserved 初始化**：可能造成 DLL 端识别失败（取决于 DLL 实现）。
7. **未挂起启动**：关键逻辑已越过，部分 hook 时机错过。

---

## 12. 一份“全量对齐”Checklist

- [ ] `nvd3dump.dll` 与启动器同目录，路径可达。
- [ ] 游戏用 `CREATE_SUSPENDED` 启动。
- [ ] 远程线程成功执行 `LoadLibraryW`。
- [ ] 共享内存名使用 `4F3E8543-40F7-4808-82DC-21E48A6037A7`。
- [ ] `IslandEnvironment` 布局与字段顺序完全一致。
- [ ] 首次创建时写入 `Reserved` 签名数组。
- [ ] 启动前将持久化配置回填到 env。
- [ ] 运行时开关改动会同步写 env（必要时写配置）。
- [ ] 你关心的 6 个功能字段都能按需置位/清位。
- [ ] 释放句柄与映射，保证多次启动稳定。

---

## 13. 本文边界说明（防误解）

本文严格基于仓库中“可见代码证据”整理：

- 能确认的，是启动器与 DLL 的注入方式、共享内存协议、UI->字段映射关系。
- 不能 100% 断言的，是 DLL 内部每一位具体 hook 实现细节（因为仓库未包含 `nvd3dump.dll` 源码）。

但对于“如何调用它、如何驱动它、如何完整复刻本项目行为”，本文已覆盖实操所需全部路径。

---

## 14. 全量功能逐项调用手册（按“项目中 DLL 可控字段”一项不漏）

> 本节按 `IslandEnvironment` 实际字段给出“能做什么 + 怎么调 + 推荐取值”。

### 14.1 画面/镜头/显示类

1. **EnableSetFieldOfView（启用 FOV 覆盖）**  
   - 作用：允许 DLL 按 `FieldOfView` 覆盖游戏 FOV。  
   - 调用：`env->EnableSetFieldOfView = TRUE/FALSE;`

2. **FieldOfView（目标 FOV）**  
   - 作用：设置目标视角。  
   - 调用：`env->FieldOfView = 45.0f ~ 100.0f;`（UI 最小 1，最大 100）

3. **FixLowFovScene（特殊界面修正）**  
   - 作用：修正低 FOV 场景表现。  
   - 调用：`env->FixLowFovScene = TRUE/FALSE;`

4. **DisableFog（移除迷雾）**  
   - 作用：关闭迷雾效果。  
   - 调用：`env->DisableFog = TRUE/FALSE;`

5. **HideQuestBanner（关闭地图横幅）**  
   - 作用：隐藏特定横幅提示。  
   - 调用：`env->HideQuestBanner = TRUE/FALSE;`

6. **DisableEventCameraMove（移除元素爆发镜头特写）**  
   - 作用：抑制事件镜头移动。  
   - 调用：`env->DisableEventCameraMove = TRUE/FALSE;`

7. **DisableShowDamageText（移除战斗伤害跳字）**  
   - 作用：隐藏战斗数字文本。  
   - 调用：`env->DisableShowDamageText = TRUE/FALSE;`

8. **HideUid（隐藏 UID）**  
   - 作用：隐藏 UID 显示。  
   - 调用：`env->HideUid = TRUE/FALSE;`

### 14.2 帧率/性能类

9. **EnableSetTargetFrameRate（启用帧率覆盖）**  
   - 作用：允许 DLL 使用 `TargetFrameRate`。  
   - 调用：`env->EnableSetTargetFrameRate = TRUE/FALSE;`

10. **TargetFrameRate（目标帧率）**  
    - 作用：设置目标 FPS。  
    - 调用：`env->TargetFrameRate = 30/60/90/120...;`（UI 最大 120）

### 14.3 交互/入口行为类

11. **RemoveOpenTeamProgress（移除打开队伍进度）**  
    - 作用：修改队伍界面相关流程表现。  
    - 调用：`env->RemoveOpenTeamProgress = TRUE/FALSE;`

12. **UsingTouchScreen（触屏模式）**  
    - 作用：切换触屏输入相关行为。  
    - 调用：`env->UsingTouchScreen = TRUE/FALSE;`

13. **RedirectCombineEntry（重定向合成入口）**  
    - 作用：改变合成入口跳转逻辑。  
    - 调用：`env->RedirectCombineEntry = TRUE/FALSE;`

### 14.4 树脂选项（字段可见，但当前 UI 未开放）

14. **ResinListItemId000106Allowed**
15. **ResinListItemId000201Allowed**
16. **ResinListItemId107009Allowed**
17. **ResinListItemId107012Allowed**
18. **ResinListItemId220007Allowed**

- 作用：控制树脂相关列表项可用性。  
- 现状：字段存在，UI 未启用，页面 setter 未实现。  
- 调用（外部控制器可直接写）：

```cpp
env->ResinListItemId000106Allowed = TRUE;
env->ResinListItemId000201Allowed = TRUE;
env->ResinListItemId107009Allowed = TRUE;
env->ResinListItemId107012Allowed = TRUE;
env->ResinListItemId220007Allowed = TRUE;
```

> 注意：由于仓库没有 DLL 源码，上述 5 项“字段可控”可确认，但具体游戏内效果需以运行验证为准。

### 14.5 Reserved 协议头（不是功能开关，但是“DLL 通信前提”）

19. **Reserved[76]**

- 作用：承载固定 19 个 `DWORD` 初始化内容；是协议的一部分。  
- 调用：首次创建映射后执行 `memcpy(env, array, sizeof(array));`。  
- 建议：不要省略。

---

## 15. 推荐你直接复用的“控制 API 封装层”

如果你要写独立启动器，建议把所有 DLL 可控能力封装成函数，避免散写字段：

```cpp
struct IslandController {
    IslandEnvironment* env{};

    void SetDisableShowDamageText(bool on) { env->DisableShowDamageText = on; }
    void SetDisableFog(bool on)            { env->DisableFog = on; }
    void SetDisableEventCameraMove(bool on){ env->DisableEventCameraMove = on; }
    void SetFixLowFovScene(bool on)        { env->FixLowFovScene = on; }
    void SetHideQuestBanner(bool on)       { env->HideQuestBanner = on; }
    void SetRedirectCombineEntry(bool on)  { env->RedirectCombineEntry = on; }
    void SetHideUid(bool on)               { env->HideUid = on; }

    void SetFovEnabled(bool on)            { env->EnableSetFieldOfView = on; }
    void SetFov(float fov)                 { env->FieldOfView = fov; }
    void SetFpsEnabled(bool on)            { env->EnableSetTargetFrameRate = on; }
    void SetFps(uint32_t fps)              { env->TargetFrameRate = fps; }

    void SetTouchScreen(bool on)           { env->UsingTouchScreen = on; }
    void SetRemoveOpenTeamProgress(bool on){ env->RemoveOpenTeamProgress = on; }

    void SetResin000106(bool on)           { env->ResinListItemId000106Allowed = on; }
    void SetResin000201(bool on)           { env->ResinListItemId000201Allowed = on; }
    void SetResin107009(bool on)           { env->ResinListItemId107009Allowed = on; }
    void SetResin107012(bool on)           { env->ResinListItemId107012Allowed = on; }
    void SetResin220007(bool on)           { env->ResinListItemId220007Allowed = on; }
};
```

这样你就能“函数级”调用 DLL 的全部可见能力，而不是手改字段。

---

## 16. 别混淆：这些是启动器功能，不属于 DLL 能力

以下项目功能确实存在，但它们不是 `nvd3dump.dll` 共享内存控制项：

- 启动参数：全屏/独占/无边框/分辨率（命令行参数拼接）。
- Windows HDR 注册表开关。
- HoYo 登录等业务逻辑。

这部分是启动器对游戏进程的“启动前配置”，不是 DLL 运行时协议。
## 17. 对你提出的 7 点补充（逐条给结论）

### 17.1 `Reserved` 的真实初始化内容（完整常量表）

源码中的初始化数组是确定常量，共 **19 个 `DWORD`**，`sigewinne` 版本值如下：

```cpp
DWORD array[] = {
    0x15946c0, 0x15fa41d0, 0x1097ad0, 0x1097ac0, 0xcc45b70,
    0x61651d0, 0x417cc0, 0x74a59b0, 0xf87d050, 0xf879e20,
    0x78ae580, 0x9d24a70, 0xefc9f90, 0x9eb61f0, 0x1091400,
    0x1090be0, 0xfcdda80, 0x10a8d900, 0xa1eea40
};
```

然后写入方式是：

```cpp
memcpy(reinterpret_cast<char*>(penv), &array, sizeof(array));
```

因为 `19 * 4 = 76`，它恰好覆盖 `Reserved[76]` 全区域。

> 当前仓库代码只看到这一组常量，未见按版本切换常量表的分支。

### 17.2 `sizeof` 与字段偏移（协议对齐）

按 `sigewinne` 当前位域结构可得：

- `sizeof(IslandEnvironment) = 88`
- `offsetof(Reserved) = 0`
- `offsetof(FieldOfView) = 76`
- `offsetof(TargetFrameRate) = 80`
- `offsetof(Flags/位域存储单元) = 84`

其中 bit 分配为：

- bit0~bit15：各功能位（`EnableSetFieldOfView` ... `HideUid`）
- bit16~bit31：保留位

兼容说明：`Snap.Hutao-main` 中仍存在 `BOOL` 展开结构（`sizeof = 148`）。如果你做跨版本工具，需按目标版本选择布局。

### 17.3 mapping 创建大小 `1024` 的依据

源码是硬编码：

```cpp
CreateFileMapping(INVALID_HANDLE_VALUE, NULL, PAGE_EXECUTE_READWRITE, 0, 1024, L"4F3E8543-40F7-4808-82DC-21E48A6037A7");
```

不是 `sizeof(IslandEnvironment)`。

因此当前关系是：

- `1024 > 88`，明显预留扩展空间；
- 现有逻辑实际只用前 `sizeof(IslandEnvironment)` 区域。

### 17.4 `init_environment` 回填覆盖范围（精确清单）

启动时回填到 `penv` 的字段（源码逐行可见）：

- `FieldOfView`
- `TargetFrameRate`
- `EnableSetFieldOfView`
- `FixLowFovScene`
- `DisableFog`
- `EnableSetTargetFrameRate`
- `RemoveOpenTeamProgress`
- `HideQuestBanner`
- `DisableEventCameraMove`
- `DisableShowDamageText`
- `UsingTouchScreen`
- `RedirectCombineEntry`
- `HideUid`

**未在 `init_environment()` 回填的字段：**

- 5 个 `ResinListItemId...Allowed`

这两类主要靠运行时 setter / 外部写入改变。

### 17.5 共享内存与注入的先后顺序

在应用启动链路中顺序是：

1. `OnLaunched()` 中先 `LoadSettingsFromFile()`；
2. 然后 `init_environment()`（即先建/开 mapping 并回填）；
3. 若隐身模式开，再调用 `Launch()` 注入 DLL 并拉起游戏；
4. 非隐身模式下，用户手动点启动时也已经初始化过环境。

结论：**共享内存在注入前就准备好**。

### 17.6 mapping 权限标志（源码事实）

- `OpenFileMapping(FILE_MAP_READ | FILE_MAP_WRITE, ...)`
- `MapViewOfFile(FILE_MAP_READ | FILE_MAP_WRITE, ...)`
- `CreateFileMapping(..., PAGE_EXECUTE_READWRITE, ..., 1024, ...)`

也就是说，当前实现给的是可读写映射 + 可执行页属性（虽然就数据共享来说 `PAGE_READWRITE` 已够用）。

### 17.7 多实例行为

需要分两层看：

1. **Launcher 进程实例数**：通过 TLS 回调 + 全局互斥体限制为单实例。重复启动会检测 `ERROR_ALREADY_EXISTS` 后直接 `TerminateProcess`。
2. **共享内存实例数**：mapping 名称固定 GUID，天然全局唯一命名对象；同名打开的是同一段共享内存。

所以在当前程序设计里：

- 正常只有一个 launcher 实例；
- mapping 也只有一个命名实例；
- 若其他外部工具用同名打开，也会读写同一协议区（需要自行协调并发）。

---

## 18. 复刻时建议加的“自检断言”

为了防止未来版本结构变化导致协议错位，建议你的实现里加入：

```cpp
// sigewinne 位域协议
static_assert(sizeof(IslandEnvironment) == 88, "IslandEnvironment size changed");
static_assert(offsetof(IslandEnvironment, FieldOfView) == 76, "offset mismatch");
static_assert(offsetof(IslandEnvironment, TargetFrameRate) == 80, "offset mismatch");

// 若你要兼容 BOOL 展开协议，可额外放对应断言（例如 sizeof=148）
```

并在启动日志输出：

- mapping 名称
- mapping 大小
- struct 大小
- Reserved 初始化是否成功

这样一旦上游变更，你能第一时间发现协议不兼容。


## 19. 这是一个示例启动脚本

> 为避免文档示例与仓库脚本漂移，本节内容与 `minimal_launch_inject.py` 保持同步。你可以直接运行仓库中的该文件。

```python
import ctypes
import os
import struct
import tkinter as tk
from ctypes import wintypes
from tkinter import filedialog, messagebox

# ------------------------------
# Protocol constants
# ------------------------------
MAPPING_NAME = "4F3E8543-40F7-4808-82DC-21E48A6037A7"
MAPPING_SIZE = 1024

# Synced from sigewinne-toolkit-master/App6/Settings.cpp
MAGIC_DWORDS = [
    0x015946C0, 0x15FA41D0, 0x01097AD0, 0x01097AC0, 0x0CC45B70,
    0x061651D0, 0x00417CC0, 0x074A59B0, 0x0F87D050, 0x0F879E20,
    0x078AE580, 0x09D24A70, 0x0EFC9F90, 0x09EB61F0, 0x01091400,
    0x01090BE0, 0x0FCDDA80, 0x10A8D900, 0x0A1EEA40,
]

FLAG_BITS = {
    "EnableSetFieldOfView": 0,
    "FixLowFovScene": 1,
    "DisableFog": 2,
    "EnableSetTargetFrameRate": 3,
    "RemoveOpenTeamProgress": 4,
    "HideQuestBanner": 5,
    "DisableEventCameraMove": 6,
    "DisableShowDamageText": 7,
    "UsingTouchScreen": 8,
    "RedirectCombineEntry": 9,
    "ResinListItemId000106Allowed": 10,
    "ResinListItemId000201Allowed": 11,
    "ResinListItemId107009Allowed": 12,
    "ResinListItemId107012Allowed": 13,
    "ResinListItemId220007Allowed": 14,
    "HideUid": 15,
}

# ------------------------------
# Win32 constants
# ------------------------------
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


# New protocol layout used by sigewinne: 76-byte header + fov + fps + packed flags
class IslandEnvironment(ctypes.Structure):
    _fields_ = [
        ("Reserved", ctypes.c_ubyte * 76),
        ("FieldOfView", ctypes.c_float),
        ("TargetFrameRate", wintypes.DWORD),
        ("Flags", wintypes.DWORD),
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
        self.root.title("nvd3dump minimal injector (sigewinne protocol)")
        self.root.geometry("920x760")

        self.h_map = None
        self.view = None
        self.env_ptr = None

        self.game_path_var = tk.StringVar()
        self.dll_path_var = tk.StringVar(value=os.path.abspath("nvd3dump.dll"))

        self.flag_vars = {
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
            "ResinListItemId000106Allowed": tk.BooleanVar(value=True),
            "ResinListItemId000201Allowed": tk.BooleanVar(value=True),
            "ResinListItemId107009Allowed": tk.BooleanVar(value=True),
            "ResinListItemId107012Allowed": tk.BooleanVar(value=True),
            "ResinListItemId220007Allowed": tk.BooleanVar(value=True),
            "HideUid": tk.BooleanVar(value=False),
        }

        self.fov_var = tk.StringVar(value="45")
        self.fps_var = tk.StringVar(value="60")

        self._build_ui()

    def _build_ui(self):
        top = tk.LabelFrame(self.root, text="Paths")
        top.pack(fill="x", padx=8, pady=6)

        tk.Label(top, text="Game EXE").grid(row=0, column=0, sticky="w")
        tk.Entry(top, textvariable=self.game_path_var, width=92).grid(row=0, column=1, padx=6)
        tk.Button(top, text="Browse", command=self.pick_game).grid(row=0, column=2)

        tk.Label(top, text="nvd3dump.dll").grid(row=1, column=0, sticky="w")
        tk.Entry(top, textvariable=self.dll_path_var, width=92).grid(row=1, column=1, padx=6)
        tk.Button(top, text="Browse", command=self.pick_dll).grid(row=1, column=2)

        map_box = tk.LabelFrame(self.root, text="Shared Memory")
        map_box.pack(fill="x", padx=8, pady=6)
        tk.Button(map_box, text="Create/Attach Mapping", command=self.ensure_mapping).pack(side="left", padx=5, pady=5)
        tk.Button(map_box, text="Write Reserved Signature", command=self.write_reserved).pack(side="left", padx=5, pady=5)
        tk.Button(map_box, text="Apply All Options", command=self.apply_all).pack(side="left", padx=5, pady=5)

        feature = tk.LabelFrame(self.root, text="Flags (bit-packed)")
        feature.pack(fill="both", expand=True, padx=8, pady=6)

        row = 0
        for key in self.flag_vars:
            tk.Checkbutton(
                feature,
                text=f"{key} (bit {FLAG_BITS[key]})",
                variable=self.flag_vars[key],
            ).grid(row=row // 2, column=row % 2, sticky="w", padx=8, pady=2)
            row += 1

        value_box = tk.Frame(feature)
        value_box.grid(row=20, column=0, columnspan=2, sticky="w", padx=8, pady=8)
        tk.Label(value_box, text="FieldOfView").pack(side="left")
        tk.Entry(value_box, textvariable=self.fov_var, width=10).pack(side="left", padx=4)
        tk.Label(value_box, text="TargetFrameRate").pack(side="left", padx=(16, 0))
        tk.Entry(value_box, textvariable=self.fps_var, width=10).pack(side="left", padx=4)

        actions = tk.LabelFrame(self.root, text="Launch")
        actions.pack(fill="x", padx=8, pady=6)
        tk.Button(actions, text="Inject DLL (CreateRemoteThread + LoadLibraryW)", command=self.launch_with_inject).pack(side="left", padx=5, pady=5)

        self.log = tk.Text(self.root, height=10)
        self.log.pack(fill="both", expand=False, padx=8, pady=6)

    def logln(self, msg: str):
        self.log.insert("end", msg + "\n")
        self.log.see("end")

    def pick_game(self):
        path = filedialog.askopenfilename(title="Select game EXE", filetypes=[("Executable", "*.exe"), ("All", "*.*")])
        if path:
            self.game_path_var.set(path)

    def pick_dll(self):
        path = filedialog.askopenfilename(title="Select nvd3dump.dll", filetypes=[("DLL", "*.dll"), ("All", "*.*")])
        if path:
            self.dll_path_var.set(path)

    def ensure_mapping(self):
        if self.env_ptr is not None:
            self.logln("Mapping already attached")
            return

        created = False
        h_map = kernel32.OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, False, MAPPING_NAME)
        if not h_map:
            h_map = kernel32.CreateFileMappingW(
                wintypes.HANDLE(-1),
                None,
                PAGE_EXECUTE_READWRITE,
                0,
                MAPPING_SIZE,
                MAPPING_NAME,
            )
            ensure(bool(h_map), "CreateFileMappingW")
            created = True

        view = kernel32.MapViewOfFile(h_map, FILE_MAP_ALL_ACCESS, 0, 0, 0)
        ensure(bool(view), "MapViewOfFile")

        self.h_map = h_map
        self.view = view
        self.env_ptr = ctypes.cast(view, ctypes.POINTER(IslandEnvironment))

        if created:
            ctypes.memset(self.view, 0, ctypes.sizeof(IslandEnvironment))
            self.logln("Created new mapping")
        else:
            self.logln("Attached existing mapping")

    def write_reserved(self):
        self.ensure_mapping()
        data = b"".join(struct.pack("<I", x) for x in MAGIC_DWORDS)
        ensure(len(data) == 76, "Reserved signature length mismatch")
        ctypes.memmove(self.view, data, 76)
        self.logln("Reserved[76] written with sigewinne signature")

    def build_flags(self) -> int:
        flags = 0
        for name, bit in FLAG_BITS.items():
            if self.flag_vars[name].get():
                flags |= (1 << bit)
        return flags

    def apply_all(self):
        try:
            self.ensure_mapping()
            self.write_reserved()

            env = self.env_ptr.contents
            env.FieldOfView = float(self.fov_var.get())
            env.TargetFrameRate = int(self.fps_var.get())
            env.Flags = self.build_flags()

            self.logln(
                f"Applied environment: FOV={env.FieldOfView:.2f}, FPS={env.TargetFrameRate}, Flags=0x{env.Flags:08X}"
            )
        except Exception as ex:
            self.logln(f"[ERROR] {ex}")
            messagebox.showerror("Error", str(ex))

    def launch_with_inject(self):
        try:
            self.apply_all()

            game = self.game_path_var.get().strip().strip('"')
            dll = self.dll_path_var.get().strip().strip('"')
            if not os.path.exists(game):
                raise RuntimeError("Game EXE not found")
            if not os.path.exists(dll):
                raise RuntimeError("DLL not found")

            si = STARTUPINFOW()
            si.cb = ctypes.sizeof(si)
            pi = PROCESS_INFORMATION()

            cmdline = ctypes.create_unicode_buffer(f'"{game}"')
            workdir = os.path.dirname(game)
            ok = kernel32.CreateProcessW(
                None,
                cmdline,
                None,
                None,
                False,
                CREATE_SUSPENDED,
                None,
                workdir,
                ctypes.byref(si),
                ctypes.byref(pi),
            )
            ensure(bool(ok), "CreateProcessW")

            remote_mem = None
            h_thread = None
            try:
                path_bytes = dll.encode("utf-16le") + b"\x00\x00"
                path_buf = ctypes.create_string_buffer(path_bytes)

                remote_mem = kernel32.VirtualAllocEx(
                    pi.hProcess,
                    None,
                    max(4096, len(path_bytes)),
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE,
                )
                ensure(bool(remote_mem), "VirtualAllocEx")

                written = SIZE_T(0)
                ok = kernel32.WriteProcessMemory(
                    pi.hProcess,
                    remote_mem,
                    ctypes.cast(path_buf, LPVOID),
                    len(path_bytes),
                    ctypes.byref(written),
                )
                ensure(bool(ok) and written.value == len(path_bytes), "WriteProcessMemory")

                h_kernel32 = kernel32.GetModuleHandleW("kernel32.dll")
                ensure(bool(h_kernel32), "GetModuleHandleW")
                p_load_library_w = kernel32.GetProcAddress(h_kernel32, b"LoadLibraryW")
                ensure(bool(p_load_library_w), "GetProcAddress(LoadLibraryW)")

                h_thread = kernel32.CreateRemoteThread(
                    pi.hProcess,
                    None,
                    0,
                    p_load_library_w,
                    remote_mem,
                    0,
                    None,
                )
                ensure(bool(h_thread), "CreateRemoteThread")

                wait_result = kernel32.WaitForSingleObject(h_thread, INFINITE)
                ensure(wait_result == WAIT_OBJECT_0, "WaitForSingleObject")

                exit_code = wintypes.DWORD(0)
                ok = kernel32.GetExitCodeThread(h_thread, ctypes.byref(exit_code))
                ensure(bool(ok), "GetExitCodeThread")
                if exit_code.value == 0:
                    raise RuntimeError("LoadLibraryW returned NULL")

                resume_result = kernel32.ResumeThread(pi.hThread)
                if resume_result == 0xFFFFFFFF:
                    raise RuntimeError(f"ResumeThread failed, GetLastError={get_last_error()}")

                self.logln(f"Inject success: PID={pi.dwProcessId}, HMODULE=0x{exit_code.value:08X}")
                messagebox.showinfo("Success", "Game launched and DLL injected successfully")
            finally:
                if h_thread:
                    kernel32.CloseHandle(h_thread)
                if remote_mem:
                    kernel32.VirtualFreeEx(pi.hProcess, remote_mem, 0, MEM_RELEASE)
                if pi.hThread:
                    kernel32.CloseHandle(pi.hThread)
                if pi.hProcess:
                    kernel32.CloseHandle(pi.hProcess)
        except Exception as ex:
            self.logln(f"[ERROR] {ex}")
            messagebox.showerror("Error", str(ex))


def main():
    if os.name != "nt":
        raise SystemExit("Windows only")

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

```
