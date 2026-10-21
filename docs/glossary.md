# 词汇

同一局域网内的设备共享剪贴板，并把文件发给已信任的对端。字节不经过云端中继。

**Decision**（决策）  
一次输入之后的下一设备状态，以及随之要执行的效果。决策不接触网络，也不接触磁盘。  
不叫：transition、command、action。

**Effect**（效果）  
决策要求执行的工作，例如一次 QUIC 写入或一段文件写入。发现、剪贴板捕获和路径留在桌面守护进程或 Android 应用里。  
不叫：side effect、job、task。

**Local channel**（本地通道）  
桌面前端用来联系守护进程的 Unix socket 或命名管道。  
不叫：IPC、pipe、socket。
