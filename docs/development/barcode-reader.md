# 条码识别组件构建

构建应用需要 PowerShell 7、CMake 3.24 或更新版本，以及支持 C++20 的编译器。Windows 使用 Visual Studio C++ 工具链，Linux 使用 GCC 或 Clang，macOS 使用 Xcode 命令行工具。

应用构建会自动编译条码读取组件。首次构建从 ZXing-C++ 官方仓库下载固定的 3.1.1 源码，校验下载包后缓存在 `artifacts/native-barcode`；后续使用 CMake 增量构建。清除此目录会重新下载和编译。

也可单独构建，例如：

```powershell
./.github/scripts/build-barcode-native.ps1 -RuntimeIdentifier win-x64
```

支持 `win-x64`、`linux-x64`、`linux-arm64`、`osx-arm64`，须在对应系统和处理器上构建。发布时只包含当前平台的读取组件和许可证，不包含其它平台的二进制或条码写入器。Windows 组件静态链接 C++ 运行库。

固定源码中的 Telepen 奇偶校验使用字节数组。构建补丁将比较值 `false` 精确改为 `uint8_t{0}`，保持算法不变并消除 MSVC C4805；未全局关闭编译警告。补丁只在原表达式存在时写入，缓存构建不会反复修改源码。
