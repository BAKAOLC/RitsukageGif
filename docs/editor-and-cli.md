# 编辑器、调试与命令行

[返回手册](README.md)

## 内置编辑器

从主界面选区按钮的预设菜单打开编辑器，也可运行 `RitsukageGif.exe editor "C:/Presets/demo.rgpreset"`。编辑器提供着色、诊断、成员补全、参数提示和脚本调试。先打开或保存外部预设文件，再选择要运行的预设。

支持多文件标签页，打开文件时可以多选；“打开文件夹”显示工作区目录，也可运行 `RitsukageGif.exe editor "C:/Presets"`。各标签保留自己的撤销记录、光标和断点。`Ctrl+Tab` 切换文件，`Ctrl+W` 关闭当前文件，`Ctrl+Shift+O` 打开工作区。

外部修改会自动载入尚未编辑的文件；存在本地修改时，选择“从磁盘重新加载”或“保留本地修改”。删除或移动文件不会丢弃编辑内容。运行期间暂缓重载，结束后同步。补全会读取已打开模块的未保存内容，运行前会保存所有已修改的标签页。

| 操作 | 快捷键 |
| --- | --- |
| 请求补全 | `Ctrl+Space` |
| 保存 | `Ctrl+S` |
| 调试／继续 | `F5` |
| 无调试运行 | `Ctrl+F5` |
| 当前行切换断点 | `F9` |
| 单步 | `F10` |

例如输入 `status().` 可查看录制状态成员；在 `switch` 分支中输入绑定变量后接 `.`，可查看推断出的成员。补全支持尚未写完的表达式；运行前需修复语法错误。

问题列表显示文件名、行号、列号和错误内容。单击问题或选中后按 `Enter`，即可跳到对应位置；导入文件中的错误会打开该文件。切换文件前会确认尚未保存的修改。行号和列号从 `1` 开始。

## VS Code

安装与系统匹配的 VSIX，打开 `.rgpreset` 或 `.rgs` 文件。在设置中指定主程序：

```json
{
  "rgscript.executable": "C:/Apps/RitsukageGif/RitsukageGif.exe"
}
```

插件提供补全、悬浮提示、签名帮助、定义跳转、问题诊断，以及运行／调试命令。运行会保存文件；使用包含 `preset` 的文件作为入口。macOS 的此设置也接受应用的 `.app` 路径。运行和调试要求工作区受信任。

可以在 `.vscode/launch.json` 中固定入口及预设：

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "rgscript",
      "request": "launch",
      "name": "调试录制",
      "program": "${workspaceFolder}/demo.rgpreset",
      "preset": "三秒录制",
      "stopOnEntry": true
    }
  ]
}
```

`preset` 留空时使用文件中的第一个预设。`stopOnEntry` 决定是否在入口暂停。断点只在可执行行生效；可以在导入的文件中设置断点。

调试时可继续、暂停、单步和停止，并查看当前变量。调试求值支持读取已有变量；变量面板以摘要形式显示对象。脚本中的 `breakpoint();` 可请求暂停。

## 外部文件与预设快捷键

在设置的预设管理界面导入外部文件，或点击“新建”选择保存位置并创建预设。选中文件后可编辑或移出列表；移出列表不会删除文件。编辑时预设管理和设置窗口保持打开，关闭编辑器即可返回。此时只编辑脚本，关闭设置后再运行或调试。

选中文件后添加快捷键，选择其中的预设并按下组合键；可以清除或移除绑定，同一预设也可绑定多个快捷键。保存时检查与录制、选区及其他预设快捷键的冲突。预设管理的修改先保存在设置草稿中，最后点击设置的保存按钮生效；已创建的外部文件不随取消设置而删除。快捷键启动该预设的逻辑，因此是否立即录制取决于脚本是否调用 `start()`。

预设文件保持在用户选定的位置，调用时重新加载。快捷键配置属于应用设置，预设 DSL 中没有 `hotkey` 声明。不同动作需使用不同的组合键。平台不支持全局快捷键时，相关控件不可用。

## 单例与命令行

命令发给当前实例；没有实例时，会启动后台服务。后台服务和编辑器不创建主窗口；无参数启动或调用 `show` 打开主窗口。重复直接启动会唤起已有主窗口并请求前台焦点；窗口最小化时恢复显示，存在设置等对话框时优先激活该对话框。命令输出一个 JSON 回复，成功退出码为 `0`，失败为 `1`。命令成功表示已受理；录制与编码的最终状态应通过 `status` 查询。

PowerShell 调用示例：

```powershell
$exe = 'C:/Apps/RitsukageGif/RitsukageGif.exe'
& $exe select 0 0 640 360
& $exe start
& $exe status
& $exe stop
```

| 命令 | 用途 |
| --- | --- |
| `help` 或 `--help` | 查看帮助 |
| `show` / `hide` | 显示／隐藏主窗口 |
| `status` | 状态、输出路径、帧进度、预设错误等 |
| `select` | 打开交互选区 |
| `select x y width height` | 设置屏幕像素区域 |
| `select-window process [title]` | 按进程和标题匹配窗口；标题省略时为 `*` |
| `windows` | 列出窗口句柄、进程、标题和区域 |
| `start` / `stop` / `cancel` | 开始、结束保存、取消当前录制或选区 |
| `presets` | 列出已配置的预设名称 |
| `preset activate name` | 启动已配置的预设 |
| `preset run file [name]` | 加载指定文件并启动；省略名称时要求文件恰有一个预设 |
| `preset stop` | 停止当前预设；若在录制中则先结束录制 |
| `editor [file]` | 打开内置编辑器 |

例如先找窗口，再按进程名称选择：

```powershell
& $exe windows
& $exe select-window 'notepad' '*'
& $exe start
```

`select-window` 使用应用当前的捕获源设置。需要固定屏幕上下文／窗口内容、偏移、布局和输出时，应使用预设。含空格的名称和路径需要加引号；调用 `editor` 时建议传绝对路径。

## 外部参数与跟随捕获

启动时可重复传 `--arg name=value`：

```powershell
& $exe preset run './examples/luastg-external-tracking.rgpreset' --arg x=160 --arg y=240 --arg w=128 --arg h=128
```

回复中的 `ParameterSession` 是本次预设的唯一标识。运行期间，用它原子更新一组参数：

```powershell
& $exe preset params '<ParameterSession>' x=200 y=280
& $exe preset params '<ParameterSession>' outputX=20 outputY=40
& $exe preset params '<ParameterSession>' stop=true
```

更新通过本机进程间通信传递给当前预设。替换或结束预设后，旧标识的更新会失败。每批参数要么全部应用，要么完全不应用；使用 `params.snapshot()` 可一次读取同一批 x/y，保持坐标来自同一次更新。启动回复是已受理，脚本错误仍应从 `status` 的 `PresetError` 检查。

| 脚本 API | 返回值 |
| --- | --- |
| `params.get(name, fallback = null)` | 参数原值，缺失时返回默认值 |
| `params.number(name, fallback = 0)` | 数字；缺失或类型不匹配时返回默认值 |
| `params.string(name, fallback = "")` | 字符串；缺失或类型不匹配时返回默认值 |
| `params.boolean(name, fallback = false)` | 布尔值；缺失或类型不匹配时返回默认值 |
| `params.snapshot()` | 独立的原子快照；修改它不影响外部参数 |
| `params.revision()` | 参数批次版本号 |
| `params.state()` | 原子读取 `{revision, values}` |
| `params.waitChanged(revision)` | 等待参数或录制／窗口事件，返回当前版本；事件唤醒时版本可以不变，取消预设会中断等待 |

参数名区分大小写。数值用小数点；`true`、`false`、`null` 是对应字面量，其余内容按文本处理，不执行表达式。每次预设最多 256 个参数；名称最长 128 字符，单个文本最长 4096 字符。重复名称、非有限数值会被拒绝。

[外部跟随范例](../examples/luastg-external-tracking.rgpreset) 将 x/y/w/h 解释为 LuaSTG 的 640×480 逻辑坐标，限制在 world 内，再换算成当前窗口像素。outputX/outputY 控制合成画面中的位置。它在录制前应用初始参数，之后由参数变化或录制／窗口状态变化唤醒，应用最新参数；目标坐标由外部程序提供。将源声明改为 `source screen`，可跟随窗口位置捕获屏幕内容。

参数驱动的循环可使用 `params.state()` 读取数据，处理完后调用 `params.waitChanged(state.revision)` 等待下一批；若等待前已有更新，该调用立即返回新版本。`scene.set(name, {crop: {...}, placement: {...}})` 将输入裁剪和输出位置一起提交，在下一帧采样时生效。

命令行适合手动控制和低频更新。持续推送逐帧坐标时，使用[外部输入通道](input-channels.md)保持连接，并在脚本中通过 `inputs` 接收数据。
