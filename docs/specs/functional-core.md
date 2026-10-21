# 决策核心

对应 `engineering-baseline` 的 `functional-core`。任务已完成。

## 决策

`ContextAll.Core.Decision.Decide` 的签名是：

`Fin<Transition> Decide(DecisionState state, DecisionInput input)`

它根据当前状态和一次输入，返回拒绝，或返回下一状态加上要执行的效果。同一次状态和同一次输入，两次调用的结果相等。决策本身不发送字节、不写文件、不读时钟。

预期内的拒绝是 `Fin` 的失败值。调用方拿到这个值，决策不为这个原因抛异常，已接受的状态保持不变。当前输入里，`RefuseCount` 以及任何未单独处理的输入都返回失败值 `"refused"`。`AcceptCount` 把 `Count` 加一，并带上一条 `RecordedCount`。

`Transition` 是不可变记录，内含下一状态和 `ImmutableArray<Effect>`。效果是 `Effect` 的封闭记录集。

## 效果由委托执行

决策不调用效果委托。`ContextAll.Effects.SharedEffects.Run` 在决策成功之后，对每条效果调用：

`Func<Effect, CancellationToken, ValueTask<Fin<Unit>>>`

调用使用 `await`。某条效果返回失败值时，`Run` 把该失败值返回给调用方，不再执行后面的效果。

生产代码需要尚未完成的异步结果时，await 该操作。`BannedSymbols.txt` 禁止 `Task.Result`、`Task.Wait` 和 `TaskAwaiter.GetResult`。分析器诊断 `RS0030` 与可空警告一起当作错误。

## 空原谅运算符

`Directory.Build.props` 开启可空引用类型。手写生产代码不使用空原谅运算符（`!`），除非操作者明确批准该处使用。生成文件保持生成器的输出。
