# 跨平台托盘集成

主程序固定使用 Avalonia 12.1.4，并显式启用官方的 `AvaloniaAccessUnstablePrivateApis` 工程选项。此选项用于接入自有托盘后端；保留 `AVA3001` 警告，不手动替换引用程序集，也不全局屏蔽警告。

Avalonia 的普通引用程序集禁止应用实现 `IWindowingPlatform` 和 `ITrayIconImpl`。这两个接口属于版本相关的内部平台边界，并非稳定的应用扩展接口。官方选项及其警告定义见 [AvaloniaPrivateApis.targets](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/packages/Avalonia/AvaloniaPrivateApis.targets)。

`DesktopTrayPlatform` 包装原有平台实例，仅替换托盘创建。窗口、嵌入式窗口及窗口排序仍交给原平台。Windows 沿用原有后端；macOS 和 Linux 使用自有托盘实现，继续读取同一份 `NativeMenu` 菜单模型和完整彩色图标。

- macOS 使用 AppKit 状态栏项目和原生菜单，处理单击、双击、中键及右键菜单。
- Linux 使用独立的 GIO helper，导出 SNI 和 DBusMenu。宿主状态来自实际注册及宿主属性；断线会重新连接。协议没有可靠的双击事件，因此不启用该操作。
- 原生回调释放、helper 输入管道关闭、菜单关闭和退出等待都有独立的生命周期处理。helper 的就绪消息不等于托盘宿主可用。

升级 Avalonia 时，应一起检查引用程序集限制、平台接口签名、菜单桥接和附加／释放顺序。随后完成应用构建、AOT 和原生 helper 编译打包；需要覆盖现窗菜单、快捷键列、彩色图标及退出流程。仅直接引用运行时 DLL 的组件编译，不能代替真实工程的引用程序集检查。
