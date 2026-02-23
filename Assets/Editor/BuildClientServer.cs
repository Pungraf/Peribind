using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildClientServer
{
    private const string ClientBuildDir = "Build/Client";
    private const string ServerBuildDir = "Build/Server";
    private const string ServerBuildLinuxDir = "Build/ServerLinux";

    private static readonly string[] ClientScenes =
    {
        "Assets/Scenes/SplashScreenScene.unity",
        "Assets/Scenes/LoginScene.unity",
        "Assets/Scenes/StarterScene.unity",
        "Assets/Scenes/PlayerProfileScene.unity",
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

        RecreateOutputDirectory(ClientBuildDir);

        var output = EnsureOutputPath(ClientBuildDir, target, "PeribindClient");
        var options = new BuildPlayerOptions
        {
            scenes = ClientScenes,
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        };

#if UNITY_2021_2_OR_NEWER
        // Explicitly force Player subtarget so client builds do not inherit dedicated-server stripping.
        options.subtarget = (int)StandaloneBuildSubtarget.Player;
#endif

        var report = BuildPipeline.BuildPlayer(options);

        LogResult(report, "Client");
        WriteRunWithLogScript(output, "Client");
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
        WriteRunWithLogScript(output, "Server");
    }

    [MenuItem("Build/Build Server (Linux64)")]
    public static void BuildServerLinux()
    {
        const BuildTarget target = BuildTarget.StandaloneLinux64;
        var output = EnsureOutputPath(ServerBuildLinuxDir, target, "PeribindServer");
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
        LogResult(report, "Server (Linux64)");
    }

    [MenuItem("Build/Build Client + Server")]
    public static void BuildClientAndServer()
    {
        BuildClient();
        BuildServer();
    }

    [MenuItem("Build/Build Client + Server (Linux64)")]
    public static void BuildClientAndServerLinux()
    {
        BuildClient();
        BuildServerLinux();
    }

    private static string EnsureOutputPath(string baseDir, BuildTarget target, string baseName)
    {
        Directory.CreateDirectory(baseDir);

        if (target == BuildTarget.StandaloneOSX)
        {
            return Path.Combine(baseDir, $"{baseName}.app");
        }

        if (target == BuildTarget.StandaloneLinux64)
        {
            return Path.Combine(baseDir, baseName);
        }

        return Path.Combine(baseDir, $"{baseName}.exe");
    }

    private static void RecreateOutputDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
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

    private static void WriteRunWithLogScript(string outputPath, string label)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            return;
        }

        var exeName = Path.GetFileName(outputPath);
        var exeDir = Path.GetDirectoryName(outputPath) ?? ".";
        var scriptPath = Path.Combine(exeDir, $"Run{label}WithLog.bat");
        var logFileName = $"{label}.log";

        var contents = "@echo off\r\n" +
                       "setlocal\r\n" +
                       $"\"%~dp0{exeName}\" -logFile \"%~dp0{logFileName}\"\r\n" +
                       "endlocal\r\n";

        File.WriteAllText(scriptPath, contents);
    }
}
