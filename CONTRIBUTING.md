# 参与开发

## 准备

克隆之后启用仓库自带的推送前钩子：

```sh
scripts/enable-hooks
```

脚本把 `core.hooksPath` 设为 `.githooks`。钩子在推送前运行 `dotnet test ContextAll.Tests.slnf -c Release`，失败时中止推送。GitHub Actions 在每次 push 时跑同一条命令。

## 代码约定

- 可空引用类型开启，可空警告按错误处理。
- 手写生产代码不使用空原谅运算符（`!`），确有需要时先征得维护者同意。
- 需要异步结果时 `await`。`BannedSymbols.txt` 禁止 `Task.Result`、`Task.Wait` 和 `TaskAwaiter.GetResult`，违反时报 `RS0030` 错误。
- `ContextAll.Core` 里的决策不发送字节、不写文件、不读时钟。预期内的拒绝用 `Fin` 的失败值返回，不抛异常。需要执行的工作作为效果交给 `ContextAll.Effects`。
- 模块引用方向见 [docs/architecture.md](docs/architecture.md)，新增引用前先看这张表。

## 测试

新增生产行为时，同一次变更里带上测试：没有该行为时失败，有该行为时通过。测试使用 xUnit，放在 `tests/` 下对应的项目里。

运行单个测试：

```sh
dotnet test tests/ContextAll.Core.Tests -c Release --filter "FullyQualifiedName~DecisionTests"
```

## 需求与文档

需求用 [OpenSpec](https://github.com/Fission-AI/OpenSpec) 管理。规格在 `openspec/specs/`，进行中的变更在 `openspec/changes/`。

一个 capability 的实现任务全部完成后，在同一次变更里：

1. 用中文改写 `docs/specs/<capability>.md`，只写代码和测试已经成立的行为。
2. 更新 [docs/README.md](docs/README.md) 里的同步表。
3. 决策有变化时在 `docs/adr/` 新增一篇 ADR。

## 许可证

提交的代码按 [MIT 许可证](LICENSE) 发布。
