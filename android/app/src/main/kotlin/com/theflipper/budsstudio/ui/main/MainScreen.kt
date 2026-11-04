package com.theflipper.budsstudio.ui.main

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.theflipper.budsstudio.R
import com.theflipper.budsstudio.bluetooth.BudsBluetoothManager
import com.theflipper.budsstudio.model.BudsBatteryState
import com.theflipper.budsstudio.service.BudsOverlayService

enum class AccentColor(val color: Color, val title: String) {
    Blue(Color(0xFF2563EB), "Синий"),
    Graphite(Color(0xFF1E293B), "Графит"),
    Emerald(Color(0xFF10B981), "Изумруд"),
    Sunset(Color(0xFFF97316), "Оранж"),
    Purple(Color(0xFF8B5CF6), "Фиолет")
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val bluetoothManager = remember { BudsBluetoothManager(context) }
    val batteryState by bluetoothManager.batteryState.collectAsState()

    var isServiceRunning by remember { mutableStateOf(true) }
    var selectedAccent by remember { mutableStateOf(AccentColor.Blue) }
    var isDarkTheme by remember { mutableStateOf(false) }

    DisposableEffect(Unit) {
        bluetoothManager.startMonitoring()
        BudsOverlayService.start(context)
        onDispose {
            bluetoothManager.stopMonitoring()
        }
    }

    val bgColor = if (isDarkTheme) Color(0xFF0F172A) else Color(0xFFF8FAFC)
    val cardBg = if (isDarkTheme) Color(0xFF1E293B) else Color(0xFFFFFFFF)
    val textPrimary = if (isDarkTheme) Color(0xFFF8FAFC) else Color(0xFF0F172A)
    val textSecondary = if (isDarkTheme) Color(0xFF94A3B8) else Color(0xFF64748B)

    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                text = "Buds Studio",
                                fontWeight = FontWeight.Bold,
                                fontSize = 18.sp,
                                color = textPrimary
                            )
                            Spacer(modifier = Modifier.width(6.dp))
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(4.dp))
                                    .background(selectedAccent.color.copy(alpha = 0.15f))
                                    .padding(horizontal = 6.dp, vertical = 2.dp)
                            ) {
                                Text(
                                    text = "Android Pro",
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = selectedAccent.color
                                )
                            }
                        }
                        Text(
                            text = batteryState?.let { "${it.deviceName} — Подключено" }
                                ?: "Ожидание подключения...",
                            fontSize = 12.sp,
                            color = if (batteryState != null) Color(0xFF10B981) else textSecondary
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = cardBg)
            )
        },
        containerColor = bgColor
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp)
        ) {
            // 1. Hero Battery Card
            HeroBatteryCard(
                state = batteryState,
                accent = selectedAccent.color,
                cardBg = cardBg,
                textPrimary = textPrimary,
                textSecondary = textSecondary
            )

            Spacer(modifier = Modifier.height(16.dp))

            // 2. System Service & Overlay Permissions
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = cardBg),
                elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text(
                        text = "СИСТЕМА И РАЗРЕШЕНИЯ",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        color = textSecondary
                    )

                    Spacer(modifier = Modifier.height(12.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text("Фоновый сервис мониторинга", fontWeight = FontWeight.SemiBold, color = textPrimary)
                            Text("Автоматически определяет подключение в фоне", fontSize = 12.sp, color = textSecondary)
                        }
                        Switch(
                            checked = isServiceRunning,
                            onCheckedChange = {
                                isServiceRunning = it
                                if (it) BudsOverlayService.start(context) else BudsOverlayService.stop(context)
                            },
                            colors = SwitchDefaults.colors(checkedThumbColor = Color.White, checkedTrackColor = selectedAccent.color)
                        )
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    val canDrawOverlay = Build.VERSION.SDK_INT < Build.VERSION_CODES.M || Settings.canDrawOverlays(context)
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text("Всплывающий виджет (Overlay)", fontWeight = FontWeight.SemiBold, color = textPrimary)
                            Text(
                                if (canDrawOverlay) "Разрешено" else "Требуется разрешение отображения поверх окон",
                                fontSize = 12.sp,
                                color = if (canDrawOverlay) Color(0xFF10B981) else Color(0xFFEF4444)
                            )
                        }
                        if (!canDrawOverlay && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                            Button(
                                onClick = {
                                    val intent = Intent(
                                        Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                                        Uri.parse("package:${context.packageName}")
                                    )
                                    context.startActivity(intent)
                                },
                                shape = RoundedCornerShape(8.dp),
                                colors = ButtonDefaults.buttonColors(containerColor = selectedAccent.color)
                            ) {
                                Text("Включить", fontSize = 12.sp)
                            }
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // 3. Customization & Styling
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = cardBg),
                elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text(
                        text = "КАСТОМИЗАЦИЯ",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        color = textSecondary
                    )

                    Spacer(modifier = Modifier.height(12.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("Тёмная тема (Obsidian)", fontWeight = FontWeight.SemiBold, color = textPrimary)
                        Switch(
                            checked = isDarkTheme,
                            onCheckedChange = { isDarkTheme = it },
                            colors = SwitchDefaults.colors(checkedThumbColor = Color.White, checkedTrackColor = selectedAccent.color)
                        )
                    }

                    Spacer(modifier = Modifier.height(14.dp))

                    Text("Акцентный цвет:", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = textPrimary)
                    Spacer(modifier = Modifier.height(8.dp))

                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        AccentColor.values().forEach { item ->
                            Box(
                                modifier = Modifier
                                    .size(36.dp)
                                    .clip(CircleShape)
                                    .background(item.color)
                                    .border(
                                        width = if (selectedAccent == item) 3.dp else 1.dp,
                                        color = if (selectedAccent == item) Color.White else Color.Transparent,
                                        shape = CircleShape
                                    )
                                    .clickable { selectedAccent = item }
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(18.dp))

                    // Test Popup Trigger Button
                    Button(
                        onClick = {
                            val intent = Intent(context, BudsOverlayService::class.java).apply {
                                action = BudsOverlayService.ACTION_TRIGGER_PREVIEW
                            }
                            context.startService(intent)
                        },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(10.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = selectedAccent.color)
                    ) {
                        Text("Проверить анимацию виджета", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}

@Composable
fun HeroBatteryCard(
    state: BudsBatteryState?,
    accent: Color,
    cardBg: Color,
    textPrimary: Color,
    textSecondary: Color
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.cardColors(containerColor = cardBg),
        elevation = CardDefaults.cardElevation(defaultElevation = 3.dp)
    ) {
        Column(
            modifier = Modifier.padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            val isOverEar = state?.isOverEar == true

            if (isOverEar) {
                // Over-Ear Layout
                val pct = state.left?.percent ?: 80
                BatteryGaugeItem(
                    label = "Наушники",
                    status = "Подключено",
                    percent = pct,
                    iconRes = R.drawable.ic_headphones_overear,
                    accent = accent,
                    textPrimary = textPrimary,
                    textSecondary = textSecondary
                )
            } else {
                // TWS 3-pod Layout
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceEvenly
                ) {
                    val leftPct = state?.left?.percent ?: 80
                    val casePct = state?.case?.percent
                    val rightPct = state?.right?.percent ?: 80

                    BatteryGaugeItem(
                        label = "Левый",
                        status = if (state != null) "В ухе" else "Демо",
                        percent = leftPct,
                        iconRes = R.drawable.ic_bud_left,
                        accent = accent,
                        textPrimary = textPrimary,
                        textSecondary = textSecondary
                    )

                    BatteryGaugeItem(
                        label = "Кейс",
                        status = if (casePct != null && casePct > 0) "Открыт" else "В ушах",
                        percent = casePct,
                        iconRes = R.drawable.ic_case_pebble,
                        accent = accent,
                        textPrimary = textPrimary,
                        textSecondary = textSecondary
                    )

                    BatteryGaugeItem(
                        label = "Правый",
                        status = if (state != null) "В ухе" else "Демо",
                        percent = rightPct,
                        iconRes = R.drawable.ic_bud_right,
                        accent = accent,
                        textPrimary = textPrimary,
                        textSecondary = textSecondary
                    )
                }
            }
        }
    }
}

@Composable
fun BatteryGaugeItem(
    label: String,
    status: String,
    percent: Int?,
    iconRes: Int,
    accent: Color,
    textPrimary: Color,
    textSecondary: Color
) {
    val progress = (percent?.coerceIn(0, 100) ?: 0) / 100f
    val animatedProgress by animateFloatAsState(
        targetValue = progress,
        animationSpec = tween(durationMillis = 600),
        label = "gauge"
    )

    Column(
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(
            modifier = Modifier.size(76.dp),
            contentAlignment = Alignment.Center
        ) {
            CircularProgressIndicator(
                progress = { 1f },
                modifier = Modifier.fillMaxSize(),
                color = accent.copy(alpha = 0.15f),
                strokeWidth = 5.dp
            )
            if (percent != null && percent > 0) {
                CircularProgressIndicator(
                    progress = { animatedProgress },
                    modifier = Modifier.fillMaxSize(),
                    color = accent,
                    strokeWidth = 5.dp
                )
            }
            Icon(
                painter = painterResource(id = iconRes),
                contentDescription = label,
                tint = accent,
                modifier = Modifier.size(38.dp)
            )
        }

        Spacer(modifier = Modifier.height(8.dp))

        Text(
            text = if (percent != null && percent > 0) "$percent%" else "—",
            fontSize = 18.sp,
            fontWeight = FontWeight.Bold,
            color = textPrimary
        )

        Text(
            text = status,
            fontSize = 12.sp,
            color = textSecondary
        )
    }
}
