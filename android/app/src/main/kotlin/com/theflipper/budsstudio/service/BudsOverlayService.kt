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
                state?.let { showFloatingPopup(it) }
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

        val contentText = if (state != null) {
            "${state.deviceName}: L ${state.left?.displayText ?: "—"} | R ${state.right?.displayText ?: "—"} | Case ${state.case?.displayText ?: "—"}"
        } else {
            "Ожидание подключения Bluetooth-наушников..."
        }

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("Buds Studio Pro")
            .setContentText(contentText)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .build()
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

                val params = WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.WRAP_CONTENT,
                    layoutType,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                            WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH,
                    PixelFormat.TRANSLUCENT
                ).apply {
                    gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
                    y = 48
                }

                val inflater = getSystemService(LAYOUT_INFLATER_SERVICE) as LayoutInflater
                val view = inflater.inflate(R.layout.floating_buds_popup, null)

                val txtTitle = view.findViewById<TextView>(R.id.txtDeviceName)
                val txtLeft = view.findViewById<TextView>(R.id.txtLeftPercent)
                val txtCase = view.findViewById<TextView>(R.id.txtCasePercent)
                val txtRight = view.findViewById<TextView>(R.id.txtRightPercent)

                txtTitle.text = state.deviceName
                txtLeft.text = state.left?.displayText ?: "—"
                txtCase.text = state.case?.displayText ?: "—"
                txtRight.text = state.right?.displayText ?: "—"

                view.setOnClickListener {
                    dismissOverlay()
                }

                windowManager?.addView(view, params)
                overlayView = view

                // Auto dismiss after 4 seconds
                dismissJob?.cancel()
                dismissJob = scope.launch {
                    delay(4000)
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
                if (overlayView != null) {
                    windowManager?.removeView(overlayView)
                    overlayView = null
                }
            } catch (ignored: Exception) {}
        }
    }

    override fun onDestroy() {
        bluetoothManager.stopMonitoring()
        dismissOverlay()
        super.onDestroy()
    }
}
