# 文件与视频作为录制源

布局中的图层可以从文件读取画面：

```text
layout(640, 480) {
    region "animation" {
        source file "assets/animation.gif";
        place(0, 0, 640, 480);
    }
}
```

文件路径相对于预设文件。静态 PNG、JPEG、WebP 会一直显示；GIF、APNG、动画 WebP 按原始帧延时播放，默认循环。图像图层支持裁剪、布局和合成，可与窗口、屏幕图层混合使用。

源图像的所有解码帧最多占用 512 MiB，超出限制时加载失败。分辨率和动画长度都会影响解码内存；解码后的内存用量与文件的压缩大小不同。

## FFmpeg 视频和流输入

```text
region "video" {
    source ffmpeg "assets/clip.mp4" {
        loop true;
        speed 1;
        position 0;
        holdLast true;
    };
    place(0, 0, 640, 480);
}
```

`file` 和 `ffmpeg` 都允许附加选项块。`speed` 是播放倍率，`position` 是起始秒数；`loop false` 时，`holdLast true` 保留末帧，否则结束后使用图层的缺失源填充。

FFmpeg 还支持 `live true;`、`size(640, 480);` 和 `staleAfter 5;`。持续输入可显式设置尺寸，以免为了探测尺寸而额外读取输入。`staleAfter` 指没有新帧时的失效秒数。有限视频文件始终需要 FFmpeg 和 ffprobe，即使已经声明尺寸，也会读取文件时间轴信息。实时 FFmpeg 输入在明确尺寸后可以只使用 FFmpeg，自动探测尺寸时还需要 ffprobe。外部 MPEG-TS 通道使用明确尺寸，不依赖 ffprobe；缺少所需依赖会报错。

源暂时无法读取时显示图层的缺失源填充，并按退避间隔重试，最长间隔 30 秒。正常播放结束的有限视频不会因此重新启动。

可直接运行 [file-animation.rgpreset](../examples/file-animation.rgpreset)：读取附带的四帧小动画，左右并排显示原图与处理后的图层，录制四秒后生成帧总览。范例仅使用文件图层。

## 播放控制

`source file` 的 GIF、APNG 和动画 WebP 在预热期间冻结播放位置，预热结束后按共享录制时钟和原始帧延时选择画面。不同输出帧率可以从同一动画时间轴取样。

有限视频文件使用原始 PTS 选择录制时间对应的画面，解码进度不会作为播放时钟。预热结束后与其他文件动画共用录制时间轴；输出帧率决定采样时刻，因此不同输出可能选取不同的输入帧。

实时输入的同步依赖时间戳和时钟对齐。外部通道可传入接收端时间轴上的时间戳；MPEG-TS 通道以首个解码帧的到达时间映射 PTS，各发送端的启动和传输延迟会影响它们之间的时间偏移，见[外部输入通道](input-channels.md)。

脚本可以单独控制某个媒体图层：

```rgscript
scene.playback('animation', {position: 0.5, speed: 0.5});
scene.playback('animation', {paused: true});
scene.playback('animation', {paused: false, loop: false, holdLast: true});
```

`position` 是源动画中的秒数，指定后提交跳转，在后续采集帧中体现；`speed` 默认 1，`file` 支持 0～100，`ffmpeg` 支持 0～16，0 表示冻结。声明选项和运行时修改使用相同范围。暂停、继续或变速时会保留当前播放位置，不会自动跳回开头。`loop` 控制循环；非循环播放结束后，`holdLast` 决定是否保留最后一帧。

多路录制使用 `scene.playbackFor(output, layer, options)`，其余参数相同。也可以在 `scene.set` / `setFor` 的 `playback` 字段中提交，与裁剪、布局和效果一起更新。`scene.get` / `getFor` 的 `playback` 字段返回当前播放状态；非媒体图层为 `null`。FFmpeg 源的跳转需要输入支持定位，并在目标帧解码完成后生效；实时流不支持随机定位。
