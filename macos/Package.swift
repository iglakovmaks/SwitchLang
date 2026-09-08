// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SwitchLangMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "SwitchLang", targets: ["SwitchLang"])
    ],
    targets: [
        .executableTarget(
            name: "SwitchLang",
            path: "Sources/SwitchLang"
        )
    ]
)
