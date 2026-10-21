# 验证

对应 `engineering-baseline` 的 `verification-gates`。任务已完成。

新增一项生产行为时，同一次变更里带上测试：没有该行为时测试失败，有该行为时测试通过。测试在 `tests/`，使用 xUnit。当前测试项目是 `ContextAll.Core.Tests`、`ContextAll.Protocol.Tests`、`ContextAll.Effects.Tests`、`ContextAll.Host.Tests` 和 `ContextAll.App.Tests`。

推送到 GitHub 会触发 `.github/workflows/test.yml`。工作流在 `push` 时运行，安装 .NET 10 SDK，然后执行：

```sh
dotnet test ContextAll.Tests.slnf -c Release
```

套件失败时，该次工作流失败。套件通过时，该次工作流成功。这个工作流不编译 `ContextAll.App.Android` 和 `ContextAll.App.Windows`。

`.githooks/pre-push` 执行同一条命令。套件失败时钩子中止推送，提交不会送出。套件通过时钩子允许推送继续。克隆之后要在本机启用钩子：

```sh
scripts/enable-hooks
```

该脚本把 `core.hooksPath` 设为 `.githooks`。尚未运行脚本的克隆没有本地钩子；远程工作流仍然会跑。钩子和工作流使用同一份解决方案过滤器和同一构建配置。
