using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterAudio.Core.Audio;
using BetterAudio.Core.Models;

namespace BetterAudio.UI.ViewModels;

public sealed partial class FlyoutViewModel
{
    private bool _isInputMode;
    private bool _changingMode;
    private bool _listenBusy;
    private int _listenVersion;
    private InputListenState _listenState = new();
    private List<string> _outputOrder = new();
    private List<string> _inputOrder = new();
    private bool _outputAutoSwitch;
    private bool _inputAutoSwitch;
    public bool IsInputMode => _isInputMode;
    public bool IsOutputMode => !_isInputMode;
    public bool CanChangeMode => !_changingMode;
    public string DeviceSectionLabel => IsInputMode ? "Entradas ⇄" : "Salidas ⇄";
    public string DeviceSectionHint => IsInputMode ? "Cambiar a salidas de audio" : "Cambiar a entradas y micrófonos";
    public string NavigationHint => $"Cambia la {(IsInputMode ? "entrada" : "salida")} automáticamente al recorrer el arco. Desmarcá para elegir con Enter o clic.";
    public bool IsListening => _listenState.IsActive;
    public bool CanListen => IsInputMode && HasDevices && !_listenBusy && _audio is IAudioInputService;
    public string ListenLabel => _listenBusy ? "Aplicando…" : IsListening ? "Detener" : "Escuchar";
    public string ListenHint => IsListening ? $"Escuchando en {_listenState.OutputName}. Pulsá para detener."
        : "Escuchar esta entrada por la salida actual. Usá auriculares para evitar acoples. Se detiene al cerrar el panel.";
    public string DefaultInputLabel => SelectedIsDefault ? "En uso ✓" : "Predeterminar";
    private Dictionary<string, string> CurrentHiddenDevices => IsInputMode ? _hiddenInputDevices : _hiddenDevices;

    public async Task ToggleDeviceSectionAsync()
    {
        if (_changingMode || _disposed) return;
        _changingMode = true; Notify(nameof(CanChangeMode)); Notify(nameof(CanInteract));
        try
        {
            _pendingDefaultId = null;
            await _switchTask;
            await StopListeningAsync();
            CloseSettings(); CloseMirror(); await CloseMixerAsync();
            // Finish an old enumeration before changing its interpretation.
            if (_refreshCompletion is { } refresh) await refresh.Task;
            _isInputMode = !_isInputMode;
            _deviceOrder = IsInputMode ? _inputOrder : _outputOrder;
            _autoSwitch = IsInputMode ? _inputAutoSwitch : _outputAutoSwitch;
            Devices.Clear(); SetSelection(-1); Message = "";
            foreach (var name in new[] { nameof(IsInputMode), nameof(IsOutputMode), nameof(DeviceSectionLabel), nameof(DeviceSectionHint),
                nameof(NavigationHint), nameof(AutoSwitchOnNavigation), nameof(CanInteract), nameof(HasDevices), nameof(CanTestSound), nameof(CanListen) }) Notify(name);
            await RefreshAsync(focusDefault: true);
        }
        finally { _changingMode = false; Notify(nameof(CanChangeMode)); Notify(nameof(CanInteract)); }
    }

    private void NotifyListening()
    {
        Notify(nameof(IsListening)); Notify(nameof(CanListen)); Notify(nameof(ListenLabel)); Notify(nameof(ListenHint));
    }
    private async Task RefreshListeningAsync()
    {
        if (_audio is not IAudioInputService service) return;
        var state = await service.GetListenStateAsync();
        if (_disposed) return;
        if (_listenState.IsActive && !state.IsActive && state.Error is not null) Message = state.Error;
        _listenState = state; NotifyListening();
    }
    public async Task ToggleListeningAsync()
    {
        if (!CanListen || _audio is not IAudioInputService service || SelectedDevice is not { } device) return;
        if (!IsListening && (_muted || Volume == 0)) { Message = "Activá el micrófono y subí su volumen para escucharlo."; return; }
        _listenBusy = true; NotifyListening();
        var version = ++_listenVersion;
        Message = "";
        try
        {
            if (IsListening) await service.StopListeningAsync();
            else
            {
                await service.StartListeningAsync(device.Id);
                if (_disposed || version != _listenVersion || !IsInputMode || SelectedDevice?.Id != device.Id)
                    await service.StopListeningAsync();
            }
        }
        catch (Exception ex) { if (!_disposed) ReportError("No se pudo escuchar la entrada. Revisá la salida y los permisos del micrófono en Windows.", ex); }
        finally { _listenBusy = false; if (!_disposed) await RefreshListeningAsync(); NotifyListening(); }
    }
    public async Task StopListeningAsync()
    {
        ++_listenVersion;
        if (_audio is not IAudioInputService service) return;
        try { await service.StopListeningAsync(); _listenState = new(); NotifyListening(); }
        catch (Exception ex) { if (!_disposed) ReportError("No se pudo detener la escucha.", ex); }
    }
}
