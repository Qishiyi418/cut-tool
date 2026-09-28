# CutTool

CutTool 是一个轻量级 Windows 截图工具，支持可调整选区、剪贴板复制、PNG/JPG
保存、Windows OCR 和中英文翻译。

它使用 Win32、WinForms 和 GDI+，空闲时只有一个进程，不启动 Electron、Chromium、
Node.js、GPU 辅助进程或常驻 PowerShell。英文 OCR 使用按需启动、完成即退出的轻量组件。

## 功能

- `Ctrl+Shift+A` 全局截图快捷键
- 自动预选当前前台窗口
- 自由框选、移动、缩放和方向键微调
- 复制、保存、另存为、OCR 和翻译
- 复制后可在聊天输入框粘贴图片，也可在文件资源管理器粘贴为 PNG 文件
- 系统托盘、快捷键设置和开机启动
- Bing、MyMemory、Google 翻译降级链
- OCR 和网络服务按需启动，用完立即释放

## 环境要求

- Windows 10 或 Windows 11
- .NET Framework 4.8（当前 Windows 通常已内置）
- 中文识别需要 Windows 中文 OCR 语言包；英文模型随程序附带
- x64 Windows 及 Visual C++ 2019/2022 x64 运行库（英文 OCR）

## 构建

无需 Visual Studio 或额外 SDK，构建脚本直接使用 Windows 自带的 .NET Framework
编译器：

```powershell
.\native\build.ps1 -Clean
```

生成文件位于 `native\dist\CutTool.exe`。
首次构建会下载固定版本并校验 SHA-256 的英文 OCR 组件，之后可离线构建。
安装和分发时必须保留整个 `dist` 目录，包括 `tools` 和 `licenses`。

## 测试

```powershell
.\native\test.ps1
.\native\test.ps1 -IncludeNetwork
.\native\test-clipboard.ps1
```

测试会验证热键解析、11/14/18 像素英文（浅色和深色背景）、中英文识别路径；
联网模式还会验证翻译服务降级链。

剪贴板集成测试需要先构建程序，会替换当前剪贴板为测试图片。它验证独立进程退出后
PNG、DIB、Bitmap 仍可读取，并验证资源管理器文件粘贴所需的 PNG 文件及像素内容。

## 2.0.1 复制兼容性修复

复制截图时同时提供 PNG、DIB、Bitmap 和文件列表，因此在资源管理器中按 `Ctrl+V`
会生成 PNG 文件。用于文件粘贴的缓存保存在 `%TEMP%\CutTool\Clipboard`，后续复制时
会清理超过 7 天的缓存；清空临时文件后，需要重新截图复制才能粘贴为文件。

## 2.0.2 英文 OCR 与内存控制

- 附带 Tesseract LSTM 英文量化模型，不再把英文请求悄悄交给中文识别器。
- 小字放大、灰度化、深色背景反色和留白，保留识别出的换行。
- 自动模式依据中文识别结果的文字比例选择中文或英文识别；混排或极短文本仍可能误判，
  可在设置中选择明确的翻译方向。
- OCR 串行执行，英文预处理图片上限约 400 万像素，30 秒超时；辅助进程完成即退出，
  模型不加载到托盘主程序中。
- MyMemory 超出长度限制时继续尝试其他服务，不再截断原文或把额度错误当成译文。

新增第三方组件和许可见 `native/licenses/THIRD-PARTY.md`。

## 安装

```powershell
.\native\install.ps1
```

安装脚本会：

- 安装到 `%LOCALAPPDATA%\Programs\CutTool`
- 保留 `%APPDATA%\cuttool\settings.json` 中的现有设置
- 创建桌面快捷方式
- 注册到 Windows“已安装的应用”
- 启动 CutTool

生成便携版压缩包：

```powershell
.\native\package.ps1
```

更多实现说明见 [`native/README.md`](native/README.md)。

## 许可证

[MIT](LICENSE)
