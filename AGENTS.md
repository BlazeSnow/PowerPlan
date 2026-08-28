# AGENTS.md

本文件面向 AI 编程助手，是 PowerPlan 仓库的简要说明；详细开发指南见 [DEVELOPMENT.md](./DEVELOPMENT.md)。

## 基本约定

1. 所有文件都以UTF-8格式存储
2. 默认禁止修改本文件；只有用户明确授权时才可修订
3. 鼓励修改 [DEVELOPMENT.md](./DEVELOPMENT.md)，使其始终匹配最新的开发进度
4. 可见字符串一律使用资源文件，不硬编码；版本号读取自安装包，不硬编码

## 项目简介

快速切换Windows电源计划的 WinUI 3 应用，最终发布至 Microsoft Store。

本地运行：

```powershell
dotnet run --project PowerPlan.csproj
```

## 内容速览

| 主题 | 要点 | 详情 |
| --- | --- | --- |
| 电源计划 | 通过 `powercfg` 读取与切换；卓越性能计划仅按 GUID（系统模板 GUID 或本程序保存的 UUID）识别，不按名称判断 | [DEVELOPMENT.md「电源计划」](./DEVELOPMENT.md#电源计划) |
| 主页面 | 解锁区（修改电源计划需管理员权限）、表格区、状态 | [DEVELOPMENT.md「主页面」](./DEVELOPMENT.md#主页面) |
| 托盘 | 自写 Win32 `Shell_NotifyIconW`，专用不可见 HWND 宿主；菜单深浅色跟随系统；禁止 `ForceDark`/`ForceLight` 等方案 | [DEVELOPMENT.md「托盘」](./DEVELOPMENT.md#托盘) |
| 设置页面 | 仿 Windows 11 设置；持久化到 `LocalSettings`，旧 `settings.json` 一次性迁移 | [DEVELOPMENT.md「设置页面」](./DEVELOPMENT.md#设置页面) |
| 界面与语言 | WinUI 3 原生侧边栏与 `TitleBar`；默认简体中文 | [DEVELOPMENT.md「侧边栏」](./DEVELOPMENT.md#侧边栏) |
| 性能要求 | 重点优化主界面关闭、托盘开启状态的性能与系统占用 | [DEVELOPMENT.md「性能要求」](./DEVELOPMENT.md#性能要求) |
