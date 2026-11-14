# 验证

对应 `engineering-baseline` 的 `verification-gates`。任务已完成。

新增一项生产行为时，同一次变更里带上测试：没有该行为时测试失败，有该行为时测试通过。

- `.NET` 单元测试与 NativeAOT 桥接测试在 `tests/`，使用 xUnit，包含 `ContextAll.Core.Tests`、`ContextAll.Protocol.Tests`、`ContextAll.Effects.Tests`、`ContextAll.Host.Tests`、`ContextAll.App.Tests` 与 `ContextAll.Native.Tests`。
- `.NET` 端到端规格验证在 `tests/ContextAll.Tests.E2E`，通过 `./scripts/test-e2e.sh --all` 运行。
- Android Compose 与 JNI 桥接测试在 `app/src/test/`（JUnit），配合 Android Lint 检查 `app/` 的 Kotlin 编译、单元测试与清单合规。

推送到 GitHub 会触发 `.github/workflows/test.yml`。工作流在 `push` 时运行，安装 .NET 10 SDK、JDK 21 与原生 `libmsquic`，然后执行：

```sh
dotnet test ContextAll.Tests.slnf -c Release --nologo
./scripts/test-e2e.sh --all
./gradlew :app:testDebugUnitTest :app:lintDebug --no-daemon
```

套件失败时，该次工作流失败。套件通过时，该次工作流成功。

`.githooks/pre-push` 执行同一组命令。套件失败时钩子中止推送，提交不会送出。套件通过时钩子允许推送继续。克隆之后要在本机启用钩子：

```sh
scripts/enable-hooks
```

该脚本把 `core.hooksPath` 设为 `.githooks`。尚未运行脚本的克隆没有本地钩子；远程工作流仍然会跑。钩子和工作流使用同一份解决方案过滤器和同一构建配置。
