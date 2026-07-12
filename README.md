# CutTool

CutTool 是一个轻量级 Windows 截图工具，支持可调整选区、剪贴板复制、PNG/JPG
保存、Windows OCR 和中英文翻译。

它使用 Win32、WinForms 和 GDI+，空闲时只有一个进程，不启动 Electron、Chromium、
Node.js、GPU 辅助进程或常驻 PowerShell。实测空闲私有内存约 23-25 MB。

## 功能

- `Ctrl+Shift+A` 全局截图快捷键
- 自动预选当前前台窗口
- 自由框选、移动、缩放和方向键微调
- 复制、保存、另存为、OCR 和翻译
- 系统托盘、快捷键设置和开机启动
- Bing、MyMemory、Google 翻译降级链
- OCR 和网络服务按需启动，用完立即释放

## 环境要求

- Windows 10 或 Windows 11
- .NET Framework 4.8（当前 Windows 通常已内置）
- 对应语言的 Windows OCR 语言包

## 构建

无需 Visual Studio 或额外 SDK，构建脚本直接使用 Windows 自带的 .NET Framework
编译器：

```powershell
.\native\build.ps1 -Clean
```

生成文件位于 `native\dist\CutTool.exe`。

## 测试

```powershell
.\native\test.ps1
.\native\test.ps1 -IncludeNetwork
```

测试会验证热键解析，并使用动态生成的图片执行真实 Windows OCR；联网模式还会验证
翻译服务降级链。

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
