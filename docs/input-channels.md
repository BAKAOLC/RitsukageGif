# 外部参数与输入通道

## 从命令行传入参数

启动预设时，用 `--arg 名称=值` 传入参数：

```powershell
$exe = 'C:/Apps/RitsukageGif/RitsukageGif.exe'
& $exe preset run './capture.rgpreset' --arg x=100 --arg y=200 --arg seconds=10
```

一个文件包含多个预设时，在文件名后指定预设名称：

```powershell
& $exe preset run './capture.rgpreset' '窗口跟随' --arg x=100
```

数值、`true`、`false`、`null` 按对应类型读取，其他内容作为字符串。脚本通过 `params` 取值：

```rgscript
const x = params.number('x', 0);
const y = params.number('y', 0);
scene.move('target', x, y, 320, 180);
```

| 调用 | 返回值 |
| --- | --- |
| `params.get(name, fallback = null)` | 参数值；不存在时返回默认值 |
| `params.number(name, fallback = 0)` | 数值；不存在或类型不符时返回默认值 |
| `params.string(name, fallback = '')` | 字符串；不存在或类型不符时返回默认值 |
| `params.boolean(name, fallback = false)` | 布尔值；不存在或类型不符时返回默认值 |
| `params.snapshot()` | 全部参数的对象副本 |
| `params.revision()` | 当前修订号 |
| `params.state()` | `{revision, values}` |
| `params.waitChanged(revision)` | 等待后返回当前修订号；需与旧值比较判断是否变化；脚本取消会结束等待 |

运行期间也可以更新参数。先执行 `status`，取响应中的 `ParameterSession`，再提交新值：

```powershell
& $exe status
& $exe preset params '<ParameterSession>' x=240 y=120
```

脚本中等待更新并移动图层：

```rgscript
let state = params.state();
while (status().recording) {
    scene.move('target',
        state.values.x ?? 0, state.values.y ?? 0, 320, 180);
    params.waitChanged(state.revision);
    state = params.state();
}
```

## 接收外部画面和 JSON

输入通道适合持续接收其他程序产生的画面及位置数据。先启动监听，再连接发送端：

```powershell
& $exe input listen camera --transport tcp --endpoint 127.0.0.1:0 --format framed
& $exe input list
```

响应中的 `Inputs` 包含实际 `Endpoint`。端口 `0` 代表自动分配端口，发送端要使用响应里的实际端口。`InputClock` 是接收端单调时钟的秒数。

| 选项 | 含义与默认值 |
| --- | --- |
| `--transport pipe\|tcp` | 默认 `pipe`；TCP 只监听本机回环地址 |
| `--endpoint` | 管道名或 TCP 地址；默认自动生成管道名，TCP 默认 `127.0.0.1:0` |
| `--format framed\|mpegts` | 默认 `framed`，接收 BGRA 画面与 JSON；`mpegts` 接收视频流 |
| `--width`、`--height` | `mpegts` 必填的解码尺寸 |
| `--idle-timeout` | 读取超时秒数，1～300，默认 30 |

通道名由 ASCII 字母、数字、点、横线和下划线组成，最长 128 字符。最多同时监听 16 个通道，每个通道同时接收一个发送端。断开后可以重连；结束监听使用：

```powershell
& $exe input stop camera
```

可以用附带的 Python 发送器生成测试画面和位置 JSON，把端口替换为监听响应中的值：

```powershell
python ./examples/input-publisher.py 127.0.0.1:49152
```

然后运行 [external-channel.rgpreset](../examples/external-channel.rgpreset)。按 Ctrl+C 停止发送器。

## 在布局中引用画面

```rgpreset
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

`source channel` 引用已有通道，不会启动监听。

| 选项 | 含义与默认值 |
| --- | --- |
| `delay 0.1;` | 取帧延迟，0～5 秒，默认 0.1；增大可缓冲传输波动，也会增加跟随延迟 |
| `staleAfter 5;` | 画面过期秒数，必须大于零，默认 5 |
| `holdLast false;` | 默认 `false`；超时或断开时用 `missing` 填充，设为 `true` 则保留已有末帧 |

通道图层支持裁剪、缩放和效果。`scene.playback` 可以切换 `paused` 与 `holdLast`，不能设置通道的 `position`、`speed` 或 `loop`。

## 在脚本中读取 JSON

| 调用 | 返回值 |
| --- | --- |
| `inputs.names()` | 已知通道名数组 |
| `inputs.read(name)` | 最新快照 `{value, version, timestamp, connected}`；通道不存在时为 `null` |
| `inputs.wait(name, afterVersion = -1, timeout = 30)` | 等待更新并返回快照；超时返回 `null`；`timeout` 为 0～86400 秒 |

`value` 是解析后的 JSON，尚未收到 JSON 时为 `null`。`version` 可判断是否有更新，`timestamp` 是接收端单调时钟秒数。断连后可读取最后数据，但 `connected` 为 `false`；重连会清空旧数据。

```rgscript
let version = -1;
while (status().recording) {
    const packet = inputs.wait('camera', version, 0.2);
    if (packet == null) continue;
    version = packet.version;
    const target = packet.value?.target;
    if (packet.connected && target != null) {
        scene.crop('detail', target.x, target.y, target.width, target.height);
    }
}
```

JSON 总是读取最新值，不受图层 `delay` 影响。需要与画面逐帧配对时，让发送端在 JSON 中提供帧编号或时间戳。

## 编写 framed 发送端

连接后先读取服务端的 16 字节握手，再连续发送包头与数据。数值均为小端序。

握手内容：

| 偏移 | 类型 | 内容 |
| --- | --- | --- |
| 0 | 4 字节 ASCII | `RGIH` |
| 4 | uint32 | 版本 `1` |
| 8 | float64 | 接收端单调时钟秒数 |

每个包有 32 字节包头，后面紧跟数据：

| 偏移 | 类型 | 内容 |
| --- | --- | --- |
| 0 | 4 字节 ASCII | `RGIP` |
| 4 | uint8 | 版本 `1` |
| 5 | uint8 | `1` 为 BGRA，`2` 为 UTF-8 JSON |
| 6 | uint16 | `0` |
| 8 | uint32 | 数据字节数 |
| 12、16 | uint32 | 画面宽、高；JSON 均填 `0` |
| 20 | uint32 | `0` |
| 24 | float64 | 接收端时间轴上的时间戳；`0` 使用接收时间 |

BGRA 按从上到下、每行 `width * 4` 字节紧密排列。宽高各不超过 16384，单帧最多 64 MiB。JSON 最多 1 MiB。

非零时间戳必须有限、非负，不能超过接收端当前时刻五秒。画面和 JSON 各自按时间戳严格递增；两种包可以使用相同时间戳。使用握手时间与发送端的相对时间计算发送时间戳，写法见 [input-publisher.py](../examples/input-publisher.py)。

## 推送 MPEG-TS 视频

启动监听时指定输出尺寸：

```powershell
& $exe input listen video --transport tcp --endpoint 127.0.0.1:0 --format mpegts --width 320 --height 180
```

在另一个终端推送视频，把端口替换为响应中的值：

```powershell
ffmpeg -re -f lavfi -i "testsrc2=size=320x180:rate=30" -an -c:v mpeg2video -f mpegts "tcp://127.0.0.1:49153"
```

录制端使用 `source channel "video";`。MPEG-TS 直接发送视频字节，不发送 `RGIP` 包头，也不读取 `RGIH` 握手；接收端需要 FFmpeg。多个源需要精确时间对齐时，使用带共同时间基准的 framed 时间戳。
