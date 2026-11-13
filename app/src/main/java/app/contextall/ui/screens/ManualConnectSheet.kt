package app.contextall.ui.screens

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.AddLink
import androidx.compose.material.icons.rounded.Badge
import androidx.compose.material.icons.rounded.Dns
import androidx.compose.material.icons.rounded.Numbers
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.contextall.R
import app.contextall.core.EndpointValidator
import app.contextall.core.ProtocolLimits
import app.contextall.ui.components.ExpressiveIconBadge
import app.contextall.ui.theme.JetBrainsMonoFamily
import app.contextall.ui.theme.LocalExpressivePalette

/**
 * Android 16 (Material 3 Expressive) bottom sheet for manual IP + QUIC Port direct peer connection and pairing.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ManualConnectBottomSheet(
    onDismiss: () -> Unit,
    onConnectAndPair: (host: String, portText: String, alias: String) -> Unit
) {
    val palette = LocalExpressivePalette.current
    val defaultPortStr = ProtocolLimits.DEFAULT_QUIC_PORT.toString()

    var hostInput by remember { mutableStateOf("") }
    var portInput by remember { mutableStateOf(defaultPortStr) }
    var aliasInput by remember { mutableStateOf("") }
    var hostTouched by remember { mutableStateOf(false) }

    val isHostValid = remember(hostInput) { EndpointValidator.isValidHost(hostInput) }
    val parsedPort = remember(portInput) { EndpointValidator.parseValidPort(portInput) }
    val isPortValid = parsedPort != null
    val canSubmit = isHostValid && isPortValid

    val quickChips = listOf(
        "192.168.1.",
        "192.168.1.42",
        "10.0.0.18",
        "fd00::1"
    )

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        shape = RoundedCornerShape(topStart = 32.dp, topEnd = 32.dp),
        containerColor = MaterialTheme.colorScheme.surfaceContainerLow
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp)
                .padding(bottom = 32.dp)
                .testTag("manual_connect_sheet")
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                ExpressiveIconBadge(
                    icon = Icons.Rounded.AddLink,
                    backgroundColor = palette.skyIconBg,
                    iconTint = palette.skyIconFg,
                    size = 48.dp
                )
                Spacer(modifier = Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = stringResource(R.string.manual_sheet_title),
                        style = MaterialTheme.typography.headlineMedium
                    )
                }
            }

            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = stringResource(R.string.manual_sheet_subtitle),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(modifier = Modifier.height(18.dp))

            // Host / IP input with smart IP:Port paste extraction
            OutlinedTextField(
                value = hostInput,
                onValueChange = { raw ->
                    hostTouched = true
                    val (extractedHost, extractedPort) = EndpointValidator.parsePastedInput(raw)
                    hostInput = extractedHost
                    if (extractedPort != null) {
                        portInput = extractedPort
                    }
                },
                label = { Text(stringResource(R.string.manual_ip_label)) },
                placeholder = { Text(stringResource(R.string.manual_ip_placeholder)) },
                leadingIcon = {
                    Icon(
                        imageVector = Icons.Rounded.Dns,
                        contentDescription = null
                    )
                },
                textStyle = TextStyle(
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 15.sp
                ),
                singleLine = true,
                isError = hostTouched && hostInput.isNotBlank() && !isHostValid,
                supportingText = if (hostTouched && hostInput.isNotBlank() && !isHostValid) {
                    { Text(stringResource(R.string.manual_ip_error)) }
                } else {
                    null
                },
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Uri,
                    imeAction = ImeAction.Next
                ),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("manual_ip_input")
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Quick subnet / sample endpoint chips for effortless mobile entry
            Text(
                text = stringResource(R.string.manual_quick_prefix_label),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(modifier = Modifier.height(4.dp))
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                quickChips.forEachIndexed { idx, preset ->
                    AssistChip(
                        onClick = {
                            hostTouched = true
                            hostInput = preset
                        },
                        label = {
                            Text(
                                text = preset,
                                fontFamily = JetBrainsMonoFamily,
                                fontSize = 12.sp
                            )
                        },
                        shape = CircleShape,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("manual_quick_chip_$idx")
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            // QUIC Port input (1..65535, defaults to 42461)
            OutlinedTextField(
                value = portInput,
                onValueChange = { raw ->
                    portInput = raw.filter { it.isDigit() }.take(5)
                },
                label = { Text(stringResource(R.string.manual_port_label)) },
                leadingIcon = {
                    Icon(
                        imageVector = Icons.Rounded.Numbers,
                        contentDescription = null
                    )
                },
                trailingIcon = if (portInput != defaultPortStr) {
                    {
                        TextButton(
                            onClick = { portInput = defaultPortStr },
                            modifier = Modifier.testTag("manual_port_reset_button")
                        ) {
                            Text(
                                text = stringResource(R.string.manual_port_reset),
                                style = MaterialTheme.typography.labelMedium
                            )
                        }
                    }
                } else {
                    null
                },
                textStyle = TextStyle(
                    fontFamily = JetBrainsMonoFamily,
                    fontSize = 15.sp
                ),
                singleLine = true,
                isError = !isPortValid,
                supportingText = if (!isPortValid) {
                    { Text(stringResource(R.string.manual_port_error)) }
                } else {
                    null
                },
                keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Number,
                    imeAction = ImeAction.Next
                ),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("manual_port_input")
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Optional alias display name
            OutlinedTextField(
                value = aliasInput,
                onValueChange = { aliasInput = it.take(64) },
                label = { Text(stringResource(R.string.manual_alias_label)) },
                placeholder = { Text(stringResource(R.string.manual_alias_placeholder)) },
                leadingIcon = {
                    Icon(
                        imageVector = Icons.Rounded.Badge,
                        contentDescription = null
                    )
                },
                singleLine = true,
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(
                    onDone = {
                        if (canSubmit) {
                            onConnectAndPair(hostInput, portInput, aliasInput)
                        }
                    }
                ),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("manual_alias_input")
            )

            Spacer(modifier = Modifier.height(22.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                OutlinedButton(
                    onClick = onDismiss,
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier
                        .weight(0.38f)
                        .height(54.dp)
                        .testTag("manual_connect_cancel_button")
                ) {
                    Text(
                        text = stringResource(R.string.common_cancel_button),
                        style = MaterialTheme.typography.titleSmall
                    )
                }

                Button(
                    onClick = { onConnectAndPair(hostInput, portInput, aliasInput) },
                    enabled = canSubmit,
                    shape = CircleShape,
                    modifier = Modifier
                        .weight(0.62f)
                        .height(54.dp)
                        .testTag("manual_connect_submit_button")
                ) {
                    Icon(
                        imageVector = Icons.Rounded.AddLink,
                        contentDescription = null,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = stringResource(R.string.manual_submit_button),
                        style = MaterialTheme.typography.titleSmall
                    )
                }
            }
        }
    }
}
