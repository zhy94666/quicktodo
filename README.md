# MyTodo

轻量、快速启动的 Windows 待办事项面板（.NET 8 + WPF，界面采用浅色青瓷风格）。

![MyTodo 界面预览](docs/screenshot.png)

## 功能

- 随手记、按日期两种独立模式；按日期支持前一天/后一天、回今天和日历选择
- 待办添加、行内二次编辑、拖动排序、完成/删除动效
- 完成后保留时间戳：随手记显示完整日期时间，按日期只显示时刻
- 一键完成、清除已完成
- 回收站：删除项软删除，保留 7 天，可单条恢复、全部恢复或清空
- 导出/导入 JSON 备份；导入前自动生成安全备份
- 主面板可置顶，支持长文本自动换行
- 全局快捷键呼出/隐藏，支持单键（如 `F1`、`V`），可在设置中录制
- 快速收集：复制文本后按快捷键直接写入随手记
- 桌面小组件：托盘开关、不透明度调节、边缘吸附、位置记忆、紧凑模式
- 系统托盘统计与近 7 日趋势

## 数据

运行数据保存在用户目录，不写入程序安装目录：

```text
%APPDATA%\MyTodo\data.json
%APPDATA%\MyTodo\trace.log
%APPDATA%\MyTodo\error.log
```

导入功能只替换待办记录；快捷键、窗口与小组件等设置保留本机配置。

## 开发

```powershell
dotnet build MyTodo -c Debug
```

构建产物位于 `MyTodo\bin\Debug\net8.0-windows\MyTodo.exe`。

## 发布

需要 .NET 8 SDK 与 Inno Setup 6：

```powershell
.\publish.ps1
```

脚本会：

1. 停止正在运行的 MyTodo
2. 生成 win-x64 自包含发布文件到 `publish\`
3. 使用 Inno Setup 打包到 `dist\`
4. 清理中间发布目录

最终安装包形如：

```text
dist\MyTodo-1.2.0-setup.exe
```
