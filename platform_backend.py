"""Operating-system integrations used by the SwitchLang background worker."""

from __future__ import annotations

import ctypes
import plistlib
import re
import subprocess
import sys
import time
from abc import ABC, abstractmethod
from ctypes import wintypes
from pathlib import Path

from switchlang_core import can_convert, convert_layout, normalize_layout


def utf16_length(text: str) -> int:
    """Return the number of UTF-16 code units used by native text controls."""

    return len(text.encode("utf-16-le")) // 2


class BackendError(RuntimeError):
    """An expected platform integration failure."""


class PlatformBackend(ABC):
    """Common interface for layout detection and selection replacement."""

    name = "unknown"

    @abstractmethod
    def current_layout(self) -> str | None:
        raise NotImplementedError

    @abstractmethod
    def replace_selected_text(self, current_layout: str) -> tuple[bool, str]:
        """Replace selected text and return ``(changed, details)``."""

    def install_autostart(self) -> None:
        raise BackendError("Автозапуск для этой платформы пока не реализован")


def create_backend() -> PlatformBackend:
    if sys.platform == "darwin":
        return MacBackend()
    if sys.platform == "win32":
        return WindowsBackend()
    return UnsupportedBackend()


class UnsupportedBackend(PlatformBackend):
    name = "unsupported"

    def current_layout(self) -> str | None:
        return None

    def replace_selected_text(self, current_layout: str) -> tuple[bool, str]:
        return False, "Поддерживаются только macOS и Windows"


class MacBackend(PlatformBackend):
    name = "macOS"

    _UTF8_ENCODING = 0x08000100

    def __init__(self) -> None:
        self._carbon = None
        self._core_foundation = None
        try:
            self._carbon = ctypes.CDLL("/System/Library/Frameworks/Carbon.framework/Carbon")
            self._core_foundation = ctypes.CDLL(
                "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"
            )
            self._configure_carbon()
        except (OSError, AttributeError):
            # The `defaults` fallback still works on supported macOS versions.
            self._carbon = None
            self._core_foundation = None

    def _configure_carbon(self) -> None:
        assert self._carbon is not None
        assert self._core_foundation is not None

        self._carbon.TISCopyCurrentKeyboardInputSource.restype = ctypes.c_void_p
        self._carbon.TISGetInputSourceProperty.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
        self._carbon.TISGetInputSourceProperty.restype = ctypes.c_void_p

        self._core_foundation.CFStringGetCString.argtypes = [
            ctypes.c_void_p,
            ctypes.c_char_p,
            ctypes.c_long,
            ctypes.c_uint32,
        ]
        self._core_foundation.CFStringGetCString.restype = ctypes.c_bool
        self._core_foundation.CFArrayGetCount.argtypes = [ctypes.c_void_p]
        self._core_foundation.CFArrayGetCount.restype = ctypes.c_long
        self._core_foundation.CFArrayGetValueAtIndex.argtypes = [
            ctypes.c_void_p,
            ctypes.c_long,
        ]
        self._core_foundation.CFArrayGetValueAtIndex.restype = ctypes.c_void_p
        self._core_foundation.CFRelease.argtypes = [ctypes.c_void_p]

    def _cf_string(self, value: int | None) -> str:
        if not value or self._core_foundation is None:
            return ""
        buffer = ctypes.create_string_buffer(512)
        ok = self._core_foundation.CFStringGetCString(
            ctypes.c_void_p(value), buffer, len(buffer), self._UTF8_ENCODING
        )
        return buffer.value.decode("utf-8", errors="replace") if ok else ""

    def _current_source_metadata(self) -> tuple[str, list[str]]:
        if self._carbon is None or self._core_foundation is None:
            return "", []

        source = self._carbon.TISCopyCurrentKeyboardInputSource()
        if not source:
            return "", []

        try:
            source_id_key = ctypes.c_void_p.in_dll(
                self._carbon, "kTISPropertyInputSourceID"
            )
            languages_key = ctypes.c_void_p.in_dll(
                self._carbon, "kTISPropertyInputSourceLanguages"
            )
            source_id = self._cf_string(
                self._carbon.TISGetInputSourceProperty(source, source_id_key)
            )
            languages_ref = self._carbon.TISGetInputSourceProperty(source, languages_key)
            languages: list[str] = []
            if languages_ref:
                count = self._core_foundation.CFArrayGetCount(languages_ref)
                for index in range(count):
                    value = self._core_foundation.CFArrayGetValueAtIndex(
                        languages_ref, index
                    )
                    languages.append(self._cf_string(value))
            return source_id, languages
        finally:
            self._core_foundation.CFRelease(source)

    def current_layout(self) -> str | None:
        source_id, languages = self._current_source_metadata()
        haystack = " ".join([source_id, *languages]).lower()
        if "russian" in haystack or "рус" in haystack or "ru" in languages:
            return "ru"
        if "english" in haystack or "abc" in haystack or any(
            language == "en" or language.startswith("en_") for language in languages
        ):
            return "en"

        # TIS is the accurate path. This fallback is useful in restricted
        # environments and keeps the app usable when Carbon symbols change.
        try:
            output = subprocess.run(
                ["defaults", "read", "com.apple.HIToolbox", "AppleSelectedInputSources"],
                capture_output=True,
                text=True,
                timeout=1.0,
                check=False,
            ).stdout.lower()
        except (OSError, subprocess.SubprocessError):
            return None
        if re.search(r"russian|рус|keyboardlayout name = ru", output):
            return "ru"
        if re.search(r"english|abc|keyboardlayout name = en", output):
            return "en"
        return None

    @staticmethod
    def _run_applescript(script: str) -> None:
        result = subprocess.run(
            ["osascript", "-e", script],
            capture_output=True,
            text=True,
            timeout=3.0,
            check=False,
        )
        if result.returncode != 0:
            error = result.stderr.strip() or "osascript завершился с ошибкой"
            raise BackendError(
                "macOS не разрешил отправить копирование/вставку. "
                "Разрешите SwitchLang управление компьютером в настройках Accessibility. "
                f"({error})"
            )

    @staticmethod
    def _clipboard_text() -> str:
        result = subprocess.run(
            ["pbpaste"], capture_output=True, timeout=2.0, check=False
        )
        return result.stdout.decode("utf-8", errors="replace") if result.returncode == 0 else ""

    @staticmethod
    def _set_clipboard_text(text: str) -> None:
        subprocess.run(
            ["pbcopy"], input=text.encode("utf-8"), capture_output=True, timeout=2.0, check=False
        )

    def _copy(self) -> None:
        self._run_applescript(
            'tell application "System Events" to keystroke "c" using {command down}'
        )

    def _paste(self) -> None:
        self._run_applescript(
            'tell application "System Events" to keystroke "v" using {command down}'
        )

    def _select_previous_characters(self, count: int) -> None:
        if count <= 0:
            return
        script = f'''tell application "System Events"
repeat {count} times
    key code 123 using {{shift down}}
end repeat
end tell'''
        self._run_applescript(script)

    def replace_selected_text(self, current_layout: str) -> tuple[bool, str]:
        layout = normalize_layout(current_layout)
        if layout not in {"ru", "en"}:
            return False, "Текущая раскладка не поддерживается"

        original_clipboard = self._clipboard_text()
        try:
            # Emptying the clipboard lets us distinguish “no selection” from a
            # stale value left there by a previous copy operation.
            self._set_clipboard_text("")
            self._copy()
            time.sleep(0.12)
            selected = self._clipboard_text()
            if not selected or not can_convert(selected, layout):
                return False, "Выделенный текст не найден"

            converted = convert_layout(selected, layout)
            if converted == selected:
                return False, "В выделении нет символов для преобразования"

            self._set_clipboard_text(converted)
            self._paste()
            time.sleep(0.12)
            self._select_previous_characters(utf16_length(converted))
            return True, f"Исправлено символов: {len(selected)}"
        finally:
            self._set_clipboard_text(original_clipboard)

    def install_autostart(self) -> None:
        launch_agents = Path.home() / "Library" / "LaunchAgents"
        launch_agents.mkdir(parents=True, exist_ok=True)
        plist_path = launch_agents / "com.switchlang.app.plist"
        python_path = Path(sys.executable).resolve()
        script_path = Path(__file__).resolve()
        with plist_path.open("wb") as plist_file:
            plistlib.dump(
                {
                    "Label": "com.switchlang.app",
                    "ProgramArguments": [str(python_path), str(script_path), "--background"],
                    "RunAtLoad": True,
                    "KeepAlive": True,
                },
                plist_file,
            )


class WindowsBackend(PlatformBackend):
    name = "Windows"

    CF_UNICODETEXT = 13
    GMEM_MOVEABLE = 0x0002
    INPUT_KEYBOARD = 1
    KEYEVENTF_KEYUP = 0x0002
    VK_CONTROL = 0x11

    def __init__(self) -> None:
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._configure_apis()

    def _configure_apis(self) -> None:
        self.user32.GetForegroundWindow.restype = wintypes.HWND
        self.user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        self.user32.GetWindowThreadProcessId.restype = wintypes.DWORD
        self.user32.GetKeyboardLayout.argtypes = [wintypes.DWORD]
        self.user32.GetKeyboardLayout.restype = ctypes.c_void_p
        self.user32.OpenClipboard.argtypes = [wintypes.HWND]
        self.user32.OpenClipboard.restype = wintypes.BOOL
        self.user32.CloseClipboard.restype = wintypes.BOOL
        self.user32.EmptyClipboard.restype = wintypes.BOOL
        self.user32.GetClipboardData.argtypes = [wintypes.UINT]
        self.user32.GetClipboardData.restype = wintypes.HANDLE
        self.user32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
        self.user32.SetClipboardData.restype = wintypes.HANDLE
        self.kernel32.GlobalLock.argtypes = [wintypes.HGLOBAL]
        self.kernel32.GlobalLock.restype = ctypes.c_void_p
        self.kernel32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
        self.kernel32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
        self.kernel32.GlobalAlloc.restype = wintypes.HGLOBAL
        self.kernel32.GlobalFree.argtypes = [wintypes.HGLOBAL]
        self.kernel32.GlobalFree.restype = wintypes.HGLOBAL

        class KEYBDINPUT(ctypes.Structure):
            _fields_ = [
                ("wVk", wintypes.WORD),
                ("wScan", wintypes.WORD),
                ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD),
                ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong)),
            ]

        class INPUT_UNION(ctypes.Union):
            _fields_ = [("ki", KEYBDINPUT)]

        class INPUT(ctypes.Structure):
            _fields_ = [("type", wintypes.DWORD), ("union", INPUT_UNION)]

        self._INPUT = INPUT
        self._KEYBDINPUT = KEYBDINPUT
        self.user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
        self.user32.SendInput.restype = wintypes.UINT

    def current_layout(self) -> str | None:
        foreground = self.user32.GetForegroundWindow()
        if not foreground:
            return None
        process_id = wintypes.DWORD()
        thread_id = self.user32.GetWindowThreadProcessId(foreground, ctypes.byref(process_id))
        hkl = self.user32.GetKeyboardLayout(thread_id)
        hkl_value = hkl.value if isinstance(hkl, ctypes.c_void_p) else int(hkl or 0)
        lang_id = hkl_value & 0xFFFF
        primary_language = lang_id & 0x03FF
        if primary_language == 0x19:
            return "ru"
        if primary_language == 0x09:
            return "en"
        return None

    def _open_clipboard(self) -> None:
        for _ in range(10):
            if self.user32.OpenClipboard(None):
                return
            time.sleep(0.02)
        raise BackendError("Windows не смог открыть буфер обмена")

    def _clipboard_text(self) -> str:
        self._open_clipboard()
        try:
            handle = self.user32.GetClipboardData(self.CF_UNICODETEXT)
            if not handle:
                return ""
            pointer = self.kernel32.GlobalLock(handle)
            if not pointer:
                return ""
            try:
                return ctypes.wstring_at(pointer)
            finally:
                self.kernel32.GlobalUnlock(handle)
        finally:
            self.user32.CloseClipboard()

    def _set_clipboard_text(self, text: str) -> None:
        encoded_size = (len(text) + 1) * ctypes.sizeof(ctypes.c_wchar)
        handle = self.kernel32.GlobalAlloc(self.GMEM_MOVEABLE, encoded_size)
        if not handle:
            raise BackendError("Windows не смог выделить память для буфера обмена")
        pointer = self.kernel32.GlobalLock(handle)
        if not pointer:
            raise BackendError("Windows не смог записать буфер обмена")
        try:
            ctypes.memmove(pointer, ctypes.create_unicode_buffer(text), encoded_size)
        finally:
            self.kernel32.GlobalUnlock(handle)

        self._open_clipboard()
        try:
            self.user32.EmptyClipboard()
            if not self.user32.SetClipboardData(self.CF_UNICODETEXT, handle):
                self.kernel32.GlobalFree(handle)
                raise BackendError("Windows не смог установить буфер обмена")
            # Ownership of handle moves to the clipboard after SetClipboardData.
        finally:
            self.user32.CloseClipboard()

    def _send_ctrl_key(self, key_code: int) -> None:
        extra_info = ctypes.c_ulong(0)
        inputs = (self._INPUT * 4)()
        events = [
            (self.VK_CONTROL, 0),
            (key_code, 0),
            (key_code, self.KEYEVENTF_KEYUP),
            (self.VK_CONTROL, self.KEYEVENTF_KEYUP),
        ]
        for index, (virtual_key, flags) in enumerate(events):
            inputs[index].type = self.INPUT_KEYBOARD
            inputs[index].union.ki = self._KEYBDINPUT(
                virtual_key, 0, flags, 0, ctypes.pointer(extra_info)
            )
        sent = self.user32.SendInput(4, inputs, ctypes.sizeof(self._INPUT))
        if sent != 4:
            raise BackendError("Windows не смог отправить Ctrl+C/Ctrl+V")

    def _select_previous_characters(self, count: int) -> None:
        if count <= 0:
            return
        extra_info = ctypes.c_ulong(0)
        inputs = (self._INPUT * (2 + count * 2))()
        events: list[tuple[int, int]] = [(0x10, 0)]
        for _ in range(count):
            events.extend([(0x25, 0), (0x25, self.KEYEVENTF_KEYUP)])
        events.append((0x10, self.KEYEVENTF_KEYUP))
        for index, (virtual_key, flags) in enumerate(events):
            inputs[index].type = self.INPUT_KEYBOARD
            inputs[index].union.ki = self._KEYBDINPUT(
                virtual_key, 0, flags, 0, ctypes.pointer(extra_info)
            )
        sent = self.user32.SendInput(len(events), inputs, ctypes.sizeof(self._INPUT))
        if sent != len(events):
            raise BackendError("Windows не смог повторно выделить исправленный текст")

    def replace_selected_text(self, current_layout: str) -> tuple[bool, str]:
        layout = normalize_layout(current_layout)
        if layout not in {"ru", "en"}:
            return False, "Текущая раскладка не поддерживается"

        original_clipboard = self._clipboard_text()
        try:
            self._set_clipboard_text("")
            self._send_ctrl_key(ord("C"))
            time.sleep(0.12)
            selected = self._clipboard_text()
            if not selected or not can_convert(selected, layout):
                return False, "Выделенный текст не найден"

            converted = convert_layout(selected, layout)
            if converted == selected:
                return False, "В выделении нет символов для преобразования"

            self._set_clipboard_text(converted)
            self._send_ctrl_key(ord("V"))
            time.sleep(0.12)
            self._select_previous_characters(utf16_length(converted))
            return True, f"Исправлено символов: {len(selected)}"
        finally:
            self._set_clipboard_text(original_clipboard)

    def install_autostart(self) -> None:
        import winreg

        executable = Path(sys.executable).resolve()
        script = Path(__file__).resolve()
        command = f'"{executable}" "{script}" --background'
        with winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Run",
            0,
            winreg.KEY_SET_VALUE,
        ) as key:
            winreg.SetValueEx(key, "SwitchLang", 0, winreg.REG_SZ, command)
