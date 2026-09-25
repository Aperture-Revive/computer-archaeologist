# Computer Archaeologist · 计算机考古学家

> **重新发现电脑里被遗忘的角落。**
> **Rediscover the forgotten corners of your computer.**

**简体中文** | [English](README.en.md)

**作者 [@Aperture-Revive](https://github.com/Aperture-Revive)** · 采用 [MIT 许可证](LICENSE)

---

计算机考古学家是一款 Windows 桌面应用。它会搜索**你自己的电脑**，找出那些承载着历史的文件——被放弃的项目、老文档、奇怪的一次性文件、游戏存档、学生时代的作业、代码实验——为每一个文件计算"有趣值"，最后生成一份可读的**考古报告**。

这是一款**只读**工具：它绝不修改、移动、重命名或删除任何文件，也**不依赖任何外部组件**——不需要索引服务、不需要后台进程、不需要联网。

---

## 目录

- [它能做什么](#它能做什么)
- [环境要求](#环境要求)
- [下载](#下载)
- [从源码构建](#从源码构建)
- [运行](#运行)
- [文件发现是怎么工作的](#文件发现是怎么工作的)
- [配置 AI 接口](#配置-ai-接口)
- [隐私：究竟有什么会离开你的电脑](#隐私究竟有什么会离开你的电脑)
- [有趣值算法](#有趣值算法)
- [架构](#架构)
- [多语言](#多语言)
- [测试](#测试)
- [常见问题](#常见问题)
- [已知限制](#已知限制)
- [许可证](#许可证)
- [作者](#作者)

---

## 它能做什么

一次考古会依次经过七个阶段：

| # | 阶段 | 发生的事情 |
|---|------|-----------|
| 1 | 准备扫描 | 解析你选择的范围（整台电脑 / 某个磁盘 / 指定文件夹），启动遍历器线程池。 |
| 2 | 搜索文件 | 并行遍历文件系统，**在进入目录之前就剪掉排除项**，并流式返回结果。 |
| 3 | 收集元数据 | 为存活的候选文件读取目录结构与上下文信息。 |
| 4 | 计算本地有趣值 | 八个可解释的本地特征得出 `0–100` 的本地分数；有界 top-N 堆让内存保持恒定。 |
| 5 | AI 分析候选文件 | 只有本地得分最高的候选才会发送到你配置的 OpenAI 兼容接口，并发受限、请求体有上限。 |
| 6 | 综合评分 | 本地证据与 AI 判断融合，**AI 权重按其自报的置信度缩放**。 |
| 7 | 生成报告 | 完整报告**在应用内**排版呈现：分章节、发现卡片、时间线，并可导出 Markdown。 |

最终你会得到一份可以浏览、排序、筛选、查看详情、预览和打开的文件清单。

---

## 环境要求

| 组件 | 要求 |
|------|------|
| 操作系统 | Windows 10 1809（内部版本 17763）及以上，含 Windows 11 |
| 架构 | x64 |
| .NET | **.NET 8** 桌面运行时。程序是框架依赖部署，因此需要安装 .NET 8 运行时。 |
| Windows App SDK | **Windows App SDK 2.5.1**——运行时**已打包在发布目录内**（`WindowsAppSDKSelfContained`），因此无需单独安装 Windows App Runtime，也不需要 MSIX 部署。 |
| AI 接口 | 可选。任何 OpenAI 兼容的 chat completions 接口都可以。 |

除此之外什么都不需要：**没有索引要装，没有服务要开，第一次扫描前也无需任何配置**。

项目使用 .NET SDK 10.0.4xx 构建，目标框架为 `net8.0-windows10.0.19041.0`，在一台只装了 .NET 8 运行时的机器上即可运行。

---

## 下载

不想自己编译的话，直接从 **Releases** 页面获取：

### [⬇ 下载 Computer-Archaeologist-1.0.0-win-x64.zip](https://github.com/Aperture-Revive/computer-archaeologist/releases/download/v1.0.0/Computer-Archaeologist-1.0.0-win-x64.zip)

| | |
|---|---|
| 平台 | Windows 10 1809+ / Windows 11，x64 |
| 大小 | 42.0 MB（解压后 127 MB） |
| SHA-256 | `ae5a2cf477ca679b9e234d61717b9c400a0cade523b6dd64ce092b22ad54c743` |
| 发布说明 | [v1.0.0](https://github.com/Aperture-Revive/computer-archaeologist/releases/tag/v1.0.0) · [全部版本](https://github.com/Aperture-Revive/computer-archaeologist/releases) |

解压到任意目录，双击 `Computer Archaeologist.exe` 即可。**无需安装程序**，Windows App SDK 运行时已随包提供，唯一的前置条件是 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。

压缩包内的 `READ-ME-FIRST.txt` 包含快速上手、隐私说明与许可信息。仓库内的 [`dist/`](dist/) 目录也保留了一份相同的副本。

---

## 从源码构建

```powershell
git clone https://github.com/Aperture-Revive/computer-archaeologist.git
cd computer-archaeologist

# 还原并构建全部内容（应用 + 核心库 + 测试）
dotnet build "Computer Archaeologist.slnx" -c Debug -p:Platform=x64

# 运行测试
dotnet test "Computer Archaeologist.Tests\Computer Archaeologist.Tests.csproj"

# 生成发布包
dotnet publish "Computer Archaeologist\Computer Archaeologist.csproj" `
  -c Release -p:Platform=x64 -r win-x64 --self-contained false -o publish\win-x64
```

应用目标框架为 `net8.0-windows10.0.19041.0`，需要 Windows 版 .NET SDK 自带的 Windows SDK 投影。解决方案使用 `.slnx` 格式，`dotnet build` 与 Visual Studio 2022+ 均可直接打开。

> **关于 Windows App SDK 2.x 的组件引用**
> 项目引用的不是 `Microsoft.WindowsAppSDK` 元包，而是它实际需要的组件（`Base`、`Foundation`、
> `InteractiveExperiences`、`WinUI`、`DWrite`、`Runtime`）。这样可以彻底避免把用不到的 AI / ML /
> Search / Widgets 负载（`onnxruntime.dll`、`DirectML.dll`、`Microsoft.Windows.AI.*` 等，约 60 MB）
> 复制进发布目录——发布体积因此从 187 MB 降到 128 MB。

---

## 运行

```powershell
# 直接运行构建产物
.\"Computer Archaeologist\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Computer Archaeologist.exe"
```

或在 Visual Studio 中按 <kbd>F5</kbd>。

首屏是**主页**，并且会**恢复上一次的运行结果**：上次扫描的各项指标、当时最有趣的文件、以及可点击的运行历史都会直接呈现，不需要先重新扫描一次。

点击**开始考古**并选择范围。对话框会用**设置 → 限定搜索范围**的值预填，并且**该选择对本次运行具有权威性**：

- **整台电脑**（默认）
- **磁盘 C:** / **磁盘 D:** / …
- **指定文件夹**——用原生文件夹选择器挑选一个或多个目录

应用写入的所有内容都位于 `%LOCALAPPDATA%\Computer Archaeologist\`：

```
settings.json          仅包含非敏感配置
credentials.bin        API Key 的 DPAPI 回退存储（仅在凭据管理器不可用时使用）
Logs\                  按天滚动的结构化日志（不含 API Key，也不含文件内容）
sessions\              每次运行一个 JSON 文件，外加一个小的摘要文件
exports\               你主动导出时生成的 Markdown 报告
```

---

## 文件发现是怎么工作的

计算机考古学家**自己遍历文件系统**。这里**没有外部索引，也没有任何第三方服务**，因此消除了一整类故障：没有东西要安装，没有进程要保持运行，也不会出现某个独立进程重建索引导致扫描卡住的情况。

这套遍历在多百万文件的机器上依然很快：

- **广度优先 + 共享工作队列。** 一组遍历器（默认 6 个，可调 1–32）从同一个队列取目录，因此每块磁盘都在并行工作，而不是单线程在深目录树里爬行。
- **排除目录在进入之前就被剪掉。** `node_modules`、`.git\objects`、`bin`、`obj`、各类包缓存、浏览器缓存、`AppData\Local\Temp`、`C:\Windows` 等等，代价只是一次属性判断，而不是对几百万条目做一遍过滤。
- **元数据直接取自目录列表。** 在 Windows 上，一次目录枚举已经带回了大小、时间和属性，因此构造元数据记录**不产生额外的文件系统往返**。
- **结果通过有界 Channel 流动。** 发现阶段永远追不上评分阶段，所以无论范围多大内存都保持恒定：流水线里始终只有一个文件加上固定大小的候选堆。
- **两个硬性预算。** 最大检查文件数（默认 400,000）与墙钟预算（默认 10 分钟）同时生效，取消是协作式的，不使用线程中止。
- **无法读取的目录会被跳过，绝不致命。** 单个权限错误不可能终止整次扫描。

在一台约 260 万文件的开发机上实测：整机扫描 **6 个磁盘根、250,000 个文件，耗时 1 分 44 秒**（默认 8 个遍历器，约 2,400 文件/秒，CPU 时间约 21 秒），界面全程保持响应。

### 哪些东西绝不会被碰到

- **符号链接、junction 等重解析点绝不跟随**，因此链接成环不会导致无限遍历。
- 发现阶段只读取目录列表与文件元数据。排除规则按**整段路径**匹配且不区分大小写，所以 `D:\Games\Save Games` 不会被误判成构建输出目录。

---

## 配置 AI 接口

打开**设置 → OpenAI 兼容 API**：

| 字段 | 说明 |
|------|------|
| API Key | 保存在 **Windows 凭据管理器**（目标名 `ComputerArchaeologist/OpenAI`）。若该 API 不可用，则改为写入以当前用户为作用域的 DPAPI 加密文件。它**绝不会**进入 `settings.json`、仓库、日志文件或崩溃转储。 |
| Base URL | 默认 `https://api.openai.com/v1`。任何 OpenAI 兼容接口都可以。 |
| 模型 | 默认 `gpt-4o-mini`。**没有硬编码**，可随意更换。 |

点击**测试连接**会发出一个最小请求，并给出人类可读的结论（`连接成功`、`API Key 被拒绝（401）`、`未找到该接口或模型（404）` 等）。Key 不会回显，也不会出现在任何错误信息或日志里。

**不用 API Key 也完全可以使用本软件。** 没有 Key 时，报告完全基于本地测量结果生成。

---

## 隐私：究竟有什么会离开你的电脑

默认是**本地优先**：

- 文件发现、元数据收集、目录分析与整个有趣值算法**全部在本机完成**。
- 只有在本地评分中存活的候选才可能被发往 AI 接口，且数量有上限（**设置 → 每次运行最大 AI 分析数**，默认 300）。

**设置 → 隐私**提供三种模式：

| 模式 | 发送的内容 |
|------|-----------|
| **仅元数据**（默认） | 文件名、路径、扩展名、大小、时间戳、属性、文件夹上下文（同级文件数量、邻近文件名）以及本地特征分数。**不发送文件内容。** |
| **智能内容** | 上述内容，外加从文本类文件中读取的**有界**片段（默认 32 KB，硬上限 256 KB）。 |
| **禁止 AI 内容分析** | 什么都不发。完全不发出请求，报告在本地生成。 |

代码中强制保证的硬性约束：

- **大文件永不上传。** 片段读取器在配置的上限处停止；压缩包、音频、视频和图片绝不会以原始数据形式被读取。
- **图片分析只读文件头字节**（尺寸与格式）。**像素永不外传。** 应用内预览在本地解码出缩小后的缩略图，仅用于显示。
- 提示词中的路径与文件名会被加引号并去掉换行，因此恶意文件名无法向提示词注入指令。
- 系统提示明确要求模型：不得编造时间戳、内容、作者或用途；不得复述片段中出现的机密；证据不足时必须使用"可能""看起来像"等措辞。
- 报告严格区分**事实（Facts，实测）**与**AI 推断（AI interpretation）**，并在报告头部标明叙述是模型撰写还是本地生成。

本软件**绝不**：

- 要求管理员权限；
- 写入注册表；
- 修改、删除、移动或重命名你的文件；
- 更改权限；
- 在没有明确点击的情况下执行文件——对于 `.exe`、`.bat`、`.cmd`、`.ps1`、`.vbs`、`.js`、`.scr` 等类型，会先弹出确认对话框。

---

## 有趣值算法

### 本地特征（全部可解释，全部映射到 `0–100`）

| 特征 | 默认权重 | 衡量的是什么 |
|------|---------|-------------|
| `Age` 年龄 | 0.15 | 非线性年龄曲线。一个月几乎不算什么，一年中等，三年偏高，十年饱和。**"老"本身绝不等于有趣**，且年龄上限低于 100，不可能单独主导结果。 |
| `Rarity` 稀有度 | 0.15 | 该扩展名在本次扫描集合中出现的频率（对数曲线）。`.jpg` 很低，`.blend` 较高，未收录的扩展名更高。 |
| `PathContext` 路径语境 | 0.15 | 目录的语义：`Documents\School\Grade8\Physics` 得分高，`AppData\Local\Temp` 得分低。 |
| `Filename` 文件名 | 0.10 | 文件名中的线索（`old`、`backup`、`final`、`prototype`、`homework`、`diary`、年份、`v2`、`draft` 等），按词边界匹配并使用饱和曲线，单个关键词无法主导。 |
| `Cluster` 文件簇 | 0.15 | 这个目录像不像一个项目？同级文件数量加上标志物（`README`、清单文件、多个源文件、`assets` 目录）。 |
| `PersonalArtifact` 个人属性 | 0.15 | 这是"人会做的文件"（项目、源码、游戏存档、设计稿、笔记）还是系统生成物？ |
| `ModificationPattern` 修改模式 | 0.10 | 很久以前创建**且此后再未改动** = 被遗弃（高分）。很久以前创建但昨天还在改 = 仍然活着（低分）。 |
| `Unusualness` 异常度 | 0.05 | 结构上的怪异：没有扩展名、超长文件名、双扩展名、非 ASCII 字符、奇怪标点、隐藏/系统属性。 |

```
LocalScore = Σ(特征 × 权重) / Σ(权重)      -- 权重会自动归一化，无需合计为 1
```

### 与 AI 的融合

```
EffectiveAiWeight = ConfiguredAiWeight × clamp(Confidence / 100, MinConfidenceScale, 1)

FinalScore = LocalScore × (1 − EffectiveAiWeight) + AiScore × EffectiveAiWeight
```

一个回答 `confidence: 10` 的模型，无论它的 `interestingness` 多么极端，对最终结果的影响都微乎其微。如果 AI 不可用或调用失败，`EffectiveAiWeight` 变为 `0`，完全由本地证据决定。所有可调参数集中在
`Computer Archaeologist.Core/Options/InterestingnessWeightsOptions.cs`，并可在**设置 → 高级 → 有趣值权重**中调整。

### 阶段漏斗

```
文件系统遍历（数十万文件，剪枝后流式输出）
        │  排除策略 + 运行范围
        ▼
流式候选 ──► 有界 top-N 堆（MaxCandidates，默认 5 000）
        │  完整本地评分 + 文件夹聚类
        ▼
AI 候选（MaxAiAnalysis，默认 300，并发 ≤ 3）
        │  融合、置信度缩放、最低有趣值过滤
        ▼
发现（MaxDiscoveries，默认 40）──► 报告
```

内存保持恒定，因为发现阶段从不物化完整结果集：候选进入固定大小的优先队列，SHA-256 只对最终入选者计算，每一次文件读取都有上限。

---

## 架构

```
Computer Archaeologist.slnx
├── Computer Archaeologist.Core/          net8.0 —— 无 UI 依赖，完全可单元测试
│   ├── Models/                           FileMetadata、FileArtifact、评分、会话、报告
│   ├── Options/                          所有可调参数，含权重
│   ├── Discovery/                        并行文件系统遍历、排除策略
│   ├── Analysis/                         本地评分、文件夹上下文、预览
│   ├── Ai/                               提示词、JSON 校验、OpenAI 兼容客户端
│   ├── Reports/                          报告组装 + Markdown 导出
│   ├── Security/                         Windows 凭据管理器 / DPAPI 存储
│   ├── Settings/  Storage/  Infrastructure/  Localization/  Utilities/
│   └── Strings/{en-US,zh-CN}/Resources.resw
├── Computer Archaeologist/               net8.0-windows —— WinUI 3 外壳（MVVM）
│   ├── App.xaml(.cs)  MainWindow.xaml(.cs)
│   ├── Views/           主页、考古、发现、发现详情、报告、设置
│   ├── ViewModels/      每页一个 + 共享 AppState
│   ├── Services/        导航、主题、文件启动、剪贴板、UI 调度
│   ├── Converters/  Resources/Styles/  Infrastructure/
│   └── Package.appxmanifest  app.manifest
├── Computer Archaeologist.Tests/         net8.0 —— xUnit
├── dist/                                 预构建的发布压缩包
└── tools/                                字符串目录生成器、UI Automation 冒烟测试
```

真正被强制执行的设计规则：

- **MVVM。** View 中不含业务逻辑。code-behind 仅限导航接线与原生对话框（文件夹选择器、确认对话框）。
- **依赖注入。** 使用 `Microsoft.Extensions.DependencyInjection`，对象图在 `ServiceCollectionExtensions` 中一次性组装。应用代码里**不存在** `new LocalFileDiscoveryService()` 这样的调用。
- **逻辑归服务所有。** 发现、评分、AI、报告、设置、存储与安全存储全部是接口，实现可替换。
- **UI 从不扫描。** 由流水线驱动发现，UI 只观察进度并请求取消。

### 给维护者的布局注意事项

- **`UniformGridLayout` 用固定单元格尺寸测量每个元素。** `MinItemWidth` / `MinItemHeight` 是**硬性高度**而非提示：需要更多空间的说明文字会被裁成一条细线。因此指标卡片使用足够宽裕的 `MinItemHeight`，并把说明行放在 `*` 行里，同时设为 `TextWrapping="NoWrap"` + `TextTrimming="CharacterEllipsis"`，让它在过长时显示省略号而不是消失。
- **除了设置页自己的草稿属性，任何东西都不双向绑定到持久化选项**；后台记录会话书签时只在文件中修改 `app.lastSessionId` 一个字段。否则一个"条目尚未加载完成就上报未选中"的控件就可能覆盖真实设置。

---

## 多语言

所有面向用户的字符串都放在 `.resw` 目录中——XAML 里没有任何硬编码文案。

- `Computer Archaeologist.Core/Strings/en-US/Resources.resw`
- `Computer Archaeologist.Core/Strings/zh-CN/Resources.resw`

这两个文件被**使用两次**：既嵌入 `ComputerArchaeologist.Core`（由 `ReswLocalizationService` 读取，单元测试与报告生成器也走这条路径），也作为 `PRIResource` 链接进 Windows 资源管线。**只有一份事实来源。**

- XAML 通过 `{Binding [Some_Key], Source={StaticResource Loc}}` 绑定；切换语言只触发一次 `PropertyChanged("Item[]")`，整棵可视化树即时重读，**无需重启**。
- **外观**提供浅色 / 深色 / 跟随系统，作用在窗口内容上，因此 WinUI 内置主题资源、Mica 背景与系统强调色全部继续生效。
- 提示词会告知模型应使用哪种语言作答，因此报告跟随界面语言。
- `tools/generate-strings.py` 可重新生成两份目录，并在两者键集合不一致时直接失败。

---

## 测试

```powershell
dotnet test "Computer Archaeologist.Tests\Computer Archaeologist.Tests.csproj"
```

**130 个测试，全部通过**，覆盖：

| 领域 | 验证内容 |
|------|---------|
| 遍历 | 递归遍历；元数据取自目录列表；排除目录被剪枝且从不进入；运行范围生效；文件预算被遵守；取消能及时停止；**40 个目录并行访问恰好一次、无重复**；空目录树；缺失的根；不可读路径。 |
| 有趣值 | 非线性年龄曲线及其上限；基于真实频率的稀有度；路径语义；按词边界的文件名匹配；项目簇识别；被遗弃 vs 仍在维护的修改模式；加权和与报告的本地分数一致；极端输入下最终分数仍落在 `0–100`。 |
| 置信度加权 | 高置信度的 AI 能改变分数，低置信度的几乎不能，AI 缺失或失败时本地分数不受影响。 |
| 排除策略 | Windows / Program Files / `$Recycle.Bin` / 各类缓存被排除；`node_modules`、`.git`、`bin`、`obj` 与包缓存被排除；**真实用户文件——包括 `\Games\...` 下的游戏存档——绝不误排除**；用户自定义排除与限定范围生效；匹配不区分大小写与分隔符。 |
| AI JSON 解析 | 合法 JSON、围栏 JSON、夹带散文的 JSON、字符串内含嵌套花括号、数字被写成字符串、超范围数值、缺字段、未知类别、非法 action，以及完全无法解析的响应——**任何一种都不得抛异常**。 |
| 报告生成 | 无论有无 AI 叙述都能生成报告；失败的 AI 分析转为警告；章节归类；总览只陈述实测数字；Markdown 导出。 |
| 流水线（集成） | 在真实目录树上跑真实的端到端流程（真实遍历器、真实评分、真实报告写入）：发现、排序、只对最终入选者计算哈希、七个阶段全部上报、取消返回部分结果、空目录树、缺失的根。 |
| 运行状态 | 启动时恢复上一次运行与历史；失效的会话指针回退到最新归档；空归档保持空状态。 |
| 多语言 | 两份目录都能从程序集加载、键集合完全一致、没有空翻译，并且**XAML 与代码中引用的每一个键都真实存在**——通过扫描源码树校验。 |
| 持久化 | 设置往返；不含任何凭据字段；损坏文件被隔离而不是阻塞启动；记录会话书签绝不改写其它设置；会话保存/读取/列表；安全存储往返。 |
| 文件分析 | UTF-8 解码、二进制/控制字符拒绝、PNG/GIF 文件头解析、ZIP 列表、不可读文件返回 `null`、SHA-256 正确性。 |

`tools/uia-smoke.ps1` 是一个 UI Automation 测试脚本，它会驱动真实窗口：从主页发起扫描、回答范围对话框、等待运行结束，并断言发现页、详情页、报告页与设置页确实渲染出了内容（包括窗口最大化之后）。

---

## 常见问题

| 现象 | 怎么办 |
|------|-------|
| 扫描比预期慢 | 在 SSD 上提高**设置 → 文件发现 → 并行目录遍历数**；在机械硬盘或网络共享上则调低。缩小范围永远是最快的办法。 |
| 扫描提前结束 | 触发了**最大扫描文件数**或**时间预算**，报告里会有对应警告。在设置里调高后重新运行。 |
| 没找到有趣的东西 | 检查范围，并调低**设置 → 最低有趣值**。 |
| 测试连接返回 `401` | Key 被拒绝。清除后重新粘贴一个新的。 |
| 测试连接返回 `404` | Base URL 或模型名不对。检查是否漏了 `/v1`。 |
| 运行中出现 `429` | 触发限流。客户端会以指数退避重试（1 秒、2 秒、4 秒，有上限），随后继续用本地评分完成，并在报告中注明部分失败。 |
| 点击打开文件没反应 | 可执行类型需要先通过确认对话框；若文件已不存在，会给出提示而不是静默失败。 |

日志位于 `%LOCALAPPDATA%\Computer Archaeologist\Logs\`。日志中**不含 API Key**（写入前会用正则清洗 `sk-…` 与 `Bearer …`），也**不含文件内容**。

---

## 已知限制

以下是刻意划定的边界，而不是未完成的工作：

1. **不使用任何外部文件索引。** 程序自己遍历文件系统，因此没有依赖、报告中的数字也完全可信；代价是在超大磁盘上比预建索引慢。
2. **绝不把图片像素发送给 AI。** 视觉上传是有意不实现的，以遵守"绝不上传整个文件"的规则。图片分析基于文件头/元数据，应用内预览只在本地解码缩小后的缩略图。
3. **PDF 与 Office 文档只显示元数据**，不做解析。渲染这些格式会引入庞大依赖并拖慢提取；元数据视图仍会显示类型、大小、时间、路径与文件夹上下文。
4. **只列出 ZIP 压缩包内容。** `.rar`、`.7z` 等仅报告格式。
5. **界面语言为简体中文与英文。** 借助目录生成器，新增一门语言只需增加一个 `.resw` 文件。
6. **已配置 MSIX 打包路径，但默认构建是免安装且自包含的**，因此应用可以直接从构建产物启动，无需安装 Windows App Runtime。

---

## 许可证

本项目采用 [MIT 许可证](LICENSE)。

```
MIT License

Copyright (c) 2026 @Aperture-Revive
```

你可以自由使用、修改、分发和商用，只需保留版权声明与许可声明。软件按"原样"提供，不附带任何担保。

---

## 作者

**Computer Archaeologist · 计算机考古学家** 由 **[@Aperture-Revive](https://github.com/Aperture-Revive)** 设计与实现。

```
Computer Archaeologist · 计算机考古学家
© 2026 @Aperture-Revive
https://github.com/Aperture-Revive
```

欢迎反馈问题、提交 Issue 与 Pull Request。
