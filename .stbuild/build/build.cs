using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using BuildSystem;
using BuildSystem.Core.Builders.Dotnet;
using BuildSystem.Core.HashGenerator;
using BuildSystem.Core.PackageManager;
using BuildSystem.Core.ProjectCache;
using BuildSystem.Core.VersionManager;
using Nuke.Common;
using BuildSystem.Info;
using BuildSystem.ManagerObject.Interfaces;
using BuildSystem.ManagerObject.Interfaces.Package;
using BuildSystem.ManagerObject.Interfaces.Variants;
using BuildSystem.ProjectList;
using Loggers;
using Logging;
using Nuke.Common.Utilities.Collections;
using LogLevel = Logging.LogLevel;

// ReSharper disable AllUnderscoreLocalParameterName

/// <inheritdoc />
// ReSharper disable once CheckNamespace
public class Build : NukeBuild
{
    /// <summary>
    /// Calling target by default
    /// </summary>
    public static int Main()
    {
        var parentDirectory = new DirectoryInfo(EnvironmentInfo.WorkingDirectory)
            .DescendantsAndSelf(x => x.Parent ?? throw new Exception("Parent directory is null for " + x.FullName))
            .First(x => x.GetDirectories(".stbuild").Any())
            .FullName;
        Environment.SetEnvironmentVariable("root", Path.Combine(parentDirectory, ".stbuild"));
        return Execute<Build>(x => x.Pack);
    }

    /// <summary>
    /// Configuration to build - 'Debug' (default) or 'Release'
    /// </summary>
    [Parameter("Settings provided for running build space")]
    public readonly string Variant = "Debug";

    /// <summary>
    /// Logging object
    /// </summary>
    private ILogger? _logger;
    private ILogger Logger => _logger ??= InitLogger();

    /// <summary>
    /// Main build space as manager over projects
    /// </summary>
    private IBuildSpace? _buildSpace;
    private IBuildSpace BuildSpace => _buildSpace ??= InitBuildSpace();
    private string GitBranch => Environment.GetEnvironmentVariable("GITHUB_REF_NAME") + "";
    
    private ILogger InitLogger() {
        // logging to console
        var console = new LoggerConsole();
        console.setMinLevel(LogLevel.info);
        
        // logging to file
        var file = new LoggerFile(Path.Combine(RootDirectory, "logs"), "log", 7);
        file.setMinLevel(LogLevel.debug);
        
        // singleton to transfer logs to all other loggers
        var logger = new LoggerBroadCaster();
        logger.Loggers.Add(file);
        logger.Loggers.Add(console);
        return logger;
    }

    private IBuildSpace InitBuildSpace()
    {
        BuildInfo.RunParams[RunInfo.Variant] = Variant;
        var gitBranch = GitBranch;

        var versionManagerProps = new VersionManagerCommonProps
        {
            Name = "VersionManagerCommon",
            DepthSearch = 2,
            DevelopBranchName = gitBranch.EndsWith("develop", StringComparison.OrdinalIgnoreCase)
                ? gitBranch
                : "develop",
            MasterBranchName = gitBranch.EndsWith("main", StringComparison.OrdinalIgnoreCase)
                ? gitBranch
                : "main",
            ReleaseBranchName = gitBranch.EndsWith("release", StringComparison.OrdinalIgnoreCase)
                ? gitBranch
                : "release"
        };
        var packageManagerProps = new PackageManagerDotnetProps
        {
            Name = "PackageManagerDotnet",
            SetStorageInfo = SetStorageInfoFunc
        };

        var settings = new SettingsObject
        {
            Projects =
            [
                Path.Combine(RootDirectory.Parent, "project", "main", ".stbuild",
                    "PunchingOperationExtensionProject.json")
            ],
            ProjectListProps = new ProjectListCommonProps(Logger)
            {
                SetStorageInfo = SetStorageInfoFunc
            },
            Variants =
            [
                new Variant
                {
                    Name = "Debug",
                    Configurations = new Dictionary<string, string>
                    {
                        [BuildSystem.ManagerObject.Interfaces.Variants.Variant.NodeConfig] = "Debug"
                    },
                    Platforms = new Dictionary<string, string>
                    {
                        [BuildSystem.ManagerObject.Interfaces.Variants.Variant.NodePlatform] = "AnyCPU"
                    }
                },

                new Variant
                {
                    Name = "Release",
                    Configurations = new Dictionary<string, string>
                    {
                        [BuildSystem.ManagerObject.Interfaces.Variants.Variant.NodeConfig] = "Release"
                    },
                    Platforms = new Dictionary<string, string>
                    {
                        [BuildSystem.ManagerObject.Interfaces.Variants.Variant.NodePlatform] = "AnyCPU"
                    }
                }
            ],
            ManagerProps =
            [
                new BuilderDotnetProps
                {
                    Name = "BuilderDotnet"
                },
                packageManagerProps,
                versionManagerProps,
                new HashGeneratorCommonProps
                {
                    Name = "HashGeneratorCommon",
                    HashAlgorithmType = HashAlgorithmType.Sha256
                },
                new ProjectCacheNuGetProps
                {
                    Name = "ProjectCacheNuGet",
                    VersionManagerProps = versionManagerProps,
                    PackageManagerProps = packageManagerProps,
                    TempDir = Path.Combine(RootDirectory, "temp")
                }
            ]
        };
        settings.ManagerNames.Add("hash_generator", "Debug", "HashGeneratorCommon");
        settings.ManagerNames.Add("hash_generator", "Release", "HashGeneratorCommon");
        settings.ManagerNames.Add("builder", "Debug", "BuilderDotnet");
        settings.ManagerNames.Add("builder", "Release", "BuilderDotnet");
        settings.ManagerNames.Add("package_manager", "Release", "PackageManagerDotnet");
        settings.ManagerNames.Add("version_manager", "Release", "VersionManagerCommon");
        settings.ManagerNames.Add("project_cache", "Release", "ProjectCacheNuGet");
        settings.ReaderLocalVars = new Dictionary<string, string?>
        {
            ["package_namespace"] = "EncySoftware"
        };

        var tempDir = Path.Combine(RootDirectory, "temp");
        return new BuildSpaceCommon(Logger, tempDir, SettingsReaderType.Object, settings);
    }
    
    private List<StorageInfo> SetStorageInfoFunc(PackageAction packageAction, string packageId, VersionProp? packageVersion)
    {
        // add the main feed anyway
        var result = new List<StorageInfo>
        {
            new()
            {
                Url = Environment.GetEnvironmentVariable("NUGET_FEED_URL")
                      ?? throw new Exception("Environment variable NUGET_FEED_URL is not set"),
                ApiKey = Environment.GetEnvironmentVariable("NUGET_AUTH_TOKEN") ?? ""
            }
        };
        
        // for search purposes add other feeds
        if (packageAction != PackageAction.Push)
        {
            result.Add(new StorageInfo
            {
                Url = "https://nexus.encycam.com/repository/master/index.json"
            });
        }
        
        // result
        return result;
    }

    /// <summary>
    /// Parameterized compile
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Compile => _ => _
        .Executes(() =>
        {
            BuildSpace.Projects.Compile(Variant, true);

            // copy settings file, if we want to debug
            foreach (var project in BuildSpace.Projects.List.All())
            {
                var mainProjectFilePath = project.MainFilePath;
                if (mainProjectFilePath == null)
                    continue;

                var dllPath = project.GetBuildResultPath(Variant, "dll")
                              ?? throw new Exception("Build results with dll type not found");
                var jsonPath = Path.ChangeExtension(mainProjectFilePath, ".settings.json");
                if (!File.Exists(jsonPath))
                    throw new Exception("Settings file not found");

                File.Copy(jsonPath, Path.ChangeExtension(dllPath, ".settings.json"), true);
            }
        });

    /// <summary>
    /// Delete build results
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Clean => _ => _
        .Executes(() =>
        {
            BuildSpace.Projects.Clean("Debug");
            BuildSpace.Projects.Clean("Release");
        });
        
    /// <summary>
    /// Removes all temporary files
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target CleanAll => _ => _
        .Description("Full clean - removes all temporary files")
        .DependsOn(Clean)
        .Executes(() =>
        {
            var tempDirectories = new[]
            {
                RootDirectory.Parent / "bin",
                RootDirectory.Parent / "obj",
                RootDirectory.Parent / "temp",
                RootDirectory.Parent / ".stbuild" / "temp",
                RootDirectory.Parent / ".stbuild" / ".nuke" / "temp",
                RootDirectory.Parent / ".stbuild" / "build" / "bin",
                RootDirectory.Parent / ".stbuild" / "build" / "obj",
                RootDirectory.Parent / "project" / "main" / "bin",
                RootDirectory.Parent / "project" / "main" / "obj"
            };

            foreach (var dirPath in tempDirectories)
            {
                string dir = dirPath.ToString(); 
                
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        Logger.head($"✅  Successfully deleted: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Logger.head($"⚠️  Could not delete {dir}: {ex.Message}");
                    }
                }
            }
        });

    /// <summary>
    /// Create .dext file, which can be injected
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var project in BuildSpace.Projects.List.All())
            {
                // path to dll (to be included into dext)
                var dllPath = project.GetBuildResultPath(Variant, "dll")
                              ?? throw new Exception("Build results with dll type not found");

                // path to json, describing extension (to be included into dext)
                var jsonPath = Path.ChangeExtension(dllPath, ".settings.json");

                // make new dext
                var outputFolder = Path.GetDirectoryName(dllPath)
                                   ?? throw new Exception("Parent folder of dll path is null");
                var dextPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(dllPath) + ".dext");
                if (File.Exists(dextPath))
                    File.Delete(dextPath);

                using var zipToOpen = new FileStream(dextPath, FileMode.Create);
                using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Update);
                archive.CreateEntryFromFile(dllPath, Path.GetFileName(dllPath));
                archive.CreateEntryFromFile(jsonPath, Path.GetFileName(jsonPath));
                Logger.head($"Created dext file: {dextPath}");
            }
        });
    
    
    /// <summary>
    /// Push packages to the NuGet feed
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Push => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            _buildSpace?.Projects.Deploy(Variant);
        });
}