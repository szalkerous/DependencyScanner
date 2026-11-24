using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DependencyScanner
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: DependencyScanner.exe <solution-folder-path>");
                return;
            }

            string solutionFolder = args[0];

            if (!Directory.Exists(solutionFolder))
            {
                Console.WriteLine($"Error: Directory '{solutionFolder}' does not exist.");
                return;
            }

            Console.WriteLine($"Scanning solution folder: {solutionFolder}");
            Console.WriteLine();

            var scanner = new DependencyScanner(solutionFolder);
            var result = scanner.ScanAndConsolidate();

            // Display consolidated dependencies (newest versions)
            Console.WriteLine("Consolidated Dependencies (Newest Versions):");
            Console.WriteLine(new string('=', 80));

            foreach (var dep in result.ConsolidatedDependencies.OrderBy(d => d.Key))
            {
                Console.WriteLine($"{dep.Key}");
                Console.WriteLine($"  Version: {dep.Value.Version}");
                Console.WriteLine($"  Path: {dep.Value.Path}");
                Console.WriteLine();
            }

            Console.WriteLine($"Total unique dependencies: {result.ConsolidatedDependencies.Count}");
            Console.WriteLine();

            // Display duplicates
            if (result.Duplicates.Any())
            {
                Console.WriteLine("DUPLICATE DEPENDENCIES DETECTED:");
                Console.WriteLine(new string('=', 80));
                Console.WriteLine("The following dependencies exist in multiple versions:");
                Console.WriteLine();

                foreach (var duplicate in result.Duplicates.OrderBy(d => d.Key))
                {
                    Console.WriteLine($"{duplicate.Key} - Found {duplicate.Value.Count} version(s):");

                    foreach (var version in duplicate.Value.OrderByDescending(v => v.Version))
                    {
                        Console.WriteLine($"  Version: {version.Version}");
                        Console.WriteLine($"  Path: {version.Path}");
                        Console.WriteLine($"  Found in: {version.FoundInProjects.Count} project(s)");
                        foreach (var project in version.FoundInProjects)
                        {
                            Console.WriteLine($"    - {project}");
                        }
                        Console.WriteLine();
                    }
                }

                Console.WriteLine($"Total dependencies with multiple versions: {result.Duplicates.Count}");
                Console.WriteLine();

                // Conflict Resolution Suggestions
                var resolver = new ConflictResolver();
                var resolutions = resolver.GenerateResolutions(result.Duplicates);

                Console.WriteLine("CONFLICT RESOLUTION RECOMMENDATIONS:");
                Console.WriteLine(new string('=', 80));
                Console.WriteLine();

                foreach (var resolution in resolutions.OrderBy(r => r.Severity).ThenBy(r => r.DependencyName))
                {
                    DisplayResolution(resolution);
                }

                // Summary
                Console.WriteLine();
                Console.WriteLine("SUMMARY:");
                Console.WriteLine(new string('-', 80));
                Console.WriteLine($"Critical conflicts (major version differences): {resolutions.Count(r => r.Severity == ConflictSeverity.Critical)}");
                Console.WriteLine($"Moderate conflicts (minor version differences): {resolutions.Count(r => r.Severity == ConflictSeverity.Moderate)}");
                Console.WriteLine($"Low conflicts (patch version differences): {resolutions.Count(r => r.Severity == ConflictSeverity.Low)}");
            }
            else
            {
                Console.WriteLine("No duplicate dependencies found.");
            }
        }

        static void DisplayResolution(ConflictResolution resolution)
        {
            string severityIcon;

            // Color-code by severity (if console supports it)
            switch (resolution.Severity)
            {
                case ConflictSeverity.Critical:
                    severityIcon = "[CRITICAL]";
                    break;
                case ConflictSeverity.Moderate:
                    severityIcon = "[MODERATE]";
                    break;
                case ConflictSeverity.Low:
                    severityIcon = "[LOW]";
                    break;
                default:
                    severityIcon = "[INFO]";
                    break;
            }

            Console.WriteLine($"{severityIcon} {resolution.DependencyName}");
            Console.WriteLine($"Severity: {resolution.Severity}");
            Console.WriteLine($"Reason: {resolution.Reason}");
            Console.WriteLine();
            Console.WriteLine($"RECOMMENDATION: {resolution.Recommendation}");
            Console.WriteLine($"Suggested Version: {resolution.SuggestedVersion}");
            Console.WriteLine($"Confidence: {resolution.Confidence}%");
            Console.WriteLine();

            if (resolution.ActionItems.Any())
            {
                Console.WriteLine("Action Items:");
                for (int i = 0; i < resolution.ActionItems.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {resolution.ActionItems[i]}");
                }
            }

            if (!string.IsNullOrEmpty(resolution.BindingRedirect))
            {
                Console.WriteLine();
                Console.WriteLine("Suggested Binding Redirect (add to app.config):");
                Console.WriteLine(resolution.BindingRedirect);
            }

            if (resolution.Risks.Any())
            {
                Console.WriteLine();
                Console.WriteLine("Potential Risks:");
                foreach (var risk in resolution.Risks)
                {
                    Console.WriteLine($"  ⚠ {risk}");
                }
            }

            Console.WriteLine();
            Console.WriteLine(new string('-', 80));
            Console.WriteLine();
        }
    }

    public enum ConflictSeverity
    {
        Low = 0,
        Moderate = 1,
        Critical = 2
    }

    public class ConflictResolution
    {
        public string DependencyName { get; set; }
        public ConflictSeverity Severity { get; set; }
        public string Reason { get; set; }
        public string Recommendation { get; set; }
        public Version SuggestedVersion { get; set; }
        public int Confidence { get; set; }
        public List<string> ActionItems { get; set; } = new List<string>();
        public List<string> Risks { get; set; } = new List<string>();
        public string BindingRedirect { get; set; }
    }

    public class ConflictResolver
    {
        public List<ConflictResolution> GenerateResolutions(Dictionary<string, List<DependencyInfo>> duplicates)
        {
            var resolutions = new List<ConflictResolution>();

            foreach (var duplicate in duplicates)
            {
                var resolution = AnalyzeConflict(duplicate.Key, duplicate.Value);
                resolutions.Add(resolution);
            }

            return resolutions;
        }

        private ConflictResolution AnalyzeConflict(string dependencyName, List<DependencyInfo> versions)
        {
            var resolution = new ConflictResolution
            {
                DependencyName = dependencyName
            };

            // Sort versions from highest to lowest
            var sortedVersions = versions
                .Where(v => v.Version != null)
                .OrderByDescending(v => v.Version)
                .ToList();

            if (!sortedVersions.Any())
            {
                resolution.Severity = ConflictSeverity.Low;
                resolution.Reason = "Unable to determine version information";
                resolution.Recommendation = "Manual review required";
                resolution.Confidence = 0;
                return resolution;
            }

            var newestVersion = sortedVersions.First();
            var oldestVersion = sortedVersions.Last();

            // Determine severity based on version differences
            var majorDiff = newestVersion.Version.Major - oldestVersion.Version.Major;
            var minorDiff = newestVersion.Version.Minor - oldestVersion.Version.Minor;

            if (majorDiff > 0)
            {
                resolution.Severity = ConflictSeverity.Critical;
                resolution.Reason = $"Major version difference detected ({oldestVersion.Version} to {newestVersion.Version})";
                resolution.Risks.Add("Breaking API changes likely between major versions");
                resolution.Risks.Add("Incompatible behavior may cause runtime errors");
                resolution.Confidence = 60; // Lower confidence due to breaking changes risk
            }
            else if (minorDiff > 2)
            {
                resolution.Severity = ConflictSeverity.Moderate;
                resolution.Reason = $"Significant minor version difference ({oldestVersion.Version} to {newestVersion.Version})";
                resolution.Risks.Add("API additions may exist in newer versions");
                resolution.Risks.Add("Deprecated features may have been removed");
                resolution.Confidence = 75;
            }
            else if (minorDiff > 0)
            {
                resolution.Severity = ConflictSeverity.Moderate;
                resolution.Reason = $"Minor version difference detected ({oldestVersion.Version} to {newestVersion.Version})";
                resolution.Confidence = 85;
            }
            else
            {
                resolution.Severity = ConflictSeverity.Low;
                resolution.Reason = $"Patch version difference only ({oldestVersion.Version} to {newestVersion.Version})";
                resolution.Confidence = 95; // High confidence - patch versions should be safe
            }

            // Determine recommendation based on usage patterns
            var totalProjects = sortedVersions.Sum(v => v.FoundInProjects.Count);
            var newestUsageCount = newestVersion.FoundInProjects.Count;
            var newestUsagePercent = (newestUsageCount * 100) / totalProjects;

            if (newestUsagePercent >= 70)
            {
                resolution.Recommendation = $"Standardize on newest version {newestVersion.Version} (already used by {newestUsagePercent}% of projects)";
                resolution.SuggestedVersion = newestVersion.Version;

                // Generate action items for minority projects
                foreach (var oldVersion in sortedVersions.Skip(1))
                {
                    foreach (var project in oldVersion.FoundInProjects)
                    {
                        resolution.ActionItems.Add($"Update {project} from v{oldVersion.Version} to v{newestVersion.Version} (Conflict Path: {oldVersion.Path})");
                    }
                }
            }
            else if (newestUsagePercent <= 30)
            {
                // Majority using older version - might want to stay with it
                var mostUsedVersion = sortedVersions.OrderByDescending(v => v.FoundInProjects.Count).First();

                if (resolution.Severity == ConflictSeverity.Critical)
                {
                    resolution.Recommendation = $"Carefully evaluate upgrading to {newestVersion.Version} OR standardize on {mostUsedVersion.Version} (most widely used)";
                    resolution.SuggestedVersion = newestVersion.Version;
                    resolution.Risks.Add($"Most projects ({mostUsedVersion.FoundInProjects.Count}) use v{mostUsedVersion.Version}");
                    resolution.Risks.Add("Upgrading all projects may require significant testing");
                }
                else
                {
                    resolution.Recommendation = $"Standardize on newest version {newestVersion.Version} for bug fixes and security patches";
                    resolution.SuggestedVersion = newestVersion.Version;
                }

                foreach (var oldVersion in sortedVersions.Where(v => v.Version != newestVersion.Version))
                {
                    foreach (var project in oldVersion.FoundInProjects)
                    {
                        resolution.ActionItems.Add($"Update {project} from v{oldVersion.Version} to v{newestVersion.Version} (Conflict Path: {oldVersion.Path})");
                    }
                }
            }
            else
            {
                // Mixed usage
                resolution.Recommendation = $"Standardize on newest version {newestVersion.Version}";
                resolution.SuggestedVersion = newestVersion.Version;

                foreach (var oldVersion in sortedVersions.Skip(1))
                {
                    foreach (var project in oldVersion.FoundInProjects)
                    {
                        resolution.ActionItems.Add($"Update {project} from v{oldVersion.Version} to v{newestVersion.Version} (Conflict Path: {oldVersion.Path})");
                    }
                }
            }

            // Generate binding redirect
            resolution.BindingRedirect = GenerateBindingRedirect(dependencyName, sortedVersions, newestVersion.Version);

            // Add additional action items
            if (resolution.Severity == ConflictSeverity.Critical)
            {
                resolution.ActionItems.Add("Review release notes for breaking changes");
                resolution.ActionItems.Add("Run comprehensive integration tests after upgrade");
                resolution.ActionItems.Add("Consider creating a feature branch for this upgrade");
            }

            resolution.ActionItems.Add("Add binding redirect to all app.config/web.config files");
            resolution.ActionItems.Add("Rebuild entire solution after updating references");
            resolution.ActionItems.Add("Clear bin/obj folders before rebuilding");

            return resolution;
        }

        private string GenerateBindingRedirect(string assemblyName, List<DependencyInfo> versions, Version targetVersion)
        {
            var oldestVersion = versions.Last().Version;
            var newestVersion = versions.First().Version;

            // Try to get public key token from one of the assemblies
            string publicKeyToken = "null";
            try
            {
                var dllPath = versions.First().Path;
                if (File.Exists(dllPath) && !dllPath.Contains("[PackageReference]"))
                {
                    var asmName = AssemblyName.GetAssemblyName(dllPath);
                    var token = asmName.GetPublicKeyToken();
                    if (token != null && token.Length > 0)
                    {
                        publicKeyToken = BitConverter.ToString(token).Replace("-", "").ToLower();
                    }
                }
            }
            catch
            {
                // If we can't get the token, use null
            }

            return $@"<dependentAssembly>
  <assemblyIdentity name=""{assemblyName}"" publicKeyToken=""{publicKeyToken}"" culture=""neutral"" />
  <bindingRedirect oldVersion=""0.0.0.0-{newestVersion}"" newVersion=""{targetVersion}"" />
</dependentAssembly>";
        }
    }

    public class DependencyInfo
    {
        public string Name { get; set; }
        public Version Version { get; set; }
        public string Path { get; set; }
        public HashSet<string> FoundInProjects { get; set; } = new HashSet<string>();
        public DependencySource Source { get; set; }
    }

    public enum DependencySource
    {
        BinFolder,
        PackagesConfig,
        PackageReference
    }

    public class ScanResult
    {
        public Dictionary<string, DependencyInfo> ConsolidatedDependencies { get; set; }
        public Dictionary<string, List<DependencyInfo>> Duplicates { get; set; }
        public Dictionary<string, List<DependencyInfo>> AllDependencies { get; set; }
    }

    public class DependencyScanner
    {
        private readonly string _solutionFolder;
        private readonly string[] _buildConfigs = { "Debug", "Release" };
        private readonly string[] _platforms = { "x64", "AnyCPU" };

        public DependencyScanner(string solutionFolder)
        {
            _solutionFolder = solutionFolder;
        }

        public ScanResult ScanAndConsolidate()
        {
            var allDependencies = new Dictionary<string, List<DependencyInfo>>(StringComparer.OrdinalIgnoreCase);

            // Find all .csproj files
            var projectFiles = Directory.GetFiles(_solutionFolder, "*.csproj", SearchOption.AllDirectories);

            Console.WriteLine($"Found {projectFiles.Length} project file(s).");
            Console.WriteLine();

            foreach (var projectFile in projectFiles)
            {
                var projectName = Path.GetFileName(projectFile);
                Console.WriteLine($"Processing: {projectName}");
                var dependencies = ScanProject(projectFile, projectName);

                foreach (var dep in dependencies)
                {
                    if (!allDependencies.ContainsKey(dep.Name))
                    {
                        allDependencies[dep.Name] = new List<DependencyInfo>();
                    }

                    // Check if we already have this exact version from this source
                    var existing = allDependencies[dep.Name].FirstOrDefault(d =>
                        d.Version == dep.Version && d.Path == dep.Path);

                    if (existing != null)
                    {
                        // Merge the project references
                        foreach (var project in dep.FoundInProjects)
                        {
                            existing.FoundInProjects.Add(project);
                        }
                    }
                    else
                    {
                        allDependencies[dep.Name].Add(dep);
                    }
                }
            }

            // Consolidate - keep only the newest version of each dependency
            var consolidated = new Dictionary<string, DependencyInfo>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new Dictionary<string, List<DependencyInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in allDependencies)
            {
                var uniqueVersions = kvp.Value
                    .GroupBy(d => d.Version?.ToString() ?? "unknown")
                    .Select(g => g.First())
                    .OrderByDescending(d => d.Version)
                    .ToList();

                if (uniqueVersions.Count > 1)
                {
                    duplicates[kvp.Key] = uniqueVersions;
                }

                var newest = uniqueVersions.FirstOrDefault();
                if (newest != null)
                {
                    consolidated[kvp.Key] = newest;
                }
            }

            return new ScanResult
            {
                ConsolidatedDependencies = consolidated,
                Duplicates = duplicates,
                AllDependencies = allDependencies
            };
        }

        private List<DependencyInfo> ScanProject(string projectFile, string projectName)
        {
            var dependencies = new List<DependencyInfo>();
            var projectDir = Path.GetDirectoryName(projectFile);

            // Scan bin folders for all configurations and platforms
            foreach (var config in _buildConfigs)
            {
                foreach (var platform in _platforms)
                {
                    var binPath = Path.Combine(projectDir, "bin", platform, config);

                    if (Directory.Exists(binPath))
                    {
                        var dllFiles = Directory.GetFiles(binPath, "*.dll", SearchOption.TopDirectoryOnly);

                        foreach (var dllFile in dllFiles)
                        {
                            var depInfo = GetDependencyInfo(dllFile, projectName, DependencySource.BinFolder);
                            if (depInfo != null && !IsSystemAssembly(depInfo.Name))
                            {
                                dependencies.Add(depInfo);
                            }
                        }
                    }
                }
            }

            // Check packages.config for NuGet dependencies (old format)
            var packagesConfig = Path.Combine(projectDir, "packages.config");
            if (File.Exists(packagesConfig))
            {
                Console.WriteLine("  Found packages.config");
                dependencies.AddRange(ScanPackagesConfig(packagesConfig, projectDir, projectName));
            }

            // Check for PackageReference in .csproj (newer format)
            var packageReferences = ScanPackageReferences(projectFile, projectName);
            if (packageReferences.Any())
            {
                Console.WriteLine($"  Found {packageReferences.Count} PackageReference(s)");
                dependencies.AddRange(packageReferences);
            }

            return dependencies;
        }

        private DependencyInfo GetDependencyInfo(string dllPath, string projectName, DependencySource source)
        {
            try
            {
                var fileInfo = new FileInfo(dllPath);
                if (IsExcludedFilename(fileInfo.Name)
                    || fileInfo.Length == 0
                    || fileInfo.Length > 100 * 1024 * 1024) // Skip files larger than 100MB
                {
                    Console.WriteLine($"  Skipping {fileInfo.Name} (excluded or suspicious file size)");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not read file info from {Path.GetFileName(dllPath)}: {ex.Message}");
            }

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);

                return new DependencyInfo
                {
                    Name = assemblyName.Name,
                    Version = assemblyName.Version,
                    Path = dllPath,
                    FoundInProjects = new HashSet<string> { projectName },
                    Source = source
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not read assembly info from {Path.GetFileName(dllPath)}: {ex.Message}");
                return null;
            }
        }

        private List<DependencyInfo> ScanPackagesConfig(string packagesConfigPath, string projectDir, string projectName)
        {
            var dependencies = new List<DependencyInfo>();

            try
            {
                var content = File.ReadAllText(packagesConfigPath);
                var regex = new Regex(@"<package\s+id=""([^""]+)""\s+version=""([^""]+)""", RegexOptions.IgnoreCase);
                var matches = regex.Matches(content);

                // Look for packages folder (could be at solution level)
                var packagesFolder = FindPackagesFolder(projectDir);

                foreach (Match match in matches)
                {
                    var packageId = match.Groups[1].Value;
                    var version = match.Groups[2].Value;

                    if (packagesFolder != null)
                    {
                        var packagePath = Path.Combine(packagesFolder, $"{packageId}.{version}", "lib");

                        if (Directory.Exists(packagePath))
                        {
                            var dllFiles = Directory.GetFiles(packagePath, "*.dll", SearchOption.AllDirectories);

                            foreach (var dll in dllFiles)
                            {
                                var depInfo = GetDependencyInfo(dll, projectName, DependencySource.PackagesConfig);
                                if (depInfo != null && !IsSystemAssembly(depInfo.Name))
                                {
                                    dependencies.Add(depInfo);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Error reading packages.config: {ex.Message}");
            }

            return dependencies;
        }

        private List<DependencyInfo> ScanPackageReferences(string projectFile, string projectName)
        {
            var dependencies = new List<DependencyInfo>();

            try
            {
                var doc = XDocument.Load(projectFile);
                var ns = doc.Root.Name.Namespace;

                // Look for PackageReference elements
                var packageReferences = doc.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference")
                    .ToList();

                var nugetPackagesFolder = GetNuGetPackagesFolder();

                foreach (var packageRef in packageReferences)
                {
                    var includeAttr = packageRef.Attribute("Include");
                    var versionAttr = packageRef.Attribute("Version");

                    // Version might also be in a child element
                    var versionElement = packageRef.Elements().FirstOrDefault(e => e.Name.LocalName == "Version");

                    var packageId = includeAttr?.Value;
                    var versionStr = versionAttr?.Value ?? versionElement?.Value;

                    if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(versionStr))
                        continue;

                    // Try to find the package in the global NuGet cache
                    var packagePath = Path.Combine(nugetPackagesFolder, packageId.ToLower(), versionStr);

                    if (Directory.Exists(packagePath))
                    {
                        // Look for DLLs in lib folders
                        var libPath = Path.Combine(packagePath, "lib");

                        if (Directory.Exists(libPath))
                        {
                            var dllFiles = Directory.GetFiles(libPath, "*.dll", SearchOption.AllDirectories);

                            foreach (var dll in dllFiles)
                            {
                                var depInfo = GetDependencyInfo(dll, projectName, DependencySource.PackageReference);
                                if (depInfo != null && !IsSystemAssembly(depInfo.Name))
                                {
                                    dependencies.Add(depInfo);
                                }
                            }
                        }
                    }
                    else
                    {
                        // If we can't find the actual DLL, at least record the reference
                        Version parsedVersion;
                        Version.TryParse(versionStr, out parsedVersion);

                        dependencies.Add(new DependencyInfo
                        {
                            Name = packageId,
                            Version = parsedVersion,
                            Path = $"[PackageReference] {packageId} v{versionStr} (not resolved to physical path)",
                            FoundInProjects = new HashSet<string> { projectName },
                            Source = DependencySource.PackageReference
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Error reading PackageReferences from project file: {ex.Message}");
            }

            return dependencies;
        }

        private string GetNuGetPackagesFolder()
        {
            // Default global NuGet packages location
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var globalPackagesFolder = Path.Combine(userProfile, ".nuget", "packages");

            if (Directory.Exists(globalPackagesFolder))
            {
                return globalPackagesFolder;
            }

            // Fallback to program data location
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var fallbackFolder = Path.Combine(programData, "NuGet", "packages");

            return fallbackFolder;
        }

        private string FindPackagesFolder(string startPath)
        {
            var currentDir = new DirectoryInfo(startPath);

            while (currentDir != null)
            {
                var packagesPath = Path.Combine(currentDir.FullName, "packages");
                if (Directory.Exists(packagesPath))
                {
                    return packagesPath;
                }
                currentDir = currentDir.Parent;
            }

            return null;
        }

        private bool IsSystemAssembly(string assemblyName)
        {
            // Filter out common system assemblies
            var systemPrefixes = new[]
            {
                "System.",
                "Microsoft.",
                "mscorlib",
                "netstandard",
                "Windows",
                "NETStandard.Library"
            };

            return systemPrefixes.Any(prefix =>
                assemblyName.Equals(prefix.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsExcludedFilename(string fileName)
        {
            // Filter out known assemblies that cannot be processed or are not relevant for version conflict analysis
            var filenamePrefixes = new[]
            {
                "Microsoft.Data.SqlClient.SNI"
            };

            return filenamePrefixes.Any(prefix =>
                fileName.Equals(prefix.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
