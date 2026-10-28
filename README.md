---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 236021e9d3b6d35e9035f1c23f28aea0_97c4e530d2a511f1992e525400638852
    ReservedCode1: yt/cjXAwNBvUii7PyTTRWwgs7o+Ut+ZepMXuHPLJrvwZCamyrqjdeIdMYPi+jFTr0RA/wdFD0WYtsDWfOfnNWC0KWyku+GYPKB09cezYbTWdYoRNnmsMRMWuU7DDChWYn1wxjVYkZqNbHUZljhOCALR6JJKbprBsXSZDWuUphWFPhPrA0QngavX8aBI=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 236021e9d3b6d35e9035f1c23f28aea0_97c4e530d2a511f1992e525400638852
    ReservedCode2: yt/cjXAwNBvUii7PyTTRWwgs7o+Ut+ZepMXuHPLJrvwZCamyrqjdeIdMYPi+jFTr0RA/wdFD0WYtsDWfOfnNWC0KWyku+GYPKB09cezYbTWdYoRNnmsMRMWuU7DDChWYn1wxjVYkZqNbHUZljhOCALR6JJKbprBsXSZDWuUphWFPhPrA0QngavX8aBI=
---

# BobCore

CS2（Counter-Strike 2）视角武器晃动整形插件，基于 **CounterStrikeSharp**。
把原本写死在客户端里的 viewmodel bob（走路时武器/手臂晃动、开镜时镜框晃动）抽出来，交给插件按档位接管。

两条互相独立的通道：

| 通道 | 控制 cvar | 作用对象 | 档位 |
|---|---|---|---|
| 手臂 | `cl_bob` | 武器/手臂在屏幕上的晃动与位移 | `0` 原版 / `1` legacy 复刻 / `2` 完全不晃（位置冻结） |
| 狙击镜 | `cl_bob_sniper` | 开镜后狙击镜黑框的晃动 | `0` 原版 / `1` legacy 复刻 / `2` 镜框完全静止 |

> 定位方式：启动时用 AOB 签名在 `client.dll` 内找到 bob 计算函数与镜框偏置写入点，运行时以指令级 detour / cave 方式接管波形。
> **不修改磁盘上的 `client.dll`**，只在进程内存里临时改写指令页；进程退出即消失。

---

## 功能

- `cl_bob` 三档：原版行为 / legacy 单源复刻（相位与幅度由插件接管）/ 位置冻结
- `cl_bob_sniper` 三档：原版 / legacy 单源复刻 / 镜框恒静止
- 幅度、周期、上摆、下沉参数沿用游戏原生 cvar（`cl_bobcycle`、`cl_bobup`、`cl_bobamt_vert`、`cl_bobamt_lat`、`cl_bob_lower_amt`）
- 参数自动持久化：改动后写入 `<数据根>\cl_bob_persist.cfg`，下次启动自动回填
- 全量日志：`<数据根>\cl_bob.log`，行首带通道前缀 `[arm]` / `[scope]` / `[loc]` / `[cfg]`
- 启动自检：每个签名都会与内置基准 RVA 比对，任一项校验失败则该站点不安装、自动退回原版，不影响其它功能

---

## 环境要求

| 项目 | 要求 |
|---|---|
| 游戏 | CS2 独立服务器 / 本地 listen server（`-insecure`） |
| 插件框架 | [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) **1.0.284**（API 版本需与本插件编译时一致） |
| 构建 | .NET 8 SDK |
| 平台 | Windows x64（依赖 `kernel32` 的 `VirtualProtect` / `VirtualAlloc`） |

> 签名与偏移是针对某个具体 `client.dll` 版本测量的，**游戏更新后大概率失效**（插件会打印定位失败并退化为原版行为）。

---

## 构建

```bat
:: 默认：从 NuGet 拉取 CounterStrikeSharp.API 1.0.284
dotnet build src\BobCore.csproj -c Release

:: 或使用服务器自带的 API dll（离线环境）
dotnet build src\BobCore.csproj -c Release -p:CssApiDir="D:\你的服务器\game\csgo\addons\counterstrikesharp\api"
```

或直接双击 `build.cmd`。产物：`src\bin\Release\BobCore.dll`

---

## 安装

```
game\csgo\addons\counterstrikesharp\plugins\BobCore\
├─ BobCore.dll
├─ BobCore.deps.json
└─ BobCore.runtimeconfig.json
```

`BobCore.deps.json` / `BobCore.runtimeconfig.json` 由构建一并产出（`bin\Release\` 内）。

---

## 配置

### cvar 表

| cvar | 默认 | 范围 | 说明 |
|---|---|---|---|
| `cl_bob` | 0 | 0 – 2 | 手臂通道档位：0 原版 / 1 legacy / 2 不晃 |
| `cl_bob_sniper` | 1 | 0 – 2 | 镜框通道档位：0 原版 / 1 legacy / 2 静止 |
| `cl_bobcycle` | 0.98 | 0.10 – 2.00 | 晃动周期 |
| `cl_bobup` | 0.5 | 0 – 1 | 上摆权重 |
| `cl_bobamt_vert` | 0.25 | 0.10 – 2.00 | 纵向幅度 |
| `cl_bobamt_lat` | 0.40 | 0.10 – 2.00 | 横向幅度 |
| `cl_bob_lower_amt` | 21 | 5 – 35 | 下沉幅度 |

示例见 `examples/BobCore.cfg`。参数被修改后会自动持久化，无需手写 cfg。

### 数据目录

日志与持久化文件默认落在**服务器根目录**（即 `...\game\csgo` 的上两级，例如 `D:\cs2server`）。
需要固定位置时设置环境变量：

```bat
set BOBCORE_ROOT=D:\cs2server
```

回退顺序：`BOBCORE_ROOT` → 服务器根目录 → 插件自身目录。

---

## 日志与排障

`cl_bob.log` 常用行：

| 前缀 | 内容 |
|---|---|
| `[loc]` | 签名定位报告：命中地址、解析 RVA、与内置基准的差值 |
| `[cfg]` | cvar 注册 / 持久化回填 / 保存 |
| `[arm]` | 手臂通道状态切换与参数来源（`BG2` 后台常量 / `cvar`） |
| `[scope]` | 镜框通道状态与偏置写入 |
| `beat` | 每帧统计：命中计数、相位、倍率等，用于定位「抖而不动 / 动而不抖」 |

---

## 兼容性与已知问题

- **`cl_bob=1` 与原生累加段叠加**：1 档的增量与原生 bob 波形在部分路径上仍会叠加，实测表现为幅度个体差异；2 档（冻结）不受影响。
- **开镜镜框与 recoil 插件共存**：镜框站点为「覆盖式写回」，改的是偏置槽位，不吞 `viewmodel_recoil` 的视角回弹（在 1.0.284 上验证）。
- **仅影响本地渲染**：改动的是客户端 viewmodel 变换，不影响弹道、命中判定与服务器权威状态。
- **使用位置**：本插件属于客户端渲染修改，只在本地 listen server / 自建服务器 + 本地客户端上有意义。

---

## 免责声明

本项目仅用于**本地离线、自建服务器与 demo 研究**。在受 VAC 保护的官方服务器上使用任何修改客户端行为的插件，均可能导致账号封禁，风险自负。
仓库不包含任何游戏本体文件、反编译产物或游戏资源。

---

## 目录结构

```
tonysinke/
├─ src/
│  ├─ BobCore.cs          # 插件主体：签名定位、cave 构造、两条通道的状态机
│  ├─ AobLocator.cs       # AOB 签名表与扫描/校验
│  └─ BobCore.csproj
├─ docs/CHANGELOG.md      # 版本记录（从源码注释抽取）
├─ examples/BobCore.cfg   # cvar 示例
├─ build.cmd
├─ LICENSE
└─ README.md
```

---

## 开源范围说明

本仓库默认公开**完整源码**。若希望只公开签名数据、不公开注入实现，可保留 `src/AobLocator.cs` 与文档、把 `src/BobCore.cs` 移入私有仓库，
README 中保留 cvar 表与行为说明即可 —— 使用者仍能自行按签名实现，但拿不到现成的 detour 逻辑。

## License

MIT，见 [LICENSE](LICENSE)。

