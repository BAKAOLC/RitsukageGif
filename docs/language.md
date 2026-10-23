# 脚本语言入门

本页介绍 `.rgs` 脚本中的通用语言。预设文件的 `script { ... }` 块使用同一套语法。录制源、布局和状态机属于预设层，不能直接当作脚本语句使用。应用的录制入口运行预设文件；下面的独立脚本片段用于学习语法，可放入预设脚本块或公共模块。

建议依次阅读本页、[类型与模式匹配](types-and-patterns.md)、[模块与错误处理](modules-and-errors.md)。查某个词时看[关键词索引](keywords.md)。

## 1. 变量与基本值

`let` 声明可重新赋值的变量，`const` 禁止重新绑定。每个变量声明必须有初始值。声明、表达式、`return` 等简单语句以分号结束；函数和控制流程的代码块不必另加分号。

```rgs
let fps = 30;
fps = 60;
const name = 'preview';
let enabled = true;
let missing = null;
assert(fps == 60);
```

脚本使用动态类型，支持可选类型注解。没有注解的变量可以重新赋为其他类型；编辑器根据声明和后续操作推断类型。

```rgs
let value = 1;
value = 'ready';
const items = [1, 2];
items.push(3);
assert(items.length == 3);
```

`const` 保护变量绑定，不会递归冻结数组和对象。变量按块作用域查找；同一作用域不能重复声明同名变量，内部块可以遮蔽外部名字。

### 数字、字符串与注释

数字统一为有限的双精度浮点数，支持十进制、科学计数法、十六进制和二进制字面量。

```rgs
let values = [12, 1.5, 2e3, 0xff, 0b1010];
let title = "A\nB";
// 单行注释
# 也是单行注释
/* 多行注释 */
assert(values[4] == 10);
```

字符串可用单引号或双引号，支持 `\n`、`\r`、`\t`、`\\`、`\'`、`\"`。文本拼接使用 `+` 或 `str()`；字符串不支持反引号模板和 `${...}` 插值。

## 2. 表达式与运算符

| 类别 | 写法 | 说明 |
| --- | --- | --- |
| 算术 | `+ - * / %` | 数字运算；除数为零会报错 |
| 字符串拼接 | `+` | 任一操作数为字符串时拼接文本 |
| 比较 | `== != < <= > >=` | 大小比较使用数字；不提供 `===`、`!==` |
| 布尔 | `! && ||` | 也可写 `not`、`and`、`or` |
| 空值回退 | `a ?? b` | 只有 `a` 为 `null` 才计算 `b` |
| 条件表达式 | `test ? a : b` | 条件必须为布尔值 |
| 赋值 | `= += -= *= /= %=` | 变量、可写成员及索引 |
| 后缀更新 | `x++`、`x--` | 返回更新前的值；没有前缀 `++x` |
| 通配匹配 | `text ~ pattern` | 通配符匹配，支持 `*`、`?` |

```rgs
let width = 640;
let doubled = width * 2;
let label = 'width=' + str(width);
let fallback = null ?? 'default';
let size = width > 500 ? 'large' : 'small';
assert('capture.gif' ~ '*.gif');
assert(doubled == 1280 and size == 'large');
```

条件、逻辑运算和谓词回调必须使用 `boolean` 值，非布尔值会报错。`&&`、`||`、`??` 和条件表达式都按需要计算分支。

使用括号明确复杂表达式的顺序。`==` 不会在字符串与数字之间隐式转换，也不会逐元素比较数组和对象。

### 可选访问

```rgs
let entry = null;
let title = entry?.title ?? 'untitled';
let first = entry?.[0];
assert(title == 'untitled');
assert(first == null);
```

可选成员、索引、调用和切片分别写作 `value?.name`、`value?.[index]`、`callback?.(args)`、`items?.[start:end]`。同一条可选访问链在空值处短路；括号结束当前链，后续普通访问仍可能报错。

## 3. 流程控制

### 条件与循环

```rgs
let total = 0;
for (let i = 0; i < 5; i++) {
    if (i == 2) continue;
    total += i;
}
while (total < 10) {
    total++;
}
assert(total == 10);
```

`if` 可接 `else`；`break` 结束当前循环，`continue` 进入下一次迭代。两者只能出现在循环内。

```rgs
let total = 0;
for (const item of [1, 2, 3]) {
    total += item;
}
assert(total == 6);
```

`for (let item in sequence)` 与 `of` 使用相同的遍历规则：数组遍历元素，对象遍历键。字符串不能直接用于这种循环，可按索引读取字符。

`switch` 是可返回值的模式匹配表达式，见[模式匹配](types-and-patterns.md#模式匹配)。分支使用 `模式 => 表达式` 的形式。

## 4. 函数与闭包

函数声明使用 `fn`，也可以写 `function`。函数可作为值传递、返回，也能捕获外层变量。函数声明在所在块内提前可见；普通变量声明没有这种行为。

```rgs
fn add(a, b = 1) {
    return a + b;
}
const twice = x => x * 2;
const combine = (a, b) => {
    return a + b;
};
assert(add(2) == 3);
assert(twice(combine(2, 3)) == 10);
```

箭头函数的表达式体自动返回该表达式；块体需要显式 `return`。不执行带值的 `return` 时，函数返回 `null`。

```rgs
fn makeCounter(start) {
    let value = start;
    return () => {
        value++;
        return value;
    };
}
let counter = makeCounter(10);
assert(counter() == 11);
assert(counter() == 12);
```

### 剩余参数与展开

```rgs
fn sum(...values) {
    let total = 0;
    for (const value of values) total += value;
    return total;
}
let base = [1, 2];
let all = [...base, 3];
assert(sum(...all) == 6);
```

剩余参数必须放最后。默认值只用于未提供的参数；显式传入 `null` 不会触发默认值。异步 API 调用会等待操作完成，脚本无需添加 `await`；不支持声明 `async` 函数或使用 Promise。

## 5. 对象、解构与切片

### 对象与展开

```rgs
let x = 10;
let original = { x, y: 20 };
let changed = { ...original, x: 30 };
assert(changed.x == 30);
assert(original.x == 10);
```

对象字段可用 `object.name` 或 `object['name']` 访问。展开是浅复制，嵌套对象和数组仍可能共享。

### 解构

```rgs
let [first, , third = 9, ...tail] = [1, 2];
let { x: left, y = 0, ...extra } = { x: 10, label: 'A' };
assert(first == 1 and third == 9);
assert(left == 10 and y == 0);
assert(extra.label == 'A');
```

对象中的 `x: left` 表示改名，`y = 0` 表示缺失时使用默认值。数组支持跳过位置，剩余绑定收集其余内容。缺失值没有默认值时会报错；存在且值为 `null` 的字段不会触发默认值。

解构也可用于函数参数和已有变量的赋值。对象解构赋值需要括号，避免被当作代码块。

```rgs
fn point({x, y = 0}) {
    return x + y;
}
let left = 0;
let right = 0;
[left, right] = [1, 2];
({x: left} = {x: 5});
assert(point({x: left}) == 5 and right == 2);
```

### 索引与切片

```rgs
let items = [0, 1, 2, 3, 4];
let middle = items[1:4];
let everyOther = items[::2];
let reverse = items[::-1];
assert(middle[0] == 1 and middle.length == 3);
assert(everyOther[2] == 4 and reverse[0] == 4);
assert('hello'[-2:] == 'lo');
```

切片形式为 `[开始:结束:步长]`，结束位置不包含在结果中。各部分可以省略，负索引从末尾计算，负步长用于反向。边界会截到可用范围；索引和步长必须是整数，步长不能为零。切片生成新数组或字符串，不能作为赋值目标。普通索引 `items[index]` 要求非负索引，不支持用 `items[-1]` 取末项。字符串索引和切片按 UTF-16 代码单元处理，可能拆开代理对或组合字符。

## 6. 常用集合处理

数组的 `map` 生成转换后的数组，`filter` 保留满足条件的元素，`reduce` 逐个累积结果。谓词必须返回布尔值。

```rgs
let doubled = [1, 2, 3].map(value => value * 2);
let selected = doubled.filter(value => value > 2);
let total = selected.reduce((sum, value) => sum + value, 0);
assert(total == 10);
```

`push`、`pop`、`shift`、`unshift`、`reverse` 会修改原数组；切片、`concat`、`map`、`filter` 生成新数组。对象可用 `keys()`、`values()`、`entries()` 转为可遍历数组。

```rgs
let data = {x: 1, y: 2};
let total = 0;
for (const [key, value] of data.entries()) total += value;
assert(total == 3);
```

`Map` 和 `Set` 是标准库提供的集合，直接调用名字构造，不使用 `new`。

```rgs
let lookup: Map = Map([['main', 1], ['preview', 2]]);
lookup.set('extra', 3);
let unique: Set = Set([1, 1, 2]);
unique.add(3);
assert(lookup.get('extra') == 3);
assert(unique.size == 3 and unique.has(2));
```

`Map` 提供 `get/set/has/delete/clear/keys/values/entries`，`Set` 提供 `add/has/delete/clear/values`。它们的大小属性是 `size`；数组和字符串使用 `length`。遍历 `Map`、`Set` 时，先调用 `entries()` 或 `values()` 获取数组。

## 下一步

为函数边界和长期配置加上[类型注解](types-and-patterns.md)，用[Result/Option](modules-and-errors.md#显式结果-result)表达可以预期的失败，再把公共逻辑放到独立模块。
