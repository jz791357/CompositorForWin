Compositor for Windows — M1 portable test build (self-contained, no install needed)

RUN
  Double-click Compositor.exe.
  If nothing opens, run "Start-Diagnostics.bat" (it keeps the console open and shows
  the error), then check the log at %LOCALAPPDATA%\Compositor\startup.log.

VM / no GPU acceleration
  If the window still fails to appear, run "Start-SoftwareRendering.bat" which forces
  WPF's software rasterizer (some virtual machines expose no usable 3D device).

This is a pre-release milestone build (M1): layers, masks, blend modes, brush/eraser/
move, .comp open+save (compatible with the macOS original), PNG/JPEG export, rulers
and guides. Known gaps (M2+): free transform, selections, text/shapes, drag-reorder,
thumbnails, keyboard remapping.
