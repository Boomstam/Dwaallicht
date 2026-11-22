#if UNITY_EDITOR && UNITY_MCP_READY && UNITY_MCP_DEPS_49 && !UNITY_MCP_EXCLUDE_FROM_COMPILATION
using System;
using com.IvanMurzak.Unity.MCP;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UnityMcpConnectOnLoad
{
    static UnityMcpConnectOnLoad()
    {
        EditorApplication.delayCall += Connect;
    }

    static async void Connect()
    {
        try
        {
            UnityMcpPluginEditor.KeepConnected = true;
            var connected = await UnityMcpPluginEditor.ConnectIfNeeded();
            Debug.Log($"[UnityMcpConnectOnLoad] ConnectIfNeeded result: {connected}");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
#endif
