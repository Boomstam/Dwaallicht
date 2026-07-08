using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dwaallicht.Cloud
{
    [AddComponentMenu("Dwaallicht/Cloud/Google Drive Folder Sync")]
    public sealed class GoogleDriveFolderSync : MonoBehaviour
    {
        private const string DriveFilesEndpoint = "https://www.googleapis.com/drive/v3/files";
        private const string GoogleFolderMimeType = "application/vnd.google-apps.folder";
        private const string GoogleAppsMimePrefix = "application/vnd.google-apps.";
        private const string ManifestFileName = ".google-drive-sync-manifest.json";

        [Header("Google Drive")]
        [SerializeField] private string folderId = "18qv9ISU9yPbRWKT4lkrr2Xq24dm8VR9b";
        [SerializeField] private AuthMode authMode = AuthMode.ApiKey;
        [SerializeField] private string apiKey = "";
        [SerializeField] private string oauthBearerToken = "";
        [SerializeField] private bool recursive = true;

        [Header("Local Target")]
        [SerializeField] private string targetFolderName = "DriveSync";
        [SerializeField] private bool syncIntoStreamingAssetsInEditor = true;
        [SerializeField] private bool syncOnStart;
        [SerializeField] private bool forceDownload;

        [Header("Testing Limits")]
        [SerializeField, Min(1)] private int maxFilesPerRun = 25;
        [SerializeField, Min(1)] private int pageSize = 50;
        [SerializeField, Min(1024)] private long maxBytesPerFile = 5 * 1024 * 1024;

        private readonly Dictionary<string, ManifestEntry> manifestByLocalPath = new Dictionary<string, ManifestEntry>();
        private SyncManifest manifest = new SyncManifest();
        private int downloadedFiles;
        private bool syncFailed;

        public enum AuthMode
        {
            ApiKey,
            OAuthBearerToken
        }

        public bool IsSyncing { get; private set; }
        public string LastSyncStatus { get; private set; } = "Idle";
        public string LocalRootPath => GetLocalRootPath();

        public event Action<string> StatusChanged;
        public event Action<bool> SyncFinished;

        private void Start()
        {
            if (syncOnStart)
            {
                StartSync();
            }
        }

        [ContextMenu("Start Sync")]
        public void StartSync()
        {
            if (IsSyncing)
            {
                SetStatus("Sync already running.");
                return;
            }

            StartCoroutine(SyncRoutine());
        }

        public IEnumerator SyncRoutine()
        {
            var resolvedFolderId = ResolveFolderId(folderId);
            if (string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                Fail("Google Drive folder id or folder URL is missing.");
                yield break;
            }

            if (authMode == AuthMode.ApiKey && string.IsNullOrWhiteSpace(apiKey))
            {
                Fail("Google Drive API key is missing.");
                yield break;
            }

            if (authMode == AuthMode.OAuthBearerToken && string.IsNullOrWhiteSpace(oauthBearerToken))
            {
                Fail("OAuth bearer token is missing.");
                yield break;
            }

            IsSyncing = true;
            syncFailed = false;
            downloadedFiles = 0;

            var rootPath = GetLocalRootPath();
            Directory.CreateDirectory(rootPath);
            LoadManifest(rootPath);

            SetStatus("Starting Google Drive sync.");
            yield return SyncDriveFolder(resolvedFolderId, rootPath);

            if (!syncFailed)
            {
                SaveManifest(rootPath);
                RefreshAssetDatabaseIfNeeded(rootPath);
                SetStatus($"Google Drive sync complete. Downloaded {downloadedFiles} file(s) to {rootPath}.");
            }

            IsSyncing = false;
            SyncFinished?.Invoke(!syncFailed);
        }

        private IEnumerator SyncDriveFolder(string driveFolderId, string localFolderPath)
        {
            Directory.CreateDirectory(localFolderPath);

            var nextPageToken = "";
            do
            {
                DriveFileList page = null;
                yield return RequestJson<DriveFileList>(BuildListUrl(driveFolderId, nextPageToken), value => page = value);
                if (syncFailed)
                {
                    yield break;
                }

                if (page?.files == null)
                {
                    yield break;
                }

                foreach (var file in page.files)
                {
                    if (downloadedFiles >= maxFilesPerRun)
                    {
                        SetStatus($"Reached test sync limit of {maxFilesPerRun} file(s).");
                        yield break;
                    }

                    if (file == null || string.IsNullOrWhiteSpace(file.id))
                    {
                        continue;
                    }

                    var safeName = SanitizeFileName(file.name);
                    if (string.Equals(file.mimeType, GoogleFolderMimeType, StringComparison.Ordinal))
                    {
                        if (recursive)
                        {
                            yield return SyncDriveFolder(file.id, Path.Combine(localFolderPath, safeName));
                            if (syncFailed)
                            {
                                yield break;
                            }
                        }

                        continue;
                    }

                    yield return DownloadDriveFile(file, localFolderPath, safeName);
                    if (syncFailed)
                    {
                        yield break;
                    }
                }

                nextPageToken = page.nextPageToken;
            }
            while (!string.IsNullOrEmpty(nextPageToken));
        }

        private IEnumerator DownloadDriveFile(DriveFile file, string localFolderPath, string safeName)
        {
            var export = GetExportFor(file.mimeType);
            if (file.IsGoogleWorkspaceFile && !export.canExport)
            {
                SetStatus($"Skipping unsupported Google Workspace file: {file.name} ({file.mimeType}).");
                yield break;
            }

            var localFileName = export.canExport ? EnsureExtension(safeName, export.extension) : safeName;
            var localPath = Path.Combine(localFolderPath, localFileName);
            var relativePath = ToRelativeLocalPath(localPath);

            if (!ShouldDownload(file, relativePath, localPath, export.mimeType))
            {
                yield break;
            }

            if (!file.IsGoogleWorkspaceFile && file.TryGetSize(out var fileSize) && fileSize > maxBytesPerFile)
            {
                SetStatus($"Skipping {file.name}: {fileSize} bytes is above the test limit.");
                yield break;
            }

            byte[] bytes = null;
            var url = file.IsGoogleWorkspaceFile
                ? BuildExportUrl(file.id, export.mimeType)
                : BuildDownloadUrl(file.id);

            SetStatus($"Downloading {localFileName}.");
            yield return RequestBytes(url, value => bytes = value);
            if (syncFailed || bytes == null)
            {
                yield break;
            }

            if (bytes.LongLength > maxBytesPerFile)
            {
                SetStatus($"Skipping {file.name}: downloaded export is above the test limit.");
                yield break;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localPath));
            File.WriteAllBytes(localPath, bytes);
            downloadedFiles++;

            manifestByLocalPath[relativePath] = new ManifestEntry
            {
                driveId = file.id,
                localPath = relativePath,
                modifiedTime = file.modifiedTime,
                md5Checksum = file.md5Checksum,
                exportMimeType = export.mimeType
            };
        }

        private IEnumerator RequestJson<T>(string url, Action<T> onSuccess)
        {
            string json = null;
            yield return RequestText(url, value => json = value);
            if (syncFailed)
            {
                yield break;
            }

            try
            {
                onSuccess?.Invoke(JsonUtility.FromJson<T>(json));
            }
            catch (Exception ex)
            {
                Fail($"Could not parse Google Drive response: {ex.Message}");
            }
        }

        private IEnumerator RequestText(string url, Action<string> onSuccess)
        {
            using (var request = CreateGetRequest(url))
            {
                yield return request.SendWebRequest();

                if (HasRequestError(request))
                {
                    Fail(BuildRequestError(request));
                    yield break;
                }

                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }

        private IEnumerator RequestBytes(string url, Action<byte[]> onSuccess)
        {
            using (var request = CreateGetRequest(url))
            {
                yield return request.SendWebRequest();

                if (HasRequestError(request))
                {
                    Fail(BuildRequestError(request));
                    yield break;
                }

                onSuccess?.Invoke(request.downloadHandler.data);
            }
        }

        private UnityWebRequest CreateGetRequest(string url)
        {
            var request = UnityWebRequest.Get(url);
            if (authMode == AuthMode.OAuthBearerToken)
            {
                request.SetRequestHeader("Authorization", "Bearer " + oauthBearerToken.Trim());
            }

            return request;
        }

        private string BuildListUrl(string driveFolderId, string pageToken)
        {
            var query = $"'{driveFolderId.Replace("'", "\\'")}' in parents and trashed = false";
            var url = DriveFilesEndpoint
                + "?q=" + Uri.EscapeDataString(query)
                + "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,modifiedTime,size,md5Checksum)")
                + "&orderBy=" + Uri.EscapeDataString("folder,name")
                + "&pageSize=" + Mathf.Clamp(pageSize, 1, 1000)
                + "&supportsAllDrives=true&includeItemsFromAllDrives=true";

            if (!string.IsNullOrEmpty(pageToken))
            {
                url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            }

            return AddApiKey(url);
        }

        private string BuildDownloadUrl(string fileId)
        {
            return AddApiKey(DriveFilesEndpoint + "/" + Uri.EscapeDataString(fileId) + "?alt=media&supportsAllDrives=true");
        }

        private string BuildExportUrl(string fileId, string exportMimeType)
        {
            return AddApiKey(DriveFilesEndpoint + "/" + Uri.EscapeDataString(fileId)
                + "/export?mimeType=" + Uri.EscapeDataString(exportMimeType));
        }

        private string AddApiKey(string url)
        {
            if (authMode != AuthMode.ApiKey)
            {
                return url;
            }

            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "key=" + Uri.EscapeDataString(apiKey.Trim());
        }

        private bool ShouldDownload(DriveFile file, string relativePath, string localPath, string exportMimeType)
        {
            if (forceDownload || !File.Exists(localPath))
            {
                return true;
            }

            if (!manifestByLocalPath.TryGetValue(relativePath, out var entry))
            {
                return true;
            }

            if (!string.Equals(entry.driveId, file.id, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(entry.modifiedTime, file.modifiedTime, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(entry.exportMimeType, exportMimeType, StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrEmpty(file.md5Checksum)
                && !string.Equals(entry.md5Checksum, file.md5Checksum, StringComparison.Ordinal);
        }

        private void LoadManifest(string rootPath)
        {
            manifestByLocalPath.Clear();
            manifest = new SyncManifest();

            var manifestPath = Path.Combine(rootPath, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return;
            }

            try
            {
                manifest = JsonUtility.FromJson<SyncManifest>(File.ReadAllText(manifestPath)) ?? new SyncManifest();
                if (manifest.files == null)
                {
                    manifest.files = new List<ManifestEntry>();
                }

                foreach (var entry in manifest.files)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.localPath))
                    {
                        manifestByLocalPath[entry.localPath] = entry;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GoogleDriveFolderSync] Could not read manifest: {ex.Message}");
                manifest = new SyncManifest();
            }
        }

        private void SaveManifest(string rootPath)
        {
            manifest.files.Clear();
            manifest.files.AddRange(manifestByLocalPath.Values);
            File.WriteAllText(Path.Combine(rootPath, ManifestFileName), JsonUtility.ToJson(manifest, true));
        }

        private string GetLocalRootPath()
        {
#if UNITY_EDITOR
            if (syncIntoStreamingAssetsInEditor)
            {
                return Path.Combine(Application.streamingAssetsPath, targetFolderName);
            }
#endif
            return Path.Combine(Application.persistentDataPath, targetFolderName);
        }

        private string ToRelativeLocalPath(string localPath)
        {
            var root = GetLocalRootPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relative = localPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? localPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.GetFileName(localPath);
            return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private void RefreshAssetDatabaseIfNeeded(string rootPath)
        {
#if UNITY_EDITOR
            if (rootPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.Refresh();
            }
#endif
        }

        private ExportInfo GetExportFor(string mimeType)
        {
            switch (mimeType)
            {
                case "application/vnd.google-apps.spreadsheet":
                    return new ExportInfo(true, "text/csv", ".csv");
                case "application/vnd.google-apps.document":
                    return new ExportInfo(true, "text/plain", ".txt");
                case "application/vnd.google-apps.presentation":
                    return new ExportInfo(true, "application/pdf", ".pdf");
                case "application/vnd.google-apps.drawing":
                    return new ExportInfo(true, "image/png", ".png");
                case "application/vnd.google-apps.script":
                    return new ExportInfo(true, "application/vnd.google-apps.script+json", ".json");
                default:
                    return new ExportInfo(false, "", "");
            }
        }

        private void SetStatus(string status)
        {
            LastSyncStatus = status;
            Debug.Log("[GoogleDriveFolderSync] " + status);
            StatusChanged?.Invoke(status);
        }

        private void Fail(string message)
        {
            syncFailed = true;
            LastSyncStatus = message;
            Debug.LogWarning("[GoogleDriveFolderSync] " + message);
            StatusChanged?.Invoke(message);
        }

        private static bool HasRequestError(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.ProtocolError
                || request.result == UnityWebRequest.Result.DataProcessingError;
#else
            return request.isNetworkError || request.isHttpError;
#endif
        }

        private static string BuildRequestError(UnityWebRequest request)
        {
            var body = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (string.IsNullOrWhiteSpace(body))
            {
                return $"Google Drive request failed: {request.responseCode} {request.error}";
            }

            return $"Google Drive request failed: {request.responseCode} {request.error}\n{body}";
        }

        private static string SanitizeFileName(string name)
        {
            var safeName = string.IsNullOrWhiteSpace(name) ? "untitled" : name.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalid, '_');
            }

            return safeName;
        }

        private static string EnsureExtension(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(extension) || fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            return fileName + extension;
        }

        private static string ResolveFolderId(string folderIdOrUrl)
        {
            if (string.IsNullOrWhiteSpace(folderIdOrUrl))
            {
                return "";
            }

            var value = folderIdOrUrl.Trim();
            const string foldersMarker = "/folders/";
            var markerIndex = value.IndexOf(foldersMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return value;
            }

            var start = markerIndex + foldersMarker.Length;
            var end = value.IndexOfAny(new[] { '?', '/', '&', '#' }, start);
            return end < 0 ? value.Substring(start) : value.Substring(start, end - start);
        }

        private struct ExportInfo
        {
            public readonly bool canExport;
            public readonly string mimeType;
            public readonly string extension;

            public ExportInfo(bool canExport, string mimeType, string extension)
            {
                this.canExport = canExport;
                this.mimeType = mimeType;
                this.extension = extension;
            }
        }

        [Serializable]
        private sealed class DriveFileList
        {
            public string nextPageToken;
            public List<DriveFile> files;
        }

        [Serializable]
        private sealed class DriveFile
        {
            public string id;
            public string name;
            public string mimeType;
            public string modifiedTime;
            public string size;
            public string md5Checksum;

            public bool IsGoogleWorkspaceFile => !string.IsNullOrEmpty(mimeType)
                && mimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal);

            public bool TryGetSize(out long parsedSize)
            {
                return long.TryParse(size, out parsedSize);
            }
        }

        [Serializable]
        private sealed class SyncManifest
        {
            public List<ManifestEntry> files = new List<ManifestEntry>();
        }

        [Serializable]
        private sealed class ManifestEntry
        {
            public string driveId;
            public string localPath;
            public string modifiedTime;
            public string md5Checksum;
            public string exportMimeType;
        }
    }
}
