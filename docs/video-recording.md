# 音视频录制

从托盘菜单打开视频录制窗口。也可以在设置的全局动作中添加“打开视频录制”，绑定快捷键或托盘入口。命令行入口为：

```powershell
& 'C:/Apps/RitsukageGif/RitsukageGif.exe' video-recorder
```

## 格式与依赖

视频录制需要 FFmpeg，并且该 FFmpeg 构建包含相应编码器。窗口会检测可用格式；缺少所需编码器时不能开始对应录制。

| 格式 | 视频编码 | 启用声音时的音频编码 |
| --- | --- | --- |
| MP4 | SDR：H.264（libx264）；HDR：HEVC Main10（libx265） | AAC |
| WebM | VP9（libvpx-vp9）；HDR 需要 10 位 Profile 2 | Opus（libopus） |

视频在录制过程中编码，沿用捕获设置的 SDR、HDR 或同时输出模式。HDR 使用 10 位 BT.2020 / PQ，保留源画面的绝对亮度；SDR 使用已选的色调映射方式。HDR 需要捕获源提供真实的线性 HDR 数据和 FFmpeg 对应的 10 位编码器；不满足时会报错，不会静默改存 SDR。为符合编码要求，奇数宽高会在右侧或底部补齐为偶数。

同时输出时，普通路径保存 SDR，旁边的 `.hdr` 文件保存 HDR，例如 `clip.mp4` 与 `clip.hdr.mp4`。两路使用同次捕获和同一时间轴，音频相同；全部编码成功后才发布文件。停止时会无损重新封装音频尾部，不会重新压缩视频。由于音频编码以采样包为单位，尾部可能有一个音频包以内的误差。

## 开始录制

1. 选择屏幕范围或窗口内容，也可以沿用当前选区。
2. 设置格式、帧率、缩放、质量、是否捕获鼠标和保存目录。
3. 按需打开系统声音或麦克风，选择设备。
4. 点击开始；录制时可查看时长、帧数与实际捕获速率。

缩放值为除数：`1` 保持原尺寸，`2` 将宽高各缩小为一半。质量提供高质量、均衡和小体积三个档位。视频以 `Video-日期-时间.扩展名` 保存到所选目录。

系统声音和麦克风默认都关闭，可以分别启用，也可以同时录制并混合到一个音轨。系统声音采集所选播放设备的输出；麦克风采集所选输入设备。可选择系统默认设备，或指定设备；指定设备不可用时会报错，不会擅自替换。

界面中的系统声音和麦克风捕获目前仅支持 Windows。其他平台的视频可用性取决于平台捕获后端和 FFmpeg，不能据此视为已经完成所有平台的实机验证。

## 停止与取消

- **停止**：结束采集，等待编码器写完文件，然后保留结果。
- **取消**：终止本次录制与编码，删除本次临时输出。
- 录制中关闭视频窗口会按停止处理，完成文件后关闭。

完成后可使用窗口中的打开按钮，通过系统关联播放器查看视频。录制参数在录制期间不可修改；视频录制与其他录制任务共用忙碌状态，不能同时启动第二个任务。

## 在脚本中录制视频

`video` 表与录制窗口使用同一套捕获和编码服务。视频沿用预设定义的选区、窗口跟随或组合画布，录制期间仍可通过脚本调整布局。

```rgs
preset "视频" {
    area screen(0, 0, 640, 480);
    script {
        const recording = video.start('clip.mp4', {
            format: VideoFormat.Mp4,
            fps: 30,
            divisor: 1,
            quality: 23,
            includeCursor: false
        })?;
        wait(3);
        return video.stop(recording.id);
    }
}
```

可以设置 `color: OutputColorMode.Both` 和 `toneMap: HdrToneMapMode.HighlightCompression`；省略时沿用共同捕获设置。`video.capabilities()` 的 `mp4Hdr`、`webmHdr` 表示编码器具备相应 10 位支持，不代表当前捕获源一定提供 HDR。句柄的 `outputs` 列出本次所有目标路径，`path` 保留主路径。

未指定 `fps`、`divisor`、`includeCursor` 时，依次使用当前预设设置和程序设置。路径相对于预设文件所在目录解析。`quality` 是编码器 CRF，范围 `0–51`，数值越小质量越高、文件通常越大。默认 `23`。默认不覆盖文件；需要覆盖时显式设置 `overwrite: true`。

| 调用 | 返回值 | 用途 |
| --- | --- | --- |
| `video.capabilities()` | `Result<VideoCapabilities, ServiceError>` | 查询 FFmpeg、格式、音频编码及输入设备支持 |
| `video.start(path, options?)` | `Result<VideoHandle, ServiceError>` | 开始录制，返回句柄，不等待录制完成 |
| `video.stop(id, cancel?)` | `Result<VideoHandle, ServiceError>` | 停止并保存；第二个参数为 `true` 时取消并删除临时输出 |
| `video.wait(id)` | `Result<VideoHandle, ServiceError>` | 等待用户停止或取消当前录制 |

句柄包含 `id`、`path`、`outputs`、`cancelled`。只保留用户手动停止的录制，可以将示例中的 `wait(3)` 和 `video.stop` 换成 `return video.wait(recording.id);`。正常退出脚本会停止仍在进行的录制；脚本失败或被取消时会取消该录制。过期句柄不能停止后来启动的任务。

### 系统声音、麦克风和外部音频

Windows 可在 `video.start` 的选项中设置 `systemAudio: true`、`microphone: true`，也可同时启用。`systemDevice` 和 `microphoneDevice` 可指定 Windows 音频端点 ID；省略时使用默认设备。

`audioInputs` 可加入 FFmpeg 支持的文件、网络音频或输入设备。每项包含 `input` 和可选的 `format`，例如：

```rgs
const recording = video.start('sound.mp4', {
    audioInputs: [{ input: 'music.wav' }]
})?;
wait(5);
return video.stop(recording.id);
```

未指定 `format` 时，本地音频的相对路径以预设文件目录为基准解析。指定 `format` 时，`input` 原样交给 FFmpeg，用于设备标识或过滤器表达式。网络地址也可作为 `input`。Linux 可使用可用的 `pulse` 或 `alsa` 输入；macOS 可使用可用的 `avfoundation` 输入，设备名称由相应系统提供。`video.capabilities().value.audioInputFormats` 列出当前 FFmpeg 构建的输入设备格式；指定输入必须提供音轨，系统声回环还取决于设备支持。

多路声音会混合成一条音轨。输入提前结束时补静音至视频结束；取消会关闭输入和编码进程。`available` 表示找到了 FFmpeg，`mp4`、`webm` 表示视频编码器可用，`mp4Audio`、`webmAudio` 表示相应的音频编码器可用；`nativeAudio` 表示支持界面中的原生系统声音和麦克风捕获。
