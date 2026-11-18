package app.contextall.clipboard

/**
 * Pure parser deciding whether a system log line is a clipboard-access denial
 * reported for [packageName]. This is the change signal the KDE-style
 * background auto-capture relies on: the platform does not expose clipboard
 * content to an unfocused app, only the denial it recorded.
 */
object ClipboardDenialSignal {
    const val DENIAL_MARKER = "Denying clipboard access to "

    fun isDenialFor(line: String, packageName: String): Boolean {
        val trimmed = line.trim()
        return trimmed.contains(DENIAL_MARKER) &&
            trimmed.contains(packageName) &&
            packageName.isNotEmpty()
    }
}
