# 使用手册

RitsukageGif 可以直接录制屏幕，也可以通过 rgscript 预设控制窗口、范围、画面布局和输出。

## 第一个预设

将下面的内容保存为 `demo.rgpreset`，在编辑器中打开，选择“三秒录制”并运行。它录制屏幕左上角的 `640 × 360` 区域，三秒后保存到预设旁的 `captures` 文件夹；请先按自己的屏幕调整范围。

```rgpreset
preset "三秒录制" {
    area screen(0, 0, 640, 360);
    record { fps 20; scale 1; format Gif; }
    save to "./captures" as "{preset}-{timestamp}.{ext}";
    script {
        select();
        start();
        wait(3);
        stop();
    }
}
```

`.rgpreset` 包含一个或多个有名称的预设；`.rgs` 用于存放可导入的公共脚本。`area`、`record`、`save` 等写在预设内，具体执行流程写在 `script { ... }` 中。等待时间以秒计。

## 学习语法

- [语言基础](language.md)：变量、运算、函数、循环、解构与切片。
- [类型与模式匹配](types-and-patterns.md)：类型注解、推断和 `switch`。
- [模块与错误处理](modules-and-errors.md)：导入、`Result`、`Option` 和错误处理。
- [关键词参考](keywords.md)：查询语法写法。

## 使用接口

- [录制与布局](recording-reference.md)：选择来源、区域、动态布局和保存位置。
- [同步多路录制](multi-output.md)：同时输出不同画面或格式。
- [文件与图像服务](services.md)：文件操作、图像处理与压缩。
- [图像合成与效果](image-processing.md)、[媒体输入](media-sources.md)、[外部输入通道](input-channels.md)、[数据处理](data-services.md)。

[编辑器、调试与命令行](editor-and-cli.md)介绍如何运行脚本；[范例](../examples/README.md)可直接打开后修改。

## 查询参考

[API 参考](api-reference.md)列出函数签名、参数和默认值；[类型参考](script-types-reference.md)列出对象字段与枚举；[命令行参考](cli-reference.md)列出完整命令、选项和返回结果。
