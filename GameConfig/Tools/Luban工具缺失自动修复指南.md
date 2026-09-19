# Luban 导表工具缺失自动修复指南

> 适用对象：Codex、Claude Code 等 AI Agent。
> 用途：当**调用导表工具失败且根因是缺少 Luban 工具**时，按本文步骤自动下载、构建并安装 Luban，然后重新导表。
> 命令在 Windows（PowerShell / cmd）下执行。
>
> **路径约定**：除特别说明外，本文所有相对路径均以**仓库根目录**（即包含 `GameConfig`、`Tools`、`GameUnity` 的 `DGame\` 目录）为基准。
> `..\` 表示仓库根目录的上一级。

---

## 0. 触发条件（什么时候执行本指南）

满足以下**任一**现象，判定为「缺少 Luban 工具」，进入修复流程：

- 运行导表脚本（`GameConfig\GenerateTool_Binary\*.bat` 或 `GameConfig\GenerateTool_Json\*.bat`）时报错。
- 检查发现 Luban 工具 DLL 不存在：
  - 路径（相对仓库根）：`GameConfig\Tools\LubanTools\Luban\Luban.dll`
  - 不存在 → 缺少 Luban 工具，需修复。
- 报错信息包含 `Luban.dll` 找不到、`dotnet` 无法定位程序集等。

> 若 `Luban.dll` 已存在，但导表仍失败，则**不是**本指南范畴（属于配置表 / Excel / 模板问题），不要执行下载构建。

---

## 1. 关键路径与脚本（已校验，均相对仓库根）

| 名称 | 相对路径 |
|------|------|
| 导表工具（二进制） | `GameConfig\GenerateTool_Binary\` |
| 导表工具（JSON） | `GameConfig\GenerateTool_Json\` |
| Luban 构建脚本目录 | `Tools\LubanTools\` |
| 构建脚本 | `Tools\LubanTools\build-luban.bat` |
| 拷贝脚本 | `Tools\LubanTools\copy_luban.bat` |
| **最终产物（校验目标）** | `GameConfig\Tools\LubanTools\Luban\Luban.dll` |
| Luban 源码根目录（仓库外层） | `..\luban\`（与仓库根同级） |
| Luban 仓库 | https://github.com/focus-creative-games/luban |

**脚本行为（已读取确认）：**

- `build-luban.bat`：执行
  `dotnet build ../../../luban/src/Luban/Luban.csproj -c Release -o Luban`
  → **从 Luban 源码编译**，输出到脚本同级的 `Tools\LubanTools\Luban\`。
  其中 `../../../luban` 相对脚本所在的 `Tools\LubanTools\` 解析为仓库根上一级的 **`..\luban\`**（即 Luban 源码需放在仓库根的同级目录 `luban\`）。
- `copy_luban.bat`：调用同目录 `path_define.bat`，用 `robocopy` 把
  `Tools\LubanTools\Luban\` → `GameConfig\Tools\LubanTools\Luban\`。
- 导表脚本的 `path_define.bat` 中 `LUBAN_DLL=../Tools/LubanTools/Luban/Luban.dll`（相对 `GameConfig\GenerateTool_*\`），
  即导表依赖 `GameConfig\Tools\LubanTools\Luban\Luban.dll`。

**前置依赖：** 需已安装 .NET SDK（`dotnet` 命令可用）。本机已确认 `dotnet 10.x` 可用。

---

## 2. 修复步骤

> 以下命令默认**已在仓库根目录**下执行（`cd` 到各脚本目录均用相对路径）。

### 步骤 1：下载 Luban 压缩包

1. 打开 Luban Releases 页面：https://github.com/focus-creative-games/luban/releases
2. 找到**最新 Release**，下载其中的 `Luban.7z`（注意大小写，资源名可能为 `Luban.7z` 或 `luban.7z`）。
3. 将压缩包下载到（相对仓库根）：
   ```
   Tools\LubanTools\
   ```

> ⚠️ **路径校验要点（重要）**：`build-luban.bat` 编译的源码路径是仓库根同级的 `..\luban\src\Luban\Luban.csproj`。
> 解压后**必须确认源码 `.csproj` 位于该路径**：
> - 若解压出的目录是 Luban 源码且含 `src\Luban\Luban.csproj`，请确保最终源码根目录在仓库根同级的 `..\luban\`（必要时把解压内容移动 / 重命名到此处）。
> - 若 `Luban.7z` 解压出的是**已编译好的二进制**（直接包含 `Luban.dll`），则**无需执行 build-luban.bat**，可直接进入步骤 3 的拷贝逻辑（或手动放置到校验目标路径）。
> - 解压后请先 `dir` 查看实际内容再决定走哪条分支，不要盲目执行。

**解压方式不限**——只要能把 `.7z` 正确解压到 `Tools\LubanTools\` 即可，用任意支持 7z 格式的工具：
- 命令行：`7z` / `7za`、WinRAR 的 `WinRAR.exe x` / `Rar.exe`、Bandizip 的 `bz.exe` 等；
- GUI：7-Zip、WinRAR、Bandizip 等右键解压；
- 注意：PowerShell 自带的 `Expand-Archive` **只支持 `.zip`，不支持 `.7z`**，不要用它解压本压缩包。

命令行示例（以 `7z` 已在 PATH 为例，先 `cd` 到压缩包目录）：
```powershell
cd Tools\LubanTools
7z x .\Luban.7z -o. -y
```
> 若解压工具不在 PATH，用其可执行文件的实际安装路径调用即可（属系统软件路径，非项目路径，按本机实际位置填写）。

### 步骤 2：构建 Luban（源码分支）

> 仅当步骤 1 得到的是**源码**时执行。

```powershell
cd Tools\LubanTools
.\build-luban.bat
```

- 成功标志：命令以 `Build succeeded` 结束，且生成
  `Tools\LubanTools\Luban\Luban.dll`。
- 失败常见原因：
  - 源码路径不对（仓库根同级 `..\luban\src\Luban\Luban.csproj` 不存在）→ 回到步骤 1 校验解压位置。
  - 未安装 .NET SDK → 安装后重试。
- 注意：`build-luban.bat` 末尾有 `pause`，自动化执行时需处理交互暂停（如以非交互方式调用或追加回车）。

### 步骤 3：拷贝到导表工具目录

构建成功后执行：
```powershell
cd Tools\LubanTools
.\copy_luban.bat
```

- 作用：把 `Tools\LubanTools\Luban\` 拷贝到 `GameConfig\Tools\LubanTools\Luban\`。
- 成功标志：脚本输出「复制完成！」（`robocopy` 退出码 ≤ 7 视为成功）。
- 同样含 `pause`，自动化时需处理交互暂停。

### 步骤 4：校验 Luban 工具是否生成

检查最终产物是否存在（相对仓库根）：

```powershell
Test-Path ".\GameConfig\Tools\LubanTools\Luban\Luban.dll"
```

- 返回 `True` → Luban 工具安装成功，进入步骤 5。
- 返回 `False` → 失败，回查步骤 2 / 步骤 3 的输出日志定位原因，**不要**继续导表。

### 步骤 5：重新调用导表工具

校验通过后重新导表。

**调用原则（遵循项目约定，来源：dgame-dev skill `luban-game-config-claude.md`）：**

1. 优先重新执行**最初失败的那个**导表脚本，保持与本次需求一致的目标（client / server / all）与格式（bin / json）。
2. 若没有明确指定脚本，**默认走客户端二进制 + LazyLoad**：`GenerateTool_Binary\gen_bin_client_lazyload.bat`。
3. 客户端无特殊说明时优先二进制（`cs-bin`）与 LazyLoad 流程，不要随意改用 Json 或非 LazyLoad 脚本。

常用入口（相对仓库根）：

| 目标 | 脚本 | 说明 |
|------|------|------|
| **二进制 · 客户端 · LazyLoad（默认首选）** | `GameConfig\GenerateTool_Binary\gen_bin_client_lazyload.bat` | 无特殊说明优先此项 |
| 二进制 · 客户端（非 LazyLoad） | `GameConfig\GenerateTool_Binary\gen_bin_client.bat` | 明确不要 LazyLoad 时 |
| 二进制 · 全部 · LazyLoad | `GameConfig\GenerateTool_Binary\gen_bin_all_lazyload.bat` | 客户端+服务器 |
| 二进制 · 全部 | `GameConfig\GenerateTool_Binary\gen_bin_all.bat` | 客户端+服务器 |
| 二进制 · 仅服务器 | `GameConfig\GenerateTool_Binary\gen_bin_server.bat` | |
| JSON · 客户端 | `GameConfig\GenerateTool_Json\gen_json_client.bat` | `cs-simple-json` |
| JSON · 全部 | `GameConfig\GenerateTool_Json\gen_json_all.bat` | |

示例（默认首选脚本）：
```powershell
cd GameConfig\GenerateTool_Binary
.\gen_bin_client_lazyload.bat
```

- 成功标志：脚本正常结束，且生成产物输出到约定目录：
  - 配置代码：`GameUnity\Assets\Scripts\HotFix\GameProto\LubanConfig\`
  - 二进制数据：`GameUnity\Assets\BundleAssets\Configs\Binary\`
  - 同时 `ConfigSystem.cs`、`ExternalTypeUtil.cs` 会复制到 `GameUnity\Assets\Scripts\HotFix\GameProto\`
- 注意：生成的 `LubanConfig\*.cs` 是自动产物，**不要手改**。

---

## 3. 流程总览

```
导表失败
   │
   ▼
缺少 Luban.dll ?  ──否──▶ 非本指南范畴（查配置表/模板/Excel）
   │是
   ▼
下载 Luban.7z 到 Tools\LubanTools\ 并解压
   │
   ├─ 解压为源码  ──▶ 确认源码在 ..\luban（仓库根同级）──▶ build-luban.bat
   └─ 解压为二进制 ──▶ （跳过构建）
   │
   ▼
copy_luban.bat  ──▶  GameConfig\Tools\LubanTools\Luban\
   │
   ▼
校验 GameConfig\Tools\LubanTools\Luban\Luban.dll 是否存在
   │存在
   ▼
重新执行导表脚本（优先最初失败的那个；
未指定时默认 gen_bin_client_lazyload.bat）
```

---

## 4. 注意事项

1. **前置依赖**：必须已安装 .NET SDK（`dotnet` 可用）；解压 `.7z` 需任意支持该格式的工具（7-Zip / WinRAR / Bandizip 等，不限定具体工具），注意 PowerShell 的 `Expand-Archive` 不支持 `.7z`。
2. **bat 的 `pause`**：`build-luban.bat` / `copy_luban.bat` 末尾都有 `pause`，AI 自动化执行时要预期到交互阻塞，需用非交互方式或自动输入回车。
3. **路径强校验**：执行任何 bat 前，先确认其依赖路径真实存在（尤其是 `build-luban.bat` 的源码路径——仓库根同级的 `..\luban`），避免基于假设盲跑。
4. **相对路径基准**：本文相对路径以仓库根目录（`DGame\`）为基准；执行命令前请确认当前工作目录，再决定 `cd` 的相对层级。
5. **只在根因为缺工具时执行**：`Luban.dll` 已存在却导表失败时，问题在配置层（Excel / Defines / 模板），不要走本流程。
6. **失败即停**：任一步骤失败时停止并报告该步骤日志，不要带着错误继续后续步骤。
