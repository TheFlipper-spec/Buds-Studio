package com.theflipper.budsstudio.model

data class BatteryComponent(
    val percent: Int?,
    val isCharging: Boolean = false
) {
    val isAvailable: Boolean get() = percent != null && percent in 0..100
    val displayText: String get() = if (isAvailable) "$percent%" else "—"

    companion object {
        val Unavailable = BatteryComponent(null, false)
    }
}

data class BudsBatteryState(
    val deviceName: String,
    val left: BatteryComponent? = null,
    val right: BatteryComponent? = null,
    val case: BatteryComponent? = null,
    val isOverEar: Boolean = false
) {
    val isConnected: Boolean get() = left?.isAvailable == true || right?.isAvailable == true || case?.isAvailable == true
}
