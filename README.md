# ORBITDESK Helper

Lightweight, zero-overhead native companion application for the **ORBITDESK — Reactive Desktop** wallpaper in Wallpaper Engine.

[🇷🇺 Инструкция на русском языке ниже](#ru)

---

## English

### Overview
ORBITDESK works out-of-the-box as a standalone wallpaper with clock, day progress, weather, offline notes, music playback and an interactive cat companion. 

Installing **ORBITDESK Helper** is completely optional and unlocks advanced native desktop metrics:
- **Bluetooth & Device Battery Rings**: Real-time battery levels for headphones, mouse, and keyboard via Windows SetupAPI.
- **Application Screen Time**: Honest tracking of active foreground applications without tracking idle or launcher processes.
- **Network Diagnostics**: Cloudflare ICMP latency & jitter, plus nearest Valve SDR relay ping for Counter-Strike 2.
- **Hardware Telemetry**: Real-time CPU usage, RAM utilization, Disk C: capacity, and download/upload network throughput via native Win32 APIs.
- **Tactile Media Transport**: Play/pause, next/previous track, volume and seek controls directly on the wallpaper via Windows System Media Transport Controls (SMTC).

### Privacy & Security
- **Strictly Local**: Listens exclusively on local loopback `127.0.0.1:18765`. Never connects to any remote telemetry server.
- **Explicit Pairing**: New connections require explicit user approval via a native Windows confirmation dialog showing a 6-digit match code. No data or tokens are transmitted without user consent.
- **Single Process**: Lightweight background service consuming **< 0.05% CPU** and **~50 MB RAM**.
- **No Background Launchers**: Does not install background updater services or require administrator privileges.

### Requirements
- Windows 10 (version 1903 or newer) or Windows 11.
- .NET Framework 4.8 (pre-installed on modern Windows).
- Wallpaper Engine with **ORBITDESK — Reactive Desktop** installed from Steam Workshop.

### Quick Setup
1. Download `ORBITDESK-Helper.exe` from the [Latest Release](https://github.com/220hiro/orbitdesk-companion/releases/latest).
2. Double-click `ORBITDESK-Helper.exe`. It will automatically configure itself and start in the background.
3. Open your desktop with ORBITDESK wallpaper and click **Подключить Helper** (Connect Helper).
4. A native Windows confirmation window will appear showing a 6-digit code. Confirm that the codes match.
5. All peripheral batteries, telemetry, and network diagnostics will appear on your desktop instantly!

### Uninstallation
To uninstall, simply close the application and delete the executable. Stored local pairings are kept in `%LOCALAPPDATA%\TerminalDeskNext` and can be deleted at any time.

---

<a name="ru"></a>
## Русский

### Описание
Обои **ORBITDESK** полностью самодостаточны и работают из коробки без сторонних программ: показывают часы, прогресс дня, погоду, офлайн-заметки, музыку, синхронизированные субтитры и интерактивного пиксельного кота.

**ORBITDESK Helper** — это опциональная фоновая программа-компаньон для Windows, которая расширяет возможности рабочего стола:
- **Заряд Bluetooth-устройств**: круговые индикаторы батареи для наушников, мыши и клавиатуры через Windows SetupAPI.
- **Учёт экранного времени**: честный Screen Time активного окна без учёта системных лаунчеров и простоя.
- **Сетевая диагностика**: пинг и джиттер Cloudflare ICMP, а также поиск ближайших игровых реле Valve SDR (CS2).
- **Аппаратная телеметрия**: загрузка CPU, использование RAM, занятое место на диске C: и скорость сети через нативные вызовы Windows.
- **Управление медиа**: кнопки паузы, переключения треков, громкости и перемотки прямо на рабочем столе через Windows SMTC.

### Безопасность и приватность
- **Строго локально**: работает исключительно на локальном адресе `127.0.0.1:18765`. Никакие персональные данные не отправляются в интернет.
- **Нативное сопряжение**: подключение обоев требует явного подтверждения в системном окне с 6-значным кодом проверки.
- **Минимальное потребление**: процесс потребляет **менее 0.05% CPU** и около 50 МБ оперативной памяти.
- Не требует прав администратора и не устанавливает лишних служб.

### Установка в 2 шага
1. Скачайте `ORBITDESK-Helper.exe` из раздела [Releases](https://github.com/220hiro/orbitdesk-companion/releases/latest).
2. Запустите файл двойным кликом. Helper предложит включить автозапуск с Windows и начнёт работу в фоне (без лишних значков).
3. Нажмите кнопку **Подключить Helper** на рабочем столе в обоях ORBITDESK.
4. В появившемся окне Windows подтвердите совпадение 6-значного кода.
5. Всё готово! Данные появятся на обоях автоматически.

---

## License
MIT License. Open source and free for personal and commercial use.
