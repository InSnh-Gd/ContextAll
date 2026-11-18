# Proposal

## Why

设备列表为空时，详情页要求用户选择一台不存在的设备。连接与本机设置页对窗口宽度的响应不同，传输页把累计数字排在当前任务之前，标题栏开关也没有说明桌面是否实际采用了这项偏好。七轮画板选型已确定这些场景的呈现方式。

## What Changes

- 为零设备场景提供首次连接引导，保留全局剪贴板入口。
- 连接页跟随外壳分栏，对方地址和备注使用四角圆润的完整描边。
- 本机设置宽屏提供分类导航，窄屏保留全部分组的单列；采用阅读优先字号和相连的圆角设置行。
- 累计统计移至传输记录之后；系统标题栏偏好旁显示实际窗口装饰状态。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `device-shell`：增加桌面空态、响应式页面、设置分组与窗口状态反馈的验收条件。

## Impact

涉及共享 `ContextAll.App` 视图、样式、窗口状态及 `ContextAll.App.Tests`。本次以 Linux 为验收平台；共享视图的改动也会被 Windows Avalonia 宿主使用。无需更改平台入口、Android、Daemon、协议、配置格式或软件包。保留正在进行的变更，不归档 `md3e-ui-overhaul`。
