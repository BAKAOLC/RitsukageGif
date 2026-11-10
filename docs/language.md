# 基本语法

rgscript 用于编写捕获预设和处理脚本。下面的片段可以写在预设的 `script { ... }` 块或公共模块中。捕获、图像处理等功能见 [脚本服务](services.md)。

## 第一个脚本

```rgscript
let width = 640;
let height = 480;
print("画面大小：" + width + " × " + height);
```

变量名区分大小写，以字母或下划线开头，后面可以包含数字。声明、赋值、调用等语句以 `;` 结束，代码块用 `{ }` 包围。

```rgscript
// 单行注释
# 也可以这样写单行注释
/* 多行注释 */
```

## 值和变量

```rgscript
let count = 3;
let ratio = 0.75;
let title = "录制";
let enabled = true;
let target = null;
const limit = 60;
```

`let` 可以重新赋值，`const` 不能重新赋值。两种声明都需要初始值。`const` 绑定的数组或对象仍可以修改内容。

```rgscript
const options = { width: 640, height: 480 };
options.width = 800;

let value = 10;
value = "完成";
```

未注明类型的变量可以保存不同类型的值。需要限制类型时，在名称后加注解：

```rgscript
let fps: number = 30;
let name: string? = null;
```

注解、转换和类型推断见 [类型与模式匹配](types-and-patterns.md)。

## 数字和字符串

数字可以写成十进制、小数、科学计数法、十六进制或二进制：

```rgscript
let values = [12, 0.5, .5, 1.2e3, 0xff, 0b1010];
```

数字必须有限；除以零和产生无穷大的运算会报错。

字符串使用单引号或双引号，支持 `\n`、`\r`、`\t`、`\\`、`\"` 和 `\'` 转义。

```rgscript
let path = "D:\\Capture\\output.png";
let message = '第一行\n第二行';
```

## 运算符

| 用途 | 写法 |
| --- | --- |
| 算术 | `+`、`-`、`*`、`/`、`%` |
| 比较 | `==`、`!=`、`<`、`<=`、`>`、`>=` |
| 布尔运算 | `!` / `not`、`&&` / `and`、`||` / `or` |
| 空值回退 | `value ?? fallback` |
| 条件表达式 | `condition ? yes : no` |
| 通配符匹配 | `text ~ pattern`，模式中 `*` 匹配任意多个字符，`?` 匹配一个字符 |
| 赋值 | `=`、`+=`、`-=`、`*=`、`/=`、`%=` |
| 后置增减 | `count++`、`count--` |

条件和布尔运算要求 `boolean`。数字、字符串和数组不会自动变成真假值。`&&`、`||`、`??` 只在需要时计算右侧。

```rgscript
let size = width * height;
let caption = "宽度：" + width;
let label = title ?? "未命名";
let valid = width > 0 && height > 0;
let matched = "capture-001.png" ~ "capture-*.png";
```

`+` 遇到字符串时进行拼接；其他算术运算要求数字。`==` 不会把字符串转换成数字。数组和对象比较的是是否为同一个值，而非逐项比较内容。

## 条件和循环

```rgscript
if (fps > 30) {
    print("高帧率");
} else {
    print("普通帧率");
}

let remaining = 3;
while (remaining > 0) {
    print(remaining);
    remaining--;
}

for (let i = 0; i < 3; i++) {
    print(i);
}
```

`break` 结束当前循环，`continue` 跳过当前轮的剩余语句。

遍历数组时得到元素，遍历对象时得到键名；`in` 和 `of` 在这里作用相同。

```rgscript
for (const item of [10, 20, 30]) {
    print(item);
}

const options = { width: 640, height: 480 };
for (const key in options) {
    print(key + ": " + options[key]);
}
```

## 函数

```rgscript
fn area(width: number, height: number): number {
    return width * height;
}

print(area(640, 480));
```

`function` 与 `fn` 等效。函数可以作为值传递，也可以读取所在作用域的变量。函数声明可在同一代码块中的声明位置之前调用；普通变量应先声明再使用。

```rgscript
let factor = 2;
let scale = value => value * factor;
let add = (left, right) => left + right;
let square = (value: number): number => value * value;

let doubled = [1, 2, 3].map(scale);
```

箭头后是表达式时，表达式的值就是返回值；箭头后是代码块时，用 `return` 返回。没有执行 `return` 的函数返回 `null`。

参数可以有默认值，也可以收集剩余参数：

```rgscript
fn greet(name = "朋友") {
    return "你好，" + name;
}

fn collect(first, ...rest) {
    return [first, ...rest];
}
```

省略实参时使用默认值，显式传入 `null` 时保留 `null`。剩余参数必须在最后。

## 数组和对象

```rgscript
let frames = [100, 200, 300];
print(frames[0]);
frames[1] = 250;
frames.push(400);

let region = { x: 10, y: 20, width: 640, height: 480 };
print(region.width);
region.x += 5;
region["height"] = 600;
```

数组索引从 `0` 开始，普通索引要求非负整数；越界读取会报错。对象也可以用字符串索引访问属性。

展开用于组合数组、对象或实参：

```rgscript
let first = [1, 2];
let combined = [...first, 3];
let base = { x: 0, y: 0, width: 640 };
let moved = { ...base, x: 20 };
let dimensions = [640, 480];
print(area(...dimensions));
```

对象后写的同名属性覆盖前面的属性。数组展开得到一个新数组，内部对象仍是原来的对象。

## 解构

解构可以一次取出多个值，用于声明、赋值和函数参数。

```rgscript
let [first, , third = 0, ...tail] = [10, 20, 30, 40];
let { x, y: top, width = 640, ...other } = { x: 10, y: 20 };

let left = 1;
let right = 2;
[left, right] = [right, left];

fn describe({ width, height }) {
    return width + " × " + height;
}
```

数组中的空位表示跳过元素。对象的 `y: top` 表示读取 `y` 并命名为 `top`。剩余部分 `...rest` 必须在最后。

默认值只在元素或属性缺失时使用，不替换已有的 `null`。缺失且没有默认值会报错。以对象解构赋值开头的语句需要括号：

```rgscript
let x = 0;
({ x } = { x: 30 });
```

## 切片

数组和字符串支持 `[开始:结束:步长]`。开始位置包含在结果中，结束位置不包含；省略步长时为 `1`。

```rgscript
let values = [0, 1, 2, 3, 4, 5];
print(values[1:4]);    // [1, 2, 3]
print(values[-2:]);    // [4, 5]
print(values[::2]);    // [0, 2, 4]
print(values[::-1]);   // [5, 4, 3, 2, 1, 0]
print("abcdef"[1:4]);  // "bcd"
```

负数位置从末尾计算，超出边界时截到可用范围。步长必须是非零整数。切片返回新值，不能作为赋值目标。

字符串索引和切片按 UTF-16 单元计算，可能拆开 emoji 等由多个单元组成的字符。

## 可选访问

在可能为 `null` 的值后使用 `?.`，空值会让该访问链返回 `null`；再用 `??` 提供默认值。

```rgscript
let target = null;
let width = target?.bounds?.width ?? 640;
let first = target?.[0];
let called = target?.(10);
```

可选访问只处理空值，不会忽略拼错的属性、非法索引或函数内部的错误。括号会结束当前访问链，例如 `(target?.bounds).width` 仍会尝试读取 `.width`。

## 作用域

代码块中的变量只在该代码块内可用。内层可以使用外层变量，也可以声明同名变量；同一作用域不能重复声明同名绑定。

```rgscript
let size = 100;
{
    let size = 200;
    print(size); // 200
}
print(size);     // 100
```

继续阅读 [类型与模式匹配](types-and-patterns.md)、[关键词速查](keywords.md)。继续阅读 [模块与错误处理](modules-and-errors.md)。
