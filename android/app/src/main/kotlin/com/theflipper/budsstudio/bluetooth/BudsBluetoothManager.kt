package com.theflipper.budsstudio.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.util.Log
import com.theflipper.budsstudio.model.BatteryComponent
import com.theflipper.budsstudio.model.BudsBatteryState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID

class BudsBluetoothManager(private val context: Context) {

    private val bluetoothManager = context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
    private val bluetoothAdapter: BluetoothAdapter? get() = bluetoothManager?.adapter

    private val _batteryState = MutableStateFlow<BudsBatteryState?>(null)
    val batteryState: StateFlow<BudsBatteryState?> = _batteryState.asStateFlow()

    private val _onDeviceConnectedEvent = MutableStateFlow<BudsBatteryState?>(null)
    val onDeviceConnectedEvent: StateFlow<BudsBatteryState?> = _onDeviceConnectedEvent.asStateFlow()

    private val scope = CoroutineScope(Dispatchers.IO + Job())
    private var pollJob: Job? = null
    private var isCurrentlyConnected = false
    private var lastCaseAvailable = false

    companion object {
        private const val TAG = "BudsBluetoothManager"
        private val SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
        private val QUERY_COMMAND = byteArrayOf(
            0xAA.toByte(), 0x07, 0x00, 0x00, 0x06, 0x81.toByte(), 0x02, 0x00, 0x00
        )
    }

    @SuppressLint("MissingPermission")
    fun startMonitoring() {
        pollJob?.cancel()
        pollJob = scope.launch {
            while (isActive) {
                try {
                    val connectedDevice = findConnectedAudioDevice()
                    if (connectedDevice != null) {
                        val state = queryDeviceBattery(connectedDevice)
                        if (state != null) {
                            val caseNowAvailable = state.case?.isAvailable == true && (state.case.percent ?: 0) > 0

                            if (!isCurrentlyConnected) {
                                // New Connection Trigger
                                isCurrentlyConnected = true
                                lastCaseAvailable = caseNowAvailable
                                _batteryState.value = state
                                _onDeviceConnectedEvent.value = state
                            } else if (!lastCaseAvailable && caseNowAvailable) {
                                // Buds returned to open case
                                lastCaseAvailable = true
                                _batteryState.value = state
                                _onDeviceConnectedEvent.value = state
                            } else {
                                lastCaseAvailable = caseNowAvailable
                                _batteryState.value = state
                            }
                        }
                    } else {
                        if (isCurrentlyConnected) {
                            isCurrentlyConnected = false
                            lastCaseAvailable = false
                            _batteryState.value = null
                        }
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "Error in Bluetooth monitor: ${e.message}")
                }
                delay(2500)
            }
        }

        startAirPodsBleScanner()
    }

    fun stopMonitoring() {
        pollJob?.cancel()
        stopAirPodsBleScanner()
    }

    @SuppressLint("MissingPermission")
    private fun findConnectedAudioDevice(): BluetoothDevice? {
        val adapter = bluetoothAdapter ?: return null
        if (!adapter.isEnabled) return null

        val bonded = adapter.bondedDevices ?: return null
        for (device in bonded) {
            val name = device.name ?: ""
            if (isTargetDevice(name)) {
                if (isDeviceConnected(device)) {
                    return device
                }
            }
        }
        return null
    }

    private fun isTargetDevice(name: String): Boolean {
        val lower = name.lowercase()
        return lower.contains("buds") ||
                lower.contains("t500") ||
                lower.contains("airpods") ||
                lower.contains("edifier") ||
                lower.contains("realme") ||
                lower.contains("oppo") ||
                lower.contains("oneplus") ||
                lower.contains("wh-1000") ||
                lower.contains("wf-1000")
    }

    @SuppressLint("MissingPermission")
    private fun isDeviceConnected(device: BluetoothDevice): Boolean {
        return try {
            val method = device.javaClass.getMethod("isConnected")
            method.invoke(device) as? Boolean ?: false
        } catch (e: Exception) {
            true // Fallback: attempt query
        }
    }

    @SuppressLint("MissingPermission")
    private suspend fun queryDeviceBattery(device: BluetoothDevice): BudsBatteryState? = withContext(Dispatchers.IO) {
        val deviceName = device.name ?: "Bluetooth Buds"
        val lowerName = deviceName.lowercase()
        val isOverEar = lowerName.contains("edifier") ||
                lowerName.contains("w800") ||
                lowerName.contains("wh-") ||
                lowerName.contains("max") ||
                lowerName.contains("over-ear")

        // 1. Realme / Oppo / OnePlus Serial Protocol via SPP socket
        if (lowerName.contains("realme") || lowerName.contains("buds") || lowerName.contains("oppo")) {
            val realmeBattery = queryRealmeSerial(device, deviceName)
            if (realmeBattery != null) return@withContext realmeBattery
        }

        // 2. Android Standard Battery Level API
        val stdLevel = getStandardBatteryLevel(device)
        if (stdLevel != null && stdLevel in 1..100) {
            return@withContext if (isOverEar) {
                BudsBatteryState(
                    deviceName = deviceName,
                    left = BatteryComponent(stdLevel, false),
                    right = BatteryComponent(stdLevel, false),
                    case = BatteryComponent.Unavailable,
                    isOverEar = true
                )
            } else {
                BudsBatteryState(
                    deviceName = deviceName,
                    left = BatteryComponent(stdLevel, false),
                    right = BatteryComponent(stdLevel, false),
                    case = BatteryComponent.Unavailable,
                    isOverEar = false
                )
            }
        }

        null
    }

    @SuppressLint("MissingPermission")
    private fun queryRealmeSerial(device: BluetoothDevice, deviceName: String): BudsBatteryState? {
        var socket: BluetoothSocket? = null
        try {
            socket = device.createRfcommSocketToServiceRecord(SPP_UUID)
            socket.connect()

            val os: OutputStream = socket.outputStream
            val `is`: InputStream = socket.inputStream

            os.write(QUERY_COMMAND)
            os.flush()

            val buffer = ByteArray(64)
            val read = `is`.read(buffer)
            if (read >= 17 && (buffer[0].toInt() and 0xFF) == 0xAA) {
                // Command AA 0F 00 00 06 81 02 08 00 00 03 01 <L> 02 <R> 03 <Case>
                val left = buffer[12].toInt() and 0xFF
                val right = buffer[14].toInt() and 0xFF
                val caseVal = buffer[16].toInt() and 0xFF

                val leftComp = if (left in 1..100) BatteryComponent(left, false) else null
                val rightComp = if (right in 1..100) BatteryComponent(right, false) else null
                val caseComp = if (caseVal in 1..100) BatteryComponent(caseVal, false) else BatteryComponent.Unavailable

                if (leftComp != null || rightComp != null) {
                    return BudsBatteryState(
                        deviceName = deviceName,
                        left = leftComp,
                        right = rightComp,
                        case = caseComp,
                        isOverEar = false
                    )
                }
            }
        } catch (e: Exception) {
            Log.d(TAG, "RFCOMM query failed for ${device.name}: ${e.message}")
        } finally {
            try { socket?.close() } catch (ignored: Exception) {}
        }
        return null
    }

    @SuppressLint("MissingPermission")
    private fun getStandardBatteryLevel(device: BluetoothDevice): Int? {
        return try {
            val method = device.javaClass.getMethod("getBatteryLevel")
            val level = method.invoke(device) as? Int
            if (level != null && level >= 0) level else null
        } catch (e: Exception) {
            null
        }
    }

    // AirPods BLE Scanner
    private var isScanningAirPods = false
    private val airPodsScanCallback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult?) {
            result ?: return
            val record = result.scanRecord ?: return
            val mfgData = record.getManufacturerSpecificData(0x004C) ?: return
            if (mfgData.size >= 25 && (mfgData[0] == 0x07.toByte() || mfgData[0] == 0x10.toByte())) {
                val state = parseAirPodsData(mfgData)
                if (state != null) {
                    _batteryState.value = state
                }
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun startAirPodsBleScanner() {
        val scanner = bluetoothAdapter?.bluetoothLeScanner ?: return
        if (isScanningAirPods) return
        try {
            val filter = ScanFilter.Builder()
                .setManufacturerData(0x004C, byteArrayOf(0x07))
                .build()
            val settings = ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_LOW_POWER)
                .build()
            scanner.startScan(listOf(filter), settings, airPodsScanCallback)
            isScanningAirPods = true
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start AirPods BLE scanner: ${e.message}")
        }
    }

    @SuppressLint("MissingPermission")
    private fun stopAirPodsBleScanner() {
        if (!isScanningAirPods) return
        try {
            bluetoothAdapter?.bluetoothLeScanner?.stopScan(airPodsScanCallback)
            isScanningAirPods = false
        } catch (ignored: Exception) {}
    }

    private fun parseAirPodsData(data: ByteArray): BudsBatteryState? {
        try {
            val podsBattery = data[6].toInt() and 0xFF
            val flagsCase = data[7].toInt() and 0xFF
            val statusByte = data[5].toInt() and 0xFF

            val primaryLeft = ((statusByte shr 5) and 0x01) == 1
            val thisInCase = ((statusByte shr 6) and 0x01) == 1
            val isFlipped = !primaryLeft

            val leftNibble = if (isFlipped) (podsBattery shr 4) and 0x0F else podsBattery and 0x0F
            val rightNibble = if (isFlipped) podsBattery and 0x0F else (podsBattery shr 4) and 0x0F
            val caseNibble = flagsCase and 0x0F

            fun decodeNibble(n: Int): Int? = when (n) {
                in 0..9 -> n * 10
                in 10..14 -> 100
                else -> null
            }

            val leftLevel = decodeNibble(leftNibble)
            val rightLevel = decodeNibble(rightNibble)
            val caseLevel = decodeNibble(caseNibble)

            if (leftLevel == null && rightLevel == null) return null

            return BudsBatteryState(
                deviceName = "AirPods Pro",
                left = leftLevel?.let { BatteryComponent(it, false) },
                right = rightLevel?.let { BatteryComponent(it, false) },
                case = caseLevel?.let { BatteryComponent(it, false) } ?: BatteryComponent.Unavailable,
                isOverEar = false
            )
        } catch (e: Exception) {
            return null
        }
    }
}
