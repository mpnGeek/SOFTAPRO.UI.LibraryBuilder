using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SOFTAPRO.Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;


/// <summary>
/// MSBuild-задача для автоматической генерации объединённого словаря стилей WPF
/// <br/>
/// Находит все XAML-файлы в указанной папке и создает итоговый <c>ResourceDictionary</c>, который включает их как <c>MergedDictionaries</c>.
/// <list type="number">
/// <item>Рекурсивно ищет все стили (<c>*.xaml</c>) в подпапках <see cref="StylesRelativePath"/> внутри проекта</item>
/// <item>Исключает системные темы (<c>Dark.xaml</c>, <c>Light.xaml</c>)</item>
/// <item>Создаёт и записывает словарь с именем <see cref="StylesFileName"/> в папке стилей</item>
/// </list>
/// <example>
/// Пример использования Task в .cproj файле:
/// <br/>
/// <code>
/// <UsingTask TaskName="GenerateStylesDictionaryTask" AssemblyFile="$(MSBuildProjectDirectory)\Build\SOFTAPRO.UI.LibraryBuilder.dll" />
/// <Target Name="GenerateStylesDictionary" BeforeTargets="CoreCompile">
/// 	<GenerateStylesDictionaryTask ProjectDir="$(MSBuildProjectDirectory)" 
/// 								  StylesRelativePath="Styles" 
/// 								  ThemesDarkPath="Themes/Dark.xaml" 
/// 								  ThemesLightPath="Themes/Light.xaml" 
/// 								  StylesFileName="Styles.xaml" 
/// 								  AssemblyName="SOFTAPRO.UI"
/// 							      EmbeddedResourcePath="Resources/StyleResourceUris.json" /> 
///	</Target>
///	<ItemGroup>
///		<EmbeddedResource Include="Resources\StyleResourceUris.json" />
///	</ItemGroup>
/// </code>
/// </example>
/// Создастся объединённый словарь стилей:
/// <br/>
/// <code><see cref="ProjectDir"/>/<see cref="StylesRelativePath"/>/<see cref="StylesFileName"/></code>
/// <br/>
///
/// Исключены системные темы и сам файл словаря стилей:
/// <list type="bullet">
/// <item><code><see cref="ProjectDir"/>/<see cref="ThemesDarkPath"/></code></item>
/// <item><code><see cref="ProjectDir"/>/<see cref="ThemesLightPath"/></code></item>
/// <item><code><see cref="ProjectDir"/>/<see cref="StylesRelativePath"/>/<see cref="StylesFileName"/></code></item>
/// </list>
/// </summary>
public class GenerateStylesDictionaryTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Путь к корневой директории проекта
    /// </summary>
    [Required]
    public string ProjectDir { get; set; }

    /// <summary>
    /// Относительный путь до папки со стилями
    /// <br/>
    /// По умолчанию: <c>"Styles"</c>
    /// </summary>
    [Required]
    public string StylesRelativePath { get; set; }

    /// <summary>
    /// Имя XAML-файла словаря тёмной темы
    /// <br/>
    /// По умолчанию: <c>"Themes/Dark.xaml"</c>
    /// </summary>
    [Required]
    public string ThemesDarkPath { get; set; }

    /// <summary>
    /// Имя XAML-файла словаря светлой темы
    /// <br/>
    /// По умолчанию: <c>"Themes/Light.xaml"</c>
    /// </summary>
    [Required]
    public string ThemesLightPath { get; set; }

    /// <summary>
    /// Имя итогового XAML-файла словаря стилей
    /// <br/>
    /// По умолчанию: <c>"Styles.xaml"</c>
    /// </summary>
    [Required]
    public string StylesFileName { get; set; }

    /// <summary>
    /// Имя сборки для формирования pack:// URI
    /// <br/>
    /// По умолчанию: <c>"SOFTAPRO.UI"</c>
    /// </summary>
    [Required]
    public string AssemblyName { get; set; }

    /// <summary>
    /// Имя генерируемого JSON-файла с URI ресурсов
    /// <br/>
    /// По умолчанию: <c>"Resources/StyleResourceUris.json"</c>
    /// </summary>
    [Required]
    public string EmbeddedResourcePath { get; set; }

    /// <summary>
    /// Полный путь к генерируемому JSON-файлу с URI
    /// </summary>
    public string FullEmbeddedResourcePath => Path.Combine(ProjectDir, EmbeddedResourcePath);

    /// <summary>
    /// Выполняет генерацию словаря стилей и JSON с URI ресурсов
    /// </summary>
    /// <returns><see langword="true"/> при успехе, <see langword="false"/> при ошибке</returns>
    public override bool Execute()
    {
        try
        {
            GenerateStyles();
            GenerateThemeUrisJson();
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    /// <summary>
    /// Генерирует объединённый словарь стилей Styles.xaml
    /// </summary>
    private void GenerateStyles()
    {
        var stylesRoot = Path.Combine(ProjectDir, StylesRelativePath);
        var themesRoot = Path.Combine(ProjectDir, Path.GetDirectoryName(ThemesDarkPath));  

        if (!Directory.Exists(stylesRoot))
        {
            Log.LogMessage(MessageImportance.High, $"Styles folder not found: {stylesRoot}");
            return;
        }

        if (!Directory.Exists(themesRoot)) 
        {
            Log.LogMessage(MessageImportance.High, $"Themes folder not found: {themesRoot}");
            return;
        }

        var allXaml = Directory.EnumerateFiles(stylesRoot, "*.xaml", SearchOption.AllDirectories).ToList();

        string dark = Path.Combine(themesRoot, ThemesDarkPath);
        string light = Path.Combine(themesRoot, ThemesLightPath);
        string styles = Path.Combine(stylesRoot, StylesFileName);

        var filtered = allXaml
            .Where(path => !String.Equals(path, dark, StringComparison.OrdinalIgnoreCase))
            .Where(path => !String.Equals(path, light, StringComparison.OrdinalIgnoreCase))
            .Where(path => !String.Equals(path, styles, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToList();

        string outputPath = Path.Combine(stylesRoot, StylesFileName);

        using (var writer = new StreamWriter(outputPath, false))
        {
            writer.WriteLine(@"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""");
            writer.WriteLine(@"                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">");
            writer.WriteLine();
            writer.WriteLine(@"    <ResourceDictionary.MergedDictionaries>");
            writer.WriteLine();

            foreach (var filePath in filtered)
            {
                var relative = GetRelativePath(stylesRoot + Path.DirectorySeparatorChar, filePath);
                var xamlPath = "./" + relative.Replace("\\", "/");
                writer.WriteLine($@"        <ResourceDictionary Source=""{xamlPath}"" />");
            }

            writer.WriteLine();
            writer.WriteLine(@"    </ResourceDictionary.MergedDictionaries>");
            writer.WriteLine();
            writer.WriteLine(@"</ResourceDictionary>");
        }

        Log.LogMessage(MessageImportance.High, $"Generated styles dictionary: {outputPath} ({filtered.Count} dictionaries)");
    }

    /// <summary>
    /// Генерирует JSON-файл с pack:// URI для тем и стилей в виле embedded resource
    /// </summary>
    private void GenerateThemeUrisJson()
    {
        var lightPath = $"pack://application:,,,/{AssemblyName};component/{ThemesLightPath}";
        var darkPath = $"pack://application:,,,/{AssemblyName};component/{ThemesDarkPath}";
        var stylesPath = $"pack://application:,,,/{AssemblyName};component/{StylesRelativePath}/{StylesFileName}";

        var json = new
        {
            LightTheme = lightPath,
            DarkTheme = darkPath,
            Styles = stylesPath
        };

        var content = JsonConvert.SerializeObject(json, SOFTAPRO.Newtonsoft.Json.Formatting.Indented);

        // Создаем директорию если не существует
        var directory = Path.GetDirectoryName(FullEmbeddedResourcePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FullEmbeddedResourcePath, content, Encoding.UTF8);

        Log.LogMessage(MessageImportance.High, $"Generated theme URIs JSON (embedded resource): {FullEmbeddedResourcePath}");
    }

    /// <summary>
    /// Возвращает относительный путь от базовой директории до указанного файла
    /// </summary>
    private string GetRelativePath(string basePath, string fullPath)
    {
        var uriBase = new Uri(AppendDirectorySeparatorChar(basePath));
        var uriFull = new Uri(fullPath);
        return Uri.UnescapeDataString(uriBase.MakeRelativeUri(uriFull).ToString());
    }

    /// <summary>
    /// Добавляет разделитель каталога в конец пути, если его нет
    /// </summary>
    private string AppendDirectorySeparatorChar(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            return path + Path.DirectorySeparatorChar;
        return path;
    }
}
