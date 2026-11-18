package app.contextall.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.DialogProperties
import androidx.compose.ui.window.SecureFlagPolicy
import app.contextall.core.FrameTypes
import kotlinx.coroutines.launch
import org.json.JSONObject

@Composable
fun ClipboardHistoryDialog(
    command: suspend (Int, JSONObject) -> JSONObject?,
    onDismiss: () -> Unit,
    localDeviceId: String,
    foregroundClipboard: Boolean,
    onForegroundClipboardChanged: (Boolean) -> Unit
) {
    val scope = rememberCoroutineScope()
    var page by remember { mutableStateOf<JSONObject?>(null) }
    var status by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    var showGroup by remember { mutableStateOf(false) }
    var deviceId by remember { mutableStateOf("") }

    suspend fun load(offset: Int = 0) {
        val response = command(FrameTypes.READ_CLIPBOARD_HISTORY, JSONObject().put("offset", offset))
        page = response?.optJSONObject("clipboardHistory")
        if (page == null) status = "剪贴板历史不可用"
    }

    fun configure(body: JSONObject, type: Int = FrameTypes.CONFIGURE_CLIPBOARD_HISTORY) {
        if (busy) return
        busy = true
        scope.launch {
            status = if (command(type, body)?.optBoolean("ok") == true) null else "未能保存设置"
            load()
            busy = false
        }
    }

    LaunchedEffect(Unit) { load() }
    val entries = page?.optJSONArray("entries")?.let { rows ->
        (0 until rows.length()).mapNotNull { rows.optJSONObject(it) }
    }.orEmpty()

    AlertDialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(securePolicy = SecureFlagPolicy.SecureOn),
        title = { Text("剪贴板历史") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("前台自动采集")
                    Switch(checked = foregroundClipboard, onCheckedChange = onForegroundClipboardChanged)
                }
                page?.let { current ->
                    val owner = current.optString("groupOwner").takeIf { it.isNotEmpty() && it != "null" }
                    val enabled = current.optBoolean("enabled")
                    val days = current.optInt("retentionDays", 7)
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Text("保留历史")
                        Switch(checked = enabled, enabled = !busy && owner == null, onCheckedChange = {
                            configure(JSONObject().put("enabled", it))
                        })
                    }
                    if (enabled) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            TextButton(enabled = !busy && days > 1, onClick = {
                                configure(JSONObject().put("totalBytes", days - 1))
                            }) { Text("−") }
                            Text("保留 $days 天")
                            TextButton(enabled = !busy && days < 30, onClick = {
                                configure(JSONObject().put("totalBytes", days + 1))
                            }) { Text("+") }
                        }
                        if (entries.isEmpty()) Text("暂无历史")
                    }

                    TextButton(onClick = { showGroup = !showGroup }) { Text(if (showGroup) "返回历史" else "共享组") }
                    if (showGroup) {
                        Text(when (owner) {
                            null -> "未加入共享组"
                            localDeviceId -> "本机管理共享组"
                            else -> "组主：$owner"
                        }, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        if (owner == null || owner == localDeviceId) {
                            OutlinedTextField(value = deviceId, onValueChange = { deviceId = it }, singleLine = true,
                                label = { Text("已配对设备 ID") })
                        }
                        Row {
                            if (owner == null) {
                                TextButton(enabled = !busy, onClick = {
                                    configure(JSONObject().put("displayName", "self"), FrameTypes.CONFIGURE_CLIPBOARD_GROUP)
                                }) { Text("创建") }
                                TextButton(enabled = !busy && deviceId.isNotBlank(), onClick = {
                                    configure(JSONObject().put("displayName", deviceId.trim()), FrameTypes.CONFIGURE_CLIPBOARD_GROUP)
                                }) { Text("加入") }
                            } else {
                                if (owner == localDeviceId) {
                                    for ((label, invited) in listOf("邀请" to true, "移除" to false)) {
                                        TextButton(enabled = !busy && deviceId.isNotBlank(), onClick = {
                                            configure(JSONObject().put("deviceId", deviceId.trim()).put("enabled", invited), FrameTypes.CONFIGURE_CLIPBOARD_GROUP)
                                        }) { Text(label) }
                                    }
                                }
                                TextButton(enabled = !busy, onClick = {
                                    configure(JSONObject().put("displayName", ""), FrameTypes.CONFIGURE_CLIPBOARD_GROUP)
                                }) { Text(if (owner == localDeviceId) "解散" else "退出") }
                            }
                        }
                    }
                }
                if (!showGroup) LazyColumn(modifier = Modifier.heightIn(max = 240.dp)) {
                    items(entries, key = { it.getString("id") }) { entry ->
                        Text(entry.optString("preview").ifEmpty { "空文本" }, maxLines = 3, overflow = TextOverflow.Ellipsis)
                        Text(entry.optString("source"), style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Row {
                            for ((label, sync) in listOf("复制到本机" to false, "同步" to true)) {
                                TextButton(enabled = !busy, onClick = {
                                    busy = true
                                    scope.launch {
                                        val body = JSONObject().put("transferId", entry.getString("id")).put("enabled", sync)
                                        val result = command(FrameTypes.USE_CLIPBOARD_HISTORY, body)
                                        busy = false
                                        if (result?.optBoolean("ok") == true) onDismiss()
                                        else status = "条目已失效或剪贴板不可用"
                                    }
                                }) { Text(label) }
                            }
                        }
                        HorizontalDivider()
                    }
                }
                page?.optInt("nextOffset", -1)?.takeIf { !showGroup && it >= 0 }?.let { offset ->
                    TextButton(enabled = !busy, onClick = { scope.launch { load(offset) } }) { Text("更早") }
                }
                status?.let { Text(it) }
            }
        },
        dismissButton = { TextButton(onClick = { scope.launch { status = null; load() } }) { Text("刷新") } },
        confirmButton = { TextButton(onClick = onDismiss) { Text("关闭") } }
    )
}
