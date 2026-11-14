# 多路录制输出

`record.startMany(outputs)` 同时录制 1～8 路输出。每路可以使用不同格式、帧率、缩放和画面布局。

## 同一画面输出两种格式

下面的完整预设将同一块屏幕区域分别保存为 GIF 和 WebP：

```rgpreset
preset "双格式输出" {
    layout(800, 450) {
        region "画面" {
            source screen;
            area screen(0, 0, 640, 360);
            place(0, 0, 800, 450);
        }
    }
    script {
        record.startMany([
            {
                name: "gif",
                path: "./captures/{timestamp}-full.gif",
                options: { format: OutputFormat.Gif, fps: 24, scale: 1, copy: false }
            },
            {
                name: "detail",
                path: "./captures/{timestamp}-detail.webp",
                options: { format: OutputFormat.WebP, fps: 16, scale: 2, copy: false }
            }
        ]);
        wait(3);
        record.stop();
        for (const output of record.outputs()) {
            print(output.name, output.state, output.path, output.error);
        }
    }
}
```

修改 `area screen(...)` 可选择其他范围。`path` 的相对路径以入口预设所在目录为基准，输出目录会自动创建。

同组输出使用相同时间戳。文件名支持 `{preset}`、`{title}`、`{process}`、`{date}`、`{time}`、`{timestamp}`、`{sequence}`、`{id}`、`{ext}`；这些占位符不在目录部分展开。

输出名称不能重复，目标路径也不能重复。多路录制不会覆盖已有文件；任一目标已存在时，不开始这一组录制。示例中的时间戳可避免重复运行时使用固定文件名。文件名末尾与输出格式不匹配时，会追加该格式的扩展名。

## 配置每一路

每个数组元素包含以下字段：

| 字段 | 含义 |
| --- | --- |
| `name` | 输出名称，用于状态查询和独立控制画面 |
| `path` | 输出文件路径 |
| `options` | 本路的录制选项，可省略 |
| `presetName` | 使用的预设名，可省略 |
| `presetFile` | 包含该预设的文件，可省略 |

`options` 支持 `fps`、`scale`、`format`、`quality`、`cursor`、`sound`、`ffmpeg`、`copy`，取值与[单路录制](recording-reference.md#临时录制选项)相同。帧率、缩放和编码选项省略时，先使用本路所选预设的 `record`，再使用应用当前设置；`copy` 的规则见下文。

不指定 `presetFile` 和 `presetName` 时，使用当前预设的画面。只指定 `presetName` 时，从当前文件选择同名预设；指定 `presetFile` 时，从该文件选择。文件内有多个预设时，必须同时给出 `presetName`。

例如，下面是一条使用其他预设的输出定义，可放入 `startMany` 数组：

```rgscript
{
    name: "camera",
    path: "./captures/{timestamp}-camera.webp",
    presetFile: "./views.rgpreset",
    presetName: "窗口细节",
    options: { format: OutputFormat.WebP, fps: 24, copy: false }
}
```

被引用的预设提供窗口匹配、来源、范围、布局和录制选项，不执行它自己的 `script` 或状态机。输出位置由本路 `path` 决定，不使用被引用预设的 `save`。

这可以用于同时录制多个窗口，或将同一个窗口分别输出为完整画面、局部放大画面和拼接画面。每个区域的来源丢失时，继续使用该区域的 `missing` 设置。

## 单独修改某一路的画面

为 `scene` 方法加上 `For`，并把输出名称放在第一个参数：

```rgscript
scene.setFor("detail", "画面", {
    crop: { x: 100, y: 80, width: 320, height: 180 },
    placement: { x: 16, y: 16, width: 768, height: 418 }
});
scene.animateFor("detail", "画面", 80, 40, 640, 360, 1, Easing.EaseInOut);
```

将这段放在上面完整示例的 `record.startMany(...)` 与 `wait(3)` 之间，只会改变 `detail` 这路画面。

可用方法包括 `setFor`、`getFor`、`moveFor`、`cropFor`、`animateFor`、`visibleFor`、`opacityFor`、`effectsFor`、`playbackFor`；除第一个输出名参数外，其余参数与对应的 `scene` 方法相同。

普通 `scene.*` 调用会同时影响当前预设和由它派生的输出。只想修改一路时使用 `For`；之后再用普通方法修改同一属性，会覆盖这一路的对应设置。引用其他预设时，区域名称应使用那个预设中的 `region` 名称。

## 停止与读取结果

`record.stop()` 停止所有路的采集，并等待编码和保存完成。`record.cancel()` 取消整组录制。脚本正常结束时会停止录制，失败或取消脚本时会取消录制。

`record.outputs()` 返回各路状态；`status().streams` 也可以读取同样的信息。

| 字段 | 含义 |
| --- | --- |
| `name`、`path` | 输出名称和文件路径 |
| `state` | `recording`、`encoding`、`complete`、`failed` 或 `cancelled` |
| `captured`、`encoded` | 已捕获和已处理的帧数 |
| `elapsed` | 录制时长，单位为秒 |
| `error` | 错误信息；正常时为 `null` |

部分输出失败时，成功文件仍保留，应逐路检查 `state` 和 `error`；所有输出都失败时，停止操作会报告错误。录制成功结束后，`status().outputs` 可读取保存的文件路径。

每路的 `copy` 决定是否自动复制输出，优先使用本路 `options.copy`，其次是来源预设的 `record.copy`，均未设置时，常驻程序默认 `true`，独立任务默认 `false`。成功且需要复制的输出会一并加入剪贴板文件列表。

多路录制目前使用 SDR 输出，不支持 HDR 或 SDR/HDR 双输出。使用系统来源选择器的平台，`startMany` 只能用于纯媒体布局，不能使用桌面或窗口来源。
