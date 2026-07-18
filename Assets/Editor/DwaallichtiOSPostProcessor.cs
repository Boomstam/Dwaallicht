using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

/// <summary>
/// Post-processes generated iOS builds to inject localized Info.plist usage
/// descriptions and required location-related Xcode settings.
/// </summary>
public static class DwaallichtiOSPostProcessor
{
    private static readonly Dictionary<string, Dictionary<string, string>> LocalizedUsageStrings =
        new Dictionary<string, Dictionary<string, string>>
    {
        {
            "nl", new Dictionary<string, string>
            {
                { "NSLocationWhenInUseUsageDescription", "Dwaallicht gebruikt je locatie om nabijgelegen bezienswaardigheden te tonen." },
                { "NSLocationAlwaysAndWhenInUseUsageDescription", "Dwaallicht gebruikt je locatie, ook op de achtergrond, om je te laten weten wanneer je in de buurt van een bezienswaardigheid bent." },
                { "NSLocationAlwaysUsageDescription", "Dwaallicht gebruikt je locatie, ook op de achtergrond, om je te laten weten wanneer je in de buurt van een bezienswaardigheid bent." },
                { "NSCameraUsageDescription", "Dwaallicht gebruikt de camera om bezienswaardigheden in augmented reality te tonen." },
                { "NSMotionUsageDescription", "Dwaallicht gebruikt bewegingssensoren om de kompasrichting van je telefoon te bepalen." }
            }
        },
        {
            "en", new Dictionary<string, string>
            {
                { "NSLocationWhenInUseUsageDescription", "Dwaallicht uses your location to show nearby points of interest." },
                { "NSLocationAlwaysAndWhenInUseUsageDescription", "Dwaallicht uses your location, including in the background, to let you know when you're near a point of interest." },
                { "NSLocationAlwaysUsageDescription", "Dwaallicht uses your location, including in the background, to let you know when you're near a point of interest." },
                { "NSCameraUsageDescription", "Dwaallicht uses the camera to show points of interest in augmented reality." },
                { "NSMotionUsageDescription", "Dwaallicht uses motion sensors to determine your phone's compass heading." }
            }
        }
    };

    private const string DevelopmentRegion = "nl";
    private const bool EnableBackgroundLocation = true;

    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
    {
#if UNITY_IOS
        if (buildTarget != BuildTarget.iOS)
        {
            return;
        }

        UpdateInfoPlist(pathToBuiltProject);
        AddLocalizedStringsFiles(pathToBuiltProject);
        LinkRequiredFrameworks(pathToBuiltProject);

        UnityEngine.Debug.Log("[DwaallichtiOSPostProcessor] Applied localized Info.plist strings and location build settings.");
#endif
    }

#if UNITY_IOS
    private static void UpdateInfoPlist(string pathToBuiltProject)
    {
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        PlistElementDict root = plist.root;
        foreach (var kvp in LocalizedUsageStrings[DevelopmentRegion])
        {
            root.SetString(kvp.Key, kvp.Value);
        }

        SetLocalizationArray(root);
        root.SetString("CFBundleDevelopmentRegion", DevelopmentRegion);

        if (EnableBackgroundLocation)
        {
            AddBackgroundLocationMode(root);
        }

        plist.WriteToFile(plistPath);
    }

    private static void SetLocalizationArray(PlistElementDict root)
    {
        if (root.values.ContainsKey("CFBundleLocalizations"))
        {
            root.values.Remove("CFBundleLocalizations");
        }

        PlistElementArray localizations = root.CreateArray("CFBundleLocalizations");
        foreach (var language in LocalizedUsageStrings.Keys)
        {
            localizations.AddString(language);
        }
    }

    private static void AddBackgroundLocationMode(PlistElementDict root)
    {
        PlistElementArray backgroundModes = root.values.ContainsKey("UIBackgroundModes")
            ? root.values["UIBackgroundModes"].AsArray()
            : root.CreateArray("UIBackgroundModes");

        foreach (PlistElement element in backgroundModes.values)
        {
            if (element.AsString() == "location")
            {
                return;
            }
        }

        backgroundModes.AddString("location");
    }

    private static void AddLocalizedStringsFiles(string pathToBuiltProject)
    {
        string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var project = new PBXProject();
        project.ReadFromFile(pbxPath);

        string mainTargetGuid = project.GetUnityMainTargetGuid();

        foreach (var language in LocalizedUsageStrings.Keys)
        {
            string lprojDir = Path.Combine(pathToBuiltProject, language + ".lproj");
            Directory.CreateDirectory(lprojDir);

            string fileSystemPath = Path.Combine(lprojDir, "InfoPlist.strings");
            WriteInfoPlistStringsFile(fileSystemPath, LocalizedUsageStrings[language]);

            string projectPath = language + ".lproj/InfoPlist.strings";
            string fileGuid = project.AddFile(fileSystemPath, projectPath, PBXSourceTree.Source);
            project.AddFileToBuild(mainTargetGuid, fileGuid);
        }

        project.WriteToFile(pbxPath);
    }

    private static void WriteInfoPlistStringsFile(string path, Dictionary<string, string> values)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var kvp in values)
        {
            string escapedValue = kvp.Value.Replace("\"", "\\\"");
            writer.WriteLine($"\"{kvp.Key}\" = \"{escapedValue}\";");
        }
    }

    private static void LinkRequiredFrameworks(string pathToBuiltProject)
    {
        string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var project = new PBXProject();
        project.ReadFromFile(pbxPath);

        project.AddFrameworkToProject(project.GetUnityMainTargetGuid(), "CoreLocation.framework", false);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "CoreLocation.framework", false);

        project.WriteToFile(pbxPath);
    }
#endif
}
