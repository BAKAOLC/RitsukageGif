# rgscript 使用手册

rgscript 是 RitsukageGif 的脚本语言。预设用于定义捕获范围、画面布局和输出位置，脚本用于控制录制流程和动态变化。预设保存在外部文件中，可以从界面、快捷键或命令行启动。

## 从这里开始

1. [编写第一个录制预设](#第一个录制预设)：先完成一次短录制。
2. [语言基础](language.md)：变量、表达式、函数、集合与控制流程。
3. [类型与模式匹配](types-and-patterns.md)：类型注解、推断、解构与 `switch`。
4. [模块与错误处理](modules-and-errors.md)：公共代码、`Result` / `Option`、失败与取消。
5. [录制与画面布局](recording-reference.md)：屏幕和窗口来源、多区域、动画、输出。
6. [编辑器、调试与命令行](editor-and-cli.md)：补全、断点、快捷键及外部调用。
7. [关键词参考](keywords.md)：按用途查找语法。
8. [同步多路录制](multi-output.md)：多源、不同布局、独立编码和逐路结果。
9. [文件与图像服务](services.md)：文件大小、转换、裁剪、多个输出与目标体积处理。
10. [图像合成与局部效果](image-processing.md)：拼图、帧总览、马赛克及图层处理。
11. [文件画面源](media-sources.md)：静态图片和动画参与实时布局。
12. [数据处理](data-services.md)：JSON、CSV、二进制与摘要。
13. [外部输入通道](input-channels.md)：管道、TCP、外部画面、JSON 数据和视频推流。
14. [动图编辑器](animation-editor.md)：帧管理、绘制、文字、水印及动画导出。
15. [截图与长截图](screenshots.md)：选区、同屏标注、静态保存、长图和托盘。
16. [音视频录制](video-recording.md)：MP4、WebM、系统声音、麦克风及停止与取消。
17. [文字与二维码识别](recognition.md)：系统 OCR、识别语言、二维码、条码及定位结果。
18. [诊断日志](diagnostics.md)：日志位置、级别、保存规则和故障定位。

## 第一个录制预设

创建 `demo.rgpreset`，写入：

```rgpreset
preset "三秒录制" {
    area screen(0, 0, 640, 360);
    record {
        fps 20;
        scale 1;
        format Gif;
        quality Balanced;
    }
    save to "./captures" as "{preset}-{timestamp}.{ext}";
    script {
        select();
        start();
        wait(3);
        stop();
    }
}
```

它录制屏幕坐标 `(0, 0)` 开始的 `640 × 360` 区域，持续三秒，保存到预设文件旁的 `captures` 文件夹。运行前，根据屏幕位置和尺寸调整坐标。`scale 1` 保持原始尺寸；`scale 2` 将宽高各缩小为一半。

在内置编辑器中打开并运行，或通过 PowerShell 调用：

```powershell
& 'C:/Apps/RitsukageGif/RitsukageGif.exe' preset run 'C:/Presets/demo.rgpreset' '三秒录制'
```

## 两种文件与两层语法

- `.rgpreset`：录制入口，可包含多个 `preset "名称" { ... }`。名称在加载的预设中必须唯一。
- `.rgs`：可复用的脚本模块，通过 `import` 引入。录制流程从 `.rgpreset` 中的 `preset` 入口启动。

预设外层的 `area`、`layout`、`record`、`save` 是声明；`script { ... }` 内是脚本语言。例如外层设置写 `fps 20;`，脚本调用写 `start({ fps: 20 });`。脚本中的等待时间以秒计，写 `wait(0.5)`。

复杂逻辑建议放在 `script` 和导入的模块中。预设也支持 `state` 状态规则；同一个预设不能同时使用 `script` 和 `state`。具体区别见[录制参考](recording-reference.md#状态规则)。

## 使用示例

代码块中标为“片段”的内容要放入指定位置；完整预设可直接保存后运行。运行前，根据录制目标调整窗口名称、进程名称、坐标和路径。窗口捕获是否可用，还取决于操作系统、权限和目标窗口的行为。
