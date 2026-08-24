using System;
using System.Collections.Generic;
using System.IO;
using BuildSystem;
using BuildSystem.Core.Builders.Dotnet;
using BuildSystem.Core.HashGenerator;
using BuildSystem.ManagerObject.Interfaces;
using BuildSystem.Core.PackageManager;
using BuildSystem.Core.ProjectCache;
using BuildSystem.Core.VersionManager;
using BuildSystem.ManagerObject.Interfaces.Package;
using BuildSystem.ManagerObject.Interfaces.Variants;
using BuildSystem.ProjectList;
using Logging;

/// <inheritdoc />
internal class BuildSpaceSettings : BuildSpaceSettingsCommon
{
    /// <inheritdoc />
    public BuildSpaceSettings(ILogger logger, string rootDirectory, string gitBranch)
        : base(logger, rootDirectory)
    {
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

        ProjectListProps = new ProjectListCommonProps(logger)
        {
            SetStorageInfo = SetStorageInfoFunc,
            GetNextVersion = GetNextVersion.FromRemotePackages
        };

        Variants =
        [
            new Variant
            {
                Name = "Debug",
                Configurations = new Dictionary<string, string> { [Variant.NodeConfig] = "Debug" },
                Platforms = new Dictionary<string, string> { [Variant.NodePlatform] = "AnyCPU" }
            },

            new Variant
            {
                Name = "Release",
                Configurations = new Dictionary<string, string> { [Variant.NodeConfig] = "Release" },
                Platforms = new Dictionary<string, string> { [Variant.NodePlatform] = "AnyCPU" }
            }
        ];

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
                TempDir = Path.Combine(rootDirectory, ".stbuild", "temp")
            }
        ];

        ManagerNames.Add("hash_generator", "Debug", "HashGeneratorCommon");
        ManagerNames.Add("hash_generator", "Release", "HashGeneratorCommon");
        ManagerNames.Add("builder", "Debug", "BuilderDotnet");
        ManagerNames.Add("builder", "Release", "BuilderDotnet");
        ManagerNames.Add("package_manager", "Debug", "PackageManagerDotnet");
        ManagerNames.Add("package_manager", "Release", "PackageManagerDotnet");
        ManagerNames.Add("version_manager", "Debug", "VersionManagerCommon");
        ManagerNames.Add("version_manager", "Release", "VersionManagerCommon");
        ManagerNames.Add("project_cache", "Debug", "ProjectCacheNuGet");
        ManagerNames.Add("project_cache", "Release", "ProjectCacheNuGet");
    }

    private static List<StorageInfo> SetStorageInfoFunc(PackageAction packageAction, string packageId, VersionProp? packageVersion)
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
}
