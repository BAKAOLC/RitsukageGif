# 模块与错误处理

模块用于抽取公共函数和配置。错误处理分两类：可以预期的失败用 `Result` / `Option`，抛出的错误用 `try` / `catch`。

## 导出公共逻辑

例如，保存下面的代码为 `common.rgs`：

```rgs
export const defaultScale: number = 1;
export fn clamp(value: number, min: number, max: number): number {
    if (value < min) return min;
    if (value > max) return max;
    return value;
}
export default fn double(value: number): number {
    return value * 2;
}
```

然后在同目录的另一个文件中使用：

```rgs
import double, {clamp, defaultScale as scale} from './common.rgs';
let result = clamp(double(3), 0, 10) * scale;
assert(result == 6);
```

模块路径相对于包含导入语句的文件解析，必须指定文件名和 `.rgs` 扩展名。模块不按 npm 包名解析。

## 导入、导出的其他形式

| 写法 | 作用 |
| --- | --- |
| `import {name} from './file.rgs';` | 命名导入 |
| `import {name as local} from './file.rgs';` | 命名导入并改名 |
| `import value from './file.rgs';` | 默认导入 |
| `import * as lib from './file.rgs';` | 模块命名空间，通过 `lib.name` 访问 |
| `import './file.rgs';` | 执行模块，不声明本地名字 |
| `export let name = value;` | 导出变量 |
| `export const name = value;` | 导出常量 |
| `export fn name(...) { ... }` | 导出函数 |
| `export {local as publicName};` | 导出已有绑定并改名 |
| `export default expression;` | 导出默认值 |

导出语句放在模块顶层。转发其他模块的成员时，先导入，再导出。

```rgs
import * as common from './common.rgs';
assert(common.clamp(12, 0, 10) == 10);
```

命名导入读取导出变量的当前值，但导入方不能重新给导入名字赋值。模块对象的导出成员也不可被导入方替换，但导出数组和对象的内容仍可修改。

### 加载和循环依赖

同一个解释器实例中，成功导入的模块会缓存，重复导入不会重新执行。模块有独立的局部作用域。循环导入会报错；应把双方共用的部分抽到第三个模块。导入嵌套超过深度限制时会报错。

还支持 `import './file.rgs' as name;` 形式：显式导出模块得到模块对象，没有显式导出时得到该文件顶层 `return` 的值。新公共模块建议统一使用显式 `export`。

显式导出不能与顶层 `return` 或 `?` 传播混用。需要传播错误时，将逻辑放入导出的函数。

## 显式结果 Result

`Result<T,E>` 表示操作成功得到 `T`，或者失败得到 `E`。构造值时使用小写的 `result` 表。

```rgs
fn divide(a: number, b: number): Result<number,string> {
    if (b == 0) return result.err('divisor is zero');
    return result.ok(a / b);
}
let outcome = divide(10, 2);
let text = outcome switch {
    Ok(var value) => 'value=' + str(value),
    Err(var error) => error
};
assert(text == 'value=5');
```

`isOk`、`isErr` 可检查状态。只有成功结果可以读取 `.value`，只有失败结果可以读取 `.error`，读取错误的一侧会报错。使用模式匹配可以把状态判断和取值放在一起。

| 方法 | 行为 |
| --- | --- |
| `unwrapOr(fallback)` | 成功时取值，否则取已经计算好的默认值 |
| `unwrapOrElse(error => fallback)` | 失败时才调用默认值函数 |
| `map(value => next)` | 只转换成功值 |
| `mapErr(error => next)` | 只转换错误值 |
| `andThen(value => result)` | 成功后调用另一个返回 `Result` 的操作 |
| `orElse(error => result)` | 失败后调用返回 `Result` 的恢复操作 |

```rgs
let value = result.ok(3)
    .map(x => x * 2)
    .andThen(x => result.ok(x + 1))
    .unwrapOr(0);
assert(value == 7);
```

`andThen`、`orElse` 的回调必须返回 `Result`。`map` 不自动展开嵌套结果：回调返回 `Result` 时会得到结果中的结果。

## 可选值 Option

`Option<T>` 表示有值或无值，不携带错误原因。

```rgs
let selected: Option<string> = option.some('main');
let absent: Option<string> = option.none;
let name = absent.unwrapOr('default');
assert(selected.isSome and absent.isNone);
assert(name == 'default');
assert(option.fromNullable(null).isNone);
assert(option.fromNullable(0).isSome);
```

无值状态直接使用 `option.none`。`fromNullable` 仅将 `null` 转为无值，`0`、`false` 和空字符串仍是有值。

`Option` 提供 `isSome`、`isNone`、`.value`，以及 `unwrapOr`、`unwrapOrElse`、`map`、`andThen`、`orElse`。无值时读取 `.value` 会报错。它的 `unwrapOrElse` 和 `orElse` 回调没有错误参数。

`some.okOr(error)` 将有值转换为 `Ok(value)`，将无值转换为 `Err(error)`。

## 用 ? 提前返回失败

后缀 `?` 在成功或有值时取出内部值；遇到 `Err` 或 `None` 时，立即从**当前函数**返回原结果，不再执行该函数剩余语句。

```rgs
fn parsePositive(text: string): Result<number,string> {
    let value = result.attempt(() => number(text))?;
    if (value <= 0) return result.err('must be positive');
    return result.ok(value);
}
assert(parsePositive('12').value == 12);
assert(parsePositive('-1').isErr);
assert(parsePositive('invalid').isErr);
```

传播规则：

- 使用 `Result` 的 `?` 后，正常路径也应返回 `Result`。
- 使用 `Option` 的 `?` 后，正常路径也应返回 `Option`。
- 一次函数执行不能混用 `Result` 和 `Option` 的 `?`，即使当时都是成功值。
- 转换错误类型使用 `mapErr`；将 `Option` 转为 `Result` 使用 `okOr`。传播时不自动转换。
- `Err` 提前返回不会触发外层 `catch`。
- `condition ? yes : no` 仍是三元条件表达式，和后缀传播是两种用法。

在录制预设里，脚本或事件回调返回 `Err` 会被宿主作为失败处理，并取消相应录制工作。普通工具函数内部的 `Err` 仍由调用方决定如何处理。

## 抛出、捕获与清理

```rgs
let message = '';
let finalized = false;
try {
    throw 'operation failed';
} catch (error) {
    message = str(error);
} finally {
    finalized = true;
}
assert(message == 'operation failed' and finalized);
```

`throw` 可以抛出脚本值。捕获脚本 `throw` 时，`error` 是原值；捕获 API 异常时通常得到消息字符串，可用 `str(error)` 读取错误文本。

`finally` 在代码块完成、返回或报错后执行。块内的 `return` 或错误会替代之前的结果，因此清理代码通常不应返回业务结果。

取消会终止脚本执行，不能通过 `catch`、`result.attempt` 或 `finally` 恢复。取消期间，清理语句也可能中断；应用负责释放录制资源。

### 将抛出的错误变成 Result

```rgs
let attempt = result.attempt(() => {
    throw 'unavailable';
});
assert(attempt.isErr);
assert(attempt.error == 'unavailable');
```

`result.attempt(callback)` 成功时返回 `Ok(callback结果)`，错误时返回 `Err(消息字符串)`。如果回调本来返回 `Result`，它不会自动展平，仍会再包一层 `Ok`。处理可能抛错的操作时使用 `result.attempt`；函数内部检测到预期失败时，直接返回 `result.err(...)`。
