# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.5] - 2025-11-16

### Added
- **Material Property Synchronization**: Major new feature for syncing material shader properties
  - `MaterialAnchor.cs`: Component that provides stable identity for renderer-bound materials across hierarchy changes
  - `MaterialSignature.cs`: Unique identification system for materials using asset GUID or anchor-based runtime IDs
  - `MaterialAnchorRegistry.cs`: Runtime registry for managing material instances and their mappings
  - `MaterialAnchorRuntimeBootstrap.cs`: Automatic initialization of material registry at runtime
  - `MaterialTracker.cs`: Editor-side tracking system for monitoring material property changes
  - `MaterialSnapshot.cs`: Snapshot-based change detection for material properties
  - Automatic detection and synchronization of material property changes in Inspector
  - Supported shader property types:
    - Color properties
    - Float and Range properties
    - Vector properties
  - Material registration with unique anchors for reliable sync across hierarchy changes
  - Bidirectional confirmation system (RegisterMaterial/RegisterMaterialResult RPCs)
  - Automatic cleanup of deleted materials
  - Support for both asset-based materials (via GUID) and runtime materials (via anchor ID)

### Changed
- Enhanced RPC system with material-specific handlers:
  - `RegisterMaterial`: Notifies clients about new materials to track
  - `UnregisterMaterial`: Cleanup for removed materials
  - `UpdateMaterialProperties`: Syncs material property changes
  - `RegisterMaterialResult`: Confirmation from client to editor
- Automatic material registration for newly created GameObjects
- Material tracking integrated with existing GameObject change detection

### Documentation
- Updated README with Material Synchronization section
- Added documentation for MaterialAnchor system
- Updated feature list with material sync capabilities

## [1.2.4] - 2025-11-15

### Added
- **Component Property Synchronization**: Major new feature for syncing component properties
  - `ComponentSyncHandlers.cs`: Handler system for different component types
  - `ComponentSyncTypes.cs`: Type definitions for component signatures and snapshots
  - Automatic detection and synchronization of component property changes
  - Support for multiple component types:
    - Behaviour components (enabled property)
    - Renderer components (enabled property)
    - Collider components (enabled property)
    - Generic reflection-based handler for other components
  - Supported value types:
    - Primitives (int, float, bool, etc.) and strings
    - Enums
    - Unity types (Vector2/3/4, Quaternion, Color, Rect, Bounds, Matrix4x4, LayerMask)
    - Nullable types
  - Change detection with property snapshots
  - Component identification by type and index
  - Test utilities for component update simulation

### Documentation
- Updated README with component synchronization capabilities
- Added documentation for supported component types
- Updated limitations section to reflect new component sync support

## [1.2.3] - 2025-11-11

### Changed
- Version bump for release

## [1.2.2] - 2025-11-11

### Removed
- Removed "Create Test Object" menu command and functionality
- Cleaned up unnecessary test utilities

### Changed
- Streamlined menu structure for better usability

## [1.2.1] - 2025-11-11

### Added
- **Auto Sync On/Off Toggle**: Performance optimization feature
  - `Enable Auto Sync` menu command with checkmark indicator
  - EditorPrefs persistence for user preference
  - Reduces Editor overhead when synchronization is not needed
  - Disables both real-time sync and Play Mode Changes recording when off

### Documentation
- Added "Performance: Auto Sync On/Off" section to README
- Updated menu commands table
- Added usage guidelines and benefits

## [1.2.0] - 2025-11-11

### Added
- **Play Mode Changes Preservation**: Major new feature that saves Play mode changes
  - `PlayModeChangeLog.cs`: System for recording all changes during Play mode
  - `PlayModeChangesWindow.cs`: EditorWindow for reviewing and applying changes
  - Automatic window display when exiting Play mode with changes
  - Selective application of changes via checkboxes
  - Full Undo/Redo support for applied changes
  - Change type icons (➕Create, ➖Delete, ✏️Rename, 👁Active, 📐Transform)
  - "Select All" and "Deselect All" buttons
  - Confirmation dialog before applying changes
- Multi-scene support
  - All RPC operations now include scene name
  - Scene-specific GameObject lookups
  - Cache uses "sceneName:path" format
  - Works correctly with multiple loaded scenes
- Menu command: `Tools` > `Remote Editor Sync` > `Show Play Mode Changes`

### Changed
- Updated all data structures to include `SceneName` field
- Enhanced `FindGameObjectByPath` to search within specific scenes
- Improved cache management for multi-scene scenarios
- Transform changes are now consolidated (only latest value kept per object)

### Documentation
- Added comprehensive "Play Mode Changes Preservation" section to README
- Updated feature list with new capabilities
- Added usage examples and tips
- Updated file structure documentation
- Enhanced version history

## [1.1.0] - 2025-11-10

### Added
- Primitive type detection (Sphere, Cube, Capsule, Cylinder, Plane, Quad)
- GameObject serialization using EditorJsonUtility
- Automatic primitive creation on client using GameObject.CreatePrimitive()
- Self-RPC filtering to prevent duplicate objects in editor
- Tag filtering support via menu commands
- Comprehensive debug logging

### Fixed
- JsonSerializationException caused by Vector3 circular references
- Self-sending RPC issue causing duplicate invisible GameObjects
- MeshRenderer/MeshFilter not being synced for primitives

### Changed
- Replaced EditorApplication.hierarchyChanged with ObjectChangeEvents
- Now only detects manual editor operations, excluding runtime script-generated objects
- Improved RPC message structure to include primitive type and serialized data

## [1.0.0] - 2025-11-10

### Added
- Initial release
- GameObject creation/deletion synchronization
- GameObject rename synchronization
- GameObject active state synchronization
- Transform synchronization (Position, Rotation, Scale)
- Integration with STYLY NetSync RPC
- Editor-only change detection
- Setup menu commands
- Basic documentation

### Dependencies
- STYLY NetSync v0.6.1+
- Newtonsoft.Json v3.2.1+
- Unity 6000.0+
