# Compositor for Windows

[![CI](https://github.com/jz791357/CompositorForWin/actions/workflows/ci.yml/badge.svg)](https://github.com/jz791357/CompositorForWin/actions/workflows/ci.yml)

**Compositor** 的 Windows 原生移植版 —— 一个免费、开源、类 Photoshop 的图像编辑器与合成工具。

> **源项目**：[robbietilton/Compositor](https://github.com/robbietilton/Compositor)
> **原作者**：[Robbie Tilton](https://robbietilton.com) · 原版为 macOS（Apple Silicon）应用，MIT 开源协议
> 本项目是其 Windows 桌面版，同样以 MIT 协议开源，能力、界面与项目结构对齐原版。

---

## 项目简介

原版 Compositor 是一款面向合成与照片后期工作流的全功能图像编辑器：图层与文件夹（含 Photoshop 全套混合模式）、图层蒙版与剪贴蒙版、12 种调整图层、GPU 渲染的图层效果、专业选区工具组、画笔/仿制图章/修复画笔、Camera Raw 滤镜、PSD/PSB 与相机 RAW 导入等。

本项目的目标是将它**完整移植到 Windows**：

- **能力对等** —— 原版功能清单逐项落地
- **UI 保留** —— 同样的深色界面、布局与交互习惯（Photoshop 风格快捷键，⌘→Ctrl）
- **结构同构** —— C# 源码按原版 `Document / Rendering / UI / IO` 四层文件级 1:1 映射，便于跟随上游迭代
- **文件互通** —— `.comp` 项目格式与 macOS 版双向完全兼容；外部工具（包括 AI agent）写入 `.comp` 时，打开的画布实时刷新
- **轻量化** —— 像原版一样保持轻量：依赖最小化、小体积安装包、快速启动、空闲低内存（预算见 [docs/PORTING-PLAN.md](docs/PORTING-PLAN.md) §1.1）

## 技术栈

| 领域 | 选型 |
|---|---|
| 语言 / 运行时 | C# / .NET 10 (LTS) |
| UI 框架 | WPF（自定义深色主题，窗口内经典菜单栏） |
| GPU 计算 | [ComputeSharp](https://github.com/Sergio0694/ComputeSharp)（D3D11 compute shader：笔刷引擎、图层效果、变形、噪点，对应原版 Metal 着色器） |
| 像素内核 | 原版 2,284 行 C 内核**原样编译**为原生 DLL（P/Invoke 调用），保证逐像素一致、上游改动零翻译跟进 |
| AI 选择 | ONNX Runtime + 开源分割模型（对象选择 / 主体选择 / 移除背景，对应 macOS 的 Vision 框架） |
| RAW 开发 | LibRaw（对应 macOS 的 CIRAWFilter） |
| 自动更新 | Velopack（GitHub Releases 为更新源，增量更新，对应原版 Sparkle） |
| 发布/体积 | Framework-dependent 发布，Velopack 首启引导安装 .NET 10 Desktop Runtime；安装包 ≤ 100 MB（AI 模型按需下载，不计入） |
| 测试 | xUnit（原版 XCTest 契约测试同名移植） |
| CI | GitHub Actions（windows-latest：构建 + 测试） |

## 与 macOS 原版的区别

功能与文件格式保持对等，但存在以下已知差异：

1. **AI 选择结果不逐像素一致** —— macOS 版使用 Apple Vision 的前景实例分割模型；Windows 版使用 ONNX 开源分割模型，功能对等、结果可能有差异。
2. **文字渲染度量** —— Windows 使用 DirectWrite（原版 CoreText），文字图层的像素渲染可能存在细微差别；`.comp` 中的文字元数据保持一致。
3. **滤镜浮点舍入** —— 自研像素管线与 CoreImage 之间存在 ±1/255 级别的舍入差异。
4. **平台惯例** —— 菜单栏位于窗口内（Photoshop Windows 版式）、快捷键 ⌘/Option → Ctrl/Alt、自动更新与文件关联按 Windows 惯例实现。
5. **系统要求** —— Windows 10 1809 及以上（原版要求 macOS 26 + Apple Silicon）。

## 路线图

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M0 | 仓库骨架 / CI / C 内核 DLL / 应用外壳 | ✅ 完成 |
| M1 | 核心编辑器：画布、图层/组/混合模式/蒙版、画笔、移动工具、撤销、`.comp` 读写、PNG/JPEG 导出、标尺参考线 | ⏳ |
| M2 | 选区工具组、自由变换、裁剪、文字/形状/渐变工具、仿制/修复/内容识别填充 | ⏳ |
| M3 | 调整图层、图层效果、滤镜、Camera Raw、导出增强 | ⏳ |
| M4 | PSD/PSB 导入、相机 RAW、ONNX AI 选择、自动更新上线 | ⏳ |

版本号跟随原版（如 `1.4.5-win.1`）；上游每次发版按固定流程同步（C 内核直接替换，Swift 改动按文件映射表翻译）。

## 开发与构建

> 完整的测试与推送门禁流程见 [docs/TESTING.md](docs/TESTING.md)：本机门禁 + pre-push 钩子 + CI 验证 + main 分支保护，**测试全部通过前代码进不了 `main`**。

每个 clone 一次性启用 pre-push 门禁：

```sh
./scripts/hooks/install.sh        # macOS
.\scripts\hooks\install.ps1       # Windows
```

日常（需要 Windows 10 1809+ 与 .NET 10 SDK；macOS 开发机跑可移植子集）：

```powershell
.\scripts\test.ps1                # 全量门禁：native 构建 + xUnit 测试
.\scripts\build.ps1               # 测试通过后构建发布产物
```

```sh
./scripts/test.sh                 # macOS：C 内核编译检查等可移植门禁
```

重要节点（里程碑/功能）完成后：推分支 → PR → CI 绿 → squash 合入 main。

## 致谢与许可

- 本项目是 [Compositor](https://github.com/robbietilton/Compositor)（作者 **Robbie Tilton**）的社区移植版，感谢原作者开源这一出色项目。
- License: MIT —— 见 [LICENSE](LICENSE)；原项目同样以 MIT 发布。

---

# Compositor for Windows (English)

A native Windows port of **Compositor**, the free and open-source Photoshop-style image editor by [Robbie Tilton](https://robbietilton.com) ([source](https://github.com/robbietilton/Compositor), MIT). The port aims for full feature parity, the same dark UI, a 1:1 source-structure mapping for fast upstream syncing, bidirectional `.comp` file compatibility with the macOS original, and a lightweight footprint like the original (minimal dependencies, installer ≤ 100 MB, fast startup).

**Stack**: C# / .NET 10, WPF, ComputeSharp (D3D11 compute), the original C pixel kernels compiled natively, ONNX Runtime for AI selections, LibRaw for camera RAW, Velopack for updates, xUnit tests mirroring the upstream suite.

**Known differences**: AI segmentation uses an open ONNX model instead of Apple Vision (equivalent features, not pixel-identical); text metrics via DirectWrite may differ slightly from CoreText; ±1/255 rounding differences in filters; Windows platform conventions (in-window menu bar, Ctrl shortcuts).

**Status**: M0 shipped — repo skeleton, CI, the C pixel kernels compiled natively, and the dark WPF shell, all verified on Windows 11. Next: M1 core editor (canvas, layers/masks/blend modes, brush, `.comp` read/write). Roadmap: M1 → M2 selections/transform/type → M3 adjustments/effects/Camera Raw → M4 PSD/RAW/AI. MIT licensed.
