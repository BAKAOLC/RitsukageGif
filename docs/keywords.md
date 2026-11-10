# 关键词速查

这些词在相应语法位置表示声明、控制流程或模式。名称区分大小写。

## 声明和函数

| 写法 | 用途 |
| --- | --- |
| `let` | 声明可重新赋值的变量 |
| `const` | 声明不可重新赋值的绑定 |
| `fn`、`function` | 声明函数或创建函数值 |
| `return` | 从函数返回 |

```rgscript
const limit = 60;
fn clamp(value) {
    return value > limit ? limit : value;
}
```

## 控制流程

| 写法 | 用途 |
| --- | --- |
| `if`、`else` | 按条件执行 |
| `while` | 条件满足时循环 |
| `for` | 计数循环或遍历 |
| `in`、`of` | 在 `for` 中遍历数组元素或对象键 |
| `break` | 退出当前循环 |
| `continue` | 开始当前循环的下一轮 |

## 值与布尔运算

| 写法 | 用途 |
| --- | --- |
| `true`、`false` | 布尔值 |
| `null` | 空值 |
| `and`、`or`、`not` | 与、或、非；表达式中也可以写 `&&`、`||`、`!` |

## 模式匹配

| 写法 | 用途 |
| --- | --- |
| `is` | 判断值是否符合模式 |
| `switch` | 从多个模式分支中选择结果 |
| `when` | 为匹配分支增加条件 |
| `var` | 在模式中绑定匹配到的值 |
| `and`、`or`、`not` | 组合、选择或否定模式 |
| `_` | 匹配任意值，不创建变量 |

```rgscript
let message = 30 switch {
    < 0 => "无效",
    number n when n <= 60 => "普通",
    _ => "高帧率",
};
```

`number` 等类型名称和 `Ok`、`Some` 等匹配名称见 [类型与模式匹配](types-and-patterns.md)。

## 模块

| 写法 | 用途 |
| --- | --- |
| `import` | 导入模块或其导出项 |
| `from` | 指定模块路径 |
| `as` | 给导入项或导出项改名 |
| `export` | 导出变量、函数或已有绑定 |
| `default` | 指定默认导出 |

```rgscript
import { area as measure } from "./geometry.rgs";
export const size = measure(640, 480);
```

## 错误处理

| 写法 | 用途 |
| --- | --- |
| `try` | 执行需要处理错误的代码 |
| `catch` | 接收抛出的错误 |
| `finally` | 在离开处理块时执行清理 |
| `throw` | 抛出错误 |

## 类型和库名称

`number`、`string`、`boolean` 等用于类型注解；`Map`、`Set`、`Result` 等是可用类型。`math.max`、`result.ok` 等是库成员访问，不是关键词。

运算符与标点的完整用法见 [基本语法](language.md)。
