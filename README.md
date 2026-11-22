# BilingualOverlay

BilingualOverlay 是一个 Windows 中英对照悬浮窗程序。它在用户输入后读取当前焦点输入框的文字，并通过本地词典或在线翻译服务显示中英对照结果。

## 当前版本

- 版本：1.1.0
- 运行环境：Windows，使用 .NET Framework 4.0 及 Windows Forms/WPF UI Automation
- 默认发布形式：当前用户安装版与便携版
- 签名状态：当前构建尚未使用正式代码签名证书

## 主要功能

- 自动判断中英文翻译方向，也可固定翻译方向。
- 支持输入监听、选中文字翻译、剪贴板翻译和暂停监听。
- 悬浮窗支持拖动、缩放调整、点击穿透、全屏自动隐藏和多显示器恢复。
- 提供翻译历史、搜索、收藏、CSV 导出和启动清理选项。
- 提供集中设置界面，包括翻译服务、快捷键、开机启动、排除程序和历史与隐私。
- 支持 Per-Monitor V2 高 DPI，以及副屏拔插后的窗口可见性恢复。
- 更新清单使用 HTTPS、SHA-256 校验和 200 MB 下载限制。

## 项目结构

```text
work/BilingualOverlay/
  BilingualOverlay.cs          主程序源码与自测
  AssemblyInfo.cs              程序集信息
  app.manifest                 高 DPI 与 Windows 兼容声明
  dictionary.tsv               本地中英词典
  glossary.tsv                 术语优先表
  build.ps1                    编译、自测和打包脚本
  sign.ps1                     Authenticode 签名脚本
  README-使用说明.txt          面向使用者的说明
```

构建产物统一写入 `outputs/` 与 `work/BilingualOverlay/outputs/`，不会提交到 Git。

## 构建

在 Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\work\BilingualOverlay\build.ps1
```

脚本会：

1. 使用系统自带的 .NET Framework C# 编译器构建程序。
2. 运行 `--self-test` 并检查自测报告是否包含 `PASS`。
3. 生成便携目录、ZIP、安装/便携 EXE、更新清单示例和 SHA-256 校验文件。
4. 将交付文件复制到仓库根目录的 `outputs/`。

当前构建使用 Windows 自带的 C# 编译器；不需要在仓库中保存二进制依赖。

## 隐私与数据

- 程序通过 Windows 辅助功能接口读取当前焦点控件或选中文字。
- 密码框、密码管理器及常见敏感窗口会被跳过。
- 历史、收藏、设置和窗口位置均保存在本机的 `%LOCALAPPDATA%\BilingualOverlay` 或程序目录。
- 仓库的 `.gitignore` 已排除本机设置、历史、构建产物、测试报告、截图、证书和常见凭据文件。

## 测试

当前 1.1.0 构建自测结果为 138 项通过、0 项失败。自测报告和截图属于生成文件，不纳入源码仓库。

## 发布说明

发布前应使用可信的代码签名证书运行 `sign.ps1`，并为安装包配置稳定的 HTTPS 下载地址和更新清单。未签名构建在部分 Windows 环境或安全软件中可能触发警告。
