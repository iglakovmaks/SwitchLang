"""SwitchLang desktop MVP.

Run with ``python main.py`` for the small control window or with
``python main.py --background`` for a background-only process.
"""

from __future__ import annotations

import argparse
import threading
import tkinter as tk
from tkinter import messagebox, ttk

from platform_backend import BackendError, PlatformBackend, create_backend


class LayoutWatcher:
    def __init__(self, backend: PlatformBackend, on_change, interval: float = 0.15) -> None:
        self.backend = backend
        self.on_change = on_change
        self.interval = interval
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._last_layout: str | None = None

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        try:
            self._last_layout = self.backend.current_layout()
        except Exception:
            self._last_layout = None
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="switchlang-layout", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=0.8)

    def _run(self) -> None:
        while not self._stop.wait(self.interval):
            try:
                current = self.backend.current_layout()
            except Exception:
                current = None
            previous = self._last_layout
            if current != previous:
                self._last_layout = current
                if previous in {"ru", "en"} and current in {"ru", "en"}:
                    self.on_change(previous, current)


class SwitchLangApp:
    def __init__(self, background: bool = False) -> None:
        self.backend = create_backend()
        self.background = background
        self.enabled = True
        self._conversion_lock = threading.Lock()
        self._pending_change: tuple[str, str] | None = None
        self._root = tk.Tk()
        self._root.title("SwitchLang")
        self._root.geometry("500x360")
        self._root.minsize(460, 320)
        self._root.protocol("WM_DELETE_WINDOW", self._close)
        self._build_ui()
        self.watcher = LayoutWatcher(self.backend, self._on_layout_change)
        self.watcher.start()
        self._root.after(500, self._refresh_status)
        if background:
            self._root.withdraw()

    def _build_ui(self) -> None:
        root = self._root
        frame = ttk.Frame(root, padding=24)
        frame.pack(fill="both", expand=True)

        ttk.Label(frame, text="SwitchLang", font=("TkDefaultFont", 22, "bold")).pack(
            anchor="w"
        )
        ttk.Label(
            frame,
            text="Исправление текста, набранного в неправильной раскладке",
            foreground="#555555",
        ).pack(anchor="w", pady=(4, 18))

        self.layout_var = tk.StringVar(value="Определяю текущую раскладку…")
        ttk.Label(frame, textvariable=self.layout_var, font=("TkDefaultFont", 12, "bold")).pack(
            anchor="w"
        )

        self.enabled_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(
            frame,
            text="Исправлять выделение при смене раскладки",
            variable=self.enabled_var,
            command=self._toggle_enabled,
        ).pack(anchor="w", pady=(18, 4))

        ttk.Label(
            frame,
            text=(
                "Выделите ошибочный текст и нажмите обычное системное сочетание "
                "переключения раскладки. SwitchLang заметит смену RU ↔ EN и заменит "
                "выделение автоматически."
            ),
            wraplength=440,
            justify="left",
        ).pack(anchor="w", pady=(0, 14))

        self.status_var = tk.StringVar(value="Запущено в фоне")
        ttk.Label(frame, textvariable=self.status_var, wraplength=440).pack(anchor="w")

        actions = ttk.Frame(frame)
        actions.pack(fill="x", side="bottom", pady=(20, 0))
        ttk.Button(actions, text="Добавить в автозапуск", command=self._install_autostart).pack(
            side="left"
        )
        ttk.Button(actions, text="Свернуть", command=root.iconify).pack(side="right")

        if self.background:
            self.status_var.set("Работает в фоне. Откройте приложение повторно для настроек.")

    def _toggle_enabled(self) -> None:
        self.enabled = self.enabled_var.get()
        self.status_var.set("Автоматическое исправление включено" if self.enabled else "Автоматическое исправление выключено")

    def _refresh_status(self) -> None:
        try:
            layout = self.backend.current_layout()
        except Exception:
            layout = None
        if layout == "ru":
            label = "Текущая раскладка: RU"
        elif layout == "en":
            label = "Текущая раскладка: EN"
        else:
            label = "Текущая раскладка: не поддерживается"
        self.layout_var.set(label)
        self._root.after(700, self._refresh_status)

    def _on_layout_change(self, previous: str, current: str) -> None:
        if not self.enabled:
            return
        if not self._conversion_lock.acquire(blocking=False):
            self._pending_change = (previous, current)
            return

        def worker() -> None:
            try:
                changed, details = self.backend.replace_selected_text(previous)
            except BackendError as error:
                changed, details = False, str(error)
            except Exception as error:  # keep the resident app alive after an app-specific failure
                changed, details = False, f"Ошибка интеграции: {error}"
            finally:
                self._conversion_lock.release()
                pending = self._pending_change
                self._pending_change = None
                if pending:
                    self._root.after(0, lambda: self._on_layout_change(*pending))
            self._root.after(0, lambda: self._show_result(changed, details))

        threading.Thread(target=worker, name="switchlang-convert", daemon=True).start()

    def _show_result(self, changed: bool, details: str) -> None:
        if changed:
            self.status_var.set(f"Готово — {details}")
        elif details != "Выделенный текст не найден":
            self.status_var.set(details)

    def _install_autostart(self) -> None:
        try:
            self.backend.install_autostart()
        except Exception as error:
            messagebox.showerror("SwitchLang", str(error), parent=self._root)
            return
        self.status_var.set("Автозапуск включён")
        messagebox.showinfo(
            "SwitchLang",
            "SwitchLang будет запускаться вместе с системой и работать в фоне.",
            parent=self._root,
        )

    def _close(self) -> None:
        self.watcher.stop()
        self._root.destroy()

    def run(self) -> None:
        self._root.mainloop()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Исправление текста в неправильной раскладке")
    parser.add_argument(
        "--background",
        action="store_true",
        help="запустить без окна управления",
    )
    parser.add_argument(
        "--install-autostart",
        action="store_true",
        help="добавить SwitchLang в автозапуск и запустить",
    )
    return parser.parse_args()


if __name__ == "__main__":
    arguments = parse_args()
    app = SwitchLangApp(background=arguments.background)
    if arguments.install_autostart:
        app._install_autostart()
    app.run()
