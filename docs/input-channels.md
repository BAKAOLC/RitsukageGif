# 外部输入通道

输入通道接收其他程序持续发送的画面和 JSON 数据，预设通过通道名称引用。使用前须通过 `input listen` 启动监听；`source channel` 仅声明数据源。

## 启动、查看和关闭

```powershell
$exe = 'C:/Apps/RitsukageGif/RitsukageGif.exe'
& $exe input listen camera --transport tcp --endpoint 127.0.0.1:0 --format framed
& $exe input list
& $exe input stop camera
```

`listen` 的响应 `Inputs` 列表包含实际 `Endpoint`；端口 0 表示自动选择空闲端口。复制响应中的实际地址供发送端连接。`input list` 还返回连接状态、接收包数、时间戳和最近错误。`InputClock` 是程序的当前单调时钟秒数，不是日期时间。

| 选项 | 含义 |
| --- | --- |
| `--transport pipe\|tcp` | 默认 `pipe`；管道限制为当前用户，TCP 只监听回环地址 |
| `--endpoint` | 管道名称或 TCP 地址；省略时生成管道名，TCP 则使用 `127.0.0.1:0` |
| `--format framed\|mpegts` | 默认 `framed`，支持 BGRA 和 JSON；`mpegts` 接收 MPEG-TS 视频字节流 |
| `--width`、`--height` | MPEG-TS 解码输出尺寸，使用该格式时必须提供 |
| `--idle-timeout` | 读取超时秒数，1～300，默认 30 |

名称使用 ASCII 字母、数字、点、横线或下划线，最长 128 字符。同名监听不能重复启动，每个通道同时接收一个发送端。连接断开后监听仍可接受重连；`input stop` 才关闭监听。最多 16 个通道。

## 发送生成画面

[Python 发送器范例](../examples/input-publisher.py) 使用 Python 标准库生成画面和位置数据：

```powershell
python ./examples/input-publisher.py 127.0.0.1:49152
```

把 `49152` 换成监听响应中的端口。默认以每秒 30 帧发送 320×180 BGRA 动画及方块位置 JSON，按 Ctrl+C 停止。可用 `--mode frames` 或 `--mode json` 只发一种数据；`--seconds 10` 在十秒后结束，`--width`、`--height`、`--fps` 调整生成画面。

启动发送器后运行 [external-channel.rgpreset](../examples/external-channel.rgpreset)。它同时录制完整画面与依据 JSON 坐标裁剪的局部画面，默认十秒。范例等待首条位置 JSON，并缓冲 0.1 秒后开始录制。默认发送器先发送画面再发送 JSON；使用 `--mode frames` 时，需要移除等待 JSON 的条件，并在画面持续发送至少 `delay` 秒后开始录制。录制结束不会关闭外部监听或发送器；需要时分别停止。

## 在布局中使用

```text
region "camera" {
    source channel "camera" {
        delay 0.1;
        staleAfter 5;
        holdLast false;
    };
    place(0, 0, 320, 180);
    missing color "#303030";
}
```

`delay` 是取帧延迟，范围 0～5 秒，默认 0.1。采集会选择不晚于目标时间的最近一帧，未来帧不会提前显示。适度延迟给传输和解码留出缓冲，但也会增加跟随延迟。

`staleAfter` 是帧过期秒数，必须大于零，默认 5。`holdLast false` 时，超时、未收到可用帧或断连会使用 `missing` 填充；`holdLast true` 可以保留已有末帧，即使过期或断连，也不会提前显示未来帧。

通道图层可裁剪、缩放、合成和使用局部效果。`scene.playback` 对它支持 `paused` 与 `holdLast`；不能对持续输入设置文件式 `position`、`speed` 或 `loop`。

## 在脚本中读取 JSON

```rgscript
print(inputs.names());
const packet = inputs.read('camera');
if (packet != null && packet.connected) {
    print(packet.version, packet.timestamp, packet.value);
}
```

`inputs.names()` 返回已知通道名，包括仍保留数据的断连通道。`inputs.read(name)` 返回 `{value, version, timestamp, connected}`；不存在时为 `null`。尚未收到 JSON 时 `value` 为 `null`。断连后可读取最后数据，但 `connected` 为 `false`；重连会清空上一连接的内容。

`version` 用于判断是否有更新；`timestamp` 使用接收端单调时钟秒数。`value` 是已经解析的 JSON，可以按普通对象读取。返回的数据可在脚本中独立修改，不影响后续读取。

需要等待更新时调用 `inputs.wait(name, afterVersion = -1, timeout = 30)`。该调用等待输入更新并返回快照，超时返回 `null`；脚本取消时终止等待并传播取消信号。`timeout` 为 0～86400 秒，传入上次 `version` 等待下一次更新，默认 -1 获取首个可用快照。连接状态变化也会唤醒等待。

JSON 接口读取最新快照，不按图层的 `delay` 回放历史数据。需要画面与元数据严格配对时，由发送端提供帧编号或时间戳，并在脚本中根据编号或时间戳建立对应关系。局部跟随范例使用最新 JSON 更新裁剪位置。

## Framed 协议

TCP 与命名管道使用相同字节协议，所有数值为小端序。连接后服务端先发 16 字节握手：

| 字节偏移 | 类型 | 值 |
| --- | --- | --- |
| 0 | 4 字节 ASCII | `RGIH` |
| 4 | uint32 | 版本 1 |
| 8 | float64 | 接收端当前单调时钟，秒 |

随后发送端连续发送 32 字节包头及紧随其后的数据，不需要等待确认包：

| 字节偏移 | 类型 | 值 |
| --- | --- | --- |
| 0 | 4 字节 ASCII | `RGIP` |
| 4 | uint8 | 版本 1 |
| 5 | uint8 | 1 = BGRA，2 = UTF-8 JSON |
| 6 | uint16 | 保留，必须为 0 |
| 8 | uint32 | 数据字节数 |
| 12、16 | uint32 | 宽、高；JSON 必须均为 0 |
| 20 | uint32 | 保留，必须为 0 |
| 24 | float64 | 单调时钟时间戳，秒；0 表示使用接收时间 |

BGRA 从上到下紧密排列，每行 `width * 4` 字节，总长度必须为 `width * height * 4`，单帧不超过 64 MiB，宽高各不超过 16384。JSON 必须为合法 UTF-8 JSON，最多 1 MiB，最大嵌套 64 层。

非零时间戳须有限、非负，不能超过接收端当前时刻五秒；画面包和 JSON 包各自的时间戳必须严格递增，重复或乱序包不会发布。可以让同一帧的画面和 JSON 使用相同时间戳，因为两种包分别计时。

Python 范例以握手时刻为基准，将自己的相对单调时间映射到接收端时间轴。映射误差包括握手传输延迟。时间戳为 0 时使用接收时间，发送前的排队延迟会影响画面时序。

每通道最多缓存 120 帧、64 MiB，所有通道缓存合计最多 256 MiB。达到限制时淘汰旧帧；录制速度低于输入速度时，部分输入帧可能不会被录制。

## 使用 FFmpeg 推送 MPEG-TS

指定解码尺寸并启动监听：

```powershell
& $exe input listen video --transport tcp --endpoint 127.0.0.1:0 --format mpegts --width 320 --height 180
```

在另一个终端发送生成画面，将示例端口替换为监听响应中的实际端口：

```powershell
ffmpeg -re -f lavfi -i "testsrc2=size=320x180:rate=30" -an -c:v mpeg2video -f mpegts "tcp://127.0.0.1:49153"
```

录制端用 `source channel "video"` 引用它；发送端 Ctrl+C 结束。该格式没有 `RGIH` 握手，也不使用 RGIP 包头，直接发送 MPEG-TS 字节；接收端需要 FFmpeg。

接收端以首个解码帧的到达时间为起点，再用 PTS 差值映射后续帧。该方式保留流内的相对时间关系，无法恢复不同设备的原始绝对时间或实现多机时钟同步。需要多个持续源对齐时，发送端应使用共同时间基准，优先通过 framed 协议明确传入时间戳。
