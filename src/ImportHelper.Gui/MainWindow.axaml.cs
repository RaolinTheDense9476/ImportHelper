using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImportHelper.Core;

namespace ImportHelper.Gui;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
  }

  private async void OnBrowseFilePattern(object? sender, RoutedEventArgs e)
  {
    IStorageProvider? storageProvider = GetTopLevel(this)?.StorageProvider;
    if (storageProvider == null)
    {
      return;
    }

    if (BrowseSingleFileCheck.IsChecked == true)
    {
      IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = "Choose a single data file",
        AllowMultiple = false,
      });

      if (files.Count > 0 && files[0].TryGetLocalPath() is string filePath)
      {
        FilePatternBox.Text = filePath;
      }
    }
    else
    {
      IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
      {
        Title = "Choose the folder containing your data files",
        AllowMultiple = false,
      });

      if (folders.Count > 0 && folders[0].TryGetLocalPath() is string folderPath)
      {
        FilePatternBox.Text = Path.Combine(folderPath, "*.*");
      }
    }
  }

  private async void OnBrowseTarget(object? sender, RoutedEventArgs e)
  {
    IStorageProvider? storageProvider = GetTopLevel(this)?.StorageProvider;
    if (storageProvider == null)
    {
      return;
    }

    IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
      Title = "Choose a target definition YAML file",
      AllowMultiple = false,
      FileTypeFilter = new[] { new FilePickerFileType("YAML files") { Patterns = new[] { "*.yaml", "*.yml" } } },
    });

    if (files.Count > 0 && files[0].TryGetLocalPath() is string filePath)
    {
      TargetBox.Text = filePath;
    }
  }

  private async void OnRunClicked(object? sender, RoutedEventArgs e)
  {
    var options = new ImportHelperOptions
    {
      FilePattern = FilePatternBox.Text ?? "",
      Delimiter = string.IsNullOrEmpty(DelimiterBox.Text) ? "," : DelimiterBox.Text,
      HasHeader = HasHeaderCheck.IsChecked == true,
      Encoding = string.IsNullOrWhiteSpace(EncodingBox.Text) ? "UTF-8" : EncodingBox.Text,
      Target = string.IsNullOrWhiteSpace(TargetBox.Text) ? "mssql" : TargetBox.Text,
      GenerateTsql = GenerateTsqlCheck.IsChecked == true,
      GenerateBcpFormat = GenerateBcpFormatCheck.IsChecked == true,
      AllowEmbeddedNewlines = AllowEmbeddedNewlinesCheck.IsChecked == true,
      ForceQuotedAsString = ForceQuotedAsStringCheck.IsChecked == true,
      PrepareForBcp = PrepareForBcpCheck.IsChecked == true,
    };

    if (string.IsNullOrWhiteSpace(options.FilePattern))
    {
      AppendLog("Error: please choose a file pattern first.");
      return;
    }

    LogBox.Text = "";
    RunButton.IsEnabled = false;

    void Log(string message) => Dispatcher.UIThread.Post(() => AppendLog(message));

    ImportHelperResult result = await Task.Run(() => ImportHelperRunner.Run(options, Log));

    RunButton.IsEnabled = true;

    var summaryWindow = new SummaryWindow(BuildSummary(result));
    await summaryWindow.ShowDialog(this);

    if (result.Success && Directory.Exists(result.OutputDirectory))
    {
      OpenFolder(result.OutputDirectory);
    }
  }

  private static string BuildSummary(ImportHelperResult result)
  {
    if (result.FilesFound == 0)
    {
      return "No files were found matching the given pattern.";
    }

    var lines = new List<string>();

    foreach (FileSummary file in result.Files)
    {
      lines.Add(Path.GetFileName(file.FilePath) + ":");
      foreach (ColumnInfo column in file.Columns)
      {
        string name = column.Name ?? $"Column {column.Index + 1}";
        string typeDescription = column.DataType == ColumnDataType.String
            ? $"{column.DataType} (MaxLength = {column.MaxLength})"
            : column.DataType.ToString();
        lines.Add($"  {name}: {typeDescription}");
      }
      lines.Add("");
    }

    if (result.FilesFailed > 0)
    {
      lines.Add($"{result.FilesFailed} file(s) failed to process — see the log for details.");
    }

    return string.Join(Environment.NewLine, lines).TrimEnd();
  }

  private void AppendLog(string message)
  {
    LogBox.Text += message + Environment.NewLine;
  }

  // Process.Start's UseShellExecute path for opening a folder in the OS file
  // manager isn't uniform across platforms, so branch explicitly rather than
  // relying on one call to behave the same way everywhere.
  private static void OpenFolder(string path)
  {
    try
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
      }
      else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
      {
        Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
      }
      else
      {
        Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
      }
    }
    catch
    {
      // Best-effort convenience only; the user still has the log showing
      // exactly which files were generated and where.
    }
  }
}
