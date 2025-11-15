# STYLY Remote Editor Sync

Sync Unity Editor Hierarchy/Inspector changes to client devices in real-time via STYLY NetSync RPC.

Perfect for XR development and remote debugging - edit in the Unity Editor and see changes instantly on your headset or client devices!

## Features

- 🎯 **Real-time Synchronization**: GameObject creation, deletion, renaming, activation, and Transform changes
- 🧩 **Component Property Sync**: Automatically sync component properties (Behaviour, Renderer, Collider, and more)
- 🎮 **Primitive Support**: Automatically detects and syncs Sphere, Cube, Capsule, Cylinder, Plane, Quad
- 🔧 **Editor-Only Detection**: Only manual editor changes are synced, not runtime script-generated objects
- 🏷️ **Tag Filtering**: Optionally sync only specific GameObjects by tag
- 📡 **RPC-based**: Built on STYLY NetSync for reliable network communication
- 💾 **Play Mode Changes Preservation**: Save Play mode changes and selectively apply them to Edit mode after stopping
- 🌍 **Multi-Scene Support**: Properly handles GameObjects across multiple loaded scenes

## Supported Operations

| Operation | Description |
|-----------|-------------|
| **Create GameObject** | Detects primitive types and creates them on clients |
| **Delete GameObject** | Removes GameObjects from clients |
| **Rename GameObject** | Updates GameObject names |
| **SetActive** | Toggles GameObject active state |
| **Transform** | Position, Rotation, Scale changes |
| **Component Properties** | Syncs component property changes (Behaviour, Renderer, Collider, etc.) |

## Requirements

- Unity 6000.0 or later
- STYLY NetSync v0.6.1 or later
- Newtonsoft.Json v3.2.1 or later

## Installation

### Via Package Manager (Recommended)

1. Open Unity Package Manager (`Window` > `Package Manager`)
2. Click `+` button → `Add package from git URL`
3. Enter: `https://github.com/afjk/remote-editor-sync.git?path=/Packages/com.styly.remote-editor-sync#main`

### Manual Installation

1. Copy `Packages/com.styly.remote-editor-sync` folder to your project's `Packages` directory
2. Unity will automatically detect and import the package

## Quick Start

### 1. Setup Scene

**Option A: Automatic Setup (Recommended)**
- Go to `Tools` > `Remote Editor Sync` > `Setup Scene`
- This automatically adds `RemoteEditorSyncReceiver` to your scene

**Option B: Manual Setup**
1. Ensure `NetSyncManager` is in your scene
2. Create an empty GameObject
3. Add `RemoteEditorSyncReceiver` component to it

### 2. Configure NetSync

Make sure `NetSyncManager` is properly configured:
- **Room ID**: Set the same Room ID on both editor and client
- **Server Address**: Leave empty for auto-discovery or specify IP address

### 3. Test

1. Enter Play Mode in Unity Editor
2. Launch your app on a client device (connected to the same Room ID)
3. Try these in the Editor while in Play Mode:
   - Create a new GameObject (e.g., 3D Object → Sphere)
   - Move, rotate, or scale objects
   - Rename objects
   - Toggle active/inactive
   - Delete objects

→ Changes will appear on the client device in real-time! ✨

## Component Property Synchronization

In addition to GameObject operations, the system automatically synchronizes component property changes made in the Inspector.

### Supported Components

The following component types are automatically synchronized:

- **Behaviour Components**: Includes MonoBehaviour and other script components
  - `enabled` property
  - Public properties with supported value types
- **Renderer Components**: MeshRenderer, SkinnedMeshRenderer, etc.
  - `enabled` property
  - Other supported properties
- **Collider Components**: BoxCollider, SphereCollider, MeshCollider, etc.
  - `enabled` property
  - Collider-specific properties (size, radius, etc.)
- **Other Components**: Any component with public properties of supported types

### Supported Value Types

- **Primitives**: int, float, bool, double, etc.
- **Strings**: string
- **Enums**: Any enum type
- **Unity Types**: Vector2/3/4, Quaternion, Color, Color32, Rect, Bounds, Matrix4x4, LayerMask
- **Nullable Types**: Nullable versions of above types

### How It Works

1. Edit a component property in the Inspector during Play Mode
2. The system detects the change using `Undo.postprocessModifications`
3. Property values are extracted and serialized
4. Changes are sent to clients via RPC
5. Clients apply the property changes to their local GameObjects

### Example

```
1. Select a GameObject with a Light component
2. Change Light intensity from 1.0 to 2.0 in Inspector
3. → All clients see the brighter light instantly!
```

## Important: Runtime vs Editor Changes

By default, **only manual editor operations are synced**, not runtime script-generated objects.

This prevents unintended synchronization of game logic objects (enemies, effects, etc.) that should remain local.

### How It Works

The system uses:
- `ObjectChangeEvents` - Detects editor-only operations
- `Undo.postprocessModifications` - Captures manual property changes

### Filtering Specific GameObjects

To sync only specific GameObjects:

1. Go to `Tools` > `Remote Editor Sync` > `Settings` > `Set Tag Filter (EditorSyncOnly)`
2. Add the `EditorSyncOnly` tag to GameObjects you want to sync
3. Only those GameObjects will be synchronized

To clear the filter:
- `Tools` > `Remote Editor Sync` > `Settings` > `Clear Tag Filter`

## Performance: Auto Sync On/Off

If you find the continuous monitoring affecting Editor performance, you can disable automatic synchronization.

### Toggle Auto Sync

- Go to `Tools` > `Remote Editor Sync` > `Enable Auto Sync`
- Click to toggle On/Off (checkmark shows current state)
- Setting is saved and persists across Unity sessions

### When to Disable

Consider disabling Auto Sync when:
- **Not actively using remote sync**: Save Editor resources when you don't need the feature
- **Heavy scene editing**: Reduce overhead during intensive editing sessions
- **Performance testing**: Isolate performance issues
- **Large projects**: Minimize monitoring impact in complex hierarchies

### Benefits

- 🚀 **Improved Performance**: No monitoring overhead when disabled
- ⚙️ **Flexible Control**: Enable only when needed
- 💾 **Persistent Setting**: Choice is saved in EditorPrefs
- ✅ **Visual Feedback**: Checkmark shows current state

**Note**: When disabled, Play Mode Changes Preservation is also disabled.

## Menu Commands

| Menu Path | Description |
|-----------|-------------|
| `Tools` > `Remote Editor Sync` > `Enable Auto Sync` | Toggle automatic synchronization On/Off (with checkmark) |
| `Tools` > `Remote Editor Sync` > `Setup Scene` | Auto-setup RemoteEditorSyncReceiver |
| `Tools` > `Remote Editor Sync` > `Show Play Mode Changes` | Show window to apply Play mode changes to Edit mode |
| `Tools` > `Remote Editor Sync` > `Settings` > `Set Tag Filter` | Enable tag-based filtering |
| `Tools` > `Remote Editor Sync` > `Settings` > `Clear Tag Filter` | Disable tag filtering |
| `Tools` > `Remote Editor Sync` > `About` | Show information dialog |
| `Tools` > `Remote Editor Sync` > `Open README` | Open this README |

## Play Mode Changes Preservation

One of the most frustrating aspects of Unity development is losing all changes made during Play mode when you stop. This package solves that problem!

### How It Works

1. **Automatic Recording**: All changes you make during Play mode are automatically recorded
2. **Stop & Review**: When you exit Play mode, a window automatically appears showing all changes
3. **Selective Application**: Choose which changes to apply to your Edit mode scene with checkboxes
4. **Safe & Undoable**: Changes are applied with full Undo support

### Usage

1. Enter Play Mode
2. Make any changes you want:
   - Create GameObjects (primitives, empty objects)
   - Move, rotate, scale objects
   - Rename objects
   - Toggle active/inactive
   - Delete objects
3. Exit Play Mode
4. The "Play Mode Changes" window appears automatically
5. Review the list of changes (with icons: ➕Create, ➖Delete, ✏️Rename, 👁Active, 📐Transform)
6. Check/uncheck changes you want to apply
7. Click "選択した変更を適用" (Apply Selected Changes)
8. Changes are applied to your Edit mode scene!

### Features

- ✅ **Individual Selection**: Check only the changes you want to keep
- 🔘 **Bulk Actions**: "Select All" and "Deselect All" buttons
- 📋 **Clear Icons**: Visual indicators for each change type
- ↩️ **Undo Support**: All applied changes can be undone (Ctrl+Z)
- 🌍 **Multi-Scene**: Works correctly with multiple loaded scenes
- 📝 **Change History**: Transform changes are consolidated (only latest value kept)

### Manual Access

If you dismiss the window, you can reopen it:
- `Tools` > `Remote Editor Sync` > `Show Play Mode Changes`

### Tips

- **Transform Optimization**: Multiple transform changes on the same object are automatically merged into one
- **Safe Workflow**: The confirmation dialog prevents accidental application
- **Scene Changes**: All changes are properly scoped to their original scenes

## Limitations

1. **GameObject Identification**
   - GameObjects are identified by hierarchy path
   - Multiple GameObjects with the same name at the same level may cause issues

2. **Component Synchronization**
   - Supported components: Behaviour, Renderer, Collider, and other components with supported value types
   - Supported value types: Primitives, strings, enums, Unity types (Vector2/3/4, Quaternion, Color, Rect, Bounds, Matrix4x4, LayerMask)
   - Complex types (arrays, lists, custom classes) are not fully supported
   - Material/Texture references use default assets

3. **Performance**
   - STYLY NetSync RPC rate limit applies (default: 30 RPC/sec)
   - Rapid bulk changes may be throttled

4. **Network Connection**
   - Editor and client must be connected to the same Room ID
   - NetSyncManager.IsReady must be true

## Troubleshooting

### Changes Not Syncing

**Check:**
- ✅ NetSyncManager is connected (IsReady = true)
- ✅ Same Room ID on editor and client
- ✅ Console shows no error messages
- ✅ `[RemoteEditorSync] Enabled (Editor changes only)` appears in Console

### Only Some Changes Sync

**Possible Cause:** RPC rate limit reached

**Solution:** Adjust the rate limit:
```csharp
NetSyncManager.Instance.ConfigureRpcLimit(60); // Increase to 60 RPC/sec
```

### GameObject Not Found Errors

**Possible Cause:** Path mismatch (duplicate names)

**Solution:**
- Ensure unique GameObject names in the hierarchy
- Or use tag filtering to sync only specific objects

### Self-Sending Issue

The receiver automatically ignores RPCs from the same client (sender) to prevent duplicate objects in the editor.

## Architecture

```
┌─────────────────┐                    ┌──────────────────┐
│  Unity Editor   │                    │  Client Device   │
│                 │                    │                  │
│  [Change        │                    │  [RPC Receiver]  │
│   Detection]    │                    │        ↓         │
│       ↓         │                    │  [Apply Changes] │
│  [RPC Sender]   │  ──── RPC ───→    │        ↓         │
│                 │   STYLY NetSync    │  [GameObject     │
│  RemoteEditor   │                    │   Updated]       │
│  Sync.cs        │                    │                  │
│  (Editor Only)  │                    │  RemoteEditor    │
│                 │                    │  SyncReceiver.cs │
└─────────────────┘                    └──────────────────┘
```

## File Structure

```
Packages/com.styly.remote-editor-sync/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE
├── Editor/
│   ├── RemoteEditorSync.cs              # Change detection & RPC sending
│   ├── RemoteEditorSyncSetup.cs         # Setup utilities & menu commands
│   ├── PlayModeChangeLog.cs             # Play mode change recording system
│   ├── PlayModeChangesWindow.cs         # EditorWindow for applying changes
│   └── RemoteEditorSync.Editor.asmdef   # Assembly definition
└── Runtime/
    ├── RemoteEditorSyncReceiver.cs      # RPC receiving & applying
    └── RemoteEditorSync.Runtime.asmdef  # Assembly definition
```

## Version History

### v1.2.0 (2025-11-11)
- ✨ **NEW**: Play Mode Changes Preservation feature
  - Automatically records all changes made during Play mode
  - Shows EditorWindow with selectable change list after stopping
  - Apply changes selectively to Edit mode with checkboxes
  - Full Undo/Redo support
- ✨ Added multi-scene support
  - All operations now properly handle multiple loaded scenes
  - Scene-specific GameObject lookups
- 🔧 Added menu command: "Show Play Mode Changes"
- 📝 Enhanced documentation with detailed usage examples

### v1.1.0 (2025-11-10)
- ✨ Added primitive type detection and synchronization
- ✨ Implemented GameObject serialization with EditorJsonUtility
- 🐛 Fixed self-RPC reception issue
- 🐛 Fixed JsonSerializationException for Vector3 properties
- 🔧 Automatic exclusion of runtime-generated GameObjects
- 🔧 Tag filtering support

### v1.0.0 (2025-11-10)
- 🎉 Initial release
- ✅ GameObject create/delete/rename/activate
- ✅ Transform synchronization

## License

MIT License - See [LICENSE](LICENSE) file for details

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## Support

For questions, issues, or feature requests:
- 📝 GitHub Issues: [Create an issue](https://github.com/afjk/remote-editor-sync/issues)
- 📧 Email: support@example.com

---

**Made with ❤️ for the Unity XR Development Community**
