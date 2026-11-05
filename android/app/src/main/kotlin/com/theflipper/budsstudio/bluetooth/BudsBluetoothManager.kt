package com.theflipper.budsstudio.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
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
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
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

    private val _onDeviceConnectedEvent = MutableSharedFlow<BudsBatteryState>(replay = 0, extraBufferCapacity = 2)
    val onDeviceConnectedEvent: SharedFlow<BudsBatteryState> = _onDeviceConnectedEvent.asSharedFlow()

    private val scope = CoroutineScope(Dispatchers.IO + Job())
    private var pollJob: Job? = null
    private var isCurrentlyConnected = false
    private var lastCaseAvailable = false

    companion object {
        private const val TAG = "BudsBluetoothManager"
        private val SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
        // Realme / Oppo SPP Handshake and Query Commands
        private val HANDSHAKE_COMMAND = byteArrayOf(
            0xAA.toByte(), 0x07, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00
        )
        private val QUERY_COMMAND = byteArrayOf(
            0xAA.toByte(), 0x07, 0x00, 0x00, 0x06, 0x01, 0x02, 0x00, 0x00
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
                                _onDeviceConnectedEvent.emit(state)
                            } else if (!lastCaseAvailable && caseNowAvailable) {
                                // Buds placed back into open case -> trigger popup!
                                lastCaseAvailable = true
                                _batteryState.value = state
                                _onDeviceConnectedEvent.emit(state)
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
                delay(2000)
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
        val adapter = bluetoothAdapter
        if (adapter != null) {
            val a2dp = adapter.getProfileConnectionState(BluetoothProfile.A2DP)
            val headset = adapter.getProfileConnectionState(BluetoothProfile.HEADSET)
            if (a2dp == BluetoothProfile.STATE_CONNECTED || headset == BluetoothProfile.STATE_CONNECTED) {
                return true
            }
        }
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
                lowerName.contains("w820") ||
                lowerName.contains("wh-") ||
                lowerName.contains("max") ||
                lowerName.contains("over-ear")

        // 1. If Over-Ear / Edifier, read standard Android Bluetooth battery API directly
        if (isOverEar) {
            val stdLevel = getStandardBatteryLevel(device)
            if (stdLevel != null && stdLevel in 1..100) {
                return@withContext BudsBatteryState(
                    deviceName = deviceName,
                    left = BatteryComponent(stdLevel, false),
                    right = BatteryComponent(stdLevel, false),
                    case = BatteryComponent.Unavailable,
                    isOverEar = true
                )
            }
        }

        // 2. Realme / Oppo / OnePlus Serial Protocol via SPP socket
        if (lowerName.contains("realme") || lowerName.contains("buds") || lowerName.contains("oppo")) {
            val realmeBattery = queryRealmeSerial(device, deviceName)
            if (realmeBattery != null) return@withContext realmeBattery
        }

        // 3. Android Standard Battery Level API fallback for TWS
        val stdLevel = getStandardBatteryLevel(device)
        if (stdLevel != null && stdLevel in 1..100) {
            return@withContext BudsBatteryState(
                deviceName = deviceName,
                left = BatteryComponent(stdLevel, false),
                right = BatteryComponent(stdLevel, false),
                case = BatteryComponent.Unavailable,
                isOverEar = false
            )
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

            // Step 1: Handshake (Cmd 0x0100)
            os.write(HANDSHAKE_COMMAND)
            os.flush()

            Thread.sleep(70)

            // Step 2: Query battery telemetry (Cmd 0x0106)
            os.write(QUERY_COMMAND)
            os.flush()

            val buffer = ByteArray(512)
            var totalRead = 0
            val deadline = System.currentTimeMillis() + 600
            while (System.currentTimeMillis() < deadline && totalRead < buffer.size) {
                if (`is`.available() > 0) {
                    val r = `is`.read(buffer, totalRead, buffer.size - totalRead)
                    if (r > 0) {
                        totalRead += r
                        if (hasOppoBatteryPacket(buffer, totalRead)) break
                    }
                } else {
                    Thread.sleep(25)
                }
            }

            if (totalRead > 0) {
                val parsed = parseOppoBattery(buffer, totalRead, deviceName)
                if (parsed != null) return parsed
            }
        } catch (e: Exception) {
            Log.d(TAG, "RFCOMM query failed for ${device.name}: ${e.message}")
        } finally {
            try { socket?.close() } catch (ignored: Exception) {}
        }
        return null
    }

    private fun hasOppoBatteryPacket(buffer: ByteArray, len: Int): Boolean {
        for (i in 0 until len - 2) {
            val b0 = buffer[i].toInt() and 0xFF
            val b1 = buffer[i + 1].toInt() and 0xFF
            if ((b0 == 0x06 && b1 == 0x81) || (b0 == 0x04 && b1 == 0x02)) {
                return true
            }
        }
        return false
    }

    private fun parseOppoBattery(buffer: ByteArray, totalRead: Int, deviceName: String): BudsBatteryState? {
        // 1. Scan for response Cmd 0x8106 (0x06, 0x81)
        for (i in 0 until totalRead - 8) {
            val b0 = buffer[i].toInt() and 0xFF
            val b1 = buffer[i + 1].toInt() and 0xFF
            if (b0 == 0x06 && b1 == 0x81) {
                val payLen = (buffer[i + 3].toInt() and 0xFF) or ((buffer[i + 4].toInt() and 0xFF) shl 8)
                val payStart = i + 5
                if (payStart + payLen <= totalRead) {
                    val payload = buffer.copyOfRange(payStart, payStart + payLen)
                    val result = parseOppoPayload(payload, deviceName)
                    if (result != null) return result
                }
            }
        }

        // 2. Scan for push event Cmd 0x0204 (0x04, 0x02)
        for (i in 0 until totalRead - 8) {
            val b0 = buffer[i].toInt() and 0xFF
            val b1 = buffer[i + 1].toInt() and 0xFF
            if (b0 == 0x04 && b1 == 0x02) {
                val payLen = (buffer[i + 3].toInt() and 0xFF) or ((buffer[i + 4].toInt() and 0xFF) shl 8)
                val payStart = i + 5
                if (payStart + payLen <= totalRead) {
                    var payload = buffer.copyOfRange(payStart, payStart + payLen)
                    if (payload.isNotEmpty() && payload[0] == 0x01.toByte()) {
                        payload = payload.copyOfRange(1, payload.size)
                    }
                    val result = parseOppoPayload(payload, deviceName)
                    if (result != null) return result
                }
            }
        }

        // 3. Fallback: Parse whole frame if starts with 0xAA
        if (totalRead >= 9 && buffer[0] == 0xAA.toByte()) {
            var offset = 1
            while (offset < totalRead && (buffer[offset].toInt() and 0x80) != 0) {
                offset++
            }
            offset++ // Skip length byte
            if (offset + 6 <= totalRead) {
                offset += 2 // Skip slice and zero
                val cmd = (buffer[offset].toInt() and 0xFF) or ((buffer[offset + 1].toInt() and 0xFF) shl 8)
                offset += 3 // Skip cmd + seq
                if (offset + 2 <= totalRead) {
                    val payLen = (buffer[offset].toInt() and 0xFF) or ((buffer[offset + 1].toInt() and 0xFF) shl 8)
                    offset += 2
                    if (offset + payLen <= totalRead) {
                        var payload = buffer.copyOfRange(offset, offset + payLen)
                        if (cmd == 0x8106) {
                            return parseOppoPayload(payload, deviceName)
                        } else if (cmd == 0x0204 && payload.isNotEmpty() && payload[0] == 0x01.toByte()) {
                            payload = payload.copyOfRange(1, payload.size)
                            return parseOppoPayload(payload, deviceName)
                        }
                    }
                }
            }
        }

        return null
    }

    private fun parseOppoPayload(rawPayload: ByteArray, deviceName: String): BudsBatteryState? {
        var data = rawPayload
        if (data.size >= 2 && data[0] == 0x00.toByte()) {
            data = data.copyOfRange(1, data.size)
        }
        if (data.isEmpty()) return null

        val count = data[0].toInt() and 0xFF
        var left: BatteryComponent? = null
        var right: BatteryComponent? = null
        var caseComp: BatteryComponent? = null

        // Pattern A: count of items followed by (comp, raw) pairs
        if (count in 1..8 && data.size >= 1 + count * 2) {
            for (i in 0 until count) {
                val comp = data[1 + i * 2].toInt() and 0xFF
                val raw = data[2 + i * 2].toInt() and 0xFF
                val level = raw and 0x7F
                val charging = (raw and 0x80) != 0
                val compInfo = if (level in 1..100) BatteryComponent(level, charging) else null
                when (comp) {
                    1 -> left = compInfo
                    2 -> right = compInfo
                    3 -> caseComp = compInfo
                }
            }
        }

        // Pattern B: direct (comp, raw) pairs without count header
        if (left == null && right == null && caseComp == null) {
            var i = 0
            while (i + 1 < data.size) {
                val comp = data[i].toInt() and 0xFF
                val raw = data[i + 1].toInt() and 0xFF
                if (comp in 1..3) {
                    val level = raw and 0x7F
                    val charging = (raw and 0x80) != 0
                    val compInfo = if (level in 1..100) BatteryComponent(level, charging) else null
                    when (comp) {
                        1 -> left = compInfo
                        2 -> right = compInfo
                        3 -> caseComp = compInfo
                    }
                    i += 2
                } else {
                    i++
                }
            }
        }

        // If only one earbud is reported (single bud taken out), mirror level so both show up
        if (left != null && right == null) {
            right = left
        } else if (right != null && left == null) {
            left = right
        }

        if (left != null || right != null || caseComp != null) {
            return BudsBatteryState(
                deviceName = deviceName,
                left = left,
                right = right,
                case = caseComp ?: BatteryComponent.Unavailable,
                isOverEar = false
            )
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
            // Strictly guard: only process AirPods BLE advertisements if an Apple device is connected!
            val connected = _batteryState.value?.deviceName?.lowercase() ?: ""
            if (!connected.contains("airpods") && !connected.contains("beats") && !connected.contains("apple")) {
                return
            }

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
