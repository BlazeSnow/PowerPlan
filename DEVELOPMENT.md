# 开发指南

PowerPlan 的详细开发指南。基本约定与内容速览见 [AGENTS.md](./AGENTS.md)。本文档允许修改，应随开发进度同步更新。

## 程序目的

快速切换Windows电源计划

## 程序架构

1. 程序使用WinUI 3架构
2. 最终发布至Microsoft store

## 电源计划

1. 读取用户拥有的Windows电源计划
2. 检查用户是否有卓越性能计划，若无，则提供创建卓越性能计划选项

### 创建卓越性能计划

1. 创建命令为：`powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61`
2. 创建后读取系统返回的UUID并保存

### 卓越性能计划存在性

1. 部分设备通过`powercfg -l`无法查看到被隐藏的卓越性能计划
2. 针对这些设备，通过读取创建时的UUID，然后可开启卓越性能计划
3. 使用`powercfg -setactive 创建时的UUID`可开启被隐藏的卓越性能计划
4. 但是用户通过其他途径删除该计划后，setactive可能会失败，此时需在log栏提示用户
5. 在设置中恢复电源计划后，需清空储存的卓越性能计划的UUID

### 卓越性能计划识别逻辑

1. 仅将以下两类计划视为卓越性能计划：
   1. GUID等于系统卓越性能模板GUID：`e9a42b02-d5df-448d-aa00-03f14749eb61`
   2. GUID等于本程序保存的卓越性能计划UUID
2. 不通过计划名称关键词判断卓越性能计划，避免用户将普通计划改名后被误判
3. 用户已有但未被本程序记录的卓越性能副本仍会显示在计划列表中，用户可直接手动切换
4. 对于未被本程序记录的卓越性能副本，创建提示属于保守提示，用户可忽略

## 主页面

1. 主页包括：解锁区、表格区、状态
2. 解锁区：
   1. 读取电源计划不需要管理员权限
   2. 但是修改电源计划需要管理员权限，因此需提供提示和按钮，要求用户提供管理员权限
3. 表格区：
   1. 展示用户的Windows电源计划
   2. 有一行卓越性能计划，提供开启按钮（若系统内无卓越性能计划）
4. 状态：
   1. 采用winui 3状态展示

## 托盘

1. 使用自写 Win32 `Shell_NotifyIconW` 实现托盘，图标使用应用 `Assets\powerplan.ico`。
2. 托盘必须使用专用的不可见顶级 HWND 作为通知区回调宿主；不可依赖主 WinUI 窗口，因为静默启动时主窗口不存在，关闭主窗口后也会被隐藏。
3. 使用稳定的通知区图标 GUID，并在 `NIM_ADD` 后设置 `NOTIFYICON_VERSION_4`。
4. 必须处理 `TaskbarCreated`，以便 Explorer 重启后重新添加图标并恢复协议版本。
5. 左右键均弹出原生菜单。菜单通过 `CreatePopupMenu`/`TrackPopupMenuEx` 临时构建和显示，结束后必须销毁 `HMENU`。
6. 电源计划列表、当前计划勾选状态和开机自启动开关等动态内容，每次打开菜单时均由当前快照生成；当前计划使用菜单勾选标识。
7. 其他菜单项的图标拼入菜单文本，不使用会挤压文本的菜单图标槽。
8. 软件开机自启动的开关，用户可点击控制开关。
9. 软件的退出，用户可点击退出；必须先关闭并销毁原生菜单，再回到 UI 线程执行退出逻辑。
10. 托盘菜单必须通过 `TrayMenuTheme` 兼容层使用 `AllowDarkModeForApp` 或 `SetPreferredAppMode(AllowDark)` 并刷新菜单主题缓存，以跟随系统应用深浅色；不支持的系统或 API 解析失败时回退系统默认菜单。
11. 禁止使用 `ForceDark`、`ForceLight`、硬编码菜单或标题栏颜色、将 `DwmSetWindowAttribute` 当作菜单主题方案、`RequestedTheme` 强制值，或把托盘主题绑定到主 WinUI 窗口。
12. 每次动态菜单更新后，必须验证菜单可正常弹出、全部项目可见、命令可执行、当前计划勾选正确，且重复打开不会出现空菜单或重复项目。
13. 修改 WinUI、Windows App SDK 或原生托盘实现后，必须重新验证动态菜单刷新、系统浅深主题、静默启动、重复打开菜单、Explorer 重启恢复和退出流程。

### 开机自启动静默启动

1. 检测到`StartupTask`激活且托盘启用时，跳过`_window.Activate()`，窗口不创建不显示，直接进入托盘
2. 用户从托盘菜单点击"打开主窗口"时，通过`ShowWindow(hwnd, 5)`显示窗口

### 开机自启动本地测试

1. 正常启动应用，开启"开机自启"和"托盘"设置，关闭应用
2. 设置环境变量`POWERPLAN_SIMULATE_STARTUP=1`后启动应用，模拟登录触发：

   ```powershell
   $env:POWERPLAN_SIMULATE_STARTUP = "1"
   dotnet run --project PowerPlan.csproj
   ```

3. 预期：窗口不弹出，图标直接进入托盘
4. 真实链路验证：登出再登入即可，不需要重启电脑

## 设置页面

1. 设置界面仿制Windows 11设置应用
2. 开机自启动（开关）：默认为关
3. 启用托盘（开关）：默认为开
4. 电源计划（按钮）：打开控制面板的电源选项
5. 恢复电源计划（按钮）：恢复电源计划到默认状态`powercfg -restoredefaultschemes`
6. 开发者官网（按钮）：<https://www.blazesnow.com>
7. 软件版本号：显示当前安装包版本，不硬编码版本号

### 持久化设置

1. 设置内容保存到`ApplicationData.Current.LocalSettings`
2. 旧版本`settings.json`仅用于一次性迁移
3. 迁移成功后，将旧`settings.json`重命名为`settings.json.migrated`
4. LocalSettings字段：
   1. 开机自启动：`AutoStartEnabled`
   2. 启用托盘：`TrayEnabled`
   3. 卓越性能计划UUID：`UltimatePerformancePlanGuid`
5. 旧JSON字段仍需保持读取兼容：
   1. 开机自启动：`startup`
   2. 启用托盘：`tray`
   3. 卓越性能计划UUID：`UltimatePerformance`

## 侧边栏

1. 采用winui 3原生侧边栏
2. 切换主页和设置页
3. 伸缩侧边栏按钮放在标题栏上

## 标题栏

1. 使用WinUI `TitleBar`
2. 标题栏显示软件名称和软件简介
3. 软件简介同时作为标题栏副标题
4. 标题栏文本使用资源文件，不硬编码可见字符串

## 语言

1. 使用winui 3的多语言架构
2. 默认语言是简体中文
3. 目前仅支持简体中文

## 性能要求

1. 程序主要运行在主界面关闭、托盘开启的状态，因此需要着重优化该状态时的性能与系统占用
