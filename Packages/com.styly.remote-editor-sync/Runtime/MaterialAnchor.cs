using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RemoteEditorSync
{
    /// <summary>
    /// Unique identifier that pins renderer-bound materials even when hierarchy changes.
    /// </summary>
    [ExecuteAlways]
    public sealed class MaterialAnchor : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private string _anchorId;

#if UNITY_EDITOR
        internal string RawAnchorId => _anchorId;
#endif

        public Renderer TargetRenderer
        {
            get
            {
                EnsureRendererBinding();
                return _renderer;
            }
        }

        public Guid AnchorId
        {
            get
            {
                if (string.IsNullOrEmpty(_anchorId) || !Guid.TryParse(_anchorId, out var guid))
                {
                    RegenerateGuid();
#if UNITY_EDITOR
                    EditorUtility.SetDirty(this);
#endif
                    Guid.TryParse(_anchorId, out guid);
                }
                return guid;
            }
        }

        private void Awake()
        {
            EnsureRendererBinding();
            EnsureAnchorGuid(false);
        }

        private void OnEnable()
        {
            EnsureRendererBinding();
            EnsureAnchorGuid(true);

#if UNITY_EDITOR
            MaterialAnchorEditorRegistry.Register(this);
#else
            MaterialAnchorRuntimeTracker.Register(this);
#endif
            MaterialAnchorRegistry.Instance?.RegisterAnchor(this);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            MaterialAnchorEditorRegistry.Unregister(this);
#else
            MaterialAnchorRuntimeTracker.Unregister(this);
#endif
            MaterialAnchorRegistry.Instance?.UnregisterAnchor(this);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            MaterialAnchorEditorRegistry.Unregister(this);
#else
            MaterialAnchorRuntimeTracker.Unregister(this);
#endif
            MaterialAnchorRegistry.Instance?.UnregisterAnchor(this);
        }

        private void OnValidate()
        {
            EnsureRendererBinding();
            EnsureAnchorGuid(true);
        }

        private void EnsureRendererBinding()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<Renderer>();
            }
        }

        private void EnsureAnchorGuid(bool forceRegenerateIfDuplicate)
        {
            if (string.IsNullOrEmpty(_anchorId) || !Guid.TryParse(_anchorId, out _))
            {
                RegenerateGuid();
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
#endif
            }

#if UNITY_EDITOR
            if (forceRegenerateIfDuplicate && MaterialAnchorEditorRegistry.IsDuplicate(_anchorId, this))
            {
                RegenerateGuid();
                EditorUtility.SetDirty(this);
            }
#else
            if (forceRegenerateIfDuplicate && MaterialAnchorRuntimeTracker.IsDuplicate(_anchorId, this))
            {
                RegenerateGuid();
            }
#endif
        }

        internal void RegenerateGuid()
        {
            _anchorId = Guid.NewGuid().ToString();
        }

        public static MaterialAnchor GetOrCreateForRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var anchors = renderer.GetComponents<MaterialAnchor>();
            foreach (var anchor in anchors)
            {
                if (anchor != null && anchor.TargetRenderer == renderer)
                {
                    return anchor;
                }
            }

#if UNITY_EDITOR
            var created = Undo.AddComponent<MaterialAnchor>(renderer.gameObject);
#else
            var created = renderer.gameObject.AddComponent<MaterialAnchor>();
#endif
            created._renderer = renderer;
            created.RegenerateGuid();
            return created;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _renderer = GetComponent<Renderer>();
            EnsureAnchorGuid(false);
        }
#endif
    }

#if UNITY_EDITOR
    [InitializeOnLoad]
    public static class MaterialAnchorEditorRegistry
    {
        private static readonly Dictionary<string, WeakReference<MaterialAnchor>> _anchors =
            new Dictionary<string, WeakReference<MaterialAnchor>>();

        static MaterialAnchorEditorRegistry()
        {
            EditorApplication.playModeStateChanged += _ => CleanupDeadAnchors();
            EditorApplication.hierarchyChanged += CleanupDeadAnchors;
        }

        public static void Register(MaterialAnchor anchor)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.RawAnchorId))
            {
                return;
            }

            CleanupDeadAnchors();
            _anchors[anchor.RawAnchorId] = new WeakReference<MaterialAnchor>(anchor);
        }

        public static void Unregister(MaterialAnchor anchor)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.RawAnchorId))
            {
                return;
            }

            _anchors.Remove(anchor.RawAnchorId);
        }

        public static bool TryGetAnchor(string anchorId, out MaterialAnchor anchor)
        {
            anchor = null;
            if (string.IsNullOrEmpty(anchorId))
            {
                return false;
            }

            CleanupDeadAnchors();

            if (_anchors.TryGetValue(anchorId, out var reference) &&
                reference.TryGetTarget(out var target) &&
                target != null)
            {
                anchor = target;
                return true;
            }

            return false;
        }

        public static bool IsDuplicate(string anchorId, MaterialAnchor owner)
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                return false;
            }

            CleanupDeadAnchors();

            if (_anchors.TryGetValue(anchorId, out var reference) &&
                reference.TryGetTarget(out var target) &&
                target != null &&
                target != owner)
            {
                return true;
            }

            return false;
        }

        private static void CleanupDeadAnchors()
        {
            var deadKeys = _anchors
                .Where(kvp => !kvp.Value.TryGetTarget(out _))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in deadKeys)
            {
                _anchors.Remove(key);
            }
        }
    }
#else
    public static class MaterialAnchorRuntimeTracker
    {
        private static readonly Dictionary<string, WeakReference<MaterialAnchor>> _anchors =
            new Dictionary<string, WeakReference<MaterialAnchor>>();

        public static void Register(MaterialAnchor anchor)
        {
            if (anchor == null)
            {
                return;
            }

            Cleanup();
            _anchors[anchor.AnchorId.ToString()] = new WeakReference<MaterialAnchor>(anchor);
        }

        public static void Unregister(MaterialAnchor anchor)
        {
            if (anchor == null)
            {
                return;
            }

            _anchors.Remove(anchor.AnchorId.ToString());
        }

        public static bool IsDuplicate(string anchorId, MaterialAnchor owner)
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                return false;
            }

            Cleanup();

            if (_anchors.TryGetValue(anchorId, out var reference) &&
                reference.TryGetTarget(out var target) &&
                target != null &&
                target != owner)
            {
                return true;
            }

            return false;
        }

        private static void Cleanup()
        {
            var deadKeys = _anchors
                .Where(kvp => !kvp.Value.TryGetTarget(out _))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in deadKeys)
            {
                _anchors.Remove(key);
            }
        }
    }
#endif
}
