# 命令行参考

下列命令写在程序名后。命令和选项区分大小写；含空格的路径、标题和名称需要加引号。`<参数>` 表示必填，`[参数]` 表示可选，不要输入括号本身。

PowerShell 示例先设置程序路径：

```powershell
$exe = 'C:/Apps/RitsukageGif/RitsukageGif.exe'
& $exe status
```

Linux、macOS 使用对应可执行文件路径。窗口查询、全局输入和捕获仍受当前系统授权限制。

## 启动与查询

| 命令 | 用途 |
| --- | --- |
| 不带参数 | 启动主界面；已经运行时显示并激活主界面 |
| `--silent` | 静默启动，不显示主界面；已经运行时不唤起主界面 |
| `--serve` | 在后台启动；已经运行时保持当前界面状态 |
| `show` | 显示并激活主界面 |
| `hide` | 隐藏主界面 |
| `help`、`--help` | 查看命令帮助 |
| `--version` | 查询版本 |
| `status` | 查询当前状态、录制进度、输出及预设状态 |
| `exit` | 退出当前命令所连接的程序或任务会话 |
| `settings open` | 打开设置，不唤起主界面 |
| `logs path` | 返回日志目录，路径在 `Output` 中 |
| `logs open` | 打开日志目录 |
| `windows` | 查询可用窗口，最多列出 100 个 |
| `presets` | 查询已添加的预设文件中的预设名称 |

`windows` 的 `Message` 每行依次是窗口标识、进程名、标题、`x,y,width,height`，用制表符分隔。`presets` 的 `Message` 每行一个名称。

## 快速录制

| 命令 | 用途 |
| --- | --- |
| `select` | 交互选择快速录制区域，等待选区操作结束 |
| `select <x> <y> <width> <height>` | 直接设置屏幕区域，并切换为屏幕捕获 |
| `select-window <process> [title]` | 选择匹配窗口的客户区，并跟随该窗口。使用当前捕获来源设置 |
| `start` | 使用当前区域与设置进行快速录制，等待本次录制及输出完成 |
| `stop` | 停止当前录制 |
| `cancel` | 取消当前截图、选区或正在处理的录制任务 |

坐标与尺寸使用整数像素，宽高必须为正；屏幕区域必须是有效可捕获范围。`select-window` 的进程和标题支持 `*`、`?` 通配符，标题省略时为 `*`。先用 `windows` 查看名称。

```powershell
& $exe select 100 100 640 360
& $exe start
```

录制期间，在另一个终端执行 `stop` 以正常结束并输出，或执行 `cancel` 取消录制。录制取消时，等待录制结果的 `start` 命令退出码为 `130`。

```powershell
& $exe stop
```

```powershell
& $exe select-window 'luastgsub' '*'
```

`video-recorder` 打开录制界面。在独立任务里打开这个界面时，使用 `--keep-alive` 保留后续操作的会话。

## 截图与编辑器

| 命令 | 用途 |
| --- | --- |
| `screenshot` | 进入截图，使用当前捕获来源 |
| `screenshot screen` | 进入屏幕截图 |
| `screenshot window` | 进入窗口内容截图 |
| `editor [path]` | 打开脚本编辑器；可指定脚本文件或工作区目录 |
| `animation-editor [file]` | 打开动图编辑器；可指定要导入的图像文件 |
| `video-recorder` | 打开录制界面 |

截图命令等待本次选区和输出操作完成。成功回复的 `Region` 为本次选区，`Outputs` 为本次保存的路径，`Output` 为其中第一条。没有保存文件时，`Outputs` 为空数组，`Output` 为 `null`。取消截图返回退出码 `130`，捕获或输出失败返回 `1`。

```powershell
& $exe editor './capture.rgpreset'
& $exe animation-editor './capture.gif'
& $exe screenshot screen
```

## 贴图

| 命令 | 用途 |
| --- | --- |
| `pin-files <file> [file ...]` | 将一个或多个文件作为贴图打开 |
| `pins show` | 显示所有贴图 |
| `pins hide` | 隐藏所有贴图 |

```powershell
& $exe pin-files './first.png' './second.png'
& $exe pins hide
& $exe pins show
```

文件路径以执行命令的目录为基准。不支持的文件会返回失败。

## 预设

| 命令 | 用途 |
| --- | --- |
| `preset activate <name>` | 激活已添加的预设文件中的指定预设 |
| `preset run <file> [name] [--arg <key=value> ...]` | 从文件运行预设，并传入参数 |
| `preset params <session> <key=value> [key=value ...]` | 更新指定运行会话的参数 |
| `preset stop` | 停止预设录制并取消激活预设 |

`preset run` 的文件路径相对于执行命令的目录。文件中有多个预设时必须填写名称；`--arg` 可重复，每次只跟一个赋值，名称应放在全部 `--arg` 之前。

在独立任务会话中，`preset run` 等待本次预设执行结束，回复包括 `OperationId` 和脚本返回值 `Value`。需要从另一个终端控制时，先保留会话，再用 `script list` 查找该预设的操作标识，或用 `script cancel <operationId>` 取消。连接常驻程序时，`preset run` 激活预设后返回。需要在常驻程序里等待执行结果时，使用 `script file`。

预设录制成功保存时，`Output` 是本次录制的第一条路径，`Outputs` 是该次录制的全部路径。

参数值中的 `true`、`false`、`null` 和数值按对应类型读取，其余为字符串。名称区分大小写，同次调用不能重复。每组最多 256 项，名称长度为 1～128，字符串值最长 4096 个字符。

```powershell
& $exe preset run './capture.rgpreset' '窗口跟随' --arg x=100 --arg y=200 --arg seconds=10
```

更新运行中的参数，先从 `status` 取到 `ParameterSession`：

```powershell
$reply = & $exe status | ConvertFrom-Json
& $exe preset params $reply.ParameterSession x=240 y=120
```

`preset params` 的赋值前不写 `--arg`。参数读取与跟随示例见[外部参数与输入通道](input-channels.md)。

## 功能动作

| 命令 | 用途 |
| --- | --- |
| `actions list` | 列出动作类型和已配置的动作 |
| `actions get <id>` | 查询一个已配置动作 |
| `actions run kind <kind> [options ...]` | 直接执行动作类型，无需事先创建动作 |
| `actions run id <id> [options ...]` | 执行已配置动作，可临时覆盖参数 |
| `action <id>` | 执行已配置动作 |

`actions list` 返回的 `Actions` 中，类型条目的 `Id` 为 `null`，已配置动作带 `Id`。每项包含 `Kind`、`Name`、`Enabled`、`Available`、`FileParameter`、`Parameter`、`PresetName`、`Arguments` 和 `UnavailableReason`。`Kind` 是下面命令使用的类型名称；`Available` 反映当前状态是否允许执行。

| 选项 | 用途 |
| --- | --- |
| `--parameter <value>` | 本次动作的路径或捕获来源等参数 |
| `--preset <name>` | 运行预设时指定名称 |
| `--arg <key=value>` | 动作参数，可重复 |
| `--id <operationId>` | 指定本次操作标识；省略时自动生成 |

这些覆盖只用于本次执行，不修改已保存动作。动作类型名称不区分大小写，已配置动作标识区分大小写。动作被停用或当前状态不允许执行时返回失败。

截图动作的 `--parameter` 可用 `screen` 或 `window`，省略时使用当前来源设置。其 `--arg` 支持：

| 参数 | 含义 |
| --- | --- |
| `x`、`y` | 初始位置，必须成对提供；整数 -99999～99999 |
| `width`、`height` | 初始尺寸，必须成对提供；整数 1～99999 |
| `ratio` | 宽高比，必须大于零 |
| `delay` | 延迟秒数，0～86400 |
| `directory` | 快速保存的目标目录，仅用于 `QuickSave` 输出 |
| `output` | 仅 `CaptureActiveWindow`、`CaptureLastRegion` 可临时选择输出；取值为 `Copy`、`Pin`、`Save`、`QuickSave`、`CopyText`、`CopyCodeText`、`Translate`、`RecognizeText`、`RecognizeTable`、`RecognizeFormula` |

`RunPreset` 的 `--parameter` 是预设路径，`--arg` 是脚本参数；文件类动作的 `--parameter` 是文件路径。需要跨目录执行时，动作路径建议填写绝对路径。

```powershell
& $exe actions list
& $exe actions run kind ScreenshotAndScroll
& $exe actions run kind PinFile --parameter 'C:/Captures/image.png'
& $exe actions run kind RunPreset --parameter 'C:/Captures/capture.rgpreset' --preset '窗口跟随' --arg seconds=5
```

动作类型：

| 范围 | `kind` |
| --- | --- |
| 快速录制 | `SelectRegion`、`ToggleRecording`、`StartRecording`、`StopRecording` |
| 截图 | `Screenshot`、`ScreenshotAndCopy`、`ScreenshotAndPin`、`ScreenshotAndSave`、`ScreenshotQuickSave` |
| 指定目标 | `CaptureActiveWindow`、`CaptureLastRegion` |
| 识别与翻译 | `ScreenshotAndCopyText`、`ScreenshotAndCopyCodeText`、`ScreenshotAndRecognizeText`、`ScreenshotAndRecognizeTable`、`ScreenshotAndRecognizeFormula`、`ScreenshotAndTranslate`、`TranslateClipboard` |
| 长截图与录制 | `ScreenshotAndScroll`、`ScreenshotAndRecord` |
| 创建贴图 | `PinClipboard`、`PinFile`、`PinSelectedFiles` |
| 管理贴图 | `ShowPinnedImages`、`HidePinnedImages`、`ClosePinnedImages`、`TogglePinnedImages`、`ToggleOtherPinnedImages`、`RestoreLastPinnedImage`、`SwitchPinnedGroup`、`TogglePinnedMouseThrough` |
| 编辑器与窗口 | `OpenScriptEditor`、`OpenImageEditor`、`OpenAnimationEditor`、`OpenVideoRecorder`、`OpenOutputDirectory`、`ShowMainWindow`、`HideMainWindow` |
| 脚本及其他 | `RunPreset`、`RunScript`、`ToggleGlobalShortcuts`、`ToggleWindowTopmostAtPointer` |

`RunScript` 使用已配置动作中的脚本文本。直接传入脚本文本时，使用下面的 `script eval`，不需要创建动作。

命令行执行 `RunPreset` 或 `RunScript` 时会等待对应预设或脚本结束。`StartRecording` 与 `ToggleRecording` 等待该次快速录制的结果。打开录制界面的动作在界面打开后返回。

脚本中的 `actions.run` 和 `actions.runId` 返回 `Result<ActionResult, string>`。成功时，截图动作的 `paths` 和 `region` 对应本次截图，其他动作返回 `paths: []` 和 `region: null`。字段定义见[脚本类型参考](script-types-reference.md#actionresult)。

## 执行脚本

| 命令 | 用途 |
| --- | --- |
| `script file <path> [options ...]` | 读取并执行 UTF-8 脚本文件 |
| `script eval <text> [options ...]` | 执行参数中的脚本文本 |
| `script stdin [options ...]` | 从标准输入读取 UTF-8 脚本，读取至输入结束后执行 |
| `script list` | 列出当前仍在执行的命令操作 |
| `script cancel <operationId>` | 取消一个操作，并等待它结束 |

文件、文本和标准输入都可以提供普通脚本或完整预设文档；预设文档使用 `preset` 开头。源文本最多 1 MiB。

| 选项 | 用途 |
| --- | --- |
| `--arg <key=value>` | 向 `params` 传入参数，可重复；值规则与预设参数相同 |
| `--preset <name>` | 在完整预设文档中选择名称；有多个预设时必须指定；普通脚本不能使用 |
| `--id <operationId>` | 指定操作标识，供查询和取消使用；省略时自动生成 |

`file` 的相对路径以执行命令的目录为基准，脚本内相对路径以脚本文件目录为基准。`eval` 和 `stdin` 则以执行命令的目录为基准；`import` 仍相对于写下导入的文件。

```powershell
& $exe script eval 'return params.number("x", 0) + 1;' --arg x=41
& $exe script file './capture.rgpreset' --preset '窗口跟随' --arg seconds=5
Get-Content -Raw -Encoding utf8 './process.rgs' | & $exe script stdin
```

执行命令会等待脚本结束，JSON 的 `Value` 是脚本返回值，`Debug.Output` 是 `print` 的输出。返回 `Err` 或产生未处理错误时，命令失败并提供 `Diagnostic`。错误位置包含文件、行和列；导入文件也保留各自的位置。

在另一个终端查询或取消：

```powershell
& $exe script list
& $exe script cancel '<OperationId>'
```

`Operations` 每项包含 `Id`、`Kind`、`State`、`Source`、`Started`，状态为 `running` 或 `cancelling`。操作完成后从列表移除。同一程序最多同时执行四个命令操作，运行中的标识必须唯一。动作命令和独立任务内的 `preset run` 也使用此列表和取消方式。

媒体处理、标注、动图编辑、贴图、长截图、剪贴板、识别与翻译等服务均可写入脚本后由这些命令执行。接口见[脚本服务](README.md)。

## 独立任务与会话

默认命令连接当前常驻程序；尚未运行时会在后台启动，不先显示主界面。需要独立执行、完成后退出时，在命令前加 `--task`：

```powershell
& $exe --task script file './process.rgs'
& $exe --task --exit-when-done script eval 'return 6 * 7;'
& $exe --task screenshot screen
```

选好区域后保存或复制，独立任务会在输出完成后退出。一次性任务从截图工具栏转入录制后，会等待录制输出完成；直接关闭且没有输出时，返回退出码 `130`。

| 前置参数 | 用途 |
| --- | --- |
| `--task` | 创建独立的静默任务，默认命令完成后退出 |
| `--keep-alive` | 配合 `--task`，命令完成后保留独立任务 |
| `--exit-when-done` | 配合 `--task`，命令完成后退出；这是默认行为 |
| `--reuse-core` | 配合 `--task`，优先使用已经运行的常驻程序；没有时创建独立任务 |
| `--session <sessionId>` | 将后面的命令发送到指定保留任务，与 `--task` 二选一 |

这些参数必须放在命令前。`--keep-alive` 与 `--exit-when-done` 控制同一选项，后出现的生效；不需要同时填写。

`help` 和 `--help` 只显示帮助，即使带上 `--task --keep-alive` 也不会创建或保留任务。

独立任务读取已有设置，但本次修改只影响该任务。截图不会触发自动保存、保存后复制或自动粘贴；仍可主动保存和复制。显式 `settings export` 或脚本保存文件仍会写入指定位置。`--reuse-core` 确实连接已有常驻程序时，按该程序的正常设置与保存方式执行，也不会因任务命令结束而自动关闭它。

需要持续操作窗口或从另一个终端停止长任务时，先用 `--task --keep-alive status` 获取 `SessionId`，再向同一会话发送命令。

```powershell
$task = & $exe --task --keep-alive status | ConvertFrom-Json
& $exe --session $task.SessionId status
& $exe --session $task.SessionId script file './next.rgs'
& $exe --session $task.SessionId exit
```

`exit` 退出当前连接的程序或会话。不带前置参数时退出常驻程序；`--session <sessionId> exit` 退出指定任务；`--task exit` 只退出本次新建的任务。若加上 `--reuse-core` 并连接了常驻程序，则会退出该常驻程序。需要保留贴图、翻译窗口或编辑器，或者分多条命令录制时，可用 `--keep-alive` 保留独立任务，操作完成后再退出。

## 配置

| 命令 | 用途 |
| --- | --- |
| `settings open` | 打开设置窗口 |
| `settings show` | 返回完整设置 |
| `settings get [pointer]` | 返回完整设置或指定值 |
| `settings set <pointer> <json>` | 修改指定值；值必须是 JSON |
| `settings import <path>` | 导入配置文件 |
| `settings export <path>` | 导出配置文件，`Output` 为绝对路径 |
| `settings reset [pointer]` | 恢复全部或指定项目的默认值 |

`get` 的结果在 `SettingsValue`，保留 JSON 本身的类型。路径使用 JSON Pointer，区分大小写：空字符串代表完整设置，`/Fps` 表示帧率，`/System/StartupHidden` 表示登录启动时隐藏主界面。名称中的 `~` 写成 `~0`，`/` 写成 `~1`；数组按从 0 开始的索引访问，`set` 末尾 `/-` 可追加数组项。

```powershell
& $exe settings get '/Fps'
& $exe settings set '/Fps' '30'
& $exe settings set '/System/StartupHidden' 'true'
& $exe settings export './settings.json'
& $exe settings import './settings.json'
& $exe settings reset '/Fps'
```

字符串值需要 JSON 双引号，例如 PowerShell 中写 `'"en"'`；布尔值写 `true` 或 `false`。配置修改会校验并通过正常保存流程生效；独立任务中的设置只作用于该任务。

## 输入通道

| 命令 | 用途 |
| --- | --- |
| `input list` | 查询全部监听通道 |
| `input listen <name> [options ...]` | 创建监听通道 |
| `input stop <name>` | 停止通道；名称不存在时不作处理 |

`input listen` 的每个选项必须跟一个值，同一选项不能重复：

| 选项 | 值与默认值 |
| --- | --- |
| `--transport <pipe\|tcp>` | 默认 `pipe` |
| `--endpoint <address>` | 管道名或回环 TCP 地址；省略时自动生成管道名或分配 TCP 端口 |
| `--format <framed\|mpegts>` | 默认 `framed` |
| `--width <pixels>`、`--height <pixels>` | `mpegts` 必填，1～16384；解码后单帧不超过 64 MiB |
| `--idle-timeout <seconds>` | 读取超时，整数 1～300，默认 30 |

通道名最长 128 字符，仅限 ASCII 字母、数字、点、横线和下划线。最多同时监听 16 个通道，不允许重名。管道名也使用上述字符和长度限制。TCP 仅监听回环地址，地址可写成 `127.0.0.1:0` 或端口号；`0` 表示自动分配。

```powershell
& $exe input listen camera --transport tcp --endpoint 127.0.0.1:0 --format framed
& $exe input list
& $exe input stop camera
```

这三种命令返回 `Inputs` 与 `InputClock`。`Inputs` 中的 `Endpoint` 是实际地址；每项还有 `Name`、`Transport`、`Format`、`Connected`、`Packets`、`Timestamp`、`Error` 和 `Status`。`InputClock` 是接收端单调时钟的秒数。

`mpegts` 需要 FFmpeg。发送格式和脚本读取方式见[外部参数与输入通道](input-channels.md)。

## 脚本调试

| 命令 | 用途 |
| --- | --- |
| `debug run <file> <name> <true\|false>` | 运行指定预设；最后一项为 `true` 时在入口暂停 |
| `debug breakpoints <lines>` | 设置当前脚本的断点行号 |
| `debug breakpoints <file> <lines>` | 设置指定源文件的断点行号 |
| `debug pause` | 请求暂停 |
| `debug continue` | 继续执行 |
| `debug step` | 单步执行 |
| `debug stop` | 停止调试脚本 |

行号从 1 开始，用逗号分隔；传入空字符串可清除对应断点。调试文件建议使用绝对路径。`debug run` 的名称传入空字符串时选择文件中的第一个预设。

```powershell
& $exe debug run 'C:/Captures/capture.rgpreset' '三秒录制' true
& $exe debug breakpoints '8,12'
& $exe debug continue
& $exe status
& $exe debug stop
```

`status` 中的 `Debug` 包含 `Paused`、`Finished`、`State`、`Line`、`Column`、`Variables`、`Output`、`Error`、`Source`、`Frames`、`ErrorDiagnostic` 和 `StackFrames`。未启动调试时不提供调试快照。没有活动调试器时，暂停、继续、单步和设置断点不会启动新的调试。

编辑器接入语言服务时可使用 `--language-server`，通过标准输入和标准输出交换语言服务消息；此模式不输出普通命令回复。

## JSON 回复与退出码

普通命令向标准输出写入一条 JSON 回复，字段名称区分大小写。`script`、`actions run`、`screenshot` 和 `start` 各自等待所执行的操作结束。需要查看进度时，可在另一个终端查询 `status`。

| 字段 | 含义 |
| --- | --- |
| `Success` | 本次命令是否成功 |
| `State` | 当前状态，如 `idle`、`selecting`、`recording`、`processing`、`waiting`、`configuring`、`closing` |
| `Message` | 说明或错误消息；`windows`、`presets` 的列表也放在这里 |
| `Region` | `X`、`Y`、`Width`、`Height`。截图回复为本次选区，`status` 等状态回复为当前区域 |
| `Output`、`Outputs` | 截图和已保存的预设录制回复分别给出本次第一条路径和全部路径。`settings export` 的 `Output` 为本次导出的绝对路径。`status` 等状态回复显示最近输出 |
| `Captured`、`Encoded` | 当前采集帧数、编码帧数 |
| `CapturedFps`、`ElapsedSeconds` | 实际采集帧率、已录制秒数 |
| `Preset`、`PresetState`、`PresetError` | 当前预设、状态和错误 |
| `ParameterSession` | 用于 `preset params` 的运行会话标识 |
| `Streams` | 多路录制状态，各项含 `Name`、`State`、`Captured`、`Encoded`、`Elapsed`、`Path`、`Error` |
| `Diagnostic` | 脚本错误的位置与详情 |
| `Debug` | 调试快照 |
| `Inputs`、`InputClock` | 输入通道及其时钟，仅输入通道命令提供 |
| `SettingsValue` | 查询得到的配置 JSON 值 |
| `Actions` | 动作类型或已配置动作信息 |
| `Operations` | 当前执行中的命令操作 |
| `OperationId` | 本次脚本、动作或独立任务内的预设操作标识 |
| `SessionId` | 本次创建的独立任务会话标识 |
| `Value` | 脚本返回值 |

字段可能为空或为默认值；查询版本或帮助时无需读取录制字段。`--version` 的版本文本位于 `Message`。

普通命令成功退出码为 `0`，失败为 `1`，取消为 `130`。Windows 管理员启动提示被取消时可能返回 `1223`。未知命令或错误参数会返回失败及帮助或错误消息。按 Ctrl+C 可取消当前等待的命令；取消不会自动退出复用的常驻程序。

脚本 `Value` 中，普通对象、数组、字符串、有限数值、布尔值和 `null` 保持对应 JSON 类型。特殊值使用以下表示：

| 脚本值 | JSON 表示 |
| --- | --- |
| `Ok(value)` | `{"tag":"ok","value":...}` |
| `Err(error)` | `{"tag":"err","error":...}` |
| `Some(value)` | `{"tag":"some","value":...}` |
| `None` | `{"tag":"none"}` |
| `Bytes` | `{"type":"bytes","base64":"..."}` |
| `Map` | `{"type":"map","entries":[...]}` |
| `Set` | `{"type":"set","values":[...]}` |

返回值最多 256 KiB，嵌套深度最多 64 层。函数和仍打开的文档、贴图或捕获会话等对象不能直接返回，应返回它们的数据或先保存文件。循环引用、非有限数值和不支持的值会使命令失败，`Value.error.code` 分别可为 `cycle`、`nonFinite`、`unsupported`；超过限制为 `tooLarge`。脚本服务自己的 `Err` 与命令执行失败需结合 `Success`、`Diagnostic` 和 `Value` 一起判断。
