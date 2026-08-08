# STYLY Remote Editor Sync

[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Sync Unity Editor Hierarchy/Inspector changes to client devices in real-time via STYLY NetSync RPC.

**Perfect for XR development and remote debugging** - edit in the Unity Editor and see changes instantly on your headset or client devices! 🚀

![Demo](https://via.placeholder.com/800x400?text=Demo+GIF+Here)

## ✨ Features

- 🎯 **Real-time Synchronization**: GameObject creation, deletion, renaming, reparenting, activation, and Transform changes
- 🧩 **Component Property Sync**: Automatically sync component properties *and serialized fields* — including `public` fields and `[SerializeField]` private fields on your own MonoBehaviours
- 🌳 **Full Hierarchy Creation**: Objects created with children and components (UI elements, Lights, etc.) are reproduced in full on the client
- 🎨 **Material Property Sync**: Sync material shader properties (Color, Float, Vector) in real-time
- 🎮 **Primitive Support**: Automatically detects and syncs Sphere, Cube, Capsule, Cylinder, Plane, Quad
- 🔧 **Editor-Only Detection**: Only manual editor changes are synced, not runtime script-generated objects
- 🏷️ **Tag Filtering**: Optionally sync only specific GameObjects by tag
- 📡 **RPC-based**: Built on STYLY NetSync for reliable network communication
- 💾 **Play Mode Changes Preservation**: Save Play mode changes and selectively apply them to Edit mode after stopping
- 🌍 **Multi-Scene Support**: Properly handles GameObjects across multiple loaded scenes

## 🎥 Demo Video

[Watch Demo Video](https://youtu.be/your-demo-video)

## 📦 Installation

### Via Unity Package Manager (Git URL)

1. Open Unity Package Manager (`Window` > `Package Manager`)
2. Click `+` button → `Add package from git URL`
3. Enter the following URL:
   ```
   https://github.com/afjk/remote-editor-sync.git?path=/Packages/com.styly.remote-editor-sync#main
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
   - **Drag objects to a different parent** in the Hierarchy
   - **Modify component properties** (e.g., Light intensity, Collider size)
   - **Edit your own script's fields** (e.g., a `public float speed` on a MonoBehaviour)
   - **Change material properties** (e.g., Albedo color, Metallic, Smoothness)

→ **Changes appear on client in real-time!** ✨

## 💾 Play Mode Changes Preservation (NEW!)

Never lose your Play mode tweaks again! This feature automatically saves all changes you make during Play mode and lets you selectively apply them to Edit mode.

### How to Use

1. **Enter Play Mode** and make any changes (create, move, rotate, rename objects, etc.)
2. **Exit Play Mode** - a window automatically appears with all your changes
3. **Select which changes to keep** using checkboxes
4. **Click "Apply"** to save selected changes to your Edit mode scene

### Features
- ✅ Individual selection with checkboxes
- 📋 Clear icons for each change type (➕Create, ➖Delete, ✏️Rename, 👁Active, 📐Transform)
- ↩️ Full Undo/Redo support
- 🌍 Multi-scene aware

**Manual Access**: `Tools` > `Remote Editor Sync` > `Show Play Mode Changes`

## ⚙️ Performance Settings

If Editor performance is a concern, you can disable automatic synchronization:

**Toggle Auto Sync**: `Tools` > `Remote Editor Sync` > `Enable Auto Sync`

- ✅ Checkmark indicates current state
- 💾 Setting persists across Unity sessions
- 🚀 Reduces overhead when not needed

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
remote-editor-sync/
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

- 📝 [GitHub Issues](https://github.com/afjk/remote-editor-sync/issues)
- 💬 [Discussions](https://github.com/afjk/remote-editor-sync/discussions)

## 🌟 Star History

If you find this project useful, please consider giving it a star! ⭐

---

**Made with ❤️ for the Unity XR Development Community**
