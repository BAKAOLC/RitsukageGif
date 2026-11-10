# 文件与视频源

## 录制动画文件

`source file` 把图像或动画放进布局。下面的预设播放同目录 `assets/animation.gif`，录制四秒：

```rgpreset
preset "动画文件" {
    layout(640, 480) {
        region "animation" {
            source file "assets/animation.gif";
            place(0, 0, 640, 480);
        }
    }
    record {
        fps 20;
        scale 1;
        format Gif;
        cursor false;
    }
    save to "captures" as "animation-{timestamp}.{ext}";
    script {
        start();
        wait(4);
        stop();
    }
}
```

路径相对于预设文件。PNG、JPEG 等静态图像保持不动；GIF、APNG、动画 WebP 按各帧时长播放，默认循环。图层可以裁剪、缩放、添加效果，并与屏幕或窗口画面合成。

图像源的解码画面总量上限为 512 MiB。高分辨率或较长动画可能超过上限，压缩文件较小也不代表解码后占用较小。

可运行的完整示例见 [file-animation.rgpreset](../examples/file-animation.rgpreset)。

## 视频与实时流

把源改成 `ffmpeg`，可以读取视频文件或流地址：

```rgpreset
region "video" {
    source ffmpeg "assets/clip.mp4" {
        loop false;
        speed 1;
        position 0;
        holdLast true;
    };
    place(0, 0, 640, 480);
    missing color "#303030";
}
```

实时流使用 `live true`，并可提供输出尺寸：

```rgpreset
source ffmpeg "http://127.0.0.1:8080/live" {
    live true;
    size(640, 480);
    staleAfter 5;
    holdLast false;
};
```

视频文件需要 FFmpeg 和 ffprobe。实时流明确指定 `size` 时只需要 FFmpeg；自动识别尺寸还需要 ffprobe。

| 选项 | 含义与默认值 |
| --- | --- |
| `loop true;` | 播放完毕后循环，默认 `true` |
| `speed 1;` | 播放倍率，默认 `1`；文件图像为 0～100，FFmpeg 为 0～16；`0` 冻结 |
| `position 0;` | 起始位置，单位秒，默认 `0` |
| `holdLast true;` | 非循环播放结束后保留末帧；设为 `false` 则使用 `missing` 填充 |
| `live false;` | 是否为实时源，默认 `false` |
| `size(640, 480);` | FFmpeg 解码尺寸，宽高各为 1～16384 |
| `staleAfter 5;` | 实时源没有新画面的失效时长，单位秒，必须大于零 |

## 在脚本中控制播放

`scene.playback(name, options)` 修改指定图层，返回 `void`。只填写要改的选项：

```rgscript
scene.playback('animation', {position: 0.5, speed: 0.5});
scene.playback('animation', {paused: true});
scene.playback('animation', {paused: false, loop: false, holdLast: true});

const playback = scene.get('animation').playback;
print(playback.position, playback.speed, playback.paused);
```

暂停和变速保留当前播放位置；`position` 指定要跳转到的秒数。文件图像和有限视频按录制时间播放，预热时间不计入播放进度。FFmpeg 跳转需要输入支持定位，实时流不能随机跳转。

多路输出使用 `scene.playbackFor(output, name, options)` 和 `scene.getFor(output, name)`。也可以把 `playback` 与位置、裁剪等选项一起交给 `scene.set` 或 `scene.setFor`。

外部程序主动发送画面时，使用[输入通道](input-channels.md)中的 `source channel`。
