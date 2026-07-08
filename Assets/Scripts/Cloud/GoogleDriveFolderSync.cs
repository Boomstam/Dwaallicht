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
        private readonly Dictionary<string, ManifestEntry> manifestByDriveId = new Dictionary<string, ManifestEntry>();
        private readonly HashSet<string> seenRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private SyncManifest manifest = new SyncManifest();
        private int downloadedFiles;
        private int deletedFiles;
        private bool syncFailed;
        private bool syncIncomplete;

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
            syncIncomplete = false;
            downloadedFiles = 0;
            deletedFiles = 0;
            seenRelativePaths.Clear();

            var rootPath = GetLocalRootPath();
            Directory.CreateDirectory(rootPath);
            LoadManifest(rootPath);

            SetStatus("Starting Google Drive sync.");
            yield return SyncDriveFolder(resolvedFolderId, rootPath);

            if (!syncFailed)
            {
                if (!syncIncomplete)
                {
                    DeleteStaleManagedFiles(rootPath);
                    DeleteEmptyDirectories(rootPath);
                }

                if (syncFailed)
                {
                    IsSyncing = false;
                    SyncFinished?.Invoke(false);
                    yield break;
                }

                SaveManifest(rootPath);
                RefreshAssetDatabaseIfNeeded(rootPath);
                var incompleteSuffix = syncIncomplete ? " Stale-file deletion skipped because the run did not traverse the full Drive folder." : "";
                SetStatus($"Google Drive sync complete. Downloaded {downloadedFiles} file(s), deleted {deletedFiles} stale file(s) in {rootPath}.{incompleteSuffix}");
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
                    if (file == null || string.IsNullOrWhiteSpace(file.id))
                    {
                        continue;
                    }

                    if (IsDriveFolder(file))
                    {
                        continue;
                    }

                    if (downloadedFiles >= maxFilesPerRun)
                    {
                        syncIncomplete = true;
                        SetStatus($"Reached test sync limit of {maxFilesPerRun} file(s).");
                        yield break;
                    }

                    var safeName = SanitizeFileName(file.name);
                    yield return DownloadDriveFile(file, localFolderPath, safeName);
                    if (syncFailed)
                    {
                        yield break;
                    }
                }

                if (recursive)
                {
                    foreach (var file in page.files)
                    {
                        if (file == null || string.IsNullOrWhiteSpace(file.id) || !IsDriveFolder(file))
                        {
                            continue;
                        }

                        var safeName = SanitizeFileName(file.name);
                        yield return SyncDriveFolder(file.id, Path.Combine(localFolderPath, safeName));
                        if (syncFailed || syncIncomplete)
                        {
                            yield break;
                        }
                    }
                }

                nextPageToken = page.nextPageToken;
            }
            while (!string.IsNullOrEmpty(nextPageToken));
        }

        private static bool IsDriveFolder(DriveFile file)
        {
            return file != null && string.Equals(file.mimeType, GoogleFolderMimeType, StringComparison.Ordinal);
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
            PrepareManifestForRemoteFile(file, relativePath);
            if (syncFailed)
            {
                yield break;
            }

            if (!ShouldDownload(file, relativePath, localPath, export.mimeType))
            {
                MarkRemoteFileSynced(file, relativePath, export.mimeType);
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

            MarkRemoteFileSynced(file, relativePath, export.mimeType);
        }

        private void PrepareManifestForRemoteFile(DriveFile file, string relativePath)
        {
            if (manifestByDriveId.TryGetValue(file.id, out var previousEntry)
                && !string.Equals(previousEntry.localPath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!DeleteManagedLocalFile(previousEntry.localPath, "renamed"))
                {
                    return;
                }

                manifestByLocalPath.Remove(previousEntry.localPath);
            }

            if (manifestByLocalPath.TryGetValue(relativePath, out var pathEntry)
                && !string.Equals(pathEntry.driveId, file.id, StringComparison.Ordinal))
            {
                manifestByDriveId.Remove(pathEntry.driveId);
            }
        }

        private void MarkRemoteFileSynced(DriveFile file, string relativePath, string exportMimeType)
        {
            seenRelativePaths.Add(relativePath);
            manifestByLocalPath[relativePath] = new ManifestEntry
            {
                driveId = file.id,
                localPath = relativePath,
                modifiedTime = file.modifiedTime,
                md5Checksum = file.md5Checksum,
                exportMimeType = exportMimeType
            };
            manifestByDriveId[file.id] = manifestByLocalPath[relativePath];
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

            if (file.IsGoogleWorkspaceFile)
            {
                return false;
            }

            if (string.IsNullOrEmpty(file.md5Checksum) || string.IsNullOrEmpty(entry.md5Checksum))
            {
                return true;
            }

            return !string.Equals(entry.md5Checksum, file.md5Checksum, StringComparison.Ordinal);
        }

        private void LoadManifest(string rootPath)
        {
            manifestByLocalPath.Clear();
            manifestByDriveId.Clear();
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
                        if (!string.IsNullOrEmpty(entry.driveId))
                        {
                            manifestByDriveId[entry.driveId] = entry;
                        }
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

        private void DeleteStaleManagedFiles(string rootPath)
        {
            var stalePaths = new List<string>();
            foreach (var entry in manifestByLocalPath.Values)
            {
                if (entry == null || string.IsNullOrEmpty(entry.localPath))
                {
                    continue;
                }

                if (!seenRelativePaths.Contains(entry.localPath))
                {
                    stalePaths.Add(entry.localPath);
                }
            }

            foreach (var relativePath in stalePaths)
            {
                if (!DeleteManagedLocalFile(relativePath, "removed from Drive"))
                {
                    return;
                }

                if (manifestByLocalPath.TryGetValue(relativePath, out var entry) && entry != null)
                {
                    manifestByDriveId.Remove(entry.driveId);
                }

                manifestByLocalPath.Remove(relativePath);
            }
        }

        private bool DeleteManagedLocalFile(string relativePath, string reason)
        {
            var localPath = Path.Combine(GetLocalRootPath(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                return true;
            }

            try
            {
                File.Delete(localPath);
                deletedFiles++;
                SetStatus($"Deleted stale synced file ({reason}): {relativePath}.");
                return true;
            }
            catch (Exception ex)
            {
                Fail($"Could not delete stale synced file {relativePath}: {ex.Message}");
                return false;
            }
        }

        private void DeleteEmptyDirectories(string rootPath)
        {
            if (syncFailed || !Directory.Exists(rootPath))
            {
                return;
            }

            var directories = new List<string>(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories));
            directories.Sort((left, right) => right.Length.CompareTo(left.Length));

            foreach (var directory in directories)
            {
                TryDeleteEmptyDirectory(directory);
            }
        }

        private void TryDeleteEmptyDirectory(string directory)
        {
            if (syncFailed || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex)
            {
                Fail($"Could not delete empty synced folder {directory}: {ex.Message}");
            }
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
