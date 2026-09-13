using System;
using System.IO;
using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Writes diagnostic logs to a file (Logs/SplitCanvas.log) so they can be read
    /// from outside the Unity console. Also mirrors to Debug.Log for the console.
    /// </summary>
    public static class SplitLog
    {
        private static readonly string FilePath =
            Path.Combine(Application.dataPath, "..", "Logs", "SplitCanvas.log");

        private static readonly object Lock = new object();

        public static void Log(string message)
        {
            lock (Lock)
            {
                try
                {
                    File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SplitLog] Failed to write log: {e.Message}");
                }
            }

            Debug.Log(message);
        }
    }
}