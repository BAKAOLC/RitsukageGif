# 脚本录制与画面布局

预设文件使用 `.rgpreset` 扩展名。一个 `preset` 定义捕获范围、录制选项和要执行的 `script`。

## 录制一个窗口

下面的预设录制记事本客户区，三秒后保存 WebP：

```rgpreset
preset "记事本录制" {
    window process "notepad" title "*";
    source window;
    area client(0, 0, 100%, 100%);
    follow true;

    record {
        fps 24;
        scale 1;
        format WebP;
        copy true;
    }
    save to "./captures" as "{preset}-{timestamp}.{ext}";

    script {
        waitUntil(() => status().exists, 30);
        select();
        start();
        wait(3);
        stop();
    }
}
```

运行前打开记事本。换成其他程序时，修改 `process` 和 `title`；两者支持 `*`、`?` 通配符，但不能同时只写 `*`。`waitUntil` 的第二个参数是超时秒数。

`stop()` 停止采集并等待输出完成；`cancel()` 取消录制。脚本正常结束时会停止仍在进行的录制，发生错误或取消脚本时会取消录制。

需要手动停止时，可把示例中的 `wait(3);` 换为：

```rgscript
while (status().recording) {
    wait(0.1);
}
```

保留循环后面的 `stop();`，等待编码和保存结束。

## 选择来源与范围

`source` 选择取得的画面，`area` 选择其中的范围。

| 声明 | 含义 |
| --- | --- |
| `source screen;` | 录制屏幕上实际显示的内容，包括其他窗口的遮挡 |
| `source window;` | 捕获指定窗口的内容 |
| `area client(x, y, width, height);` | 相对窗口客户区选择范围 |
| `area window(x, y, width, height);` | 相对包含边框的窗口范围 |
| `area screen(x, y, width, height);` | 使用屏幕绝对坐标，只搭配 `source screen` |

例如，保留窗口四周各 16 像素的边距：

```rgpreset
area client(16, 16, width - 32, height - 32);
```

横向百分比以宽度为基准，纵向百分比以高度为基准。`area screen` 使用固定数值，不接受百分比或窗口宽高；多屏坐标可以为负。

`follow true;` 让范围跟随窗口移动，预设默认启用。`follow false;` 固定单区域录制的首次位置。将 `source screen` 与 `area client(...)`、`follow true` 组合，可以跟随窗口的某个区域，同时录下它所在位置的屏幕内容。

窗口匹配、窗口内容捕获及遮挡处理受系统和目标程序限制。使用系统来源选择器的平台不能按进程名、标题自动匹配窗口；需要先选择系统允许捕获的来源。

## 临时录制选项

`record { ... }` 放在 `preset` 内、`script` 外；只填写需要覆盖的字段。

| 字段 | 可用值 |
| --- | --- |
| `fps` | 1～100 的整数，目标采集帧率 |
| `scale` | 1～100 的整数，尺寸除数；`2` 表示宽高各缩小一半 |
| `format` | `Gif`、`WebP`、`APng` |
| `quality` | `Fast`、`Balanced`、`Best`，GIF 质量策略 |
| `cursor` | `true` / `false`，是否包含光标 |
| `sound` | `true` / `false`，是否播放提示音，不是录制音轨 |
| `ffmpeg` | `true` / `false`，是否使用 FFmpeg 编码路径 |
| `copy` | `true` / `false`，完成后是否自动复制输出；默认 `true` |

也可以只为某次 `start` 覆盖选项：

```rgscript
start({ fps: 30, scale: 2, format: OutputFormat.Gif, quality: GifQuality.Balanced });
```

取值顺序为本次 `start` 的选项、预设的 `record`、应用当前设置。预设和 `start` 的覆盖值不会保存为界面的常用配置；本次录制结束后恢复使用原设置。

## 保存位置与文件名

`save` 可以写多次，将同一次录制保存到多个目录：

```rgpreset
save to "./captures" as "{preset}-{timestamp}.{ext}";
save to "./archive" as "{date}-{sequence}-{title}.{ext}";
```

相对目录以预设文件所在目录为基准。多个 `save` 保存相同内容；需要不同格式或不同画面时，使用[多路输出](multi-output.md)。

| 占位符 | 内容 |
| --- | --- |
| `{preset}`、`{title}`、`{process}` | 预设名、窗口标题、进程名 |
| `{date}`、`{time}` | `yyyy-MM-dd`、`HH-mm-ss` |
| `{timestamp}` | 包含毫秒的时间戳 |
| `{sequence}`、`{id}` | 录制序号、随机标识 |
| `{ext}` | 当前格式的扩展名，不含点号 |

占位符用于文件名，目录部分不展开这些占位符。文件名中不能填写目录分隔符。单路 `save` 遇到同名文件时追加数字后缀。

## 保存单帧图像

在预设的 `script` 中调用 `capture.save`，直接保存当前范围或布局，不需要先 `start()`：

```rgscript
const image = capture.save("./captures/snapshot.png", {
    format: StaticImageFormat.Png,
    includeCursor: false,
    scalePercent: 100
})?;
print(image.path);
return result.ok(null);
```

它返回 `Result<MediaInfo, ServiceError>`；`?` 在失败时向外返回错误，成功时取出图像信息。使用 `?` 的脚本或函数，正常结束时也要返回 `Result`。相对路径以预设文件所在目录为基准。

| 选项 | 含义 |
| --- | --- |
| `format` | `StaticImageFormat.Png`、`Jpeg`、`Bmp`、`WebP`、`Avif`；省略时按扩展名选择 |
| `quality` | 0～100，默认 90 |
| `includeCursor` | 是否包含光标；省略时沿用录制设置 |
| `scalePercent` | 1～100 的缩放百分比，`100` 为原尺寸；不同于录制的尺寸除数 `scale` |
| `overwrite` | 是否覆盖已有文件，默认 `false` |
| `background` | 不支持透明度的输出所用背景色，默认 `"#ffffff"` |
| `outputName` | 多路录制中要抓取的输出名称；省略时使用当前预设 |

需要把同一帧保存为多个文件时，用 `capture.saveOutputs`，一次可指定 1～16 个目标：

```rgscript
const images = capture.saveOutputs([
    { path: "./captures/snapshot.png", format: StaticImageFormat.Png },
    { path: "./captures/snapshot.jpg", format: StaticImageFormat.Jpeg, quality: 90 }
], { includeCursor: false, scalePercent: 100 })?;
return result.ok(null);
```

每个目标可指定 `color: OutputColorMode.Sdr` 或 `OutputColorMode.Hdr`。HDR 目标改用 `hdrFormat: HdrImageFormat.Png` 或 `HdrImageFormat.Exr`，不与 `format`、`quality`、`background` 混用；来源必须提供 HDR 画面。批量保存不覆盖已有文件。

## 合成多个区域

`layout` 定义固定大小的输出画布。每个 `region` 分别定义输入范围和画布中的摆放位置，后声明的区域覆盖前面的区域。

下面把窗口画面与跟随该窗口的屏幕局部合成在一起，并移动局部画面：

```rgpreset
preset "窗口与局部镜头" {
    layout(800, 450) {
        background "#202020";
        region "主画面" {
            window process "notepad" title "*";
            source window;
            area client(0, 0, 100%, 100%);
            place(0, 0, 800, 450);
        }
        region "局部" {
            window process "notepad" title "*";
            source screen;
            area client(0, 0, 240, 180);
            place(16, 16, 240, 180);
            shape rounded(12);
            missing color "#303030";
        }
    }
    save to "./captures" as "{preset}-{timestamp}.{ext}";
    script {
        select();
        start({ format: OutputFormat.WebP, fps: 24, copy: false });
        scene.animate("局部", 480, 16, 240, 180, 1, Easing.EaseInOut);
        wait(1);
        scene.crop("局部", 40, 40, 240, 180);
        wait(2);
        stop();
    }
}
```

`area` 决定输入裁剪，`place(x, y, width, height)` 决定输出位置与尺寸；图像会缩放到 `place` 的大小。布局内坐标以画布为准，不随录制的 `scale` 改写。

### 区域形状与来源丢失

在 `region` 内选择一种形状：

```rgpreset
shape rect;
shape ellipse;
shape rounded(12);
shape polygon(0, 0, 240, 0, 120, 180);
```

多边形参数依次为各顶点的 `x, y`，至少三个顶点。以上是不同写法，实际区域只填写其中一种。

`missing` 决定来源丢失时显示什么：

```rgpreset
missing transparent;
missing color "#202020";
missing image "./offline.png";
missing source "主画面";
```

`missing source` 引用同一布局内的另一个区域，不能循环引用。未指定时使用透明画面。

### 在录制中修改布局

以下函数放在 `script` 内，`name` 是 `region` 名称。

| 调用 | 用途 |
| --- | --- |
| `scene.move(name, x, y, width, height)` | 立即改变画布中的位置和尺寸 |
| `scene.crop(name, x, y, width, height)` | 改变输入裁剪范围 |
| `scene.animate(name, x, y, width, height, seconds, easing)` | 平滑改变输出位置与尺寸 |
| `scene.visible(name, visible)` | 显示或隐藏区域 |
| `scene.opacity(name, opacity)` | 设置透明度，范围 0～1 |
| `scene.get(name)` | 读取区域当前设置 |

`animate` 不等待动画结束，继续执行下一条语句；需要等到动画结束时，配合 `wait(seconds)`。缓动使用 `Easing` 的 `Linear`、`EaseIn`、`EaseOut`、`EaseInOut` 成员。

用 `scene.set` 一次修改多个属性：

```rgscript
scene.set("局部", {
    crop: { x: 40, y: 40, width: 240, height: 180 },
    placement: { x: 480, y: 16, width: 240, height: 180 },
    visible: true,
    opacity: 0.8
});
```

裁剪坐标沿用该区域的 `area client`、`area window` 或 `area screen` 坐标基准。

## 用状态机组织录制

除了 `script`，也可以用状态描述“等待窗口、录制、结束”等步骤。同一个预设只能选择 `script` 或状态机，不能同时使用两者。

```rgpreset
preset "等待窗口后录制" {
    window process "notepad" title "*";
    source window;
    area client(0, 0, 100%, 100%);
    save to "./captures" as "{preset}-{timestamp}.{ext}";
    initial waiting;

    state waiting {
        when exists -> capturing;
    }
    state capturing {
        enter { select; start; }
        after 3s -> done;
        on lost { cancel; goto waiting; }
    }
    state done {
        enter { stop; finish; }
    }
}
```

| 写法 | 含义 |
| --- | --- |
| `initial name;` | 指定初始状态；省略时使用第一个状态 |
| `enter { ... }` | 进入状态时执行 |
| `exit { ... }` | 离开状态时执行，只能放立即完成的动作，不能等待或跳转 |
| `when 条件 -> name;` | 条件首次成立或再次由假变真时切换状态 |
| `after 3s -> name;` | 进入该状态三秒后切换，也支持 `ms`、`m` |
| `on lost { ... }` | 收到事件时执行动作 |
| `goto name;`、`finish;` | 切换状态、停止录制并结束状态机 |

状态动作使用 `select;`、`start;`、`stop;`、`cancel;` 等写法，以分号结束。条件中直接使用 `exists`、`recording`、`busy`、`idle`、`width`、`height`、`elapsed`、`recording.elapsed`，不写 `status()`。

规则也可用块形式，例如 `when exists { select; start; }`；事件可附加条件，例如 `on resized when recording { emit("window-changed"); }`。状态内的规则优先于预设外层的规则。`wait 500ms;` 暂停当前动作序列，`await exists timeout 30s;` 等待条件成立并设置超时。
