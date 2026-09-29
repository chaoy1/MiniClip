# 构建与发布

本文说明如何从源码构建 MiniClip，以及如何生成 Windows x64 安装包和免安装压缩包。所有命令均在仓库根目录执行。

## 环境要求

- Windows 10 或 11（x64）。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，用于构建应用。
- [Inno Setup](https://jrsoftware.org/isinfo.php)，仅在构建安装包时需要。
- Python 3 与 Pillow，仅在重新生成图标时需要。

先检查 SDK 是否可用：

```powershell
dotnet --list-sdks
```

输出中应包含 `10.` 开头的版本。如果系统 `PATH` 中的 `dotnet` 只有运行时，请调用实际安装的 .NET 10 SDK 可执行文件，或使用安装脚本的 `-DotnetExe` 参数。

**仓库里没有任何写死的本机路径。** 构建脚本按以下顺序寻找 SDK，第一个真正报出 `10.x` SDK 的生效（只有运行时的 `dotnet` 不会被误用）：

1. 命令行 `-DotnetExe`
2. `tools\local.build.json`（本机配置，已被 Git 忽略）
3. 环境变量 `MINICLIP_DOTNET`
4. `PATH` 中的 `dotnet.exe`
5. 常规安装位置：`%ProgramFiles%\dotnet`、`%ProgramFiles(x86)%\dotnet`、`%LOCALAPPDATA%\Microsoft\dotnet`

若你的 SDK 装在非常规位置（便携解压、另一个盘符等），在 `tools\local.build.json` 里写一次即可，**不要**把它提交进仓库：

```json
{
  "dotnetExe": "D:\\Software\\.NET\\dotnet-sdk\\dotnet.exe"
}
```

Inno Setup 同理：`isccExe` 字段、环境变量 `MINICLIP_ISCC`、`-IsccExe` 参数三者优先级相同。

## 构建与运行

```powershell
dotnet build src\MiniClip\MiniClip.csproj -c Release
.\src\MiniClip\bin\Release\net10.0-windows\MiniClip.exe
```

MiniClip 没有主窗口。启动后可在系统托盘找到图标。关闭程序请使用托盘菜单的“退出 MiniClip”。默认构建依赖目标机器上已安装的 .NET 10 Desktop Runtime。

## 免安装版本

项目文件 `src/MiniClip/MiniClip.csproj` 中的 `Version` 是版本号来源。以下以 `1.0.0` 为例：

```powershell
dotnet publish src\MiniClip\MiniClip.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=false `
  -o release\MiniClip-1.0.0-win-x64

Compress-Archive -Path release\MiniClip-1.0.0-win-x64\* `
  -DestinationPath release\MiniClip-1.0.0-win-x64.zip -Force
```

发布目录及压缩包中的文件须保持完整。目标机器需要 .NET 10 Desktop Runtime；普通的 .NET Runtime 不包含 WPF 所需组件。`release/` 为本地构建输出，不提交到源码仓库。

## 安装包

安装脚本先发布自包含的 `win-x64` 应用，再调用 Inno Setup 生成安装程序：

```powershell
.\tools\build-installer.ps1
```

若脚本未找到 SDK 或 Inno Setup，可显式指定路径：

```powershell
.\tools\build-installer.ps1 `
  -DotnetExe 'C:\path\to\dotnet.exe' `
  -IsccExe 'C:\path\to\ISCC.exe'
```

`Release` 构建设置了 `DebugType=none`，**不生成 PDB**。这是有意为之：PDB 会把源码文件的绝对路径逐个记下来，编译器还会把输出 PDB 自身的绝对路径写进程序集的 CodeView 调试目录，两者都会随安装包和 zip 一起发出去，暴露开发机的目录结构与用户名。运行时并不需要符号——诊断日志只记录异常**类型名**，从不写堆栈。Debug 配置仍保留完整符号，本地调试不受影响；若某次确实需要 Release 的行号，用 `-p:DebugType=portable` 单独构建，并且不要把 PDB 发出去。

安装包输出到 `dist\MiniClip-Setup-<版本>-x64.exe`。脚本会打印文件大小和 SHA-256。发布前请确认安装包与免安装压缩包版本一致，并在目标 Windows 环境执行安装、启动、升级和卸载检查。

安装包默认安装在 `%LOCALAPPDATA%\Programs\MiniClip`，可在向导中更改。它包含 .NET 10 Desktop Runtime，不要求目标机器另行安装。当前安装包没有代码签名。

## 数据位置与卸载

程序目录可写时，`history.json`、`settings.json` 和日志位于 `<程序目录>\data\`。目录不可写时回退到 `%LOCALAPPDATA%\MiniClip\`。安装包写入的 `MiniClip.portable` 标记仅用于允许导入旧版数据，不决定新数据的位置。

卸载安装版会删除安装目录中的 `data\` 和 `%LOCALAPPDATA%\MiniClip\`。这可能包含用户历史；卸载前应自行备份。免安装版没有卸载器，删除程序目录前也应确认数据位置。删除文件并非安全擦除。

## 验证

构建后运行 [验证说明](VERIFICATION.md) 中的自检，并检查实际安装场景。生成的 `release/`、`dist/` 和 `artifacts/` 均已被 Git 忽略；二进制发布文件应上传到 [GitHub Releases](https://github.com/chaoy1/MiniClip/releases)，不要提交进仓库。
