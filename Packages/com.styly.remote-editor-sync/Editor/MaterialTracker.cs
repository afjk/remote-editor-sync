using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace RemoteEditorSync
{
    /// <summary>
    /// Tracks material usage across the scene and mirrors inspector changes to the runtime via RPC.
    /// </summary>
    public static class MaterialTracker
    {
        private static readonly Dictionary<string, MaterialSnapshot> _materialSnapshots = new Dictionary<string, MaterialSnapshot>();
        private static readonly Dictionary<string, HashSet<int>> _materialUsers = new Dictionary<string, HashSet<int>>();
        private static readonly Dictionary<int, List<MaterialSignature>> _instanceToSignatures = new Dictionary<int, List<MaterialSignature>>();
        private static readonly HashSet<string> _pendingRuntimeRegistrations = new HashSet<string>();
        private static readonly Dictionary<string, MaterialSignature> _runtimeIdToSignature = new Dictionary<string, MaterialSignature>();
        private static readonly Dictionary<int, HashSet<string>> _gameObjectToRuntimeIds = new Dictionary<int, HashSet<string>>();
        private static readonly Dictionary<string, int> _runtimeIdToInstanceId = new Dictionary<string, int>();

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        };

        public static void RegisterMaterial(Material material, Renderer renderer, int materialIndex, GameObject user)
        {
            if (material == null || renderer == null || user == null)
            {
                return;
            }

            var signature = MaterialSignature.Create(material, renderer, materialIndex);
            int materialInstanceId = material.GetInstanceID();
            int userInstanceId = user.GetInstanceID();

            _runtimeIdToSignature[signature.RuntimeMaterialId] = signature;
            _runtimeIdToInstanceId[signature.RuntimeMaterialId] = materialInstanceId;

            if (!_materialSnapshots.ContainsKey(signature.RuntimeMaterialId))
            {
                SendRegisterMaterial(material, signature);
                _pendingRuntimeRegistrations.Add(signature.RuntimeMaterialId);
                _materialSnapshots[signature.RuntimeMaterialId] = new MaterialSnapshot(material);
            }

            if (!_materialUsers.TryGetValue(signature.RuntimeMaterialId, out var users))
            {
                users = new HashSet<int>();
                _materialUsers[signature.RuntimeMaterialId] = users;
            }
            users.Add(userInstanceId);

            if (!_instanceToSignatures.TryGetValue(materialInstanceId, out var signatures))
            {
                signatures = new List<MaterialSignature>();
                _instanceToSignatures[materialInstanceId] = signatures;
            }
            if (!signatures.Contains(signature))
            {
                signatures.Add(signature);
            }

            if (!_gameObjectToRuntimeIds.TryGetValue(userInstanceId, out var runtimeIds))
            {
                runtimeIds = new HashSet<string>();
                _gameObjectToRuntimeIds[userInstanceId] = runtimeIds;
            }
            runtimeIds.Add(signature.RuntimeMaterialId);
        }

        public static void UnregisterMaterial(Material material, Renderer renderer, int materialIndex, GameObject user)
        {
            if (material == null || renderer == null || user == null)
            {
                return;
            }

            var signature = MaterialSignature.Create(material, renderer, materialIndex);
            int materialInstanceId = material.GetInstanceID();
            int userInstanceId = user.GetInstanceID();

            RemoveRuntimeIdFromGameObject(userInstanceId, signature.RuntimeMaterialId);

            if (_materialUsers.TryGetValue(signature.RuntimeMaterialId, out var users))
            {
                users.Remove(userInstanceId);
                if (users.Count == 0)
                {
                    _materialUsers.Remove(signature.RuntimeMaterialId);
                    FinalizeMaterialRemoval(signature);
                }
            }

            if (_instanceToSignatures.TryGetValue(materialInstanceId, out var signatures))
            {
                signatures.Remove(signature);
                if (signatures.Count == 0)
                {
                    _instanceToSignatures.Remove(materialInstanceId);
                }
            }
        }

        public static void UnregisterAllMaterials(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null)
                    {
                        UnregisterMaterial(materials[i], renderer, i, renderer.gameObject);
                    }
                }
            }
        }

        public static void UnregisterGameObject(int gameObjectInstanceId)
        {
            if (!_gameObjectToRuntimeIds.TryGetValue(gameObjectInstanceId, out var runtimeIds))
            {
                return;
            }

            foreach (var runtimeId in runtimeIds.ToList())
            {
                UnregisterRuntimeIdForGameObject(gameObjectInstanceId, runtimeId);
            }

            _gameObjectToRuntimeIds.Remove(gameObjectInstanceId);
        }

        public static void CheckForChanges()
        {
            foreach (var kvp in _instanceToSignatures.ToList())
            {
                var material = EditorUtility.InstanceIDToObject(kvp.Key) as Material;
                if (material == null)
                {
                    continue;
                }

                bool materialChanged = kvp.Value.Any(signature =>
                    _materialSnapshots.TryGetValue(signature.RuntimeMaterialId, out var snapshot) &&
                    snapshot.HasChanged(material));

                if (!materialChanged)
                {
                    continue;
                }

                foreach (var signature in kvp.Value)
                {
                    SendUpdateMaterialProperties(material, signature);
                    _materialSnapshots[signature.RuntimeMaterialId] = new MaterialSnapshot(material);
                }
            }
        }

        public static void CleanupDeletedMaterials()
        {
            var deletedInstanceIds = new List<int>();

            foreach (var kvp in _instanceToSignatures)
            {
                if (EditorUtility.InstanceIDToObject(kvp.Key) == null)
                {
                    deletedInstanceIds.Add(kvp.Key);
                }
            }

            foreach (var instanceId in deletedInstanceIds)
            {
                if (_instanceToSignatures.TryGetValue(instanceId, out var signatures))
                {
                    foreach (var signature in signatures)
                    {
                        RemoveRuntimeIdFromAllGameObjects(signature.RuntimeMaterialId);
                        _materialUsers.Remove(signature.RuntimeMaterialId);
                        _runtimeIdToSignature.Remove(signature.RuntimeMaterialId);
                        _runtimeIdToInstanceId.Remove(signature.RuntimeMaterialId);
                        _materialSnapshots.Remove(signature.RuntimeMaterialId);
                        _pendingRuntimeRegistrations.Remove(signature.RuntimeMaterialId);
                        SendUnregisterMaterial(signature);
                    }
                }

                _instanceToSignatures.Remove(instanceId);
            }
        }

        public static void HandleRegisterMaterialResult(RegisterMaterialResultData result)
        {
            if (result == null || string.IsNullOrEmpty(result.RuntimeMaterialId))
            {
                return;
            }

            if (result.Success)
            {
                _pendingRuntimeRegistrations.Remove(result.RuntimeMaterialId);
                return;
            }

            Debug.LogWarning($"[MaterialTracker] RegisterMaterial failed for {result.RuntimeMaterialId}: {result.ErrorMessage}");
            _pendingRuntimeRegistrations.Add(result.RuntimeMaterialId);
        }

        public static void RetryPendingRegistrations()
        {
            if (_pendingRuntimeRegistrations.Count == 0)
            {
                return;
            }

            foreach (var runtimeId in _pendingRuntimeRegistrations.ToList())
            {
                if (!_runtimeIdToSignature.TryGetValue(runtimeId, out var signature))
                {
                    continue;
                }

                if (!MaterialAnchorEditorRegistry.TryGetAnchor(signature.AnchorId, out var anchor) || anchor == null)
                {
                    continue;
                }

                var renderer = anchor.TargetRenderer;
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (signature.MaterialIndex < 0 || signature.MaterialIndex >= materials.Length)
                {
                    continue;
                }

                var material = materials[signature.MaterialIndex];
                if (material == null)
                {
                    continue;
                }

                SendRegisterMaterial(material, signature);
            }
        }

        public static void Clear()
        {
            _materialSnapshots.Clear();
            _materialUsers.Clear();
            _instanceToSignatures.Clear();
            _pendingRuntimeRegistrations.Clear();
            _runtimeIdToSignature.Clear();
            _gameObjectToRuntimeIds.Clear();
            _runtimeIdToInstanceId.Clear();
        }

        private static void SendRegisterMaterial(Material material, MaterialSignature signature)
        {
            var data = new RegisterMaterialData
            {
                Signature = signature
            };

            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            SendRpc("RegisterMaterial", new[] { json });
            Debug.Log($"[MaterialTracker] Sent RegisterMaterial RPC: {signature.RuntimeMaterialId}");
        }

        private static void SendUnregisterMaterial(MaterialSignature signature)
        {
            var data = new UnregisterMaterialData
            {
                RuntimeMaterialId = signature.RuntimeMaterialId
            };

            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            SendRpc("UnregisterMaterial", new[] { json });
            Debug.Log($"[MaterialTracker] Sent UnregisterMaterial RPC: {signature.RuntimeMaterialId}");
        }

        private static void SendUpdateMaterialProperties(Material material, MaterialSignature signature)
        {
            var snapshot = new MaterialSnapshot(material);
            var data = new UpdateMaterialPropertiesData
            {
                Signature = signature,
                PropertiesJson = JsonConvert.SerializeObject(snapshot.Properties, _jsonSettings)
            };

            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            SendRpc("UpdateMaterialProperties", new[] { json });
            Debug.Log($"[MaterialTracker] Sent UpdateMaterialProperties RPC: {signature.RuntimeMaterialId}");
        }

        private static void SendRpc(string methodName, string[] args)
        {
            var manager = Styly.NetSync.NetSyncManager.Instance;
            if (manager == null || manager.ClientNo < 0)
            {
                return;
            }

            manager.Rpc(methodName, args);
        }

        private static void FinalizeMaterialRemoval(MaterialSignature signature)
        {
            RemoveRuntimeIdFromAllGameObjects(signature.RuntimeMaterialId);
            _runtimeIdToSignature.Remove(signature.RuntimeMaterialId);
            _runtimeIdToInstanceId.Remove(signature.RuntimeMaterialId);
            _materialSnapshots.Remove(signature.RuntimeMaterialId);
            _pendingRuntimeRegistrations.Remove(signature.RuntimeMaterialId);
            SendUnregisterMaterial(signature);
        }

        private static void RemoveRuntimeIdFromGameObject(int gameObjectInstanceId, string runtimeId)
        {
            if (!_gameObjectToRuntimeIds.TryGetValue(gameObjectInstanceId, out var runtimeIds))
            {
                return;
            }

            runtimeIds.Remove(runtimeId);
            if (runtimeIds.Count == 0)
            {
                _gameObjectToRuntimeIds.Remove(gameObjectInstanceId);
            }
        }

        private static void RemoveRuntimeIdFromAllGameObjects(string runtimeId)
        {
            var emptyKeys = new List<int>();
            foreach (var kvp in _gameObjectToRuntimeIds)
            {
                if (kvp.Value.Remove(runtimeId) && kvp.Value.Count == 0)
                {
                    emptyKeys.Add(kvp.Key);
                }
            }

            foreach (var key in emptyKeys)
            {
                _gameObjectToRuntimeIds.Remove(key);
            }
        }

        private static void UnregisterRuntimeIdForGameObject(int gameObjectInstanceId, string runtimeId)
        {
            if (_materialUsers.TryGetValue(runtimeId, out var users))
            {
                users.Remove(gameObjectInstanceId);
                if (users.Count == 0)
                {
                    _materialUsers.Remove(runtimeId);

                    if (_runtimeIdToSignature.TryGetValue(runtimeId, out var signature))
                    {
                        if (_runtimeIdToInstanceId.TryGetValue(runtimeId, out var instanceId) &&
                            _instanceToSignatures.TryGetValue(instanceId, out var signatures))
                        {
                            signatures.Remove(signature);
                            if (signatures.Count == 0)
                            {
                                _instanceToSignatures.Remove(instanceId);
                            }
                        }

                        FinalizeMaterialRemoval(signature);
                    }
                }
            }
        }
    }
}
