// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "GroqVoice",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "GroqVoice",
            path: "Sources/GroqVoice"
        )
    ]
)
