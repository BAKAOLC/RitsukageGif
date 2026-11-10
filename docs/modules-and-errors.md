# 模块与错误处理

## 导入公共函数

把公共函数保存为 `common.rgs`：

```rgscript
export fn clamp(value: number, min: number, max: number): number {
    if (value < min) return min;
    if (value > max) return max;
    return value;
}

export const defaultScale = 2;
```

在同目录的脚本中导入：

```rgscript
import {clamp, defaultScale as scale} from './common.rgs';

const width = clamp(320 * scale, 100, 1920);
print(width);
```

导入路径相对于写下 `import` 的文件。路径要包含完整文件名；不会自动补扩展名。同一次脚本运行中，同一个模块重复导入时只执行一次。循环导入会报错，应把共用部分放到第三个模块。

| 写法 | 用途 |
| --- | --- |
| `import {name} from './common.rgs';` | 导入命名成员 |
| `import {name as local} from './common.rgs';` | 导入后改名 |
| `import * as common from './common.rgs';` | 用 `common.name` 访问成员 |
| `import value from './common.rgs';` | 导入默认值 |
| `import './common.rgs';` | 只执行模块 |
| `export let name = value;` | 导出变量 |
| `export const name = value;` | 导出常量 |
| `export fn name(...) { ... }` | 导出函数 |
| `export {name as publicName};` | 导出已有名字并改名 |
| `export default value;` | 导出默认值 |

默认导出也可以是函数：

```rgscript
export default fn double(value: number): number {
    return value * 2;
}
```

导出语句写在模块顶层。命名导入会读取导出变量的当前值，导入方不能重新给这个名字赋值。`import './common.rgs' as common;` 也可导入模块；文件没有显式导出时，得到它顶层 `return` 返回的值。

显式导出的模块不能在顶层使用 `return` 或后缀 `?`。把需要返回结果的逻辑写在函数内。

## Result：成功或失败

`Result<T,E>` 表示成功值 `T` 或错误值 `E`。用 `result.ok(value)` 和 `result.err(error)` 构造：

```rgscript
fn divide(a: number, b: number): Result<number,string> {
    if (b == 0) return result.err('除数不能为零');
    return result.ok(a / b);
}

const text = divide(10, 2) switch {
    Ok(var value) => '结果：' + str(value),
    Err(var error) => error
};
print(text);
```

`isOk`、`isErr` 用于判断状态。成功时读取 `.value`，失败时读取 `.error`；读取不符合状态的成员会报错。

| 方法 | 返回值 |
| --- | --- |
| `unwrapOr(fallback)` | 成功值，或默认值 |
| `unwrapOrElse(error => fallback)` | 成功值，或失败时调用函数得到的默认值 |
| `map(value => next)` | 只转换成功值，返回 `Result` |
| `mapErr(error => next)` | 只转换错误值，返回 `Result` |
| `andThen(value => nextResult)` | 成功后继续另一个返回 `Result` 的操作 |
| `orElse(error => nextResult)` | 失败后执行返回 `Result` 的恢复操作 |

```rgscript
const value = result.ok(3)
    .map(x => x * 2)
    .andThen(x => result.ok(x + 1))
    .unwrapOr(0);
assert(value == 7);
```

`andThen` 和 `orElse` 的回调必须返回 `Result`。`map` 会保留回调返回的值，包括嵌套的 `Result`。

## Option：有值或无值

`Option<T>` 用于表示可能不存在的值：

```rgscript
const selected: Option<string> = option.some('main');
const absent: Option<string> = option.none;

print(selected.value);
print(absent.unwrapOr('default'));
```

`option.fromNullable(value)` 只把 `null` 转为 `None`，`0`、`false` 和空字符串仍是有值。

| 成员或方法 | 用途 |
| --- | --- |
| `isSome`、`isNone` | 判断是否有值 |
| `.value` | 读取值；`None` 时会报错 |
| `unwrapOr(fallback)` | 取值或使用默认值 |
| `unwrapOrElse(() => fallback)` | 无值时才计算默认值 |
| `map(value => next)` | 转换已有值，返回 `Option` |
| `andThen(value => nextOption)` | 有值时继续另一个返回 `Option` 的操作 |
| `orElse(() => nextOption)` | 无值时提供另一个 `Option` |
| `okOr(error)` | 有值转为 `Ok(value)`，无值转为 `Err(error)` |

模式匹配中使用 `Some(var value)` 和 `None`。

## 用 ? 提前返回

后缀 `?` 取出 `Ok` 或 `Some` 的值；遇到 `Err` 或 `None`，立即从当前函数返回该结果。

```rgscript
fn positive(text: string): Result<number,string> {
    const value = result.attempt(() => number(text))?;
    if (value <= 0) return result.err('必须大于零');
    return result.ok(value);
}

print(positive('12').unwrapOr(0));
```

使用 `Result` 传播的函数，正常路径也要返回 `Result`；使用 `Option` 传播的函数则返回 `Option`。同一次函数调用不能混用两种传播。需要转换时，使用 `mapErr` 或 `okOr`。

`Err` 是普通结果，不会进入 `catch`。预设脚本或事件回调返回 `Err` 时，会结束对应工作并报告失败。

## try、catch 和 finally

```rgscript
try {
    throw '无法读取输入';
} catch (error) {
    print('失败：', str(error));
} finally {
    print('处理结束');
}
```

`throw` 可以抛出任意脚本值，`catch` 接收该值。API 错误通常以消息字符串接收。`finally` 在正常完成、返回和报错后执行；其中的返回或错误会替代先前结果。

希望把抛出的错误改为 `Result` 时，使用 `result.attempt(callback)`：

```rgscript
const parsed = result.attempt(() => number('12'));
print(parsed.unwrapOr(0));
```

回调成功得到 `Ok(value)`，报错得到 `Err(错误消息)`。回调已经返回 `Result` 时，仍会再包一层 `Ok`。取消会终止执行，不能用 `catch` 或 `result.attempt` 恢复；取消时也不要依赖 `finally` 中的语句全部执行。
