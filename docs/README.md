# ContextAll 文档

正文使用中文。这里记录仓库里已经成立的行为。OpenSpec 里的 spec 仍是要求的原文；一篇文档只描述对应 spec 已经实现、测试通过的部分。

## 和 spec 的对应

capability 的目录名就是文档文件名。

`openspec/changes/<change>/specs/<capability>/spec.md` 对应 `docs/specs/<capability>.md`。

一个 capability 的实现任务全部勾选之后，按该 spec 里已经成立的要求改写这篇文档，并更新下面的表。任务还没做完的 spec，不写成当前行为。决策记录放在 `docs/adr/`，不并进 capability 文档。

## 已同步

| capability | 文档 | 变更 |
| --- | --- | --- |
| `repository-baseline` | [仓库](specs/repository-baseline.md) | `engineering-baseline` |
| `functional-core` | [决策核心](specs/functional-core.md) | `engineering-baseline` |
| `verification-gates` | [验证](specs/verification-gates.md) | `engineering-baseline` |
| `lan-session` | [局域网会话](specs/lan-session.md) | `session-protocol` |
| `clipboard-sync` | [剪贴板同步](specs/clipboard-sync.md) | `session-protocol` |
| `file-transfer` | [文件传输](specs/file-transfer.md) | `session-protocol` |
| `local-operator` | [本地操作](specs/local-operator.md) | `session-protocol` |
| `framed-channel` | [成帧通道](specs/framed-channel.md) | `session-protocol` |
| `device-shell` | [图形外壳](specs/device-shell.md) | `session-protocol` |

## 待同步

没有待同步的 capability。

## 其他

- [模块边界](architecture.md)：当前解决方案里各项目的引用关系。
- [词汇](glossary.md)
- [ADR 0001](adr/0001-linux-color-scheme.md)：Linux 外观只读桌面门户。
- [ADR 0002](adr/0002-file-sync-is-a-transfer.md)：文件走传输，剪贴板只放文本。
- [ADR 0003](adr/0003-shared-effects.md)：共享效果放在一个模块里。
