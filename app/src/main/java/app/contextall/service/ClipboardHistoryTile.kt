package app.contextall.service

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.service.quicksettings.TileService
import app.contextall.MainActivity

class ClipboardHistoryTile : TileService() {
    override fun onClick() {
        super.onClick()
        unlockAndRun {
            if (Build.VERSION.SDK_INT >= 34) {
                startActivityAndCollapse(openHistoryIntent(this))
            } else {
                @Suppress("DEPRECATION")
                startActivityAndCollapse(historyActivity(this))
            }
        }
    }

    companion object {
        private fun historyActivity(context: Context) = Intent(context, MainActivity::class.java)
            .setAction(MainActivity.ACTION_CLIPBOARD_HISTORY)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        fun openHistoryIntent(context: Context): PendingIntent = PendingIntent.getActivity(
            context, 0, historyActivity(context), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
    }
}
