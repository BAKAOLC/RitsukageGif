# 示例

用脚本编辑器打开 `.rgpreset` 文件，点击“运行”即可执行。LuaSTG 示例需要先打开 LuaSTGSub，捕获范围按 640×480 游戏布局计算。

## 选择示例

| 示例 | 用途 |
| --- | --- |
| [file-animation.rgpreset](file-animation.rgpreset) | 使用附带动画录制左右对比画面，演示局部效果和播放速度 |
| [luastg-world.rgpreset](luastg-world.rgpreset) | 捕获 LuaSTG World 区域，持续录制直到手动停止，并复制结果 |
| [luastg-dynamic-layout.rgpreset](luastg-dynamic-layout.rgpreset) | 在同一画面中组合游戏区域与动态画中画 |
| [luastg-screen-context.rgpreset](luastg-screen-context.rgpreset) | 对比窗口内容捕获与包含遮挡的屏幕捕获 |
| [luastg-scripted-camera.rgpreset](luastg-scripted-camera.rgpreset) | 通过脚本移动镜头、调整裁剪范围 |
| [luastg-external-tracking.rgpreset](luastg-external-tracking.rgpreset) | 用外部参数控制镜头范围和输出位置 |
| [multi-recording.rgpreset](multi-recording.rgpreset) | 同时输出游戏画面的多种编码、屏幕上下文和动态镜头 |
| [scene-fallback-states.rgpreset](scene-fallback-states.rgpreset) | 演示状态切换、来源丢失时的替代画面和多目标保存 |
| [image-composition.rgpreset](image-composition.rgpreset) | 从动画中选帧拼图，生成帧总览 |
| [media-processing.rgpreset](media-processing.rgpreset) | 把图片转换为缩略图、GIF 和 WebP |
| [compress-to-budget.rgpreset](compress-to-budget.rgpreset) | 调整 GIF 的颜色数，尝试压到指定体积，并保留原文件 |
| [data-processing.rgpreset](data-processing.rgpreset) | 练习 JSON、CSV、字节数据和摘要计算 |
| [external-channel.rgpreset](external-channel.rgpreset) | 接收外部画面和位置数据；与 [input-publisher.py](input-publisher.py) 配合使用，步骤见[外部输入通道](../docs/input-channels.md) |
| [language](language/) | 从值和表达式开始，依次练习函数、集合、类型、模式匹配、错误处理和模块导入 |

## 命令行执行与参数

以下命令以程序和 `examples` 文件夹位于同一目录为例。在该目录执行，或把命令中的程序、示例路径换成各自的完整路径。

```powershell
.\RitsukageGif.exe preset run ".\examples\file-animation.rgpreset"
```

每个参数写成 `--arg 名称=值`。例如，把多路录制改为 10 秒，并指定输出目录：

```powershell
.\RitsukageGif.exe preset run ".\examples\multi-recording.rgpreset" --arg seconds=10 --arg "directory=D:/Captures/multi"
```

指定要压缩的 GIF 和目标体积：

```powershell
.\RitsukageGif.exe preset run ".\examples\compress-to-budget.rgpreset" --arg "input=D:/Captures/recording.gif" --arg targetMB=30
```

数字和 `true`、`false` 会按对应类型传入；路径或文本含空格时，给整个 `名称=值` 加引号。参数名区分大小写。

| 示例 | 可传参数 |
| --- | --- |
| `multi-recording.rgpreset` | `seconds`：录制秒数，默认 6；`directory`：输出目录，默认 `../captures/multi` |
| `media-processing.rgpreset` | `input`：输入图片，默认使用附带图片 |
| `compress-to-budget.rgpreset` | `input`：输入 GIF；`targetMB`：目标体积，默认 30 MB，1 MB 按 1,000,000 字节计算 |
| `luastg-external-tracking.rgpreset` | `x`、`y`、`w`、`h`：游戏逻辑坐标中的镜头范围；`outputX`、`outputY`：输出画面中的位置 |
| `external-channel.rgpreset` | `seconds`：录制秒数，默认 10 |

需要提前结束录制时，可以执行：

```powershell
.\RitsukageGif.exe stop
```
