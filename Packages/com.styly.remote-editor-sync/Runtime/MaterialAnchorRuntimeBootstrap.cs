using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RemoteEditorSync
{
    /// <summary>
    /// Ensures runtime-generated renderers receive anchors and stay registered with the registry.
    /// </summary>
    public class MaterialAnchorRuntimeBootstrap : MonoBehaviour
    {
        private static readonly Dictionary<int, WeakReference<Renderer>> _trackedRenderers =
            new Dictionary<int, WeakReference<Renderer>>();

        [SerializeField] private float _fullScanIntervalSeconds = 10f;
        [SerializeField] private bool _enablePeriodicFallbackScan = true;

        private static bool _fullScanRequested;
        private Coroutine _scanCoroutine;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureAnchorsForAllScenes();
            if (_enablePeriodicFallbackScan)
            {
                _scanCoroutine = StartCoroutine(FullScanLoop());
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }
        }

        private IEnumerator FullScanLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_fullScanIntervalSeconds);
                if (!_fullScanRequested)
                {
                    continue;
                }

                _fullScanRequested = false;
                EnsureAnchorsForAllScenes();
                CleanupTrackedRenderers();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureAnchorsForScene(scene);
        }

        public static void EnsureAnchorsForHierarchy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                NotifyRendererAdded(renderer);
            }
        }

        public static void NotifyRendererAdded(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            int instanceId = renderer.GetInstanceID();
            if (_trackedRenderers.TryGetValue(instanceId, out var reference) &&
                reference.TryGetTarget(out var existingRenderer) &&
                existingRenderer != null)
            {
                return;
            }

            var anchor = MaterialAnchor.GetOrCreateForRenderer(renderer);
            _trackedRenderers[instanceId] = new WeakReference<Renderer>(renderer);
            MaterialAnchorRegistry.Instance?.RegisterAnchor(anchor);
        }

        public static void RequestFullScan()
        {
            _fullScanRequested = true;
        }

        private static void EnsureAnchorsForAllScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                EnsureAnchorsForScene(scene);
            }
        }

        private static void EnsureAnchorsForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                EnsureAnchorsForHierarchy(root);
            }
        }

        private static void CleanupTrackedRenderers()
        {
            var deadRendererIds = _trackedRenderers
                .Where(kvp => !kvp.Value.TryGetTarget(out _))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in deadRendererIds)
            {
                _trackedRenderers.Remove(id);
            }
        }
    }
}
