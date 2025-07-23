using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Manages language file discovery and loading
/// </summary>
public class LanguageManager : ILanguageManager, IDisposable
{
    private readonly ILogger<LanguageManager> _logger;
    private readonly string _languagesDirectory;
    private FileSystemWatcher? _fileWatcher;
    private readonly object _lockObject = new object();
    private bool _disposed = false;

    // All JSON files in the Languages directory are considered language files

    public LanguageManager(ILogger<LanguageManager> logger)
    {
        _logger = logger;
        _languagesDirectory = Path.Combine(AppContext.BaseDirectory, "Languages");
        InitializeFileWatcher();
    }

    public string LanguagesDirectory => _languagesDirectory;

    /// <summary>
    /// Event fired when language files are added, removed, or modified
    /// </summary>
    public event EventHandler<LanguageFilesChangedEventArgs>? LanguageFilesChanged;

    public async Task<IEnumerable<LanguageInfo>> DiscoverLanguagesAsync()
    {
        var languages = new List<LanguageInfo>();

        try
        {
            if (!Directory.Exists(_languagesDirectory))
            {
                _logger.LogWarning("Languages directory not found: {Directory}", _languagesDirectory);
                return languages;
            }

            var languageFiles = Directory.GetFiles(_languagesDirectory, "*.json");
            _logger.LogInformation("Found {Count} language files in {Directory}", languageFiles.Length, _languagesDirectory);

            foreach (var filePath in languageFiles)
            {
                var fileName = Path.GetFileName(filePath);
                _logger.LogDebug("Processing language file: {FileName}", fileName);
                
                try
                {
                    var languageInfo = await LoadLanguageInfoAsync(filePath);
                    if (languageInfo != null)
                    {
                        languages.Add(languageInfo);
                        _logger.LogInformation("Successfully loaded language: {Code} - {DisplayName} from {FileName}", 
                            languageInfo.Code, languageInfo.DisplayName, fileName);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to load language info from {FileName} - returned null", fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while loading language file: {FilePath}", filePath);
                }
            }

            // Ensure we have at least Chinese as fallback
            if (!languages.Any(l => l.Code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("No Chinese language file found, creating default");
                languages.Add(new LanguageInfo
                {
                    Code = "zh-CN",
                    DisplayName = "简体中文",
                    NativeName = "简体中文",
                    FilePath = Path.Combine(_languagesDirectory, "zh-CN.json"),
                    Version = "1.0.0"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover languages");
        }

        return languages.OrderBy(l => l.DisplayName);
    }

    public async Task<Dictionary<string, string>> LoadLanguageAsync(string languageCode)
    {
        var translations = new Dictionary<string, string>();

        try
        {
            var filePath = Path.Combine(_languagesDirectory, $"{languageCode}.json");
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Language file not found: {FilePath}", filePath);
                return translations;
            }

            // Attempt to load and parse the language file with comprehensive error handling
            translations = await LoadLanguageFileWithRecovery(filePath, languageCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading language: {LanguageCode}", languageCode);
            LogCorruptedFileError(languageCode, ex);
        }

        return translations;
    }

    /// <summary>
    /// Loads a language file with error recovery mechanisms
    /// </summary>
    private async Task<Dictionary<string, string>> LoadLanguageFileWithRecovery(string filePath, string languageCode)
    {
        var translations = new Dictionary<string, string>();
        
        try
        {
            // First, validate file size and basic structure
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                _logger.LogWarning("Language file is empty: {FilePath}", filePath);
                return translations;
            }

            if (fileInfo.Length > 10 * 1024 * 1024) // 10MB limit
            {
                _logger.LogWarning("Language file is unusually large ({Size} bytes): {FilePath}", fileInfo.Length, filePath);
            }

            // Read file content with encoding detection
            string jsonContent;
            try
            {
                jsonContent = await File.ReadAllTextAsync(filePath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error reading language file: {FilePath}", filePath);
                return translations;
            }

            // Validate JSON content is not empty or whitespace
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("Language file contains only whitespace: {FilePath}", filePath);
                return translations;
            }

            // Attempt to parse JSON with detailed error handling
            LanguageFileData? languageData;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                languageData = JsonSerializer.Deserialize<LanguageFileData>(jsonContent, options);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error in language file: {FilePath}. Error at position {Position}", 
                    filePath, ex.BytePositionInLine);
                
                // Attempt recovery by trying to fix common JSON issues
                var recoveredTranslations = AttemptJsonRecovery(jsonContent, filePath, languageCode);
                if (recoveredTranslations.Count > 0)
                {
                    _logger.LogInformation("Recovered {Count} translations from corrupted file: {FilePath}", 
                        recoveredTranslations.Count, filePath);
                    return recoveredTranslations;
                }
                
                LogCorruptedFileError(languageCode, ex);
                return translations;
            }

            // Validate the parsed data structure
            if (languageData == null)
            {
                _logger.LogWarning("Language file deserialized to null: {FilePath}", filePath);
                return translations;
            }

            // Validate and extract translations
            if (languageData.Strings != null)
            {
                // Validate individual translation entries
                var validTranslations = ValidateTranslations(languageData.Strings, filePath);
                translations = validTranslations;
                
                _logger.LogInformation("Loaded {Count} translations for {Language} from {FilePath}", 
                    translations.Count, languageCode, filePath);
                
                // Log validation issues if any translations were filtered out
                var filteredCount = languageData.Strings.Count - translations.Count;
                if (filteredCount > 0)
                {
                    _logger.LogWarning("Filtered out {Count} invalid translation entries from {FilePath}", 
                        filteredCount, filePath);
                }
            }
            else
            {
                _logger.LogWarning("Language file has no 'strings' section: {FilePath}", filePath);
            }

            // Validate metadata if present
            if (languageData.Metadata != null)
            {
                ValidateLanguageMetadata(languageData.Metadata, Path.GetFileName(filePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in language file recovery process: {FilePath}", filePath);
            LogCorruptedFileError(languageCode, ex);
        }

        return translations;
    }

    /// <summary>
    /// Validates individual translation entries and filters out invalid ones
    /// </summary>
    private Dictionary<string, string> ValidateTranslations(Dictionary<string, string> rawTranslations, string filePath)
    {
        var validTranslations = new Dictionary<string, string>();
        
        foreach (var kvp in rawTranslations)
        {
            try
            {
                // Validate key
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    _logger.LogWarning("Skipping translation with empty key in {FilePath}", filePath);
                    continue;
                }

                // Validate value (allow empty strings but not null)
                if (kvp.Value == null)
                {
                    _logger.LogWarning("Skipping translation with null value for key '{Key}' in {FilePath}", kvp.Key, filePath);
                    continue;
                }

                // Check for reasonable key format
                if (kvp.Key.Length > 200)
                {
                    _logger.LogWarning("Translation key unusually long ({Length} chars): '{Key}' in {FilePath}", 
                        kvp.Key.Length, kvp.Key.Substring(0, 50) + "...", filePath);
                }

                // Check for reasonable value length
                if (kvp.Value.Length > 10000)
                {
                    _logger.LogWarning("Translation value unusually long ({Length} chars) for key '{Key}' in {FilePath}", 
                        kvp.Value.Length, kvp.Key, filePath);
                }

                validTranslations[kvp.Key] = kvp.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating translation entry '{Key}' in {FilePath}", kvp.Key, filePath);
            }
        }

        return validTranslations;
    }

    /// <summary>
    /// Attempts to recover translations from corrupted JSON by trying common fixes
    /// </summary>
    private Dictionary<string, string> AttemptJsonRecovery(string jsonContent, string filePath, string languageCode)
    {
        var recoveredTranslations = new Dictionary<string, string>();
        
        try
        {
            _logger.LogInformation("Attempting JSON recovery for {FilePath}", filePath);
            
            // Try common fixes
            var fixes = new[]
            {
                // Remove trailing commas
                (content: jsonContent.Replace(",}", "}").Replace(",]", "]"), description: "trailing commas"),
                
                // Fix common quote issues
                (content: jsonContent.Replace(""", "\"").Replace(""", "\""), description: "smart quotes"),
                
                // Try to extract just the strings section if the file is partially corrupted
                (content: ExtractStringsSection(jsonContent), description: "strings section extraction")
            };

            foreach (var (content, description) in fixes)
            {
                if (string.IsNullOrEmpty(content)) continue;
                
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    
                    var recoveredData = JsonSerializer.Deserialize<LanguageFileData>(content, options);
                    if (recoveredData?.Strings != null && recoveredData.Strings.Count > 0)
                    {
                        _logger.LogInformation("JSON recovery successful using {Description} for {FilePath}", description, filePath);
                        return ValidateTranslations(recoveredData.Strings, filePath);
                    }
                }
                catch (JsonException)
                {
                    // Try next fix
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during JSON recovery attempt for {FilePath}", filePath);
        }

        return recoveredTranslations;
    }

    /// <summary>
    /// Attempts to extract just the strings section from a partially corrupted JSON file
    /// </summary>
    private string ExtractStringsSection(string jsonContent)
    {
        try
        {
            // Look for strings section
            var stringsStart = jsonContent.IndexOf("\"strings\"", StringComparison.OrdinalIgnoreCase);
            if (stringsStart == -1) return string.Empty;

            // Find the opening brace
            var braceStart = jsonContent.IndexOf('{', stringsStart);
            if (braceStart == -1) return string.Empty;

            // Find the matching closing brace
            var braceCount = 0;
            var braceEnd = -1;
            
            for (int i = braceStart; i < jsonContent.Length; i++)
            {
                if (jsonContent[i] == '{') braceCount++;
                else if (jsonContent[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        braceEnd = i;
                        break;
                    }
                }
            }

            if (braceEnd == -1) return string.Empty;

            var stringsJson = jsonContent.Substring(braceStart, braceEnd - braceStart + 1);
            return $"{{\"strings\":{stringsJson}}}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Logs detailed information about corrupted language files
    /// </summary>
    private void LogCorruptedFileError(string languageCode, Exception ex)
    {
        // Log structured error for monitoring systems
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["LanguageCode"] = languageCode,
            ["EventType"] = "CorruptedLanguageFile",
            ["Timestamp"] = DateTime.UtcNow,
            ["ErrorType"] = ex.GetType().Name
        });

        _logger.LogError(ex, "Corrupted language file detected for {LanguageCode}. " +
            "File may need manual repair or replacement. Error: {ErrorMessage}", 
            languageCode, ex.Message);
    }

    private async Task<LanguageInfo?> LoadLanguageInfoAsync(string filePath)
    {
        try
        {
            // Validate file naming convention
            var fileName = Path.GetFileName(filePath);
            if (!ValidateLanguageFileName(fileName))
            {
                _logger.LogWarning("Language file does not follow naming convention: {FileName}", fileName);
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            var languageData = JsonSerializer.Deserialize<LanguageFileData>(jsonContent, options);

            if (languageData?.Metadata != null)
            {
                _logger.LogDebug("Loaded metadata for {FileName}: Code={Code}, DisplayName={DisplayName}, NativeName={NativeName}", 
                    fileName, languageData.Metadata.Code, languageData.Metadata.DisplayName, languageData.Metadata.NativeName);

                // Validate metadata
                if (!ValidateLanguageMetadata(languageData.Metadata, fileName))
                {
                    _logger.LogWarning("Invalid language metadata in file: {FileName}", fileName);
                    return null;
                }

                var languageInfo = new LanguageInfo
                {
                    Code = languageData.Metadata.Code ?? Path.GetFileNameWithoutExtension(filePath),
                    DisplayName = languageData.Metadata.DisplayName ?? languageData.Metadata.Code ?? "Unknown",
                    NativeName = languageData.Metadata.NativeName ?? languageData.Metadata.DisplayName ?? "Unknown",
                    FilePath = filePath,
                    Version = languageData.Metadata.Version ?? "1.0.0"
                };

                _logger.LogDebug("Created LanguageInfo: Code={Code}, DisplayName={DisplayName}, NativeName={NativeName}", 
                    languageInfo.Code, languageInfo.DisplayName, languageInfo.NativeName);

                return languageInfo;
            }
            else
            {
                // Fallback for files without metadata
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                _logger.LogWarning("No metadata found in {FileName}, using fallback with DisplayName={DisplayName}", 
                    fileName, fileNameWithoutExt);
                
                return new LanguageInfo
                {
                    Code = fileNameWithoutExt,
                    DisplayName = fileNameWithoutExt,
                    NativeName = fileNameWithoutExt,
                    FilePath = filePath,
                    Version = "1.0.0"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse language file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Initializes the file system watcher for the Languages directory
    /// </summary>
    private void InitializeFileWatcher()
    {
        try
        {
            // Check if Languages directory exists, but don't create it if it doesn't
            // This prevents overwriting files that were copied during build
            if (!Directory.Exists(_languagesDirectory))
            {
                _logger.LogWarning("Languages directory not found at startup: {Directory}", _languagesDirectory);
                // Don't create the directory here - let it be created by the build process
                return;
            }

            _fileWatcher = new FileSystemWatcher(_languagesDirectory)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _fileWatcher.Created += OnLanguageFileChanged;
            _fileWatcher.Changed += OnLanguageFileChanged;
            _fileWatcher.Deleted += OnLanguageFileChanged;
            _fileWatcher.Renamed += OnLanguageFileRenamed;

            _logger.LogInformation("File system watcher initialized for Languages directory");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize file system watcher");
        }
    }

    /// <summary>
    /// Handles language file changes (created, modified, deleted)
    /// </summary>
    private void OnLanguageFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lockObject)
        {
            try
            {
                _logger.LogDebug("Language file {ChangeType}: {FilePath}", e.ChangeType, e.FullPath);

                // Validate file naming convention for new/changed files
                if (e.ChangeType != WatcherChangeTypes.Deleted)
                {
                    var changedFileName = Path.GetFileName(e.FullPath);
                    if (!ValidateLanguageFileName(changedFileName))
                    {
                        _logger.LogWarning("Ignoring file with invalid naming convention: {FileName}", changedFileName);
                        return;
                    }
                }

                // Fire event to notify about language files change
                LanguageFilesChanged?.Invoke(this, new LanguageFilesChangedEventArgs(e.ChangeType, e.FullPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling language file change: {FilePath}", e.FullPath);
            }
        }
    }

    /// <summary>
    /// Handles language file rename events
    /// </summary>
    private void OnLanguageFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lockObject)
        {
            try
            {
                _logger.LogDebug("Language file renamed from {OldPath} to {NewPath}", e.OldFullPath, e.FullPath);

                // Validate new file name
                var newFileName = Path.GetFileName(e.FullPath);
                if (!ValidateLanguageFileName(newFileName))
                {
                    _logger.LogWarning("Ignoring renamed file with invalid naming convention: {FileName}", newFileName);
                    return;
                }

                // Fire events for both old (deleted) and new (created) files
                LanguageFilesChanged?.Invoke(this, new LanguageFilesChangedEventArgs(WatcherChangeTypes.Deleted, e.OldFullPath));
                LanguageFilesChanged?.Invoke(this, new LanguageFilesChangedEventArgs(WatcherChangeTypes.Created, e.FullPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling language file rename: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
            }
        }
    }

    /// <summary>
    /// Validates language file naming convention
    /// </summary>
    /// <param name="fileName">The file name to validate</param>
    /// <returns>True if the file name is a JSON file, false otherwise</returns>
    private bool ValidateLanguageFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // Accept all JSON files in the Languages directory as potential language files
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates language metadata consistency
    /// </summary>
    /// <param name="metadata">The metadata to validate</param>
    /// <param name="fileName">The file name for cross-validation</param>
    /// <returns>True if metadata is valid, false otherwise</returns>
    private bool ValidateLanguageMetadata(LanguageMetadata metadata, string fileName)
    {
        if (metadata == null)
        {
            return false;
        }

        // Check if code is provided
        if (string.IsNullOrEmpty(metadata.Code))
        {
            _logger.LogWarning("Language metadata missing code in file: {FileName}", fileName);
            return false;
        }

        // Check if displayName is provided
        if (string.IsNullOrEmpty(metadata.DisplayName))
        {
            _logger.LogWarning("Language metadata missing displayName in file: {FileName}", fileName);
            return false;
        }

        // Log successful validation
        _logger.LogDebug("Language metadata validated successfully for {FileName}: Code={Code}, DisplayName={DisplayName}", 
            fileName, metadata.Code, metadata.DisplayName);

        return true;
    }

    /// <summary>
    /// Disposes the file system watcher
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _fileWatcher?.Dispose();
            _disposed = true;
            _logger.LogDebug("LanguageManager disposed");
        }
    }

    /// <summary>
    /// Data structure for language JSON files
    /// </summary>
    private class LanguageFileData
    {
        public LanguageMetadata? Metadata { get; set; }
        public Dictionary<string, string>? Strings { get; set; }
    }

    /// <summary>
    /// Metadata section of language files
    /// </summary>
    private class LanguageMetadata
    {
        public string? Code { get; set; }
        public string? DisplayName { get; set; }
        public string? NativeName { get; set; }
        public string? Version { get; set; }
    }
}