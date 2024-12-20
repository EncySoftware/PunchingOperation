using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using Nuke.Common;
using BuildSystem.Builder.Dotnet;
using BuildSystem.BuildSpace;
using BuildSystem.BuildSpace.Common;
using BuildSystem.Info;
using BuildSystem.Loggers;
using BuildSystem.Logging;
using BuildSystem.ManagerObject;
using BuildSystem.SettingsReader;
using BuildSystem.SettingsReader.Object;
using BuildSystem.Variants;
using LoggingLevel = BuildSystem.Logging.LogLevel;

// ReSharper disable AllUnderscoreLocalParameterName

/// <inheritdoc />
// ReSharper disable once CheckNamespace
public class Build : NukeBuild
{
    /// <summary>
    /// Calling target by default
    /// </summary>
    public static int Main() => Execute<Build>(x => x.Pack);
    
    /// <summary>
    /// Configuration to build - 'Debug' (default) or 'Release'
    /// </summary>
    [Parameter("Settings provided for running build space")]
    public readonly string Variant = "Debug";

    /// <summary>
    /// Logging object
    /// </summary>
    private readonly ILogger _logger;
    
    /// <summary>
    /// Main build space as manager over projects
    /// </summary>
    private readonly IBuildSpace _buildSpace;

    /// <summary>
    /// Build system
    /// </summary>
    public Build()
    { 
        _logger = InitLogger();
        _buildSpace = InitBuildSpace();        
    }
    
    private ILogger InitLogger() {
        // logging to console
        var console = new LoggerConsole();
        console.setMinLevel(LoggingLevel.info);
        
        // logging to file
        var file = new LoggerFile(Path.Combine(RootDirectory, "logs"), "log", 7);
        file.setMinLevel(LoggingLevel.debug);
        
        // singleton to transfer logs to all other loggers
        var logger = new LoggerBroadCaster();
        logger.Loggers.Add(file);
        logger.Loggers.Add(console);
        return logger;
    }

    private IBuildSpace InitBuildSpace()
    {
        BuildInfo.RunParams[RunInfo.Variant] = Variant;
        
        var settings = new SettingsObject
        {
            Projects = new HashSet<string>
            {
                Path.Combine(RootDirectory.Parent, "project", "main", ".stbuild", "PunchingOperationExtensionProject.json")
            },
            Variants = new VariantList
            {
                new()
                {
                    Name = "Debug",
                    Configurations = new Dictionary<string, string>
                    {
                        [BuildSystem.Variants.Variant.NodeConfig] = "Debug"
                    },
                    Platforms = new Dictionary<string, string>
                    {
                        [BuildSystem.Variants.Variant.NodePlatform] = "AnyCPU"
                    }
                },
                new()
                {
                    Name = "Release",
                    Configurations = new Dictionary<string, string>
                    {
                        [BuildSystem.Variants.Variant.NodeConfig] = "Release"
                    },
                    Platforms = new Dictionary<string, string>
                    {
                        [BuildSystem.Variants.Variant.NodePlatform] = "AnyCPU"
                    }
                }
            },
            ManagerProps = new List<IManagerProp>
            {
                new BuilderDotnetProps
                {
                    Name = "BuilderDotnet"
                }
            }
        };
        settings.ManagerNames.Add("builder", "Debug", "BuilderDotnet");
        settings.ManagerNames.Add("builder", "Release", "BuilderDotnet");
        
        var tempDir = Path.Combine(RootDirectory, "temp");
        return new BuildSpaceCommon(_logger, tempDir, SettingsReaderType.Object, settings);
    }

    /// <summary>
    /// Parameterized compile
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Compile => _ => _
        .Executes(() =>
        {
            _buildSpace.Projects.Compile(Variant, true);

            // copy xml and settings files, if we want to debug
            foreach (var project in _buildSpace.Projects)
            {
                var mainProjectFilePath = project.MainFilePath;
                if (mainProjectFilePath == null)
                    continue;
                var mainProjectFolder = Path.GetDirectoryName(mainProjectFilePath)
                                        ?? throw new Exception("Parent folder of main project file path is null");
                var dllPath = project.GetBuildResultPath(Variant, "dll")
                              ?? throw new Exception("Build results with dll type not found");
                var dllFolder = Path.GetDirectoryName(dllPath)
                                ?? throw new Exception("Parent folder of dll path is null");
                
                // copy settings file
                var jsonPath = Path.ChangeExtension(mainProjectFilePath, ".settings.json");
                if (!File.Exists(jsonPath))
                    throw new Exception("Settings file not found");
                File.Copy(jsonPath, Path.ChangeExtension(dllPath, ".settings.json"), true);
                
                // copy xml file
                var sourceXmlPath = Path.Combine(mainProjectFolder, "PunchingOperation_ExtOp.xml");
                if (!File.Exists(sourceXmlPath))
                    throw new Exception($"{sourceXmlPath} file not found");
                var targetXmlPath = Path.Combine(dllFolder, "PunchingOperation_ExtOp.xml");
                File.Copy(sourceXmlPath, targetXmlPath, true);
            }
        });

    /// <summary>
    /// Delete build results
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Clean => _ => _
        .Executes(() =>
        {
            _buildSpace.Projects.Clean(Variant);
        });

    /// <summary>
    /// Create .dext file, which can be injected
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    private Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var project in _buildSpace.Projects)
            {
                // path to dll (to be included into dext)
                var dllPath = project.GetBuildResultPath(Variant, "dll")
                              ?? throw new Exception("Build results with dll type not found");
                var dllFolder = Path.GetDirectoryName(dllPath)
                                ?? throw new Exception("Parent folder of dll path is null");

                // path to json, describing extension (to be included into dext)
                var jsonPath = Path.ChangeExtension(dllPath, ".settings.json");
                
                // additional files
                var xmlOperation = Path.Combine(dllFolder, "PunchingOperation_ExtOp.xml");

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
                archive.CreateEntryFromFile(xmlOperation, Path.GetFileName(xmlOperation));
                _logger.head($"Created dext file: {dextPath}");
            }
        });
}