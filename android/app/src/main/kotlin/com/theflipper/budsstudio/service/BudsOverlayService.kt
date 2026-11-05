package com.theflipper.budsstudio.service

import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import android.view.Gravity
import android.view.LayoutInflater
import android.view.View
import android.view.WindowManager
import android.widget.RemoteViews
import android.widget.TextView
import androidx.core.app.NotificationCompat
import com.theflipper.budsstudio.MainActivity
import com.theflipper.budsstudio.R
import com.theflipper.budsstudio.bluetooth.BudsBluetoothManager
import com.theflipper.budsstudio.model.BudsBatteryState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class BudsOverlayService : Service() {

    private lateinit var bluetoothManager: BudsBluetoothManager
    private val scope = CoroutineScope(Dispatchers.Main + Job())
    private var windowManager: WindowManager? = null
    private var overlayView: View? = null
    private var dismissJob: Job? = null

    companion object {
        const val CHANNEL_ID = "buds_studio_service"
        const val NOTIFICATION_ID = 101
        const val ACTION_START = "ACTION_START"
        const val ACTION_STOP = "ACTION_STOP"
        const val ACTION_TRIGGER_PREVIEW = "ACTION_TRIGGER_PREVIEW"

        fun start(context: Context) {
            val intent = Intent(context, BudsOverlayService::class.java).apply {
                action = ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stop(context: Context) {
            val intent = Intent(context, BudsOverlayService::class.java).apply {
                action = ACTION_STOP
            }
            context.startService(intent)
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        bluetoothManager = BudsBluetoothManager(this)
        windowManager = getSystemService(WINDOW_SERVICE) as WindowManager

        scope.launch {
            bluetoothManager.batteryState.collect { state ->
                updateNotification(state)
            }
        }

        scope.launch {
            bluetoothManager.onDeviceConnectedEvent.collect { state ->
                showFloatingPopup(state)
            }
        }

        bluetoothManager.startMonitoring()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
            }
            ACTION_TRIGGER_PREVIEW -> {
                val previewState = BudsBatteryState(
                    deviceName = "realme Buds T500 Pro",
                    left = com.theflipper.budsstudio.model.BatteryComponent(80, false),
                    right = com.theflipper.budsstudio.model.BatteryComponent(80, false),
                    case = com.theflipper.budsstudio.model.BatteryComponent(60, false),
                    isOverEar = false
                )
                showFloatingPopup(previewState)
            }
            else -> {
                startForeground(NOTIFICATION_ID, buildNotification(bluetoothManager.batteryState.value))
            }
        }
        return START_STICKY
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Buds Studio Service",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Monitors connected Bluetooth earbuds battery telemetry"
            }
            val nm = getSystemService(NotificationManager::class.java)
            nm?.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(state: BudsBatteryState?): Notification {
        val launchIntent = Intent(this, MainActivity::class.java)
        val pendingIntent = PendingIntent.getActivity(
            this, 0, launchIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        try {
            val collapsedView = RemoteViews(packageName, R.layout.notification_buds_collapsed)
            val expandedView = RemoteViews(packageName, R.layout.notification_buds_expanded)

            if (state != null) {
                collapsedView.setTextViewText(R.id.txtNotifCollapsedTitle, state.deviceName)
                expandedView.setTextViewText(R.id.txtNotifDeviceName, state.deviceName)
                expandedView.setTextViewText(R.id.txtNotifHeaderStatus, "Подключено")

                if (state.isOverEar) {
                    val valText = state.left?.displayText ?: "—"
                    collapsedView.setTextViewText(R.id.txtNotifCollapsedBattery, "Заряд: $valText")

                    expandedView.setViewVisibility(R.id.layoutNotifTws, View.GONE)
                    expandedView.setViewVisibility(R.id.layoutNotifOverEar, View.VISIBLE)
                    expandedView.setTextViewText(R.id.txtNotifOverEarPercent, valText)
                    expandedView.setTextViewText(R.id.txtNotifOverEarStatus, "Наушники • Подключено")
                } else {
                    val lText = state.left?.displayText ?: "—"
                    val rText = state.right?.displayText ?: "—"
                    val cText = state.case?.displayText ?: "—"
                    collapsedView.setTextViewText(R.id.txtNotifCollapsedBattery, "L: $lText | Кейс: $cText | R: $rText")

                    expandedView.setViewVisibility(R.id.layoutNotifTws, View.VISIBLE)
                    expandedView.setViewVisibility(R.id.layoutNotifOverEar, View.GONE)

                    expandedView.setTextViewText(R.id.txtNotifLeftPercent, lText)
                    expandedView.setTextViewText(R.id.txtNotifLeftStatus, if (state.left?.isAvailable == true) "В ухе" else "—")

                    expandedView.setTextViewText(R.id.txtNotifCasePercent, cText)
                    expandedView.setTextViewText(R.id.txtNotifCaseStatus, if (state.case?.isAvailable == true && (state.case.percent ?: 0) > 0) "Открыт" else "Кейс")

                    expandedView.setTextViewText(R.id.txtNotifRightPercent, rText)
                    expandedView.setTextViewText(R.id.txtNotifRightStatus, if (state.right?.isAvailable == true) "В ухе" else "—")
                }
            } else {
                collapsedView.setTextViewText(R.id.txtNotifCollapsedTitle, "Buds Studio Pro")
                collapsedView.setTextViewText(R.id.txtNotifCollapsedBattery, "Поиск Bluetooth...")

                expandedView.setTextViewText(R.id.txtNotifDeviceName, "Buds Studio Pro")
                expandedView.setTextViewText(R.id.txtNotifHeaderStatus, "Ожидание...")
                expandedView.setViewVisibility(R.id.layoutNotifTws, View.VISIBLE)
                expandedView.setViewVisibility(R.id.layoutNotifOverEar, View.GONE)
                expandedView.setTextViewText(R.id.txtNotifLeftPercent, "—")
                expandedView.setTextViewText(R.id.txtNotifCasePercent, "—")
                expandedView.setTextViewText(R.id.txtNotifRightPercent, "—")
            }

            return NotificationCompat.Builder(this, CHANNEL_ID)
                .setSmallIcon(R.drawable.ic_notif_status)
                .setStyle(NotificationCompat.DecoratedCustomViewStyle())
                .setCustomContentView(collapsedView)
                .setCustomBigContentView(expandedView)
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .build()
        } catch (e: Exception) {
            val title = state?.deviceName ?: "Buds Studio Pro"
            val text = if (state != null) {
                if (state.isOverEar) "Заряд: ${state.left?.displayText ?: "—"}"
                else "L: ${state.left?.displayText ?: "—"} | Кейс: ${state.case?.displayText ?: "—"} | R: ${state.right?.displayText ?: "—"}"
            } else "Ожидание подключения Bluetooth..."

            return NotificationCompat.Builder(this, CHANNEL_ID)
                .setSmallIcon(R.drawable.ic_notif_status)
                .setContentTitle(title)
                .setContentText(text)
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .build()
        }
    }

    private fun updateNotification(state: BudsBatteryState?) {
        val nm = getSystemService(NotificationManager::class.java)
        nm?.notify(NOTIFICATION_ID, buildNotification(state))
    }

    @SuppressLint("InflateParams")
    private fun showFloatingPopup(state: BudsBatteryState) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(this)) {
            return
        }

        scope.launch(Dispatchers.Main) {
            try {
                if (overlayView != null) {
                    windowManager?.removeView(overlayView)
                    overlayView = null
                }

                val layoutType = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                } else {
                    @Suppress("DEPRECATION")
                    WindowManager.LayoutParams.TYPE_PHONE
                }

                val displayMetrics = resources.displayMetrics
                val popupHeight = (displayMetrics.heightPixels * 0.48f).toInt()

                val params = WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT,
                    popupHeight,
                    layoutType,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                            WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH,
                    PixelFormat.TRANSLUCENT
                ).apply {
                    gravity = Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL
                    x = 0
                    y = 0
                }

                val inflater = getSystemService(LAYOUT_INFLATER_SERVICE) as LayoutInflater
                val view = inflater.inflate(R.layout.floating_buds_popup, null)

                val txtTitle = view.findViewById<TextView>(R.id.txtDeviceName)
                val btnClose = view.findViewById<View>(R.id.btnClosePopup)
                val layoutTws = view.findViewById<View>(R.id.layoutTws)
                val layoutOverEar = view.findViewById<View>(R.id.layoutOverEar)
                val card = view.findViewById<View>(R.id.cardContainer)

                txtTitle.text = state.deviceName

                btnClose.setOnClickListener {
                    dismissOverlay()
                }

                if (state.isOverEar) {
                    layoutOverEar.visibility = View.VISIBLE
                    layoutTws.visibility = View.GONE

                    val txtOverEarPercent = view.findViewById<TextView>(R.id.txtOverEarPercent)
                    val txtOverEarStatus = view.findViewById<TextView>(R.id.txtOverEarStatus)
                    txtOverEarPercent.text = state.left?.displayText ?: "—"
                    txtOverEarStatus.text = "Наушники • Подключено"
                } else {
                    layoutTws.visibility = View.VISIBLE
                    layoutOverEar.visibility = View.GONE

                    val txtLeft = view.findViewById<TextView>(R.id.txtLeftPercent)
                    val txtLeftStatus = view.findViewById<TextView>(R.id.txtLeftStatus)
                    val txtCase = view.findViewById<TextView>(R.id.txtCasePercent)
                    val txtCaseStatus = view.findViewById<TextView>(R.id.txtCaseStatus)
                    val txtRight = view.findViewById<TextView>(R.id.txtRightPercent)
                    val txtRightStatus = view.findViewById<TextView>(R.id.txtRightStatus)

                    txtLeft.text = state.left?.displayText ?: "—"
                    txtCase.text = state.case?.displayText ?: "—"
                    txtRight.text = state.right?.displayText ?: "—"

                    txtLeftStatus.text = if (state.left?.isAvailable == true) "В ухе" else "—"
                    txtCaseStatus.text = if (state.case?.isAvailable == true && (state.case.percent ?: 0) > 0) "Открыт" else "Кейс"
                    txtRightStatus.text = if (state.right?.isAvailable == true) "В ухе" else "—"
                }

                view.setOnClickListener {
                    dismissOverlay()
                }

                // Smooth bottom-sheet slide-up with hardware acceleration
                card.setLayerType(View.LAYER_TYPE_HARDWARE, null)
                card.translationY = popupHeight.toFloat()
                windowManager?.addView(view, params)
                overlayView = view

                card.animate()
                    .translationY(0f)
                    .setDuration(360)
                    .setInterpolator(android.view.animation.DecelerateInterpolator(1.8f))
                    .withEndAction {
                        try { card.setLayerType(View.LAYER_TYPE_NONE, null) } catch (ignored: Exception) {}
                    }
                    .start()

                // Auto dismiss after 6 seconds
                dismissJob?.cancel()
                dismissJob = scope.launch {
                    delay(6000)
                    dismissOverlay()
                }
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }
    }

    private fun dismissOverlay() {
        scope.launch(Dispatchers.Main) {
            try {
                val current = overlayView ?: return@launch
                overlayView = null
                val card = current.findViewById<View>(R.id.cardContainer) ?: current
                val h = card.height.takeIf { it > 0 } ?: 900
                card.animate()
                    .translationY(h.toFloat())
                    .alpha(0f)
                    .setDuration(220)
                    .setInterpolator(android.view.animation.AccelerateInterpolator(1.5f))
                    .withEndAction {
                        try {
                            windowManager?.removeView(current)
                        } catch (ignored: Exception) {}
                    }
                    .start()
            } catch (ignored: Exception) {}
        }
    }

    override fun onDestroy() {
        bluetoothManager.stopMonitoring()
        dismissOverlay()
        super.onDestroy()
    }
}
