# STYLY Remote Editor Sync

[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Sync Unity Editor Hierarchy/Inspector changes to client devices in real-time via STYLY NetSync RPC.

**Perfect for XR development and remote debugging** - edit in the Unity Editor and see changes instantly on your headset or client devices! 🚀

![Demo](https://via.placeholder.com/800x400?text=Demo+GIF+Here)

## ✨ Features

- 🎯 **Real-time Synchronization**: GameObject creation, deletion, renaming, activation, and Transform changes
- 🎮 **Primitive Support**: Automatically detects and syncs Sphere, Cube, Capsule, Cylinder, Plane, Quad
- 🔧 **Editor-Only Detection**: Only manual editor changes are synced, not runtime script-generated objects
- 🏷️ **Tag Filtering**: Optionally sync only specific GameObjects by tag
- 📡 **RPC-based**: Built on STYLY NetSync for reliable network communication

## 🎥 Demo Video

[Watch Demo Video](https://youtu.be/your-demo-video)

## 📦 Installation

### Via Unity Package Manager (Git URL)

1. Open Unity Package Manager (`Window` > `Package Manager`)
2. Click `+` button → `Add package from git URL`
3. Enter the following URL:
   ```
   https://github.com/YOUR_USERNAME/runtime-hierarchy.git?path=/Packages/com.styly.remote-editor-sync
   ```

### Via OpenUPM (Coming Soon)

```bash
openupm add com.styly.remote-editor-sync
```

### Manual Installation

1. Clone or download this repository
2. Copy `Packages/com.styly.remote-editor-sync` folder to your project's `Packages` directory

## 🚀 Quick Start

### 1. Setup Scene

**Option A: Automatic Setup (Recommended)**
```
Tools > Remote Editor Sync > Setup Scene
```

**Option B: Manual Setup**
1. Ensure `NetSyncManager` is in your scene
2. Add `RemoteEditorSyncReceiver` component to any GameObject

### 2. Configure NetSync

- Set the same **Room ID** on both editor and client
- Leave **Server Address** empty for auto-discovery

### 3. Test

1. Enter **Play Mode** in Unity Editor
2. Launch your app on a **client device** (connected to same Room ID)
3. Edit in the Editor:
   - Create GameObject (e.g., 3D Object → Sphere)
   - Move, rotate, scale objects
   - Toggle active/inactive
   - Rename or delete objects

→ **Changes appear on client in real-time!** ✨

## 📖 Documentation

For detailed documentation, see:
- [Package Documentation](Packages/com.styly.remote-editor-sync/README.md)
- [Changelog](Packages/com.styly.remote-editor-sync/CHANGELOG.md)

## 🎯 Use Cases

### XR Development
- Edit scene while wearing headset
- Instant iteration without rebuild
- Remote debugging on device

### Multiplayer Testing
- Test multiplayer interactions
- Debug client-specific issues
- Visual debugging in real-time

### Remote Collaboration
- Show changes to remote team members
- Live demonstrations
- Remote troubleshooting

## 🛠️ Requirements

- Unity 6000.0 or later
- [STYLY NetSync](https://openupm.com/packages/com.styly.styly-netsync/) v0.6.1+
- Newtonsoft.Json v3.2.1+

## 📁 Project Structure

```
runtime-hierarchy/
├── Packages/
│   └── com.styly.remote-editor-sync/     # Main package
│       ├── Editor/                       # Editor scripts
│       ├── Runtime/                      # Runtime scripts
│       ├── README.md                     # Package documentation
│       └── package.json                  # Package manifest
├── Assets/
│   ├── Scenes/                           # Sample scenes
│   └── ...                               # Other project assets
└── README.md                             # This file
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built on [STYLY NetSync](https://styly.inc/)
- Inspired by Unity's Remote Config and Device Simulator

## 📧 Support

- 📝 [GitHub Issues](https://github.com/YOUR_USERNAME/runtime-hierarchy/issues)
- 💬 [Discussions](https://github.com/YOUR_USERNAME/runtime-hierarchy/discussions)

## 🌟 Star History

If you find this project useful, please consider giving it a star! ⭐

---

**Made with ❤️ for the Unity XR Development Community**
