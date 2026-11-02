# Windows 资源管理器菜单

便携版使用传统右键菜单。Windows 11 的现代菜单需要包身份和原生 `IExplorerCommand`，仅写传统注册表菜单不会获得现代菜单。[Microsoft 文档](https://learn.microsoft.com/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)

`native/windows/explorer` 编译为 `RitsukageGif.ExplorerShell.dll`。它使用 Windows SDK 和静态 C++ 运行库，不包含另一份 .NET 运行时。DLL 与 `RitsukageGif.exe` 放在同一目录；当前 Windows 发行架构为 x64。

菜单只读取本应用的 `HKCU\Software\RitsukageGif\ShellIntegration`：`ContextMenuMode` 为 DWORD，0 禁用、1 图片、2 所有文件；`Caption` 为菜单文字。没有配置时隐藏。应用负责在现代和传统后端之间切换，避免重复显示。

一次操作会将所有文件交给 `pin-files`，不经过命令解释器。超过 Windows 命令行限制时，改用 `pin-files-list` 和用户临时目录中的 UTF-8 JSON 清单。应用接收后删除清单；启动失败由 DLL 删除。菜单构建不解码图片、不启动应用、不读取文件内容。

## 编译

在 Windows、PowerShell 7、Visual Studio C++ 工具和 CMake 环境中运行：

```powershell
./.github/scripts/build-explorer-shell.ps1 -Destination ./artifacts/publish
```

Windows ZIP 打包阶段调用同一脚本；其它平台不编译这个 DLL。脚本不会注册 COM、安装包或修改资源管理器。

## 可选包身份

包身份是单独选择的部署方式，不改变默认 ZIP 的发布渠道。准备与签名证书主题一致的 Publisher，再生成未签名身份包和应用清单：

```powershell
./.github/scripts/new-explorer-identity-package.ps1 `
  -Publisher 'CN=Your publisher' `
  -Output ./artifacts/identity/RitsukageGif.Identity.msix `
  -StagingDirectory ./artifacts/identity/staging
```

生成的同名 `.manifest` 保留原应用的 UAC、DPI 等声明，并加入匹配的 `msix` 身份元数据。为这一部署显式构建应用：

```powershell
dotnet publish src/RitsukageGif -c Release -r win-x64 `
  -p:ApplicationManifest=C:/absolute/path/RitsukageGif.Identity.manifest `
  -o ./artifacts/identity/application
```

该应用目录还需要原生 DLL，以及生成目录中的 `Assets`。身份包版本保持 `1.1.4.0`。实际部署必须使用匹配 Publisher 的可信证书签名，并由安装过程将包注册到正确的外部应用目录。仅存在 DLL 或生成 MSIX 不等于应用已拥有身份；应用清单中的名称、Publisher、应用 ID 必须与身份包一致。[外部位置包身份文档](https://learn.microsoft.com/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)

脚本按微软的稀疏包流程使用 MakeAppx `/nv`，因为应用二进制位于包外；它跳过的是语义检查中的包内文件存在要求。XML 架构仍由 MakeAppx 检查。生成成功不代表已签名、已安装或已通过实际资源管理器测试。[MakeAppx 文档](https://learn.microsoft.com/windows/msix/package/create-app-package-with-makeappx-tool)

本地验证直接加载编译后的 DLL，检查 COM 生命周期、真实 Shell 文件集合和多文件命令移交；注册表读取与进程启动在隔离测试进程中替换。没有安装身份包、信任证书、启用开发者模式或操作用户的资源管理器。
