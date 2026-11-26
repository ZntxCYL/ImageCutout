# ImageCutout v0.1.1 — Windows 本地 AI 抠图（开发预览）

基于 ImageOptimWin v0.9.0 TemporaryDebian 源码中的 WPF 主题、错误日志、独立更新器和更新对话框重构，不包含旧项目的图片压缩程序。目标 Windows x64 / Windows 10、11，使用 .NET 10 WPF + ONNX Runtime DirectML。无 Python 运行时要求；无需账号/API Key。

## GitHub Actions 构建（无需 Debian）

将本目录内所有文件（包括隐藏的 `.github` 目录）上传到自己的 GitHub 仓库根目录，默认分支 `main`。打开 Actions → Windows build and signed release → Run workflow。普通提交及 PR 也会触发检查和构建。构建完成后进入该次运行的 Artifacts 下载 `ImageCutout-win-x64`，外层 ZIP 中包含 `ImageCutout-win-x64-Bundled.zip`。

GitHub 托管 Windows Runner 自动安装 .NET 10 SDK 和 Python 3.12，执行 `build-windows.ps1`，无需自己的服务器。包是自包含 Windows x64 程序；不依赖系统 .NET SDK 或 Python。Actions 账户额度及使用规则由 GitHub 决定。也可在本机 Windows 安装 SDK 和 Python 后运行 `pwsh -File build-windows.ps1`。旧 `build-linux.sh` 仅作兼容保留。

生成 `dist/ImageCutout-win-x64-Bundled.zip`。解压到独立且有写权限的目录（例如 `%LOCALAPPDATA%\Programs\ImageCutout`），不要解压到桌面根目录、下载目录根目录或存放其他文件的目录。运行 `ImageCutout.Win.exe`。不是单 EXE：原生 DLL 必须随包保留。更新器会整体切换应用目录；图片、模型、用户文件请勿放在安装目录中。旧目录备份由用户确认成功后手动清理。

## 使用

- 添加或拖入图片/目录（目录仅处理一级）；支持 PNG/JPEG/BMP/TIFF/GIF（多帧只取第一帧）。
- 默认选择 BiRefNet Lite，1024×1024 模型输入。General 模式用于对比更精细的结果；不代表每张图必然更准确。
- GPU 优先使用 DirectML 的默认适配器，初始化失败时退回 CPU；实际执行设备显示在状态栏。GPU 推理阶段失败时会标记失败，可手动切换 CPU 重试。未实现 CUDA 专用版、显卡选择或 GPU 性能基准。
- 第一次运行下载对应 ONNX；后续缓存于 `%LOCALAPPDATA%\ImageCutout\models`，每批启动时重新校验一次。也可在 Debian 已下载的 rembg 缓存中找到对应 ONNX，复制到 Windows，选对应模型并使用“导入模型”。无需重新下载。
- 批次内复用同一推理 Session，逐张执行以减少内存压力，输出保留原图尺寸和原 RGB，原 Alpha 与预测蒙版相乘。默认输出到原图旁 `ImageCutout` 子目录，也可选择其他位置；重名自动加编号，不覆盖。
- 取消推理/下载，左右对比，队列进度、单张耗时和错误提示。仅处理第一帧；当前未实现 EXIF 方向修正、手工蒙版、专用 alpha matting、色键/黑底特效算法。

## 发布及国内热更新

见 `UPDATE_GUIDE_中文.md`。更新源及公钥默认为空，未配置时不会联网检查更新。模型下载镜像单独设置。没有附带模型权重或私钥。

## 验证状态

本交付为源码预览。当前环境无 .NET SDK / Windows GPU，未实际运行 GitHub Actions 或编译 .NET、未运行真实 ONNX 权重、未测试 Windows 自动更新。`scripts/check-source.py` 仅执行 XML/JSON/Python 解析及关键源码契约检查，不能替代编译和运行。发布脚本另有模拟签名检查。请首先在 GitHub Actions 编译并在 Windows 测试模型输出、CPU/GPU 性能及升级回滚。
