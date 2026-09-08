import AppKit
import ApplicationServices
import Carbon
import CoreServices
import Foundation
import ServiceManagement

private enum Layout: Equatable {
    case russian
    case english

    var title: String {
        switch self {
        case .russian: return "RU"
        case .english: return "EN"
        }
    }
}

private struct LayoutConverter {
    private static let englishUnshifted = Array("`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./")
    private static let russianUnshifted = Array("ё1234567890-=йцукенгшщзхъ\\фывапролджэячсмитьбю.")
    private static let englishShifted = Array("~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?")
    private static let russianShifted = Array("Ё!\"№;%:?*()_+ЙЦУКЕНГШЩЗХЪ/ФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,")
    private static let punctuation = Set(",./<>?")

    private static let maps: ([Character: Character], [Character: Character]) = {
        var englishToRussian: [Character: Character] = [:]
        var russianToEnglish: [Character: Character] = [:]

        for (source, target) in [
            (englishUnshifted, russianUnshifted),
            (englishShifted, russianShifted)
        ] {
            for (sourceCharacter, targetCharacter) in zip(source, target) {
                // The question mark is layout-neutral in SwitchLang.
                if sourceCharacter == "?" || targetCharacter == "?" {
                    continue
                }
                if sourceCharacter.isLetter || targetCharacter.isLetter ||
                    punctuation.contains(sourceCharacter) || punctuation.contains(targetCharacter) {
                    englishToRussian[sourceCharacter] = targetCharacter
                    russianToEnglish[targetCharacter] = sourceCharacter
                }
            }
        }
        return (englishToRussian, russianToEnglish)
    }()

    static func convert(_ text: String, from layout: Layout) -> String {
        let map = layout == .russian ? maps.1 : maps.0
        return String(text.map { map[$0] ?? $0 })
    }

    static func canConvert(_ text: String, from layout: Layout) -> Bool {
        let map = layout == .russian ? maps.1 : maps.0
        return text.contains { map[$0] != nil }
    }
}

private struct InputSourceReader {
    static func currentLayout() -> Layout? {
        guard let unmanagedSource = TISCopyCurrentKeyboardInputSource() else {
            return nil
        }
        let source = unmanagedSource.takeUnretainedValue()

        let sourceID = propertyString(
            TISGetInputSourceProperty(source, kTISPropertyInputSourceID)
        )?.lowercased() ?? ""
        let languages = propertyArray(
            TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages)
        ).map { $0.lowercased() }

        if sourceID.contains("russian") || languages.contains("ru") {
            return .russian
        }
        if sourceID.contains("english") || sourceID.contains("abc") ||
            languages.contains(where: { $0 == "en" || $0.hasPrefix("en_") }) {
            return .english
        }
        return nil
    }

    private static func propertyString(_ pointer: UnsafeMutableRawPointer?) -> String? {
        guard let pointer else { return nil }
        return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
    }

    private static func propertyArray(_ pointer: UnsafeMutableRawPointer?) -> [String] {
        guard let pointer else { return [] }
        let array = Unmanaged<CFArray>.fromOpaque(pointer).takeUnretainedValue() as NSArray
        return array.compactMap { $0 as? String }
    }
}

private struct AXSelection {
    let element: AXUIElement
    let text: String
    let range: CFRange?
}

private enum AXSelectionState {
    case text(AXSelection)
    case confirmedSelection
    case noSelection
    case unavailable
}

private final class AccessibilityBridge {
    var isTrusted: Bool {
        AXIsProcessTrusted()
    }

    func selectionState() -> AXSelectionState {
        // Without Accessibility permission we cannot safely distinguish an
        // empty selection from an editor that needs the clipboard fallback.
        // Do not synthesize Cmd+C/V in that state — it can produce a system beep.
        guard isTrusted else { return .noSelection }

        let system = AXUIElementCreateSystemWide()
        var focusedValue: CFTypeRef?
        let focusedError = AXUIElementCopyAttributeValue(
            system,
            kAXFocusedUIElementAttribute as CFString,
            &focusedValue
        )
        guard focusedError == .success,
              let focusedValue else {
            return .noSelection
        }
        let element = focusedValue as! AXUIElement

        var range: CFRange?
        var rangeValue: CFTypeRef?
        let rangeError = AXUIElementCopyAttributeValue(
            element,
            kAXSelectedTextRangeAttribute as CFString,
            &rangeValue
        )
        if rangeError == .success,
           let rangeValue {
            let axValue = rangeValue as! AXValue
            var value = CFRange(location: 0, length: 0)
            if AXValueGetValue(axValue, .cfRange, &value) {
                range = value
            }
        }

        var selectedValue: CFTypeRef?
        let selectedError = AXUIElementCopyAttributeValue(
            element,
            kAXSelectedTextAttribute as CFString,
            &selectedValue
        )
        if selectedError == .success,
           let selectedValue,
           let selectedString = selectedValue as? String {
            if !selectedString.isEmpty {
                return .text(AXSelection(element: element, text: selectedString, range: range))
            }
        }

        if range?.length == 0 {
            return .noSelection
        }

        if let range, range.length > 0 {
            // Word and Pages can expose the selected range while refusing
            // direct selected-text access. The range is enough to safely use
            // the clipboard fallback without sending Cmd+C on empty focus.
            return .confirmedSelection
        }

        // The focused app has a UI element but does not expose its selected
        // text or selected range through Accessibility.
        return .unavailable
    }

    func replace(_ selection: AXSelection, with text: String) -> Bool {
        let error = AXUIElementSetAttributeValue(
            selection.element,
            kAXSelectedTextAttribute as CFString,
            text as CFString
        )
        guard error == .success else { return false }

        if let oldRange = selection.range {
            var newRange = CFRange(
                location: oldRange.location,
                length: text.utf16.count
            )
            if let rangeValue = AXValueCreate(.cfRange, &newRange) {
                _ = AXUIElementSetAttributeValue(
                    selection.element,
                    kAXSelectedTextRangeAttribute as CFString,
                    rangeValue
                )
            }
        }
        return true
    }
}

private final class ClipboardFallback {
    private let pasteboard = NSPasteboard.general

    func replaceSelection(from layout: Layout) -> (Bool, String) {
        let originalText = pasteboard.string(forType: .string) ?? ""
        let originalChangeCount = pasteboard.changeCount
        postKey(8, flags: .maskCommand) // C

        var selected: String?
        let deadline = Date().addingTimeInterval(0.5)
        repeat {
            if pasteboard.changeCount != originalChangeCount,
               let candidate = pasteboard.string(forType: .string),
               !candidate.isEmpty {
                selected = candidate
                break
            }
            Thread.sleep(forTimeInterval: 0.02)
        } while Date() < deadline

        guard let selected,
              LayoutConverter.canConvert(selected, from: layout) else {
            pasteboard.clearContents()
            pasteboard.setString(originalText, forType: .string)
            return (false, "Выделенный текст не найден")
        }

        let converted = LayoutConverter.convert(selected, from: layout)
        pasteboard.clearContents()
        pasteboard.setString(converted, forType: .string)
        postKey(9, flags: .maskCommand) // V
        Thread.sleep(forTimeInterval: 0.12)

        // Keep the newly inserted text selected. This makes the next layout
        // switch reversible even in editors that collapse selection on paste.
        for _ in 0..<converted.utf16.count {
            postKey(123, flags: .maskShift) // Left arrow
        }

        pasteboard.clearContents()
        pasteboard.setString(originalText, forType: .string)
        return (true, "Исправлено символов: \(selected.count)")
    }

    private func postKey(_ keyCode: CGKeyCode, flags: CGEventFlags) {
        guard let source = CGEventSource(stateID: .hidSystemState) else { return }
        let down = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true)
        let up = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false)
        down?.flags = flags
        up?.flags = flags
        down?.post(tap: .cghidEventTap)
        up?.post(tap: .cghidEventTap)
    }
}

private final class ConversionController: NSObject {
    private let accessibility = AccessibilityBridge()
    private let clipboard = ClipboardFallback()
    private var timer: Timer?
    private var previousLayout: Layout?
    private var conversionGeneration = 0
    private(set) var enabled = true
    var onStatus: ((String) -> Void)?

    func start() {
        previousLayout = InputSourceReader.currentLayout()
        timer = Timer.scheduledTimer(
            timeInterval: 0.12,
            target: self,
            selector: #selector(checkLayout),
            userInfo: nil,
            repeats: true
        )
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        conversionGeneration &+= 1
    }

    func setEnabled(_ value: Bool) {
        enabled = value
        onStatus?(value ? "Автоматическое исправление включено" : "Автоматическое исправление выключено")
    }

    @objc private func checkLayout() {
        guard let current = InputSourceReader.currentLayout() else { return }
        guard let previous = previousLayout else {
            previousLayout = current
            return
        }
        guard current != previous else { return }
        previousLayout = current
        guard enabled else { return }

        conversionGeneration &+= 1
        tryReplaceSelection(from: previous, generation: conversionGeneration, attempt: 0)
    }

    private func tryReplaceSelection(from layout: Layout, generation: Int, attempt: Int) {
        guard generation == conversionGeneration, enabled else { return }

        let result = replaceSelection(from: layout)
        if result.0 {
            onStatus?("Готово — \(result.1)")
            return
        }

        // Some editors expose the selected text a moment after the input source
        // changes. Retry briefly instead of losing the conversion permanently.
        if attempt < 6 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.06) { [weak self] in
                self?.tryReplaceSelection(from: layout, generation: generation, attempt: attempt + 1)
            }
        } else if result.1 != "Выделенный текст не найден" {
            onStatus?(result.1)
        }
    }

    private func replaceSelection(from layout: Layout) -> (Bool, String) {
        switch accessibility.selectionState() {
        case .noSelection:
            return (false, "Выделенный текст не найден")
        case .text(let selection):
            guard LayoutConverter.canConvert(selection.text, from: layout) else {
                return (false, "Выделенный текст не найден")
            }
            let converted = LayoutConverter.convert(selection.text, from: layout)
            if accessibility.replace(selection, with: converted) {
                return (true, "Исправлено символов: \(selection.text.count)")
            }
            return clipboard.replaceSelection(from: layout)
        case .confirmedSelection:
            return clipboard.replaceSelection(from: layout)
        case .unavailable:
            // An unknown selection is not proof that text is selected. Do
            // not synthesize Cmd+C/V here: apps commonly play the alert sound
            // when those keys are sent without an active selection.
            return (false, "Выделенный текст не найден")
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var statusDoubleClickRecognizer: NSClickGestureRecognizer?
    private var settingsWindow: NSWindow?
    private weak var accessibilityStatusLabel: NSTextField?
    private let controller = ConversionController()
    private let statusText = NSTextField(labelWithString: "Запускаю…")
    private let enabledItem = NSMenuItem()
    private var enabledSwitch: NSSwitch?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildMenuBar()
        controller.onStatus = { [weak self] status in
            self?.statusText.stringValue = status
        }
        controller.start()
        updateStatus()
        // The window is intentionally shown on every launch: it contains the
        // permission reminder, while closing it leaves the menu-bar service running.
        DispatchQueue.main.async { [weak self] in
            self?.showSettings(welcome: true)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        controller.stop()
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        // Accessibility permission can be changed in System Settings while
        // this window stays open. Refresh the indicator when the user returns.
        updateAccessibilityStatus()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings(welcome: false)
        return true
    }

    private func buildMenuBar() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.title = "SL"
            let doubleClick = NSClickGestureRecognizer(
                target: self,
                action: #selector(statusItemDoubleClicked)
            )
            doubleClick.numberOfClicksRequired = 2
            button.addGestureRecognizer(doubleClick)
            statusDoubleClickRecognizer = doubleClick
        }

        let menu = NSMenu()
        let toggleRow = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 30))
        let toggleLabel = NSTextField(labelWithString: "Автоисправление")
        toggleLabel.translatesAutoresizingMaskIntoConstraints = false
        toggleRow.addSubview(toggleLabel)

        let toggle = NSSwitch()
        toggle.controlSize = .regular
        toggle.state = .on
        toggle.isEnabled = true
        toggle.translatesAutoresizingMaskIntoConstraints = false
        toggle.target = self
        toggle.action = #selector(toggleEnabled(_:))
        toggleRow.addSubview(toggle)
        NSLayoutConstraint.activate([
            toggleLabel.leadingAnchor.constraint(equalTo: toggleRow.leadingAnchor, constant: 12),
            toggleLabel.centerYAnchor.constraint(equalTo: toggleRow.centerYAnchor),
            toggle.trailingAnchor.constraint(equalTo: toggleRow.trailingAnchor, constant: -12),
            toggle.centerYAnchor.constraint(equalTo: toggleRow.centerYAnchor),
            toggle.widthAnchor.constraint(equalToConstant: 46),
            toggle.heightAnchor.constraint(equalToConstant: 26)
        ])
        enabledSwitch = toggle

        enabledItem.view = toggleRow
        menu.addItem(enabledItem)
        menu.addItem(.separator())

        let settings = NSMenuItem(title: "Настройки…", action: #selector(openSettings), keyEquivalent: "")
        settings.target = self
        menu.addItem(settings)

        let login = NSMenuItem(title: "Добавить в автозапуск", action: #selector(registerLoginItem), keyEquivalent: "")
        login.target = self
        menu.addItem(login)
        menu.addItem(.separator())

        let quit = NSMenuItem(title: "Выйти", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
        statusItem.menu = menu
    }

    private func updateStatus() {
        let layout = InputSourceReader.currentLayout()?.title ?? "—"
        statusText.stringValue = "Текущая раскладка: \(layout)"
    }

    @objc private func toggleEnabled(_ sender: NSSwitch) {
        controller.setEnabled(sender.state == .on)
    }

    @objc private func openSettings() {
        showSettings(welcome: false)
    }

    @objc private func statusItemDoubleClicked() {
        showSettings(welcome: false)
    }

    private func showSettings(welcome: Bool) {
        if settingsWindow == nil {
            let content = NSView(frame: NSRect(x: 0, y: 0, width: 500, height: 380))
            let title = NSTextField(labelWithString: welcome ? "Добро пожаловать в SwitchLang" : "SwitchLang")
            title.font = .boldSystemFont(ofSize: 24)
            title.frame = NSRect(x: 28, y: 320, width: 420, height: 32)
            content.addSubview(title)

            let description = NSTextField(wrappingLabelWithString:
                "Выделите текст и нажмите обычное системное сочетание переключения раскладки. SwitchLang заметит RU ↔ EN и исправит выделение. После вставки текст остаётся выделенным, поэтому переключение можно повторить обратно.")
            description.frame = NSRect(x: 28, y: 220, width: 440, height: 76)
            content.addSubview(description)

            let permissionText = NSTextField(wrappingLabelWithString:
                "Для полноценной работы добавьте SwitchLang в Системные настройки → Конфиденциальность и безопасность → Универсальный доступ.")
            permissionText.frame = NSRect(x: 28, y: 158, width: 440, height: 48)
            content.addSubview(permissionText)

            let permission = NSButton(title: "Открыть настройки доступа", target: self, action: #selector(openAccessibilitySettings))
            permission.bezelStyle = .rounded
            permission.frame = NSRect(x: 28, y: 116, width: 240, height: 32)
            content.addSubview(permission)

            let accessibilityStatus = NSTextField(labelWithString: "")
            accessibilityStatus.font = .systemFont(ofSize: 12)
            accessibilityStatus.frame = NSRect(x: 28, y: 88, width: 440, height: 20)
            content.addSubview(accessibilityStatus)
            accessibilityStatusLabel = accessibilityStatus

            let note = NSTextField(wrappingLabelWithString:
                "Вы можете закрыть это окно — приложение продолжит работать в фоновом режиме.")
            note.frame = NSRect(x: 28, y: 50, width: 440, height: 32)
            content.addSubview(note)

            let credit = NSTextField(labelWithString: "Developed by iglakovmaks")
            credit.font = .systemFont(ofSize: 11)
            credit.textColor = .secondaryLabelColor
            credit.frame = NSRect(x: 28, y: 24, width: 260, height: 18)
            content.addSubview(credit)

            let close = NSButton(title: "Понятно", target: self, action: #selector(closeSettings))
            close.bezelStyle = .rounded
            close.frame = NSRect(x: 370, y: 18, width: 98, height: 30)
            content.addSubview(close)

            settingsWindow = NSWindow(
                contentRect: content.frame,
                styleMask: [.titled, .closable],
                backing: .buffered,
                defer: false
            )
            settingsWindow?.title = "SwitchLang"
            settingsWindow?.contentView = content
            settingsWindow?.isReleasedWhenClosed = false
            settingsWindow?.center()
        }
        updateAccessibilityStatus()
        settingsWindow?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func updateAccessibilityStatus() {
        let trusted = AccessibilityBridge().isTrusted
        accessibilityStatusLabel?.stringValue = trusted
            ? "Статус доступа: разрешён"
            : "Статус доступа: не выдан — добавьте SwitchLang в Универсальный доступ"
        accessibilityStatusLabel?.textColor = trusted ? .systemGreen : .systemRed
    }

    private func loadIcon() -> NSImage? {
        let developmentDirectory = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
        let candidates = [
            Bundle.main.url(forResource: "icon", withExtension: "png"),
            developmentDirectory.appendingPathComponent("icon.png"),
            developmentDirectory.appendingPathComponent("..", isDirectory: true).appendingPathComponent("icon.png")
        ]
        for candidate in candidates.compactMap({ $0 }) where FileManager.default.fileExists(atPath: candidate.path) {
            if let image = NSImage(contentsOf: candidate) {
                return image
            }
        }
        return nil
    }

    @objc private func closeSettings() {
        settingsWindow?.close()
    }

    @objc private func openAccessibilitySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func registerLoginItem() {
        do {
            if #available(macOS 13.0, *) {
                try SMAppService.mainApp.register()
                statusText.stringValue = "Автозапуск включён"
            }
        } catch {
            statusText.stringValue = "Не удалось включить автозапуск: \(error.localizedDescription)"
        }
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}

let application = NSApplication.shared
let delegate = AppDelegate()
application.delegate = delegate
application.run()
