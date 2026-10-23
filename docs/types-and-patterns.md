# 类型与模式匹配

本页介绍类型注解、类型推断和模式匹配。变量、函数等基本用法见[语言入门](language.md)。

## 类型一览

| 类型 | 含义 |
| --- | --- |
| `number` | 有限双精度数字，整数和小数使用同一类型 |
| `string` | 字符串 |
| `boolean` | `true` 或 `false` |
| `null` | 空值 |
| `array` | 元素可不同类型的动态数组 |
| `object` | 普通对象 |
| `function` | 可调用的函数；此注解不描述参数和返回签名 |
| `Map`、`Set` | 标准库的键值集合和去重集合，使用 `Map` / `Set` 注解 |
| `any`、`unknown` | 均接受任意值；`unknown` 不要求先收窄再使用 |
| `void` | 无有效返回值，函数可返回 `null` |
| `never` | 不包含任何正常值，常见于结果推断，如 `Result<number,never>` |
| `T[]` | 元素满足 `T` 的数组 |
| `T?` | `T` 或 `null` |
| `A\|B` | 联合类型 |
| `Result<T,E>` | 成功值为 `T`、失败值为 `E` 的显式结果 |
| `Option<T>` | 有 `T` 类型的值，或没有值 |

录制 API 还提供对象、枚举和配置类型，可通过编辑器的类型补全查看。`Result`、`Option` 用于类型注解，`result`、`option` 对象提供创建和处理这些值的方法。

## 可选注解

```rgs
let fps: number = 30;
let title: string? = null;
let samples: number[] = [1, 2, 3];
fn scale(value: number, factor: number = 2): number {
    return value * factor;
}
assert(scale(samples[0]) == 2);
```

注解会在运行时检查变量初始化、后续赋值、函数参数和返回值。编辑器也会检查能确定的类型不匹配。注解检查不会执行类型转换；将字符串传给 `number` 参数会报错。

类型转换使用 `number('12')`、`str(12)` 等函数。运算符遵循各自的规则，例如字符串参与 `+` 时会执行文本拼接。

没有注解的 `let` 允许改变类型。动态数据、函数调用的副作用或复杂循环可能使编辑器无法确定类型，此时推断为 `any`。运行时仍会检查操作是否合法。

### 数组与联合类型的括号

```rgs
let mixed: (number|string)[] = [1, 'two'];
let either: number|string[] = ['one', 'two'];
let optionalList: number[]? = null;
let optionalItems: number?[] = [1, null];
assert(mixed.length == 2);
assert(optionalItems[1] == null);
```

- `(number|string)[]`：一个数组，其元素可以是数字或字符串。
- `number|string[]`：一个数字，或者字符串数组。
- `number[]?`：整个数组可为空。
- `number?[]`：数组本身不为空，但元素可为空。

显式元素类型的数组会约束写入。没有注解的数组允许不同类型的元素；编辑器合并已知元素类型，无法确定的元素类型使用 `any`。

### 类型注解的范围

类型注解使用已有类型及其数组、可空、联合形式，以及 `Result<T,E>`、`Option<T>`。不支持自定义类型声明、泛型函数、交叉类型和函数签名注解。类型转换使用转换函数；不支持 `as T` 或后缀非空断言。

## 模式匹配

模式既可以用于返回值的 `switch` 表达式，也可以用于产生布尔值的 `is` 表达式。

### switch 表达式

```rgs
fn describe(value): string {
    return value switch {
        null => 'empty',
        number n when n > 0 => 'positive: ' + str(n),
        number => 'zero or negative',
        string text => text,
        _ => 'other'
    };
}
let text = describe(3);
assert(text == 'positive: 3');
```

从上到下选择第一个模式及守卫都满足的分支，只计算该分支的表达式。被匹配的值只求值一次。结果可以赋值、返回或作为函数参数。

分支之间使用逗号，右侧填写表达式。无分支匹配时会报错；匹配范围无法列举完整时，使用 `_` 兜底。

### 常见模式

| 模式 | 示例 | 含义 |
| --- | --- | --- |
| 字面量 | `null`、`true`、`42`、`'ready'` | 匹配该值 |
| 通配 | `_` | 匹配任何值，不声明变量 |
| 绑定 | `var item` | 匹配任何值并绑定名字 |
| 类型 | `number`、`string text` | 检查类型，可同时绑定名字 |
| 属性 | `{x: number x, y: 0}` | 检查指定属性，允许存在其他属性 |
| 列表 | `[var first, .. var rest]` | 匹配元素并收集剩余部分 |
| 关系 | `>= 0`、`< 100` | 与字面量比较 |
| 组合 | `>= 0 and <= 100` | 同时满足 |
| 或 | `0 or 1` | 任一满足 |
| 否定 | `not null` | 不满足内部模式 |
| 构造模式 | `Ok(var x)`、`Err(var error)` | 匹配结果种类并提取内部值 |

```rgs
let point = {x: 12, y: 0, name: 'A'};
let position = point switch {
    {x: number x, y: 0} => x,
    _ => -1
};
let sequence = [1, 2, 3, 4];
let count = sequence switch {
    [1, .. var middle, 4] => middle.length,
    _ => 0
};
let accepted = 30 is >= 0 and <= 100;
assert(position == 12 and count == 2 and accepted);
```

列表模式没有剩余部分时要求长度恰好匹配。剩余部分最多一个，可位于中间；也支持 `...rest` 风格的剩余写法。属性缺失不等于属性存在且为 `null`。

关系模式右侧是字面量。涉及变量、函数调用或其他复杂条件时写到 `when` 守卫中。组合优先级为 `not` 高于 `and`，`and` 高于 `or`；可用括号明确顺序。

### is 与变量作用域

```rgs
let input = 'hello';
let size = 0;
if (input is string text && text.length > 0) {
    size = text.length;
}
assert(size == 5);
```

成功绑定的变量可用于 `&&` 右侧和条件成功的分支；也支持 `while`、`for` 条件成功后的循环体和条件表达式的成功分支。失败分支不能使用这些绑定。

单独写 `let matched = value is number n;` 不会把 `n` 声明到外层作用域。布尔 `||` 不向外导出绑定。模式 `or` 的两侧必须绑定相同的一组名字；`not` 模式不能声明绑定变量。同一模式里不能重复绑定同名变量。

### Result 和 Option 模式

```rgs
let outcome: Result<number,string> = result.ok(12);
let message = outcome switch {
    Ok(var value) => 'value=' + str(value),
    Err(var error) => error
};
let selected: Option<string> = option.none;
let name = selected switch {
    Some(var value) => value,
    None => 'default'
};
assert(message == 'value=12' and name == 'default');
```

`Ok`、`Err`、`Some`、`None` 用于匹配和提取值。创建值使用 `result.ok(...)`、`result.err(...)`、`option.some(...)`、`option.none`。

编辑器会提示可确定的重复、不可达和缺失分支。`Result`、`Option` 等分支种类确定的类型支持完整性检查；带守卫的分支不一定覆盖该模式的所有值，仍可能需要补充无守卫分支。
