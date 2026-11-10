# 类型与模式匹配

## 类型注解

注解用于限制变量、参数和返回值的类型。不写注解时，变量可以重新赋值为其他类型。

```rgscript
let fps: number = 30;
let title: string = "录制";
let visible: boolean = true;

fn area(width: number, height: number): number {
    return width * height;
}
```

| 类型 | 接受的值 |
| --- | --- |
| `number` | 有限数字 |
| `string` | 字符串 |
| `boolean` | `true` 或 `false` |
| `null` | 空值 |
| `array` | 数组，元素类型不限 |
| `object` | 普通对象 |
| `function` | 可调用的函数 |
| `any`、`unknown` | 任意值 |
| `void` | 空返回值 `null`，通常用在函数返回注解 |
| `never` | 没有可赋的普通值，用于表达不存在的结果分支 |

`unknown` 在这里也接受任意值，不会要求先收窄类型才能使用。需要约束时，应写出具体类型。

数字字符串不会因为 `: number` 自动变成数字，使用转换函数：

```rgscript
let width: number = number("640");
let label: string = str(width);
```

`number()` 接受有效的十进制数字文本，转换失败会报错。枚举值使用库提供的成员，例如 `OutputFormat.WebP`；可用值以相应 API 的参数说明为准。

## 数组、可空值和联合类型

```rgscript
let delays: number[] = [50, 100, 50];
let selected: object? = null;
let identifier: number | string = "main";
let rows: (number | string)[] = [1, "标题"];
```

| 写法 | 含义 |
| --- | --- |
| `T[]` | 元素为 `T` 的数组 |
| `T?` | `T` 或 `null`，等同于 `T | null` |
| `A | B` | 接受 `A` 或 `B` |
| `(A | B)[]` | 元素可为 `A` 或 `B` 的数组 |
| `T[]?` | 整个数组可以为 `null` |
| `T?[]` | 数组中的元素可以为 `null` |

类型约束也适用于后续赋值和数组写入。`number[]` 不能通过索引赋值或 `push()` 混入字符串；把该数组交给另一个变量不会取消约束。

## 集合和结果类型

| 写法 | 含义 |
| --- | --- |
| `Map` | 键值集合 |
| `Set` | 不重复的值集合 |
| `Result<T, E>` | 成功值为 `T`，错误值为 `E` |
| `Option<T>` | 有一个 `T` 值，或没有值 |

```rgscript
let titles: Map = Map([["main", "主窗口"]]);
let used: Set = Set([1, 2, 2]);
let outcome: Result<number, string> = result.ok(42);
let selected: Option<string> = option.none;
```

`Map` 用 `keys()`、`values()` 或 `entries()` 取得数组后遍历，`Set` 使用 `values()`。`Result` 和 `Option` 的处理方法见 [模块与错误处理](modules-and-errors.md)。

库还会提供捕获、图像等专用类型及枚举。注解可以使用这些已提供的名称；脚本中不能用 `class`、`interface` 或 `type` 创建新类型。

## 编辑器中的类型推断

编辑器会识别自定义函数、局部变量、对象属性、导入项和参数，为调用显示签名，并根据表达式推断结果类型。

```rgscript
fn double(value: number) {
    return value * 2;
}

let values = [1, 2, 3].map(double);
let first = values[0];
```

这里 `double()` 的结果和 `first` 可以推断为 `number`。多个返回分支可能形成联合类型；没有返回值的路径包含 `null`。

推断用于补全和诊断，不会把未注解变量固定成首次赋值的类型。复杂递归或无法确定的动态值可能显示为 `any`；需要稳定的调用约定时，给参数和返回值写注解。

## `is`：判断并取值

`value is pattern` 返回布尔值。模式还可以绑定值，在匹配成功的分支里使用。

```rgscript
let value = 30;
if (value is number fps && fps > 0) {
    print("帧率：" + fps);
}
```

这里 `fps` 可以在右侧条件和 `if` 成功分支中使用，不会成为外层变量。三元表达式的成功分支、`while` 和 `for` 的成功条件也支持这种绑定。

```rgscript
let label = value is string text ? text : "不是字符串";
```

## `switch`：按模式取得结果

```rgscript
let fps = 30;
let quality = fps switch {
    <= 0 => "无效",
    < 24 => "低帧率",
    number n when n <= 60 => "普通",
    _ => "高帧率",
};
```

输入只求值一次，从上到下选择第一个模式匹配且 `when` 条件为真的分支。每个分支用 `=>` 给出结果，分支之间用逗号分隔。

`switch` 是表达式，可以出现在赋值、返回和实参等位置。各分支中的绑定只属于该分支。没有匹配分支时会报错；使用 `_` 可以接住剩余情况。

## 常用模式

| 模式 | 示例 | 含义 |
| --- | --- | --- |
| 字面值 | `null`、`true`、`0`、`"main"` | 匹配相等的值 |
| 忽略值 | `_` | 匹配任意值 |
| 绑定值 | `var item` | 匹配任意值，并保存为 `item` |
| 类型 | `number`、`string name` | 匹配指定类型，可同时绑定 |
| 数值关系 | `>= 0`、`< 100` | 比较数字 |
| 属性 | `{ width: > 0, height: number h }` | 匹配指定属性 |
| 列表 | `[var first, .. var rest]` | 匹配元素和剩余部分 |
| 结果 | `Ok(var value)`、`Err(var error)` | 匹配成功或错误 |
| 可选值 | `Some(var value)`、`None` | 匹配有值或无值 |

### 属性模式

```rgscript
let region = { x: 10, y: 20, width: 640, height: 480 };
if (region is { width: number w, height: number h } && w > 0 && h > 0) {
    print(w * h);
}
```

只检查列出的属性，其余属性不影响匹配。缺少属性与属性值为 `null` 不同；`{ title: null }` 要求 `title` 确实存在且为空。

### 列表模式

```rgscript
let values = [10, 20, 30, 40];
let text = values switch {
    [] => "空数组",
    [var only] => "单个元素：" + only,
    [var first, .. var middle, var last] => first + " 到 " + last,
};
```

没有 `..` 时，元素数量必须完全一致。`..` 匹配任意数量的中间元素，一个列表模式只能出现一次；不需要取值时写 `..` 或 `.. _`。也可以写 `...rest`。

字符串同样可以使用列表模式，单个位置匹配一个 UTF-16 单元，剩余部分绑定为字符串。

### 组合模式

```rgscript
let fps = 30;
let valid = fps is >= 1 and <= 120;
let absent = null is null or false;
let nonzero = fps is not 0;
```

模式中 `not` 优先于 `and`，`and` 优先于 `or`；使用括号明确组合关系。

`or` 两侧必须声明相同的一组绑定名，`not` 内不能声明新绑定。例如：

```rgscript
let value = "main";
if (value is number item or string item) {
    print(item);
}
```

### 匹配结果和可选值

```rgscript
let outcome = result.ok(42);
let message = outcome switch {
    Ok(number value) => "结果：" + value,
    Err(var error) => "失败：" + str(error),
    _ => "未知结果",
};

let selected = option.some("main");
let name = selected switch {
    Some(string text) => text,
    None => "未选择",
    _ => "未知值",
};
```

`Some(null)` 与 `None` 不同：前者确实保存了一个空值，后者没有值。匹配名称也能由库扩展；它们不是新增关键词。
