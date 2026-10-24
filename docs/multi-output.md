# 同步多路录制

`record.startMany` 同时启动多个输出。每路可以选择相同画面或独立预设，使用自己的帧率、缩放、格式和文件路径。完整范例见 [multi-recording.rgpreset](../examples/multi-recording.rgpreset)。

## 同画面，不同格式

以下片段放在已有预设的 `script` 内：

```rgscript
record.startMany([
    {name: 'gif', path: './captures/game.gif',
        options: {format: OutputFormat.Gif, fps: 30, scale: 1, copy: false}},
    {name: 'webp', path: './captures/game.webp',
        options: {format: OutputFormat.WebP, fps: 15, scale: 2, copy: false}}
]);
wait(5);
record.stop();
```

开始前创建输出目录，例如 `files.createDirectory('./captures')?;`。相对路径以入口预设所在目录为基准。文件必须不存在，不覆盖已有录制；已有目标会在开始前拒绝整组。重复运行时换路径或先用文件服务移动旧文件。文件名可用 `{preset}`、`{title}`、`{process}`、`{date}`、`{time}`、`{timestamp}`、`{id}`、`{sequence}`、`{ext}`；目录部分不展开占位符。同一组使用共同时间戳，每路有自己的序号，必要时自动追加对应格式的扩展名。

`name` 是脚本中引用这一路的名称。`options` 沿用 `start(options)` 的字段：`fps`、`scale`、`format`、`quality`、`ffmpeg`、`cursor`、`sound`、`copy`。省略字段时继承所选预设的录制设置，再回退到应用设置。`scale 2` 表示宽高各缩小一半。

## 不同源或不同布局

每个输出可用 `presetFile` 和 `presetName` 选择画面定义：

```rgscript
record.startMany([
    {name: 'original', path: './captures/original.gif'},
    {name: 'camera', path: './captures/camera.gif',
        presetFile: './sources/luastg-camera.rgpreset'},
    {name: 'context', path: './captures/context.gif',
        presetFile: './sources/luastg-screen.rgpreset'}
]);
```

- 不指定两者：使用当前预设画面。
- 只指定 `presetName`：从当前文件选择同名预设。
- 指定 `presetFile`：从该文件选择；文件有多个预设时还要指定 `presetName`。

被引用预设只提供窗口匹配、捕获来源、区域、布局和录制设置，不执行其中的 `script` 或 `state`。多路入口统一控制开始和停止，文件路径由每个输出的 `path` 决定。

此方式可同时录制两个窗口，或把同一个窗口保存为完整画面、局部镜头和合成画面。`source window` 请求窗口内容；`source screen` 记录窗口所在位置的屏幕上下文，包括遮挡。来源丢失时各区域继续使用自己的 `missing` 规则。

## 每路单独改变画面

在原有 `scene` 方法后加 `For`，第一个参数为输出名称：

```rgscript
scene.setFor('camera', 'detail', {
    crop: {x: 100, y: 80, width: 240, height: 180},
    placement: {x: 16, y: 16, width: 240, height: 180}
});
scene.animateFor('camera', 'detail', 80, 20, 240, 180, 1, Easing.EaseInOut);
```

可用方法为 `setFor`、`getFor`、`moveFor`、`cropFor`、`animateFor`、`visibleFor`、`opacityFor`、`effectsFor`、`playbackFor`。其余参数与对应的单画面方法相同。`setFor` 一次更新裁剪、位置、透明度或可见性，在后续采集帧生效；动画不阻塞脚本。

普通 `scene.*` 更新当前预设及由它派生的输出。需要独立控制时始终使用 `For`，避免后续普通更新覆盖某一路的同一属性。引用其他预设的输出使用自己的区域名称。

这些调用也可以放入外部参数循环：收到参数后调用 `scene.setFor`，即可只让一路镜头跟随，其他输出保持不动。见[外部参数](editor-and-cli.md#外部参数与跟随捕获)。

## 进度与结果

`record.outputs()` 和 `status().streams` 返回每路状态：

| 字段 | 含义 |
| --- | --- |
| `name`、`path` | 输出名称与目标路径 |
| `state` | `recording`、`encoding`、`complete`、`failed` 或 `cancelled` |
| `captured`、`encoded` | 已捕获和已处理的帧数 |
| `elapsed` | 录制时长，秒 |
| `error` | 失败信息，正常时为 `null` |

```rgscript
record.stop();
for (const output of record.outputs()) {
    if (output.error != null) {
        print(output.name, output.error);
        continue;
    }
    const file = files.info(output.path)?;
    print(output.name, file.size / 1000000, 'MB');
}
```

`record.stop()` 停止所有路的采集，并等待编码和保存完成。`record.cancel()` 取消整个录制组并丢弃未完成的输出。脚本正常退出会结束录制，脚本失败或取消会取消录制。停止完成后，输出文件可用于后续文件和图像处理。

部分路失败时，停止仍返回并保留成功文件，应逐路检查 `error`；全部失败时，停止会报告录制错误。只有目标成功保存后才会进入输出列表。

自动复制将所有有效 `copy` 设置为 `true` 的成功输出一次性写入剪贴板文件列表。每路优先使用 `options.copy`，然后继承来源预设；两者均未设置时默认复制。可给所有路明确设置 `copy: false`。

`status().outputs` 列出成功保存的文件，可继续交给 `media.process` 或 `media.processMany` 生成缩略图、封面和较小的分享版本。

## 数量限制与性能

一次允许 1～8 路，名称与目标路径不能重复。各路按同一时间轴采集，可以使用不同帧率。窗口画面的呈现时间由各目标程序及系统合成器决定。编码器使用各自的画面时间戳，避免因处理落后把动画整体放慢。文件动画在预热后按共享录制时钟取样；有限视频文件按 PTS 选择录制时刻对应画面；实时流的同步需要发送端时间戳与接收端时钟对齐，详见[媒体源的时间与播放控制](media-sources.md#播放控制)。

两路及以上录制时，同一窗口和光标配置共用一个后台采集源。首帧准备完成后开始计时，各输出在自己的采样时刻读取最近完成的窗口画面；某个窗口采集变慢不会阻塞其他窗口。每个窗口只保留当前显示帧和待取帧，不积压历史画面。超过 250 毫秒或两个采集周期（取较长者）仍未完成采集时，使用该区域的 `missing` 规则；窗口恢复后继续更新。

固定的屏幕区域也可独立后台采集：区域须使用绝对坐标、关闭跟随（`follow false`），且没有脚本裁剪覆盖。相同区域、输出尺寸和光标配置共用画面。显示器布局或 DPI 变化后重新准备捕获源；暂时不可用时按 `missing` 规则显示，并间隔重试。

跟随窗口或由脚本调整的屏幕区域继续同步捕获，以保证使用当前坐标。后台窗口源和屏幕源各最多八个，超过时使用同步捕获，不限制可录制的区域数量。单路写盘变慢时只跳过该路来不及处理的采样点，其他路继续录制；时间戳保持真实间隔，停止时等待已采集帧处理完毕。

多路会增加采集、缩放、编码、内存和磁盘压力。先用适中的尺寸与帧率，再按实际处理速度增加路数。GIF 原生编码可边录边编码；其他编码后端可能仍需停止后完成输出。各路独立报告处理结果，结束后应检查每一路的 `error`。

可用捕获源取决于平台后端、窗口类型和权限。
