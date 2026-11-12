#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Ooda.App
{
    /// <summary>
    /// Ends the Windows game the moment Unity has finished quitting. Without it the game sometimes crashed on the way out:
    /// an access violation inside D3D12Core.dll while the graphics device was released, after Unity's own shutdown had
    /// completed (Windows logged 61 of them between 4 and 11 November 2026; Unity's crash handler never saw one). Nothing
    /// of the game's is still to be saved at that point (OnApplicationQuit has run, the settings are written here), so the
    /// process is ended there with exit code 0 and the graphics teardown that crashed never runs.
    /// </summary>
    static class CleanExit
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [RuntimeInitializeOnLoadMethod]
        static void Watch() => Application.quitting += () =>
        {
            PlayerPrefs.Save();
            TerminateProcess(GetCurrentProcess(), 0);
        };
    }
}
#endif
