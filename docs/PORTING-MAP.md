# Porting map — upstream file → Windows counterpart

Generated from the upstream tree; the single source of truth for syncing. Update the
Status column as work lands. Upstream tag tracked: **1.4.5**.

Milestones: M1 core editor · M2 selections/transform/type · M3 adjustments/effects/Camera Raw · M4 formats/AI.

> 结构说明：平台无关的 Document/IO 层与原生绑定位于 `Compositor.Core` 库（net10.0，macOS 可跑逻辑测试），
> WPF 外壳与 GPU 渲染在 `Compositor` exe。这是上游单 target + `@testable import` 测试结构的 .NET 等价翻译。
> C 内核的 C# 绑定：`src/Compositor.Core/Rendering/NativeMethods.cs`（随各里程碑扩展）。

### Document layer (model & algorithms)

| Upstream (`Document/`) | Windows | Milestone | Status |
|---|---|---|---|
| `AdjustmentEditing.swift` | `src/Compositor/Document/AdjustmentEditing.cs` | M2 | planned |
| `BlurTool.swift` | `src/Compositor/Document/BlurTool.cs` | M2 | planned |
| `BrushStroke.swift` | `src/Compositor/Document/BrushStroke.cs` | M1 | planned |
| `CameraRaw.swift` | `src/Compositor/Document/CameraRaw.cs` | M3 | planned |
| `CameraRawColor.swift` | `src/Compositor/Document/CameraRawColor.cs` | M3 | planned |
| `CameraRawDetailOptics.swift` | `src/Compositor/Document/CameraRawDetailOptics.cs` | M3 | planned |
| `CameraRawGeometryCalibration.swift` | `src/Compositor/Document/CameraRawGeometryCalibration.cs` | M3 | planned |
| `CanvasSize.swift` | `src/Compositor/Document/CanvasSize.cs` | M2 | planned |
| `CloneStamp.swift` | `src/Compositor/Document/CloneStamp.cs` | M2 | planned |
| `ColorPalette.swift` | `src/Compositor/Document/ColorPalette.cs` | M2 | planned |
| `ColorRangeSelection.swift` | `src/Compositor/Document/ColorRangeSelection.cs` | M2 | planned |
| `ContentFill.swift` | `src/Compositor/Document/ContentFill.cs` | M2 | planned |
| `Crop.swift` | `src/Compositor/Document/Crop.cs` | M2 | planned |
| `Curves.swift` | `src/Compositor/Document/Curves.cs` | M3 | planned |
| `Distort.swift` | `src/Compositor/Document/Distort.cs` | M2 | planned |
| `Dither.swift` | `src/Compositor/Document/Dither.cs` | M3 | planned |
| `DocumentHistory.swift` | `src/Compositor/Document/DocumentHistory.cs` | M1 | planned |
| `DocumentLimits.swift` | `src/Compositor.Core/Document/DocumentLimits.cs` | M1 | ✅ M1.1 (含内存预算跨平台实现) |
| `EditorSession.swift` | `src/Compositor/Document/EditorSession.cs` | M1 | planned |
| `EditorSession+Brush.swift` | `src/Compositor/Document/EditorSession+Brush.cs` | M1 | planned |
| `EditorSession+Projects.swift` | `src/Compositor/Document/EditorSession+Projects.cs` | M1 | planned |
| `Filters.swift` | `src/Compositor/Document/Filters.cs` | M3 | planned |
| `FloatingSelection.swift` | `src/Compositor/Document/FloatingSelection.cs` | M2 | planned |
| `Gradient.swift` | `src/Compositor/Document/Gradient.cs` | M2 | planned |
| `GuidedMatte.swift` | `src/Compositor/Document/GuidedMatte.cs` | M4 | planned |
| `Guides.swift` | `src/Compositor.Core/Document/Guides.cs` | M1 | ✅ M1.1 持久化形状（标尺交互随 M1.4） |
| `HueSaturation.swift` | `src/Compositor/Document/HueSaturation.cs` | M3 | planned |
| `ImageAdjustments.swift` | `src/Compositor/Document/ImageAdjustments.cs` | M3 | planned |
| `ImageTrim.swift` | `src/Compositor/Document/ImageTrim.cs` | M2 | planned |
| `LayerAdjustment.swift` | `src/Compositor.Core/Document/LayerAdjustment.cs` + `AdjustmentSettings.cs` | M3 | ✅ M1.1 模型+校验（像素引擎随 M3） |
| `LayerAppearance.swift` | `src/Compositor.Core/Document/LayerAppearance.cs` | M1 | ✅ M1.1 序列化枚举（混合数学随渲染器） |
| `LayerEffects.swift` | `src/Compositor.Core/Document/LayerEffects.cs` | M3 | ✅ M1.1 模型（GPU 渲染随 M3） |
| `LayerFlip.swift` | `src/Compositor/Document/LayerFlip.cs` | M1 | planned |
| `LayerGroups.swift` | `src/Compositor.Core/Document/LayerGroups.cs` | M1 | ✅ M1.1 遍历+校验 |
| `LayerMask.swift` | `src/Compositor/Document/LayerMask.cs` | M1 | planned |
| `LayerMerge.swift` | `src/Compositor/Document/LayerMerge.cs` | M1 | planned |
| `LayerTransform.swift` | `src/Compositor.Core/Document/LayerTransform.cs` | M1 | ✅ M1.1 模型+校验（交互部分随 M2） |
| `Levels.swift` | `src/Compositor/Document/Levels.cs` | M3 | planned |
| `LevelsAutomatic.swift` | `src/Compositor/Document/LevelsAutomatic.cs` | M3 | planned |
| `LiveLayerMask.swift` | `src/Compositor.Core/Document/LiveLayerMask.cs` | M1 | ✅ M1.1 图校验（会话部分随 M2） |
| `MagicWand.swift` | `src/Compositor/Document/MagicWand.cs` | M2 | planned |
| `MaskTracing.swift` | `src/Compositor/Document/MaskTracing.cs` | M2 | planned |
| `ObjectSelection.swift` | `src/Compositor/Document/ObjectSelection.cs` | M2 | planned |
| `PixelAdjust.swift` | `src/Compositor/Document/PixelAdjust.cs` | M3 | planned |
| `PixelInvert.swift` | `src/Compositor/Document/PixelInvert.cs` | M3 | planned |
| `ProjectWorkspace.swift` | `src/Compositor/Document/ProjectWorkspace.cs` | M1 | planned |
| `Selection.swift` | `src/Compositor/Document/Selection.cs` | M2 | planned |
| `SelectionClipboard.swift` | `src/Compositor/Document/SelectionClipboard.cs` | M2 | planned |
| `SelectionEdits.swift` | `src/Compositor/Document/SelectionEdits.cs` | M2 | planned |
| `ShapeTool.swift` | `src/Compositor.Core/Document/TypeTool.cs` (LayerShapeStyle) | M2 | ✅ M1.1 形状元数据模型 |
| `SmudgeLiquify.swift` | `src/Compositor/Document/SmudgeLiquify.cs` | M2 | planned |
| `SubjectRemoval.swift` | `src/Compositor/Document/SubjectRemoval.cs` | M2 | planned |
| `ToolDefaults.swift` | `src/Compositor/Document/ToolDefaults.cs` | M4 | planned |
| `TypeTool.swift` | `src/Compositor.Core/Document/TypeTool.cs` | M2 | ✅ M1.1 文字元数据模型（渲染随 M2） |

### Rendering layer (canvas & GPU)

| Upstream (`Rendering/`) | Windows | Milestone | Status |
|---|---|---|---|
| `AdjustmentSurface.swift` | `src/Compositor/Rendering/AdjustmentSurface.cs` | M3 | planned |
| `BrushCursorOverlay.swift` | `src/Compositor/Rendering/BrushCursorOverlay.cs` | M4 | planned |
| `CanvasLinesOverlay.swift` | `src/Compositor/Rendering/CanvasLinesOverlay.cs` | M4 | planned |
| `CanvasViewport.swift` | `src/Compositor/Rendering/CanvasViewport.cs` | M1 | planned |
| `DownsampleCache.swift` | `src/Compositor/Rendering/DownsampleCache.cs` | M1 | planned |
| `EditorCanvas.swift` | `src/Compositor/Rendering/EditorCanvas.cs` | M1 | planned |
| `EffectsPreviewCache.swift` | `src/Compositor/Rendering/EffectsPreviewCache.cs` | M3 | planned |
| `GPUCanvas.swift` | `src/Compositor/Rendering/GPUCanvas.cs` | M1 | planned |
| `GPUNoise.swift` | `src/Compositor/Rendering/GPUNoise.cs` | M3 | planned |
| `InlineTextEditor.swift` | `src/Compositor/Rendering/InlineTextEditor.cs` | M2 | planned |
| `LayerEffectsSurface.swift` | `src/Compositor/Rendering/LayerEffectsSurface.cs` | M3 | planned |
| `LayerRenderer.swift` | `src/Compositor/Rendering/LayerRenderer.cs` | M1 | planned |
| `LiveMaskRenderer.swift` | `src/Compositor/Rendering/LiveMaskRenderer.cs` | M4 | planned |
| `MetalBrushCoverage.swift` | `src/Compositor/Rendering/MetalBrushCoverage.cs` | M3 | planned |
| `MetalLayerEffects.swift` | `src/Compositor/Rendering/MetalLayerEffects.cs` | M3 | planned |
| `MetalWarp.swift` | `src/Compositor/Rendering/MetalWarp.cs` | M3 | planned |
| `RasterSnapshot.swift` | `src/Compositor/Rendering/RasterSnapshot.cs` | M1 | planned |
| `SampleRingOverlay.swift` | `src/Compositor/Rendering/SampleRingOverlay.cs` | M4 | planned |
| `SeparableBlend.swift` | `src/Compositor/Rendering/SeparableBlend.cs` | M1 | planned |
| `TiledLayerRenderer.swift` | `src/Compositor/Rendering/TiledLayerRenderer.cs` | M1 | planned |
| `TransformOverlay.swift` | `src/Compositor/Rendering/TransformOverlay.cs` | M2 | planned |

### UI layer (panels & controls)

| Upstream (`UI/`) | Windows | Milestone | Status |
|---|---|---|---|
| `BlendModePicker.swift` | `src/Compositor/UI/BlendModePicker.cs` | M1 | planned |
| `BrushControls.swift` | `src/Compositor/UI/BrushControls.cs` | M2 | planned |
| `CameraRawColorControls.swift` | `src/Compositor/UI/CameraRawColorControls.cs` | M3 | planned |
| `CameraRawControls.swift` | `src/Compositor/UI/CameraRawControls.cs` | M3 | planned |
| `CameraRawDetailOpticsControls.swift` | `src/Compositor/UI/CameraRawDetailOpticsControls.cs` | M3 | planned |
| `CameraRawGeometryCalibrationControls.swift` | `src/Compositor/UI/CameraRawGeometryCalibrationControls.cs` | M3 | planned |
| `CameraRawSlider.swift` | `src/Compositor/UI/CameraRawSlider.cs` | M3 | planned |
| `CanvasRulers.swift` | `src/Compositor/UI/CanvasRulers.cs` | M1 | planned |
| `CanvasSizeSheet.swift` | `src/Compositor/UI/CanvasSizeSheet.cs` | M2 | planned |
| `CanvasThumbnail.swift` | `src/Compositor/UI/CanvasThumbnail.cs` | M4 | planned |
| `ColorPaletteControls.swift` | `src/Compositor/UI/ColorPaletteControls.cs` | M2 | planned |
| `ColorPickerSheet.swift` | `src/Compositor/UI/ColorPickerSheet.cs` | M2 | planned |
| `ColorRangeSheet.swift` | `src/Compositor/UI/ColorRangeSheet.cs` | M2 | planned |
| `CropControls.swift` | `src/Compositor/UI/CropControls.cs` | M2 | planned |
| `CurvesControls.swift` | `src/Compositor/UI/CurvesControls.cs` | M3 | planned |
| `EffectsSheet.swift` | `src/Compositor/UI/EffectsSheet.cs` | M3 | planned |
| `FilterSheet.swift` | `src/Compositor/UI/FilterSheet.cs` | M3 | planned |
| `FloatingPanel.swift` | `src/Compositor/UI/FloatingPanel.cs` | M1 | planned |
| `GradientControls.swift` | `src/Compositor/UI/GradientControls.cs` | M2 | planned |
| `GridSettingsSheet.swift` | `src/Compositor/UI/GridSettingsSheet.cs` | M1-4 | planned |
| `HeldModifiers.swift` | `src/Compositor/UI/HeldModifiers.cs` | M1 | planned |
| `HueSaturationSheet.swift` | `src/Compositor/UI/HueSaturationSheet.cs` | M3 | planned |
| `ImageSizeSheet.swift` | `src/Compositor/UI/ImageSizeSheet.cs` | M2 | planned |
| `IndicatorlessScrollView.swift` | `src/Compositor/UI/IndicatorlessScrollView.cs` | M1 | planned |
| `JPEGExportSheet.swift` | `src/Compositor/UI/JPEGExportSheet.cs` | M3 | planned |
| `KeyboardShortcuts.swift` | `src/Compositor/UI/KeyboardShortcuts.cs` | M1 | planned |
| `LassoControls.swift` | `src/Compositor/UI/LassoControls.cs` | M2 | planned |
| `LayerAppearanceControls.swift` | `src/Compositor/UI/LayerAppearanceControls.cs` | M1-4 | planned |
| `LayerMaskMenu.swift` | `src/Compositor/UI/LayerMaskMenu.cs` | M1-4 | planned |
| `LayersPanel.swift` | `src/Compositor/UI/LayersPanel.cs` | M1 | planned |
| `LevelsSheet.swift` | `src/Compositor/UI/LevelsSheet.cs` | M3 | planned |
| `NativeLayerList.swift` | `src/Compositor/UI/NativeLayerList.cs` | M1 | planned |
| `NavigationToolHeader.swift` | `src/Compositor/UI/NavigationToolHeader.cs` | M1-4 | planned |
| `NewCanvasSheet.swift` | `src/Compositor/UI/NewCanvasSheet.cs` | M1 | planned |
| `NumericScrub.swift` | `src/Compositor/UI/NumericScrub.cs` | M1 | planned |
| `PSDConversionSheet.swift` | `src/Compositor/UI/PSDConversionSheet.cs` | M4 | planned |
| `ProjectTabLayout.swift` | `src/Compositor/UI/ProjectTabLayout.cs` | M4 | planned |
| `ProjectTabs.swift` | `src/Compositor/UI/ProjectTabs.cs` | M1 | planned |
| `ProjectWindowBridge.swift` | `src/Compositor/UI/ProjectWindowBridge.cs` | M4 | planned |
| `RawDevelopSheet.swift` | `src/Compositor/UI/RawDevelopSheet.cs` | M4 | planned |
| `ShapeControls.swift` | `src/Compositor/UI/ShapeControls.cs` | M2 | planned |
| `SliderSnap.swift` | `src/Compositor/UI/SliderSnap.cs` | M1 | planned |
| `ToolHeaderStyle.swift` | `src/Compositor/UI/ToolHeaderStyle.cs` | M1 | planned |
| `TransformInspector.swift` | `src/Compositor/UI/TransformInspector.cs` | M2 | planned |
| `TrimSheet.swift` | `src/Compositor/UI/TrimSheet.cs` | M2 | planned |
| `TypeControls.swift` | `src/Compositor/UI/TypeControls.cs` | M2 | planned |

### IO layer (project files, import/export)

| Upstream (`IO/`) | Windows | Milestone | Status |
|---|---|---|---|
| `CanvasResizer.swift` | `src/Compositor/IO/CanvasResizer.cs` | M1 | planned |
| `CompositorApplicationDelegate.swift` | `src/Compositor/IO/CompositorApplicationDelegate.cs` | M1 | planned |
| `ImageExporter.swift` | `src/Compositor/IO/ImageExporter.cs` | M1 | planned |
| `ImageFileDrop.swift` | `src/Compositor/IO/ImageFileDrop.cs` | M4 | planned |
| `ImageImporter.swift` | `src/Compositor.Core/IO/ImageImporter.cs` | M1 | planned |
| `ImageResizer.swift` | `src/Compositor/IO/ImageResizer.cs` | M2 | planned |
| `ProjectController.swift` | `src/Compositor/IO/ProjectController.cs` | M1 | planned |
| `ProjectController+ExternalChanges.swift` | `src/Compositor/IO/ProjectController+ExternalChanges.cs` | M1-4 | planned |
| `ProjectDigest.swift` | `src/Compositor.Core/IO/ProjectDigest.cs` | M1 | ✅ M1.1 |
| `ProjectStore.swift` | `src/Compositor.Core/IO/ProjectStore.cs` + `ProjectManifest.cs` | M1 | ✅ M1.1 加载+校验（保存随 M1.2 编码器） |
| `ProjectWatcher.swift` | `src/Compositor/IO/ProjectWatcher.cs` | M1 | planned |
| `RawImporter.swift` | `src/Compositor/IO/RawImporter.cs` | M4 | planned |
| `RecentProjects.swift` | `src/Compositor/IO/RecentProjects.cs` | M1 | planned |

### Native C kernels (compiled verbatim)

| Upstream (`Compositor/Rendering/`) | Windows | Note |
|---|---|---|
| `AdjustPixels.c` | `src/Compositor.Native/c/AdjustPixels.c` | verbatim |
| `BrushPixels.c` | `src/Compositor.Native/c/BrushPixels.c` | verbatim |
| `ContentFill.c` | `src/Compositor.Native/c/ContentFill.c` | verbatim |
| `DitherPixels.c` | `src/Compositor.Native/c/DitherPixels.c` | patched by scripts/sync-native.mjs (Apple blocks → serial) |
| `HealPixels.c` | `src/Compositor.Native/c/HealPixels.c` | verbatim |
| `LensPixels.c` | `src/Compositor.Native/c/LensPixels.c` | verbatim |
| `LevelsPixels.c` | `src/Compositor.Native/c/LevelsPixels.c` | verbatim |
| `NoisePixels.c` | `src/Compositor.Native/c/NoisePixels.c` | verbatim |
| `WandPixels.c` | `src/Compositor.Native/c/WandPixels.c` | verbatim |

### Tests (xUnit mirrors of upstream XCTest)

| Upstream (`CompositorTests/`) | Windows |
|---|---|
| `AdjustmentLayerTests.swift` | `src/Compositor.Tests/AdjustmentLayerTests.cs` (port with its milestone) |
| `BlendShortcutTests.swift` | `src/Compositor.Tests/BlendShortcutTests.cs` (port with its milestone) |
| `BlurBrushTests.swift` | `src/Compositor.Tests/BlurBrushTests.cs` (port with its milestone) |
| `BrushIntersectionTests.swift` | `src/Compositor.Tests/BrushIntersectionTests.cs` (port with its milestone) |
| `BrushPerformanceTests.swift` | `src/Compositor.Tests/BrushPerformanceTests.cs` (port with its milestone) |
| `BrushTests.swift` | `src/Compositor.Tests/BrushTests.cs` (port with its milestone) |
| `CameraRawSliderTests.swift` | `src/Compositor.Tests/CameraRawSliderTests.cs` (port with its milestone) |
| `CameraRawTests.swift` | `src/Compositor.Tests/CameraRawTests.cs` (port with its milestone) |
| `CanvasEntryTests.swift` | `src/Compositor.Tests/CanvasEntryTests.cs` (port with its milestone) |
| `CanvasSizeTests.swift` | `src/Compositor.Tests/CanvasSizeTests.cs` (port with its milestone) |
| `CanvasThumbnailTests.swift` | `src/Compositor.Tests/CanvasThumbnailTests.cs` (port with its milestone) |
| `CloneStampTests.swift` | `src/Compositor.Tests/CloneStampTests.cs` (port with its milestone) |
| `ColorPickerTests.swift` | `src/Compositor.Tests/ColorPickerTests.cs` (port with its milestone) |
| `CompositorTests.swift` | `src/Compositor.Tests/CompositorTests.cs` (port with its milestone) |
| `CropTests.swift` | `src/Compositor.Tests/CropTests.cs` (port with its milestone) |
| `CropToCanvasImportTests.swift` | `src/Compositor.Tests/CropToCanvasImportTests.cs` (port with its milestone) |
| `CursorTests.swift` | `src/Compositor.Tests/CursorTests.cs` (port with its milestone) |
| `DistortTests.swift` | `src/Compositor.Tests/DistortTests.cs` (port with its milestone) |
| `DitherTests.swift` | `src/Compositor.Tests/DitherTests.cs` (port with its milestone) |
| `DownsampleTests.swift` | `src/Compositor.Tests/DownsampleTests.cs` (port with its milestone) |
| `ExportTests.swift` | `src/Compositor.Tests/ExportTests.cs` (port with its milestone) |
| `ExternalChangeTests.swift` | `src/Compositor.Tests/ExternalChangeTests.cs` (port with its milestone) |
| `FilterTests.swift` | `src/Compositor.Tests/FilterTests.cs` (port with its milestone) |
| `FinishingFilterTests.swift` | `src/Compositor.Tests/FinishingFilterTests.cs` (port with its milestone) |
| `FloatingPanelTests.swift` | `src/Compositor.Tests/FloatingPanelTests.cs` (port with its milestone) |
| `GPUCanvasTests.swift` | `src/Compositor.Tests/GPUCanvasTests.cs` (port with its milestone) |
| `GradientTests.swift` | `src/Compositor.Tests/GradientTests.cs` (port with its milestone) |
| `GroupTests.swift` | `src/Compositor.Tests/GroupTests.cs` (port with its milestone) |
| `GroupingSelectionTests.swift` | `src/Compositor.Tests/GroupingSelectionTests.cs` (port with its milestone) |
| `GuideTests.swift` | `src/Compositor.Tests/GuideTests.cs` (port with its milestone) |
| `HistoryTests.swift` | `src/Compositor.Tests/HistoryTests.cs` (port with its milestone) |
| `HueSaturationTests.swift` | `src/Compositor.Tests/HueSaturationTests.cs` (port with its milestone) |
| `ImageAdjustmentTests.swift` | `src/Compositor.Tests/ImageAdjustmentTests.cs` (port with its milestone) |
| `ImageImportTests.swift` | `src/Compositor.Tests/ImageImportTests.cs` (port with its milestone) |
| `ImageSizeTests.swift` | `src/Compositor.Tests/ImageSizeTests.cs` (port with its milestone) |
| `ImageTrimTests.swift` | `src/Compositor.Tests/ImageTrimTests.cs` (port with its milestone) |
| `InnerGlowTests.swift` | `src/Compositor.Tests/InnerGlowTests.cs` (port with its milestone) |
| `JPEGExportTests.swift` | `src/Compositor.Tests/JPEGExportTests.cs` (port with its milestone) |
| `LargeCanvasBrushTests.swift` | `src/Compositor.Tests/LargeCanvasBrushTests.cs` (port with its milestone) |
| `LayerAppearanceTests.swift` | `src/Compositor.Tests/LayerAppearanceTests.cs` (port with its milestone) |
| `LayerMaskTests.swift` | `src/Compositor.Tests/LayerMaskTests.cs` (port with its milestone) |
| `LayerTests.swift` | `src/Compositor.Tests/LayerTests.cs` (port with its milestone) |
| `LevelsTests.swift` | `src/Compositor.Tests/LevelsTests.cs` (port with its milestone) |
| `LiveMaskTests.swift` | `src/Compositor.Tests/LiveMaskTests.cs` (port with its milestone) |
| `MagicWandTests.swift` | `src/Compositor.Tests/MagicWandTests.cs` (port with its milestone) |
| `MaskAloneTests.swift` | `src/Compositor.Tests/MaskAloneTests.cs` (port with its milestone) |
| `MaskTransformTests.swift` | `src/Compositor.Tests/MaskTransformTests.cs` (port with its milestone) |
| `MetalWarpTests.swift` | `src/Compositor.Tests/MetalWarpTests.cs` (port with its milestone) |
| `NativeResolutionPaintTests.swift` | `src/Compositor.Tests/NativeResolutionPaintTests.cs` (port with its milestone) |
| `OuterGlowTests.swift` | `src/Compositor.Tests/OuterGlowTests.cs` (port with its milestone) |
| `PSBImportTests.swift` | `src/Compositor.Tests/PSBImportTests.cs` (port with its milestone) |
| `PSDAdjustmentTests.swift` | `src/Compositor.Tests/PSDAdjustmentTests.cs` (port with its milestone) |
| `PSDFixture.swift` | `src/Compositor.Tests/PSDFixture.cs` (port with its milestone) |
| `PSDRoundTripTests.swift` | `src/Compositor.Tests/PSDRoundTripTests.cs` (port with its milestone) |
| `PSDVectorFixtures.swift` | `src/Compositor.Tests/PSDVectorFixtures.cs` (port with its milestone) |
| `ProjectTabLayoutTests.swift` | `src/Compositor.Tests/ProjectTabLayoutTests.cs` (port with its milestone) |
| `ProjectTests.swift` | `src/Compositor.Tests/ProjectTests.cs` (port with its milestone) |
| `ProjectWorkspaceTests.swift` | `src/Compositor.Tests/ProjectWorkspaceTests.cs` (port with its milestone) |
| `RasterSnapshotTests.swift` | `src/Compositor.Tests/RasterSnapshotTests.cs` (port with its milestone) |
| `ResizeSnapTests.swift` | `src/Compositor.Tests/ResizeSnapTests.cs` (port with its milestone) |
| `SelectionClipboardTests.swift` | `src/Compositor.Tests/SelectionClipboardTests.cs` (port with its milestone) |
| `SelectionEditTests.swift` | `src/Compositor.Tests/SelectionEditTests.cs` (port with its milestone) |
| `SelectionFeatherTests.swift` | `src/Compositor.Tests/SelectionFeatherTests.cs` (port with its milestone) |
| `SelectionTests.swift` | `src/Compositor.Tests/SelectionTests.cs` (port with its milestone) |
| `ShapeToolTests.swift` | `src/Compositor.Tests/ShapeToolTests.cs` (port with its milestone) |
| `SliderSnapTests.swift` | `src/Compositor.Tests/SliderSnapTests.cs` (port with its milestone) |
| `SmartEditTests.swift` | `src/Compositor.Tests/SmartEditTests.cs` (port with its milestone) |
| `SpotHealingTests.swift` | `src/Compositor.Tests/SpotHealingTests.cs` (port with its milestone) |
| `TiledLayerTests.swift` | `src/Compositor.Tests/TiledLayerTests.cs` (port with its milestone) |
| `TitleBarDragTests.swift` | `src/Compositor.Tests/TitleBarDragTests.cs` (port with its milestone) |
| `TransformPressTests.swift` | `src/Compositor.Tests/TransformPressTests.cs` (port with its milestone) |
| `TransformTests.swift` | `src/Compositor.Tests/TransformTests.cs` (port with its milestone) |
| `TypeToolTests.swift` | `src/Compositor.Tests/TypeToolTests.cs` (port with its milestone) |
