# 录制来源、布局与输出

[返回手册](README.md)

## 选择要捕获的内容

`source` 决定取得哪种图像，`area` 决定裁剪哪里。两者分别设置。

| 写法 | 含义 |
| --- | --- |
| `source screen;` | 捕获屏幕实际显示的内容，包括覆盖在目标区域上的其他窗口 |
| `source window;` | 请求目标窗口内容捕获；行为受平台及目标程序限制 |
| `area screen(x,y,w,h);` | 屏幕绝对像素坐标，只能搭配 `source screen` |
| `area client(x,y,w,h);` | 相对目标窗口客户区 |
| `area window(x,y,w,h);` | 相对包含边框的窗口范围 |

以下预设跟随记事本窗口，捕获其客户区内的屏幕内容：

```rgpreset
preset "跟随记事本" {
    window process "notepad" title "*";
    source screen;
    area client(16, 16, width - 32, height - 32);
    follow true;
    script {
        waitUntil(() => status().exists, 30);
        select();
        start();
        wait(3);
        stop();
    }
}
```

`window` 的进程和标题支持 `*`、`?` 通配符；两者不能同时只写 `*`。先用命令行 `windows` 查看实际名称。将示例中的 `source screen` 改成 `source window`，即可捕获窗口内容。遮挡、最小化、受保护内容和权限限制的行为取决于捕获后端。

预设默认跟随窗口。`follow false;` 可固定单区域的首次采集位置。`area client(0,0,100%,100%);` 覆盖整个客户区；百分比在横向参数按宽度计算，纵向参数按高度计算。绝对屏幕区域不使用百分比或 `width` / `height`。多屏坐标可能为负数。

在 Wayland 等使用系统来源选择器的环境中，需先通过系统界面授权并选择捕获来源。

## 设置帧率、尺寸和编码

以下片段放在 `preset` 内、`script` 外：

```text
record {
    fps 30;
    scale 2;
    format Gif;
    quality Balanced;
    cursor true;
    sound false;
    ffmpeg false;
    copy true;
}
```

| 字段 | 可用值／含义 |
| --- | --- |
| `fps` | `1`～`100` 的整数，目标采集帧率 |
| `scale` | `1`～`100` 的整数，尺寸除数；`2` 表示宽高各为一半 |
| `format` | `Gif`、`WebP`、`APng` |
| `quality` | `Fast`、`Balanced`、`Best`，GIF 质量策略 |
| `cursor` | 是否包含光标 |
| `sound` | 是否播放已配置的提示音；不包含音轨录制 |
| `ffmpeg` | 是否启用 FFmpeg 路径 |
| `copy` | 完成后自动复制输出到剪贴板，默认 `true` |

脚本中使用注册枚举和对象设置：

```text
start({ fps: 24, scale: 1, format: OutputFormat.WebP });
```

设置优先级是 `start({...})` 本次覆盖 → 预设 `record` → 当前界面设置。未填写的字段从下一层取得。录制完成后恢复界面的帧率、缩放和格式，预设中的覆盖值仅对本次录制生效。

录制时显示实际采集 FPS 和编码进度。窗口捕获会等待首个有效画面，初始化等待不计入动画时长。GIF 按实际采集时间生成时间轴，最高输出 50 FPS，以避免播放器对极短帧延迟的不同处理；更高采集帧率会重采样，保持播放时长。WebP 和 APNG 不受这一 GIF 限制。

帧率、像素数和持续时间共同影响编码成本与文件大小。先选实际需要的区域和尺寸，再按画面运动程度调整帧率。

## 保存到多个位置

`save` 可以声明多次；以下片段放在 `preset` 内、`script` 外：

```text
save to "./captures" as "{preset}-{timestamp}.{ext}";
save to "./archive" as "{date}-{sequence}-{title}.{ext}";
```

多个目的地保存同一次编码的副本。需要不同格式时，使用[多路录制](multi-output.md)。多目标保存失败或被取消时，会回滚本次已经创建的输出；已有文件不会被覆盖。相对目录以预设文件所在目录为基准。没有 `save` 声明时使用应用的输出目录。文件名冲突会添加编号，非法文件名字符会清理，缺少输出扩展名时会补上。

| 占位符 | 内容 |
| --- | --- |
| `{preset}` / `{title}` / `{process}` | 预设名／目标标题／进程名 |
| `{date}` | `yyyy-MM-dd` |
| `{time}` | `HH-mm-ss` |
| `{timestamp}` | `yyyyMMdd-HHmmss-fff` |
| `{id}` | 随机标识 |
| `{sequence}` | 至少三位的录制序号 |
| `{ext}` | 不含点的格式扩展名，APNG 为 `png` |

占位符用于文件名模板。文件名不能包含目录分隔符；目录写在 `to` 后。脚本可以在 `stop();` 完成后调用 `copy();` 再次复制录制输出。

## 多区域与异形裁剪

`layout(width,height)` 定义输出画布。`area` 是输入裁剪区域，`place` 是它在画布中的位置和尺寸；输入会缩放到 `place` 的大小。后声明的区域覆盖在前面的区域上。

```rgpreset
preset "双区域" {
    layout(800, 450) {
        background "#202020";
        region "main" {
            source screen;
            area screen(0, 0, 640, 360);
            place(0, 0, 800, 450);
            shape rect;
            missing color "#202020";
        }
        region "detail" {
            source screen;
            area screen(640, 0, 240, 240);
            place(560, 210, 220, 220);
            shape ellipse;
            missing source "main";
        }
    }
    script {
        select();
        start();
        scene.animate("detail", 20, 20, 220, 220, 2, Easing.EaseInOut);
        wait(3);
        stop();
    }
}
```

区域可单独声明 `window process "..." title "...";` 和 `source window;`。未声明窗口的区域继承预设的窗口匹配；全是 `area screen(...)` 的布局无需窗口匹配。每个区域的捕获源独立指定，默认是屏幕。

形状写法：

- `shape rect;`：矩形。
- `shape ellipse;`：椭圆。
- `shape rounded(16);`：圆角矩形。
- `shape polygon(0,0, 200,0, 180,120, 20,120);`：至少三个顶点，坐标相对该区域最初 `place` 的尺寸。

画布宽高各最多 `16384`，画布像素数及各区域放置面积之和都有 `64 × 1024 × 1024` 的限制，最多 `32` 个区域。最终编码尺寸还会受 `scale` 影响。

## 来源丢失时的填充

`missing` 可放在单区域预设的 `preset` 内，或布局的各个 `region` 内。默认透明。

| 写法 | 行为 |
| --- | --- |
| `missing transparent;` | 透明填充 |
| `missing color "#303030";` | 纯色填充 |
| `missing image "./offline.png";` | 使用图片；相对路径以预设文件为基准 |
| `missing source "main";` | 使用同一布局的另一个来源，仅限 `region` |

来源引用必须存在且不能成环。背景和颜色可写 `transparent`、`#RRGGBB` 或 `#AARRGGBB`。透明区域最终如何显示还受输出格式影响，GIF 不提供完整的半透明通道。

## 在脚本中改变布局

下面都是 `script` 中的调用，区域名必须已在 `layout` 中声明：

```text
scene.move("detail", 30, 20, 240, 180);
scene.crop("detail", 10, 10, 200, 160);
scene.opacity("detail", 0.8);
scene.visible("detail", true);
scene.animate("detail", 400, 20, 240, 180, 1.5, Easing.EaseOut);
wait(1.5);
```

`move` 改输出位置和大小；`crop` 改输入裁剪，沿用区域原本的屏幕／客户区／窗口坐标基准。`animate` 立即安排动画，不会阻塞到动画结束，需用 `wait` 保持录制。缓动选项为 `Linear`、`EaseIn`、`EaseOut`、`EaseInOut`。`scene.get(name)` 读取当前位置、尺寸、可见性和不透明度。

布局控制方法用于修改已声明的区域。区域列表、画布尺寸和形状定义在预设中设置，运行期间保持不变。

## 屏幕配置变化与录制尺寸

主界面可直接切换是否录制鼠标指针。进入选区后，可用现场开关修改鼠标入图、窗口／控件识别和捕获来源，也可按 `W` 切换录制来源；Windows 下还可按 `Tab` 切换窗口／原生控件感知。提示区显示当前模式，并自动避让光标。左键选择或拖动，按 `Enter` 确认；右键逐步取消当前操作、清空选区或退出。`Delete`／`Backspace` 清空当前选区并继续选择；`Esc` 在有选区时清空、无选区时退出。取消重选会保留原录制区域与预设，录制过程中按选区快捷键不会改变录制状态。`Shift+Tab` 切换边缘吸附模式，按住 `Shift` 暂时关闭吸附。

窗口内容模式将选区裁剪到目标客户区，并绑定该窗口；完整客户区跟随窗口尺寸，局部区域保留相对偏移。屏幕模式保留实际显示的屏幕区域。Wayland 的来源由系统选择器确定，需要从主界面重新选择才能更换来源。控件感知识别具有独立 HWND 的 Windows 原生子窗口。

感知半径按逻辑像素设置，每块屏幕单独按缩放比例换算；200% 缩放时使用两倍物理像素范围。边缘检测优先选择附近连续、方向一致的边界，减少文字和纹理的干扰。

交互选区期间，如果屏幕配置或 DPI 改变，当前选区会取消，避免使用旧截图确定错误坐标。切换到其他应用也会取消选区，不会主动抢回焦点；多个选区遮罩之间的焦点转移会保留选区。拖动时丢失鼠标捕获会清理尚未完成的拖动。

录制坐标和预设偏移使用物理像素。单纯改变 DPI 不会自动放大像素裁剪区域或偏移；屏幕拓扑变化时会更新采集使用的屏幕快照，输出画布尺寸保持固定。来源丢失时按 `missing` 设置填充，来源恢复后继续采集。预览将完整像素图像交给界面框架缩放显示。

## 状态、事件和生命周期

`status()` 返回 `exists`、`recording`、`busy`、`idle`、`title`、`width`、`height`、`elapsed`。录制脚本常用：

```text
waitUntil(() => status().exists, 30);
select();
start();
wait(2);
stop();
```

`wait`、`waitUntil` 的时间单位为秒，大于零且不超过一天；`waitUntil` 默认超时为三十秒。`stop` 结束并等待输出处理，`cancel` 取消。脚本正常退出时会结束尚未结束的录制；异常或失败结果会取消并记录错误。

`events.on(name, handler)` 注册无参数处理函数，`events.off(name)` 移除该名称的所有处理函数，`events.emit(name)` 投递自定义事件。`wait` / `waitUntil` 会处理事件，也可显式调用 `events.pump()`。持续运行的循环应包含等待或事件处理调用。

```text
events.on(CaptureEvent.Moved, () => {
    scene.opacity("detail", 0.7);
});
wait(5);
events.off(CaptureEvent.Moved);
```

`CaptureEvent` 提供 `Found`、`Lost`、`Closed`、`Moved`、`Resized`、`Title`、`Saved`、`Cancelled`、`Stopped`、`Error` 名称；窗口变化事件针对预设的主目标；布局区域不独立发送这些事件。

## 状态规则

固定流程可以使用状态规则：

```rgpreset
preset "状态式录制" {
    area screen(0, 0, 640, 360);
    initial recording;
    state recording {
        enter { select; start; }
        after 3s { stop; finish; }
    }
}
```

`after 3s`、`wait 200ms;` 属于预设状态规则语法，仅用于状态规则。状态可以包含 `enter`、`exit`、`on 事件`、`when 条件`、`after 时长`，用 `goto 状态名;` 或规则后的 `-> 状态名;` 跳转。复杂函数、模块和类型能力请使用 `script` 层。
