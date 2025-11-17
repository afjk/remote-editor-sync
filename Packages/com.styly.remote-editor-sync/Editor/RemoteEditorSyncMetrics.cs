using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RemoteEditorSync
{
    internal static class RemoteEditorSyncMetrics
    {
        private class RpcMetric
        {
            public int Count;
            public long TotalBytes;
        }

        private static readonly Dictionary<string, RpcMetric> _metrics = new Dictionary<string, RpcMetric>();

        public static void Record(string functionName, int payloadBytes)
        {
            if (!RemoteEditorSyncSettings.Instance.EnableMetrics)
            {
                return;
            }

            if (!_metrics.TryGetValue(functionName, out var metric))
            {
                metric = new RpcMetric();
                _metrics[functionName] = metric;
            }

            metric.Count++;
            metric.TotalBytes += payloadBytes;
        }

        [MenuItem("Tools/Remote Editor Sync/Diagnostics/Print Metrics")] 
        private static void PrintMetrics()
        {
            if (_metrics.Count == 0)
            {
                Debug.Log("[RemoteEditorSync] No metrics recorded yet.");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("[RemoteEditorSync] RPC Metrics");
            foreach (var kvp in _metrics)
            {
                builder.AppendLine($" - {kvp.Key}: {kvp.Value.Count} call(s), {kvp.Value.TotalBytes} bytes");
            }

            Debug.Log(builder.ToString());
        }

        [MenuItem("Tools/Remote Editor Sync/Diagnostics/Clear Metrics")] 
        private static void ClearMetrics()
        {
            _metrics.Clear();
            Debug.Log("[RemoteEditorSync] Cleared RPC metrics.");
        }
    }
}
