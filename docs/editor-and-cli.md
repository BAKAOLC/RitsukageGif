# 编辑器与命令行

[返回手册](README.md)

## 打开与运行

从主界面的“预设”菜单打开编辑器。打开 `.rgpreset`，选择要执行的预设，再点击运行。`.rgs` 用作公共模块，需要从预设中导入。

可以同时打开多个文件，或打开文件夹浏览工作区。运行前会保存已修改的文件；问题列表显示文件、行号和列号，点击问题可跳到对应位置。

编辑器提供着色、补全和参数提示。调试时可设置断点、单步并查看变量；按 F5 开始调试会先在入口暂停，再按 F5 继续。点击“停止”取消当前脚本。

| 快捷键 | 操作 |
| --- | --- |
| Ctrl+O / Ctrl+Shift+O | 打开文件 / 打开工作区 |
| Ctrl+S / Ctrl+Shift+S | 保存 / 另存为 |
| Ctrl+Tab / Ctrl+W | 切换文件 / 关闭当前文件 |
| Ctrl+Space | 请求补全 |
| Ctrl+F5 | 运行 |
| F5 | 调试 / 继续 |
| F9 | 切换当前行断点 |
| F10 | 单步 |

## 命令行

完整命令、脚本执行、独立任务和 JSON 回复见[命令行参考](cli-reference.md)；脚本接口见[API 参考](api-reference.md)和[类型参考](script-types-reference.md)。

下面以 PowerShell 为例，将路径换成实际程序位置：

```powershell
$exe = 'C:/Apps/RitsukageGif/RitsukageGif.exe'
& $exe editor 'C:/Presets/demo.rgpreset'
& $exe preset run 'C:/Presets/demo.rgpreset' '三秒录制'
& $exe status
```

`editor` 也接受文件夹路径。`preset run` 省略预设名称时，文件中必须恰好有一个预设。

| 命令 | 用途 |
| --- | --- |
| `help` / `--help` | 查看命令帮助 |
| `--version` | 查看版本 |
| `show` / `hide` | 显示 / 隐藏主界面 |
| `editor [文件或文件夹]` | 打开脚本编辑器 |
| `preset run 文件 [名称]` | 运行指定文件中的预设 |
| `presets` | 列出已配置的预设 |
| `preset activate 名称` | 激活已配置的预设 |
| `preset stop` | 停止当前预设 |
| `select` | 交互选择录制区域 |
| `select x y width height` | 按屏幕像素设置录制区域 |
| `windows` | 查看可选窗口 |
| `select-window 进程 [标题]` | 选择匹配窗口，标题可使用通配符 |
| `start` / `stop` / `cancel` | 开始录制 / 停止保存 / 取消 |
| `status` | 查询录制、输出和错误状态 |
| `screenshot [screen\|window]` | 截图，可指定屏幕画面或窗口内容 |
| `animation-editor [文件]` | 打开动图编辑器 |
| `video-recorder` | 打开视频录制 |

例如，手动设置区域后开始录制，再由用户停止：

```powershell
& $exe select 0 0 640 360
& $exe start
# 录制所需内容后停止
& $exe stop
```

命令返回 JSON。成功退出码为 `0`，失败为 `1`；开始命令成功后，可用 `status` 查看后续录制、编码和保存结果。

预设可以接收参数；每个参数使用一次 `--arg`：

```powershell
& $exe preset run 'C:/Presets/demo.rgpreset' --arg x=120 --arg y=80
```

脚本通过 `params.get('x')`、`params.number('x', 0)` 等读取参数，再自行用于范围或布局。

## VS Code

安装 rgscript 的 VSIX，在设置中将 `rgscript.executable` 指向主程序。打开包含 `preset` 的文件即可运行或调试；运行和调试需要信任工作区。
