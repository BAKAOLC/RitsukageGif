# 关键词与上下文词索引

同一个文件可能包含预设声明和 `script { ... }`。两层语法用途不同：`save`、`layout` 等是预设词汇，`let`、`fn` 等用于脚本。

## 声明与函数

| 词 | 用途 | 示例 |
| --- | --- | --- |
| `let` | 声明可重新赋值的变量 | `let count = 0;` |
| `const` | 声明不可重新绑定的变量 | `const limit = 10;` |
| `fn`、`function` | 声明或创建函数 | `fn add(a, b) { return a + b; }` |
| `return` | 从当前函数返回；无值时返回 `null` | `return value;` |

`const` 仅限制重新绑定，对象内容仍可修改。`var` 仅用于模式绑定，见下文。

```rgs
fn next(value: number): number {
    const step = 1;
    return value + step;
}
let count = next(0);
assert(count == 1);
```

## 条件与循环

| 词 | 用途 |
| --- | --- |
| `if`、`else` | 条件分支，条件需要布尔值 |
| `while` | 条件满足时循环 |
| `for` | 计数循环或序列循环 |
| `in`、`of` | 在 `for` 中遍历序列元素，二者在此处含义相同 |
| `break` | 离开当前循环 |
| `continue` | 进入当前循环的下一次迭代 |

`in`、`of` 在序列循环中含义相同；`in` 不能用于判断对象是否包含属性。

```rgs
let total = 0;
for (const item in [1, 2, 3]) {
    if (item == 2) continue;
    total += item;
}
assert(total == 4);
```

## 值与逻辑

| 词 | 用途 |
| --- | --- |
| `true`、`false` | 布尔字面量 |
| `null` | 空值字面量 |
| `and`、`or`、`not` | 普通表达式中的逻辑运算，分别对应 `&&`、`||`、`!`；在模式里组合匹配条件 |

普通逻辑运算要求布尔值，不能把非空字符串或非零数字隐式当作 `true`。详情见[表达式与运算符](language.md#2-表达式与运算符)。

## 模式匹配

| 词或符号 | 用途 |
| --- | --- |
| `switch` | 对一个值依次尝试模式并返回匹配分支的表达式结果 |
| `is` | 检查一个值是否满足模式，返回布尔值 |
| `var` | 把匹配到的值绑定到名字 |
| `when` | 为 `switch` 分支增加布尔守卫 |
| `_` | 匹配任何值，不绑定名字 |
| `and`、`or`、`not` | 同时满足、任一满足、不满足内部模式 |

```rgs
let input = 12;
let label = input switch {
    number n when n > 10 => 'large',
    var other => str(other)
};
assert(label == 'large');
```

分支写作 `模式 => 表达式`，兜底分支写作 `_ => expression`。`Ok`、`Err`、`Some`、`None` 用于匹配结果和可选值。

完整规则见[类型与模式匹配](types-and-patterns.md)。

## 模块

| 词 | 用途 |
| --- | --- |
| `import` | 导入模块或模块成员 |
| `export` | 导出顶层声明、已有绑定或默认值 |
| `from` | 指定命名导入或默认导入的模块路径 |
| `as` | 给导入或导出名字起别名 |
| `default` | 声明模块的默认导出 |

`as` 用于重命名，`default` 用于默认导出。多文件示例见[模块与错误处理](modules-and-errors.md)。

## 错误处理

| 词或符号 | 用途 |
| --- | --- |
| `try` | 执行可能抛错的代码块 |
| `catch` | 捕获错误并声明接收变量 |
| `finally` | 完成、返回或错误后的清理代码块 |
| `throw` | 抛出一个脚本值 |
| 后缀 `?` | 取出结果中的值，或从当前函数提前返回 `Err` / `None` |

```rgs
let recovered = false;
try {
    throw 'failed';
} catch (error) {
    recovered = error == 'failed';
}
assert(recovered);
```

`catch` 捕获抛出的错误；`result.err(...)` 返回普通结果值，后缀 `?` 将失败结果提前返回。使用方法见[错误处理教程](modules-and-errors.md)。

## 预设层的词汇

以下词用于预设声明、状态和动作，具体写法见[录制与布局参考](recording-reference.md)。

| 场景 | 词汇 |
| --- | --- |
| 预设与脚本块 | `preset`、`script` |
| 窗口与捕获源 | `window`、`process`、`title`、`source`、`screen`、`area`、`client`、`follow` |
| 丢失源与背景 | `missing`、`transparent`、`color`、`image`、`background` |
| 输出与录制 | `record`、`copy`、`save`、`to` |
| 布局与形状 | `layout`、`region`、`place`、`shape`、`rect`、`ellipse`、`rounded`、`polygon` |
| 状态及触发条件 | `initial`、`state`、`enter`、`exit`、`on`、`when`、`after`、`repeat` |
| 等待与跳转 | `wait`、`await`、`timeout`、`goto` |
| 录制及界面动作 | `start`、`stop`、`cancel`、`select`、`show`、`hide`、`finish` |
| 通知及调试动作 | `emit`、`log`、`assert`、`breakpoint` |
| 场景属性 | `move`、`visible`、`opacity`、`off` |
| 状态条件和数据 | `exists`、`recording`、`busy`、`idle`、`width`、`height`、`elapsed` |
| 事件名称 | `found`、`lost`、`closed`、`moved`、`resized`、`stopped`、`saved`、`cancelled`、`error` |

`await` 用于预设层的等待动作。预设可用的配置属性和枚举项可通过编辑器补全查看，诊断提示会指出无效配置。

## 类型、函数与成员名称

类型、函数与对象成员使用名称访问。例如，`number`、`Result`、`Option` 是类型名称；`math`、`result`、`option` 是标准库对象；`print`、`assert` 是函数。`math.abs(...)` 通过成员访问调用函数。`Ok`、`Err`、`Some`、`None` 是模式名称。

脚本不支持 `class`、`interface`、`type`、`new`、`async`、`await`、`yield`、`typeof`。类型注解见[类型与模式匹配](types-and-patterns.md)，异步 API 的调用方式见[函数与闭包](language.md#4-函数与闭包)。
