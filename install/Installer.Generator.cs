using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using WixSharp;

namespace Installer;

public static partial class Generator
{
    /// <summary>
    ///     Generates Wix entities, features and directories for the installer.
    /// </summary>
    public static InstallerEntities GenerateWixEntities(string[] args)
    {
        var versionStorages = new Dictionary<string, List<WixEntity>>();
        var revitFeature = new Feature
        {
            Name = "Revit Add-in",
            Description = "Revit add-in installation files",
            Display = FeatureDisplay.expand
        };

        foreach (var directory in args)
        {
            var directoryInfo = new DirectoryInfo(directory);
            if (!TryParseVersion(directoryInfo.FullName, out var fileVersion))
            {
                throw new Exception($"Could not parse version from directory name: {directoryInfo.FullName}");
            }

            // Level 2 keeps the feature cleared; the condition drops it to the install level
            // once AppSearch reports Revit of this version present.
            var feature = new Feature
            {
                Name = fileVersion,
                Description = $"Install add-in for Revit {fileVersion}",
                ConfigurableDir = $"INSTALL{fileVersion}",
                AttributesDefinition = "Level=2",
                Condition = new FeatureCondition(RevitDetectionPropertyName(fileVersion), 1)
            };

            revitFeature.Add(feature);

            var files = new Files(feature, $@"{directory}\*.*");
            if (versionStorages.TryGetValue(fileVersion, out var storage))
            {
                storage.Add(files);
            }
            else
            {
                versionStorages.Add(fileVersion, [files]);
            }

            LogFeatureFiles(directory, fileVersion);
        }

        var directories = versionStorages
            .Select(storage => new Dir(new Id($"INSTALL{storage.Key}"), storage.Key, storage.Value.ToArray()))
            .Cast<WixEntity>()
            .ToArray();

        return new InstallerEntities(directories, versionStorages.Keys.ToArray());
    }

    /// <summary>
    ///     Builds the property name holding the Revit detection result for the given version.
    /// </summary>
    public static string RevitDetectionPropertyName(string version)
    {
        return $"REVIT{version}INSTALLED";
    }

    /// <summary>
    ///     Registry searches that set a property for every Revit version the package can install.
    ///     Revit writes <c>Installed</c> under the RCL subkey of its versioned key; the key exists
    ///     only in the 64-bit registry view.
    /// </summary>
    public static Property[] CreateRevitDetectionProperties(string[] versions)
    {
        return versions
            .Select(version => (Property) new RegValueProperty(
                RevitDetectionPropertyName(version),
                RegistryHive.LocalMachine,
                $@"SOFTWARE\Autodesk\Revit\{version}\RCL",
                "Installed",
                string.Empty))
            .ToArray();
    }

    /// <summary>
    ///     Parse a version string from the given input.
    /// </summary>
    private static bool TryParseVersion(string input, [NotNullWhen(true)] out string? version)
    {
        version = null;
        var match = VersionRegex().Match(input);
        if (!match.Success) return false;

        switch (match.Value.Length)
        {
            case 4:
                version = match.Value;
                return true;
            case 2:
                version = $"20{match.Value}";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///    Write a list of installer files.
    /// </summary>
    private static void LogFeatureFiles(string directory, string fileVersion)
    {
        var assemblies = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Console.WriteLine($"Installer files for version {fileVersion}:");

        foreach (var assembly in assemblies)
        {
            Console.WriteLine($"- {assembly}");
        }
    }

    /// <summary>
    ///     A regular expression to match the last sequence of numeric characters in a string.
    /// </summary>
    [GeneratedRegex(@"(\d+)(?!.*\d)")]
    private static partial Regex VersionRegex();
}
/// <summary>
///     Directories to install and the Revit versions they target.
/// </summary>
public sealed record InstallerEntities(WixEntity[] Directories, string[] Versions);
