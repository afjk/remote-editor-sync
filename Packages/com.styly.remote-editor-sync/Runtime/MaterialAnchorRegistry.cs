using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RemoteEditorSync
{
    /// <summary>
    /// Runtime-side registry that maps anchor GUIDs to renderers and runtime material identifiers to concrete Material instances.
    /// </summary>
    public class MaterialAnchorRegistry : MonoBehaviour
    {
        private static MaterialAnchorRegistry _instance;
        public static MaterialAnchorRegistry Instance => _instance;

        private readonly Dictionary<Guid, Renderer> _anchorToRenderer = new Dictionary<Guid, Renderer>();
        private readonly Dictionary<string, HashSet<Material>> _runtimeIdToMaterials = new Dictionary<string, HashSet<Material>>();
        private readonly Dictionary<Guid, HashSet<string>> _anchorToRuntimeIds = new Dictionary<Guid, HashSet<string>>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            RegisterAllAnchors();
            Debug.Log("[MaterialAnchorRegistry] Initialized");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public void RegisterAnchor(MaterialAnchor anchor)
        {
            if (anchor == null || anchor.TargetRenderer == null)
            {
                return;
            }

            var anchorId = anchor.AnchorId;
            if (_anchorToRenderer.TryGetValue(anchorId, out var existingRenderer))
            {
                if (existingRenderer == anchor.TargetRenderer)
                {
                    return;
                }

                Debug.LogWarning($"[MaterialAnchorRegistry] Anchor GUID conflict detected: {anchorId}. Regenerating anchor guid.");
                anchor.RegenerateGuid();
                RegisterAnchor(anchor);
                return;
            }

            _anchorToRenderer[anchorId] = anchor.TargetRenderer;
            Debug.Log($"[MaterialAnchorRegistry] Registered anchor {anchorId} for renderer {anchor.TargetRenderer.name}");
        }

        public void UnregisterAnchor(MaterialAnchor anchor)
        {
            if (anchor == null)
            {
                return;
            }

            var anchorId = anchor.AnchorId;
            _anchorToRenderer.Remove(anchorId);

            if (_anchorToRuntimeIds.TryGetValue(anchorId, out var runtimeIds))
            {
                foreach (var runtimeId in runtimeIds)
                {
                    _runtimeIdToMaterials.Remove(runtimeId);
                }

                _anchorToRuntimeIds.Remove(anchorId);
            }
        }

        public Renderer FindRenderer(string anchorId)
        {
            if (!Guid.TryParse(anchorId, out var guid))
            {
                return null;
            }

            return _anchorToRenderer.TryGetValue(guid, out var renderer) ? renderer : null;
        }

        public IReadOnlyCollection<Material> FindMaterials(MaterialSignature signature)
        {
            if (string.IsNullOrEmpty(signature.RuntimeMaterialId))
            {
                return Array.Empty<Material>();
            }

            if (_runtimeIdToMaterials.TryGetValue(signature.RuntimeMaterialId, out var materials) && materials != null)
            {
                var alive = new List<Material>();
                var removed = false;
                foreach (var material in materials)
                {
                    if (material == null)
                    {
                        removed = true;
                        continue;
                    }
                    alive.Add(material);
                }

                if (removed)
                {
                    materials.RemoveWhere(mat => mat == null);
                    if (materials.Count == 0)
                    {
                        _runtimeIdToMaterials.Remove(signature.RuntimeMaterialId);
                    }
                }

                if (alive.Count > 0)
                {
                    Debug.Log($"[MaterialAnchorRegistry] Found {alive.Count} registered material(s) for {signature.RuntimeMaterialId}");
                    return alive;
                }
            }

            var fallbackMaterial = ResolveMaterialFromRenderer(signature);
            if (fallbackMaterial != null)
            {
                return new[] { fallbackMaterial };
            }

            return Array.Empty<Material>();
        }

        private Material ResolveMaterialFromRenderer(MaterialSignature signature)
        {
            var renderer = FindRenderer(signature.AnchorId);
            if (renderer == null)
            {
                Debug.LogWarning($"[MaterialAnchorRegistry] ResolveMaterialFromRenderer failed: renderer not found for anchor {signature.AnchorId}");
                return null;
            }

            var materials = renderer.sharedMaterials;
            if (signature.MaterialIndex < 0 || signature.MaterialIndex >= materials.Length)
            {
                Debug.LogWarning($"[MaterialAnchorRegistry] ResolveMaterialFromRenderer failed: index {signature.MaterialIndex} out of range ({materials.Length}) for renderer {renderer.name}");
                return null;
            }

            var material = materials[signature.MaterialIndex];
            if (material == null)
            {
                Debug.LogWarning($"[MaterialAnchorRegistry] ResolveMaterialFromRenderer failed: material is null at index {signature.MaterialIndex} on renderer {renderer.name}");
                return null;
            }

            if (!_runtimeIdToMaterials.TryGetValue(signature.RuntimeMaterialId, out var materialSet))
            {
                materialSet = new HashSet<Material>();
                _runtimeIdToMaterials[signature.RuntimeMaterialId] = materialSet;
            }
            materialSet.Add(material);
            Debug.Log($"[MaterialAnchorRegistry] Recovered material {signature.RuntimeMaterialId} from renderer {renderer.name}");
            return material;
        }

        public bool RegisterMaterialDynamic(MaterialSignature signature, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(signature.RuntimeMaterialId))
            {
                error = "RuntimeMaterialId is empty";
                return false;
            }

            if (!Guid.TryParse(signature.AnchorId, out var anchorGuid))
            {
                error = "Invalid AnchorId";
                Debug.LogError($"[MaterialAnchorRegistry] Invalid AnchorId: {signature.AnchorId}");
                return false;
            }

            if (!_anchorToRenderer.TryGetValue(anchorGuid, out var renderer) || renderer == null)
            {
                error = "Renderer not found";
                Debug.LogError($"[MaterialAnchorRegistry] Renderer not found for anchor {anchorGuid}");
                return false;
            }

            var materials = renderer.sharedMaterials;
            if (signature.MaterialIndex < 0 || signature.MaterialIndex >= materials.Length)
            {
                error = $"Material index out of range ({signature.MaterialIndex})";
                Debug.LogError($"[MaterialAnchorRegistry] Material index out of range for renderer {renderer.name}");
                return false;
            }

            var material = materials[signature.MaterialIndex];
            if (material == null)
            {
                error = "Material is null";
                Debug.LogError($"[MaterialAnchorRegistry] Material is null at renderer {renderer.name}[{signature.MaterialIndex}]");
                return false;
            }

            if (!_runtimeIdToMaterials.TryGetValue(signature.RuntimeMaterialId, out var materialSet))
            {
                materialSet = new HashSet<Material>();
                _runtimeIdToMaterials[signature.RuntimeMaterialId] = materialSet;
            }

            materialSet.Add(material);

            if (IsAnchorRuntimeId(signature.RuntimeMaterialId))
            {
                if (!_anchorToRuntimeIds.TryGetValue(anchorGuid, out var runtimeIds))
                {
                    runtimeIds = new HashSet<string>();
                    _anchorToRuntimeIds[anchorGuid] = runtimeIds;
                }
                runtimeIds.Add(signature.RuntimeMaterialId);
            }

            Debug.Log($"[MaterialAnchorRegistry] Registered material {signature.RuntimeMaterialId} ({material.name})");
            return true;
        }

        public void UnregisterMaterialDynamic(string runtimeMaterialId)
        {
            if (string.IsNullOrEmpty(runtimeMaterialId))
            {
                return;
            }

            if (_runtimeIdToMaterials.Remove(runtimeMaterialId))
            {
                Debug.Log($"[MaterialAnchorRegistry] Unregistered material {runtimeMaterialId}");
            }

            if (TryExtractAnchorGuid(runtimeMaterialId, out var anchorGuid) &&
                _anchorToRuntimeIds.TryGetValue(anchorGuid, out var runtimeIds))
            {
                runtimeIds.Remove(runtimeMaterialId);
                if (runtimeIds.Count == 0)
                {
                    _anchorToRuntimeIds.Remove(anchorGuid);
                }
            }
        }

        public void Clear()
        {
            _anchorToRenderer.Clear();
            _runtimeIdToMaterials.Clear();
            _anchorToRuntimeIds.Clear();
        }

        private void RegisterAllAnchors()
        {
            var allAnchors = FindObjectsOfType<MaterialAnchor>(true);
            foreach (var anchor in allAnchors)
            {
                RegisterAnchor(anchor);
            }
        }

        private static bool IsAnchorRuntimeId(string runtimeMaterialId)
        {
            return runtimeMaterialId.Contains(":");
        }

        private static bool TryExtractAnchorGuid(string runtimeMaterialId, out Guid anchorGuid)
        {
            anchorGuid = Guid.Empty;
            if (!IsAnchorRuntimeId(runtimeMaterialId))
            {
                return false;
            }

            int splitIndex = runtimeMaterialId.IndexOf(':');
            if (splitIndex <= 0)
            {
                return false;
            }

            var guidPart = runtimeMaterialId.Substring(0, splitIndex);
            return Guid.TryParse(guidPart, out anchorGuid);
        }
    }

}
