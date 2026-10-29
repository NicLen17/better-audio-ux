using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BetterAudio.Services;

namespace BetterAudio.UI.ViewModels;

public sealed partial class FlyoutViewModel
{
    private readonly IStartupService _startup;
    private Dictionary<string, string> _hiddenDevices;
    private bool _isSettingsOpen;
    private bool _startWithWindows;
    public bool IsSettingsOpen => _isSettingsOpen;
    public ObservableCollection<KeyValuePair<string, string>> HiddenDevices { get; } = new();
    public bool HasHiddenDevices => HiddenDevices.Count > 0;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            try { _startup.Enabled = value; _startWithWindows = _startup.Enabled; Message = ""; }
            catch (Exception ex) { ReportError("No se pudo cambiar el inicio con Windows.", ex); }
            Notify();
        }
    }
    public async Task OpenSettingsAsync()
    {
        await CloseMixerAsync();
        _isSettingsOpen = true;
        Message = "";
        try { _startWithWindows = _startup.Enabled; }
        catch (Exception ex) { ReportError("No se pudo consultar el inicio con Windows.", ex); }
        Notify(nameof(StartWithWindows)); Notify(nameof(IsSettingsOpen)); Notify(nameof(HudHeight));
    }
    public void CloseSettings()
    {
        _isSettingsOpen = false;
        Notify(nameof(IsSettingsOpen)); Notify(nameof(HudHeight));
    }
    public async Task HideSelectedDeviceAsync()
    {
        if (SelectedDevice is not { } device) return;
        var nextId = Devices.Count > 1 ? Devices[(_selectedIndex + 1) % Devices.Count].Id : null;
        _hiddenDevices[device.Id] = device.Name;
        SaveHiddenDevices();
        await RefreshAsync();
        var next = System.Linq.Enumerable.FirstOrDefault(Devices, d => d.Id == nextId);
        if (next is not null)
        {
            SetSelection(Devices.IndexOf(next));
            if (_autoSwitch) await RequestSwitchAsync(next.Id);
        }
    }
    public async Task RestoreDeviceAsync(string id)
    {
        if (!_hiddenDevices.Remove(id)) return;
        SaveHiddenDevices();
        await RefreshAsync();
    }
    private void SaveHiddenDevices()
    {
        Message = "";
        UpdateHiddenDevices();
        try { _preferences.HiddenDevices = _hiddenDevices; }
        catch (Exception ex) { ReportError("Los dispositivos ocultos se recordarán solo durante esta sesión.", ex); }
    }
    private void UpdateHiddenDevices()
    {
        HiddenDevices.Clear();
        foreach (var device in _hiddenDevices) HiddenDevices.Add(device);
        Notify(nameof(HasHiddenDevices));
    }
}
