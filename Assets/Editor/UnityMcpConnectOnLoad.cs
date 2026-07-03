#if UNITY_EDITOR
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
