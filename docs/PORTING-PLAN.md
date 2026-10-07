# Compositor Windows 移植计划

> 版本：v1.1（2026-10-07，补充第 5 项硬性要求：保持轻量）
> 原项目：https://github.com/robbietilton/Compositor （MIT，Swift/macOS，当前 1.4.5，.comp 格式 v11）
> 本文档是 Windows 版的唯一权威计划，开工后随里程碑推进更新状态。

---

## 1. 目标与原则

把 Compositor 移植为 **Windows 原生桌面应用**，达成五个硬性要求：

1. **能力完全对等**：功能清单（README Features 全部条目）逐项对齐，包括 Photoshop 级图层系统、蒙版、混合模式、调整图层、图层效果、选区工具组、画笔引擎、Camera Raw、PSD/PSB 导入、AI 选择。
2. **UI 完全保留**：同样的深色界面、布局（标签页条、工具头、画布、右侧图层面板、浮动面板）、交互（拖拽标签改数值 NumericScrub、右键菜单、拖放图层跨项目复制）。
3. **项目结构保留**：C# 工程按 Swift 源码的 `Document / Rendering / UI / IO` 四层 1:1 映射，文件同名（`EditorSession.swift → EditorSession.cs`），使上游 diff 可按文件机械翻译。
4. **快速跟随上游**：新仓库挂 `upstream` remote；每次上游发版按固定流程（§8）同步；**C 像素内核直接复用上游 .c 文件编译**，零翻译成本。
5. **保持轻量**：原版是单个原生 Swift 应用，零第三方运行时依赖、体积极小；Windows 版按同样的轻量工具定位约束自己——依赖最小化，安装包/启动/内存设预算（§1.1），禁止为图省事引入重型框架（Electron/WebView 壳、大而全的通用库）。

**不变的契约**：`.comp` 格式 v11 双向完全兼容——macOS 版能打开 Windows 版保存的项目，反之亦然；外部写入 .comp 时画布实时刷新（AI agent 协作特性）同样保留。

### 1.1 轻量化预算

原版是单个原生 App，零第三方运行时依赖。Windows 版的对应约束（除注明外均指主程序，不含按需下载内容）：

| 指标 | 预算 | 说明 |
|---|---|---|
| 安装包/磁盘占用 | ≤ 100 MB | 不含按需下载的 AI 模型（40–160 MB）；ONNX Runtime、LibRaw 等随包 DLL 计入预算，M4 若超限则把 ONNX Runtime 一并改为按需下载 |
| 冷启动到可交互 | ≤ 2 s | CI 记录启动耗时，防持续回退 |
| 空文档常驻内存 | ≤ 300 MB | 大文档内存随画布规模走、不另设上限（与原版一致），进性能基准抽样 |
| NuGet/native 依赖 | 仅 §4 映射表列出的库 | 新增依赖须在本文档登记理由；同类选型取更轻者 |

发布方式默认 **framework-dependent**（Velopack 首启引导安装 .NET 10 Desktop Runtime），M0 实测体积与首装体验后定稿；若改 self-contained 须重新核对预算（WPF 不支持 trimming，包体约 +100 MB）。

## 2. 已确认决策

| 决策点 | 结论 |
|---|---|
| 技术栈 | C# + WPF + ComputeSharp（D3D11 compute），.NET 10 LTS |
| 测试环境 | Windows 11 实体机/虚拟机；macOS 写码 + Windows 构建调试 + CI |
| 交付策略 | 分阶段 M0–M4，核心优先 |
| AI 选择功能 | ONNX Runtime + 开源分割模型（MobileSAM 一类），M4 落地 |
| 目标系统/分发 | Windows 10 1809+ / Velopack 增量自动更新 |
| 界面语言 | 英文（对照原版逐条）+ 内置中文双语切换 |
| 仓库策略 | 新仓库，upstream 挂 remote，配 PORTING-MAP 映射表 |
| 命名/图标 | 同名 Compositor，复用原版图标（PNG→ico） |
| 菜单形态 | 窗口内经典菜单栏（Photoshop Windows 版式），Cmd→Ctrl 快捷键映射 |
| M1 范围 | 画布缩放平移 / 图层、组、混合模式、不透明度 / 蒙版（绘制、启用、链接）/ 画笔、橡皮擦、移动 / 撤销重做 / .comp 新建打开保存 / PNG JPEG 导出 / 标尺参考线 |
| 轻量化 | 第 5 项硬性要求：预算与依赖政策见 §1.1；发布方式默认 framework-dependent，M0 定稿 |

## 3. 新仓库结构

```
CompositorForWin/
├── src/
│   ├── Compositor/                # 与上游 Compositor/ 同构
│   │   ├── App/                   # CompositorApp.swift + ApplicationDelegate → App 引导
│   │   ├── Document/              # 54 个文件同名映射
│   │   ├── Rendering/             # 渲染层（GPU + 画布控件）
│   │   ├── UI/                    # WPF 控件/面板/对话框
│   │   └── IO/                    # 项目存储/导入导出/PSD/RAW
│   ├── Compositor.Native/         # 上游 .c/.h 原样编译为 native DLL（+ dispatch shim）
│   └── Compositor.Tests/          # 与上游 CompositorTests/ 同名映射的 xUnit 测试
├── upstream/                      # 只读的上游 Swift 源码 checkout（同步参照）
├── docs/
│   ├── PORTING-PLAN.md            # 本文档（含里程碑状态）
│   ├── PORTING-MAP.md             # 全量文件映射表 + 状态（同步流程核心资产）
│   └── SYNC-UPSTREAM.md           # 上游同步操作手册
├── scripts/                       # 构建/打包/发版脚本
├── .github/workflows/ci.yml       # windows-latest: build + test
└── CompositorWin.sln
```

上游 Swift 源保留在 `upstream/`（remote 跟踪），任何翻译都以它为唯一参照，禁止凭记忆实现。

## 4. 技术架构映射

| macOS（原） | Windows（移植） | 说明 |
|---|---|---|
| SwiftUI 声明式 UI | WPF + XAML（MVVM） | View ↔ UserControl；@State/@Bindable ↔ ViewModel 属性 + INPC；@AppStorage ↔ Settings |
| AppKit（NSView 图层列表、菜单、拖放） | WPF 自定义控件 + HwndHook | NativeLayerList(1382 行 NSView) → 自绘 VirtualizingItemsControl |
| Metal 内联 MSL 着色器 | ComputeSharp（C# 写 HLSL compute） | 笔刷覆盖、变形 warp、噪点、图层效果四组着色器逐一对应 |
| CoreImage（39 种滤镜） | 自研像素管线 + GPU 通路 | 混合模式(30 种 Photoshop 模式)是纯数学，CPU SIMD 先行、GPU 随后；模糊/渐变/色彩立方对应移植 |
| C 内核 2284 行 | **MSVC 直接编译为 DLL，P/Invoke** | 唯一 Apple 依赖是 `<dispatch/dispatch.h>`，写 20 行 shim（映射到串行或 Win32 线程池）；上游 .c 更新直接替换重编 |
| Vision（前景实例分割） | ONNX Runtime + MobileSAM 类模型 | 结果非逐像素一致，功能对等；模型按需下载（约 40–160 MB） |
| CIRAWFilter（30 种 RAW） | LibRaw（P/Invoke 绑定） | M4 |
| CoreText/NSLayoutManager | DirectWrite 自定义布局 | 文字度量与 macOS 不保证逐像素一致（记录为已知差异） |
| Sparkle 自动更新 | Velopack（GitHub Releases 为源） | 支持增量更新，版本号 `1.4.5-win.1` 跟随上游 |
| CryptoKit SHA | System.Security.Cryptography | 项目摘要/外部变更检测 |
| UniformTypeIdentifiers | 自定义文件类型注册 | `.comp` 关联、Progid/图标注册表项 |
| XCTest（13.9k 行） | xUnit 同名映射 | 格式与算法契约测试优先移植（见 §9） |

**像素管线约定**：存储为 8 位 RGBA（PNG 直通 alpha），合成内部用预乘 alpha（与 CoreGraphics 一致），大文档（上限 3 亿源像素）走分块渲染（移植 TiledLayerRenderer + DownsampleCache）。像素格式、四舍五入方式以 C 内核和上游 Swift 实现为准，golden 文件校验（§9）。

## 5. .comp 格式兼容（最高优先级契约）

- manifest.json（v1–v11 全部可读，保存写 v11）用 System.Text.Json 移植 `ProjectStore.swift` 的全部校验：不安全路径、尺寸上限（单边 30,000px、1 亿源像素、蒙版另计 1 亿、图层 1 万、manifest 4 MiB、单资产 512 MiB）、版本拒绝规则。
- 组/嵌套（≤64 层）、蒙版命名 `<uuid>.mask.png`、live mask（maskSourceID 链 ≤256）、调整图层 12 种 kind、文字元数据（colorRuns/fontRuns）、图层效果、参考线、形状、蒙版独立变换（maskPlacement/maskLinked）——全部字段逐一映射。
- 外部修改监视：FileSystemWatcher 移植 `ProjectWatcher.swift`（防抖、协调原子替换、摘要比对）。
- 原子保存：临时目录写全量 → `ReplaceFile`/事务式替换，对应 macOS 的 coordinated replacement。

## 6. 界面复刻清单（对照原版）

- 布局：窗口内菜单栏 + 项目标签条 + 工具头（随工具切换：变换检查器/笔刷控制/选区控制/渐变/形状/文字）+ 中央画布（标尺 ⌘R→Ctrl+R、参考线、网格、像素网格）+ 右侧图层面板（252px 默认宽、可调、记忆）。
- 浮动面板（Levels/Adjustment/ColorRange/Filter/Effects…）带记忆位置，移植 FloatingPanel 控制器。
- 交互细节：数字标签拖动改值（NumericScrub）、滑杆吸附、Alt 采样吸管环、笔刷光标预览、右键图层菜单、图层间拖拽（含跨项目窗口）。
- 快捷键：Photoshop 风格 + Edit > Keyboard Shortcuts 重映射（用户可编辑、持久化），Cmd→Ctrl、Option→Alt 系统映射。
- 双语：字符串资源化（resx/dictionary），en-US 为源（从 Swift 代码逐条提取），zh-CN 内置翻译，跟随系统语言可切换。

## 7. 里程碑

> 工作量按 AI 辅助开发会话估算；每阶段结束产出可安装包 + 更新 PORTING-MAP 状态。

### M0 骨架（1 个会话）
- [ ] 仓库/解决方案/四层目录/CI（windows-latest build+test）
- [ ] Compositor.Native：上游 .c 编译通过（dispatch shim）+ 冒烟测试
- [ ] 图标（1024px PNG→多尺寸 ico）、窗口外壳（自定义深色主题框架）、菜单栏骨架
- [ ] PORTING-MAP.md 全量文件清单生成；SYNC-UPSTREAM.md 流程成文
- 验收：CI 绿；空窗口带菜单在 Windows 11 运行。

### M1 核心编辑器（3–6 个会话）
- [ ] EditorSession/DocumentHistory 核心模型 + Undo/Redo
- [ ] .comp 新建/打开/保存（v11 全字段读写 + 校验）+ Recent 菜单
- [ ] 图层系统：像素图层/组、不透明度、30 种混合模式、重命名/拖拽排序/嵌套/复制
- [ ] 蒙版：添加/删除/启用、画笔绘制（ hardness/opacity）、链接开关
- [ ] 画笔 + 橡皮擦（大小/硬度/不透明度/平滑）、移动工具（非破坏变换）
- [ ] 画布：缩放平移、高质量降采样、像素网格、标尺 + 参考线拖出
- [ ] PNG/JPEG 导出；图层面板完整 UI（缩略图/可见性/右键菜单）
- 验收：与 macOS 版互开 .comp（含蒙版/组/混合模式）像素一致；8K 画布 60fps 平移缩放。

### M2 选区/变换/文字/修饰（4–8 个会话）
- [ ] 矩形/椭圆/套索/多边形套索选区，加减/移动轮廓/移动像素、羽化扩展收缩
- [ ] 魔棒（C 内核）+ 颜色范围；内容识别填充 + 仿制图章 + 修复画笔 + 模糊工具（C 内核）
- [ ] 自由变换全套（缩放/旋转/翻转/自由扭曲 ⌘拖→Ctrl 拖、吸附、精确数值）
- [ ] 裁剪（比例/对称）、画布大小、图像大小、裁切
- [ ] 文字工具（DirectWrite 段落框、colorRuns/fontRuns）+ 形状工具 + 渐变工具（保持可编辑）
- [ ] 复制粘贴图层/跨项目拖拽
- 验收：PSD 常用素材工作流可完成；selection/mask 全操作有测试。

### M3 调整/效果/Camera Raw（4–8 个会话）
- [ ] 调整图层 12 种（HueSat/Levels/Curves/Exposure/GradientMap/Grain/BW/ColorBalance/Invert/GaussianBlur/MotionBlur/AddNoise）
- [ ] 图层效果 6 种（描边/投影/颜色叠加/内阴影/外发光/内发光，GPU 渲染、可随时编辑）
- [ ] 直方图无关；滤镜面板（Vignette/Bloom/TonalContrast/LensCorrection/Dither 等）+ 选区内实时预览
- [ ] Camera Raw 滤镜面板（光/颜色/曲线/混色器/分级/细节/光学/几何）
- [ ] JPEG 导出实时预览、Copy Merged、分辨率元数据
- 验收：调整/效果往返 .comp 与 macOS 版一致；Curves/Levels 交互复刻。

### M4 格式与 AI（3–6 个会话）
- [ ] PSD/PSB 解析器移植（图层/组/蒙版/混合/填充图形/横排文字可编辑 + 转换报告）
- [ ] RAW 导入（LibRaw + 开发面板）
- [ ] ONNX 对象选择/主体选择/移除背景；Select Subject
- [ ] HEIC/TIFF/SVG 导入；超大文档降级策略
- [ ] Velopack 发布通道上线（含增量更新）
- 验收：与 macOS 版功能清单逐项核对通过；更新流程实测。

## 8. 上游同步流程（每次上游发版）

1. `git fetch upstream && git diff <旧tag>..<新tag> --stat`，按 `docs/PORTING-MAP.md` 找到每个改动文件的 Windows 对应物。
2. C 内核改动 → 直接拷贝 .c/.h 重编 Native 项目（零翻译）。
3. Swift 改动 → 对照 diff 翻译到同名 C# 文件；格式字段变化 → 同步 manifest 读写 + 版本号 + 双端测试。
4. UI 文案变化 → 同步 en-US 资源并补 zh-CN。
5. 跑全量测试 + golden 文件比对，更新 PORTING-MAP 状态列，发 `x.y.z-win.n` 版本。

## 9. 测试策略

- **契约测试优先**：上游 CompositorTests 中 ProjectStore/格式/混合模式/蒙版/调整等纯逻辑测试（约 60% 用例）同名移植到 xUnit，作为两版行为一致的证据。
- **Golden 文件**：在 mac（本机若可构建原版）或手工生成一组 .comp 基准（含组/蒙版/混合/调整/效果/文字），Windows 版加载后导出 PNG 逐像素/逐哈希比对，容差为 0（C 内核路径）或记录已知差异（CoreImage/DirectWrite 路径）。
- **性能门禁**：CI 中跑大画布合成基准，防性能回退。
- **轻量化门禁**：CI 记录安装包体积、冷启动耗时与空文档内存，超出 §1.1 预算即失败。

## 10. 已知差异（会与 macOS 版不同，需接受）

1. AI 分割模型不同 → 对象/主体选择结果非逐像素一致。
2. DirectWrite 与 CoreText 文字度量差异 → 文字图层像素渲染可能有细微差别（.comp 中的 text 元数据保持一致，PNG 是显示兜底）。
3. 滤镜数值一致但 CoreImage 与自研管线的浮点舍入可能有 ±1/255 级别差异。
4. 系统级体验：自动更新、文件关联、通知中心等按 Windows 惯例实现。

## 11. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 大文档性能（1 亿像素级） | M1 即建分块渲染 + 降采样缓存；性能门禁进 CI |
| ComputeSharp 在老 GPU 不可用 | 运行时探测 feature level，回退 CPU SIMD 路径（笔刷覆盖已有 CPU 参考实现可移植） |
| WPF 图层面板复杂交互（拖拽嵌套 1382 行） | 自绘虚拟化列表 + 状态机测试；必要时降级为"先功能后动画" |
| 上游大重构导致映射断裂 | PORTING-MAP 每次同步后校正；映射以"模块职责"为锚而非仅文件名 |
| PSD 边缘格式怪癖 | 移植全部上游测试 + 收集真实 PSD 样本库 |
| 单人 + AI 会话的长周期 | 里程碑均可独立交付使用，随时可停在任一阶段 |

## 12. 开工前最后确认项

1. 新仓库本地路径用 this repository's root，GitHub 仓库为 `jz791357/CompositorForWin`。
2. Windows 11 机器上需要装 .NET 10 SDK + VS 2026（或 Build Tools）；到时我给出清单。
3. 是否需要我在 mac 上尝试构建原版（若装有 Xcode 26+）用于生成 golden 基准——M1 开始时验证。
