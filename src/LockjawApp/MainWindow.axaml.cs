using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lockjaw.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lockjaw.App;

public partial class MainWindow : Window
{
    private readonly List<string> _paths = new();
    private bool _decryptMode;
    private bool _busy;
    private CancellationTokenSource? _cts;

    private static readonly IBrush BorderIdle = Brush.Parse("#26313b");
    private static readonly IBrush BorderHot = Brush.Parse("#c9a227");

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropZone.BorderBrush = BorderIdle);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DropZone.PointerPressed += async (_, _) => await BrowseAsync();
        Passphrase.TextChanged += (_, _) => UpdateUi();
        PassphraseConfirm.TextChanged += (_, _) => UpdateUi();

        UpdateUi();
    }

    public void LoadInitialPaths(string[] args)
    {
        SetPaths(args.Where(a => File.Exists(a) || Directory.Exists(a)));
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.DragEffects != DragDropEffects.None)
        {
            DropZone.BorderBrush = BorderHot;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.BorderBrush = BorderIdle;
        if (_busy) return;
        var items = e.Data.GetFiles();
        if (items is null) return;
        SetPaths(items.Select(f => f.Path.LocalPath));
    }

    private async Task BrowseAsync()
    {
        if (_busy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose files to encrypt (or a .lockjaw file to decrypt)",
            AllowMultiple = true,
        });
        if (files.Count > 0)
        {
            SetPaths(files.Select(f => f.Path.LocalPath));
        }
    }

    private void SetPaths(IEnumerable<string> paths)
    {
        _paths.Clear();
        _paths.AddRange(paths.Where(p => File.Exists(p) || Directory.Exists(p)));
        _decryptMode = _paths.Count == 1
            && File.Exists(_paths[0])
            && (_paths[0].EndsWith(LockjawConstants.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || _paths[0].EndsWith(LockjawConstants.ArmoredExtension, StringComparison.OrdinalIgnoreCase));
        StatusText.Text = string.Empty;
        UpdateUi();
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _paths.Clear();
        StatusText.Text = string.Empty;
        UpdateUi();
    }

    private void UpdateUi()
    {
        bool hasItems = _paths.Count > 0;
        SelectionPanel.IsVisible = hasItems;

        if (hasItems)
        {
            SelectionTitle.Text = _decryptMode
                ? Path.GetFileName(_paths[0])
                : _paths.Count == 1
                    ? Path.GetFileName(_paths[0])
                    : $"{_paths.Count} items selected";
            SelectionDetail.Text = _decryptMode
                ? "Encrypted Lockjaw file — will be decrypted"
                : string.Join(", ", _paths.Take(4).Select(Path.GetFileName)) + (_paths.Count > 4 ? ", …" : string.Empty);
        }

        PassphraseConfirm.IsVisible = !_decryptMode;
        ActionButton.Content = _decryptMode ? "Decrypt" : "Encrypt";

        string pass = Passphrase.Text ?? string.Empty;
        bool passOk = pass.Length > 0 && (_decryptMode || pass == (PassphraseConfirm.Text ?? string.Empty));
        ActionButton.IsEnabled = hasItems && passOk && !_busy;

        DropTitle.Text = hasItems ? "Drop different files to replace" : "Drop files or folders here";
    }

    private async void OnActionClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _paths.Count == 0) return;

        string pass = Passphrase.Text ?? string.Empty;
        bool decrypt = _decryptMode;
        var inputs = _paths.ToArray();

        _busy = true;
        _cts = new CancellationTokenSource();
        Progress.IsVisible = true;
        ActionButton.IsEnabled = false;
        StatusText.Foreground = Brush.Parse("#8a97a1");
        StatusText.Text = decrypt ? "Decrypting…" : "Compressing and encrypting…";

        try
        {
            string resultMessage = await Task.Run(() =>
                decrypt ? RunDecrypt(inputs[0], pass, _cts.Token) : RunEncrypt(inputs, pass, _cts.Token));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Foreground = Brush.Parse("#7dc37d");
                StatusText.Text = resultMessage;
                Passphrase.Text = string.Empty;
                PassphraseConfirm.Text = string.Empty;
                _paths.Clear();
            });
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled. No output was written.";
        }
        catch (LockjawAuthenticationException)
        {
            StatusText.Foreground = Brush.Parse("#d9776f");
            StatusText.Text = "Incorrect passphrase or damaged file. Nothing was extracted.";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = Brush.Parse("#d9776f");
            StatusText.Text = ex is LockjawException or IOException
                ? ex.Message
                : $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _cts.Dispose();
            _cts = null;
            Progress.IsVisible = false;
            UpdateUi();
        }
    }

    private static string RunEncrypt(string[] inputs, string pass, CancellationToken token)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Environment.CurrentDirectory;
        string baseName = inputs.Length == 1
            ? Path.GetFileNameWithoutExtension(inputs[0])
            : "archive";
        string destination = UniquePath(parent, baseName, LockjawConstants.BinaryExtension);

        LockjawService.Encrypt(inputs, destination, pass, options: null, token);
        return $"Encrypted → {destination}";
    }

    private static string RunDecrypt(string input, string pass, CancellationToken token)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(input)) ?? Environment.CurrentDirectory;
        string baseName = Path.GetFileName(input);
        if (baseName.EndsWith(LockjawConstants.ArmoredExtension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^LockjawConstants.ArmoredExtension.Length];
        }
        else if (baseName.EndsWith(LockjawConstants.BinaryExtension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^LockjawConstants.BinaryExtension.Length];
        }

        string destination = UniquePath(parent, baseName, suffix: string.Empty);
        LockjawService.Decrypt(input, destination, pass, token);
        return $"Decrypted → {destination}";
    }

    private static string UniquePath(string parent, string baseName, string suffix)
    {
        string candidate = Path.Combine(parent, baseName + suffix);
        int n = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(parent, $"{baseName} ({n++}){suffix}");
        }
        return candidate;
    }
}
