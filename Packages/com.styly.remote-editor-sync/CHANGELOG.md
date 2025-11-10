# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
