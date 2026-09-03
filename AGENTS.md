# MyTodo Agent Notes

轻量 Windows 待办应用：.NET 8 WPF，安装包使用 Inno Setup 6。

## 构建

在仓库根目录执行：

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-home"
$env:NUGET_PACKAGES="$PWD\.nuget-packages"
$env:APPDATA="$PWD\.dotnet-home"
dotnet build MyTodo -c Debug
```

启动 exe 前必须结束旧进程；不要把 `APPDATA` 覆盖值带进应用启动命令。

## 运行数据

真实用户数据在 `%APPDATA%\MyTodo\`，不要在仓库内创建或提交数据文件。

## 发布

使用 `.\publish.ps1`；中间产物在 `publish/`，最终安装包在 `dist/`。

## 约定

- 源码在 `MyTodo/`
- 图标源文件与生成脚本在 `icon-design/`
- 版本号同步维护 `MyTodo/MyTodo.csproj` 的 `Version` 与 `FileVersion`
