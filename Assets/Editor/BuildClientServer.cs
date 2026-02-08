using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildClientServer
{
    private const string ClientBuildDir = "Build/Client";
    private const string ServerBuildDir = "Build/Server";

    private static readonly string[] ClientScenes =
    {
        "Assets/Scenes/StarterScene.unity",
        "Assets/Scenes/LobbyScene.unity",
        "Assets/Scenes/GameScene.unity"
    };

    private static readonly string[] ServerScenes =
    {
        "Assets/Scenes/BootScene.unity",
        "Assets/Scenes/GameScene.unity"
    };

    [MenuItem("Build/Build Client")]
    public static void BuildClient()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows64 &&
            target != BuildTarget.StandaloneLinux64 &&
            target != BuildTarget.StandaloneOSX)
        {
            target = BuildTarget.StandaloneWindows64;
        }

        var output = EnsureOutputPath(ClientBuildDir, target, "PeribindClient");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = ClientScenes,
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });

        LogResult(report, "Client");
    }

    [MenuItem("Build/Build Server")]
    public static void BuildServer()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows64 &&
            target != BuildTarget.StandaloneLinux64 &&
            target != BuildTarget.StandaloneOSX)
        {
            target = BuildTarget.StandaloneWindows64;
        }

        var output = EnsureOutputPath(ServerBuildDir, target, "PeribindServer");
        var options = new BuildPlayerOptions
        {
            scenes = ServerScenes,
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        };

#if UNITY_2021_2_OR_NEWER
        options.subtarget = (int)StandaloneBuildSubtarget.Server;
#endif

        var report = BuildPipeline.BuildPlayer(options);
        LogResult(report, "Server");
    }

    [MenuItem("Build/Build Client + Server")]
    public static void BuildClientAndServer()
    {
        BuildClient();
        BuildServer();
    }

    private static string EnsureOutputPath(string baseDir, BuildTarget target, string baseName)
    {
        Directory.CreateDirectory(baseDir);

        if (target == BuildTarget.StandaloneOSX)
        {
            return Path.Combine(baseDir, $"{baseName}.app");
        }

        return Path.Combine(baseDir, $"{baseName}.exe");
    }

    private static void LogResult(BuildReport report, string label)
    {
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] {label} build succeeded: {report.summary.outputPath}");
        }
        else
        {
            Debug.LogError($"[Build] {label} build failed: {report.summary.result}");
        }
    }
}
