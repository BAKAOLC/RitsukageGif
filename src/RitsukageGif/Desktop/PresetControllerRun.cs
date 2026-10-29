using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal sealed class PresetControllerRun
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Lock _gate = new();
        private bool _retired;
        private int _users;

        public PresetControllerRun(WindowPreset preset, PresetDebugger? debugger)
        {
            Preset = preset;
            Debugger = debugger;
            Machine = new(preset, debugger);
            Token = _cancellation.Token;
        }

        public CancellationToken Token { get; }
        public WindowPreset Preset { get; }
        public string ParameterSession { get; } = Guid.NewGuid().ToString("N");
        public ScriptParameters Parameters { get; } = new();
        public PresetDebugger? Debugger { get; }
        public PresetMachine Machine { get; }
        public Stopwatch Clock { get; } = new();
        public List<string> Signals { get; } = [];
        public Action? DebugChanged { get; set; }
        public WindowTarget? Target { get; set; }
        public WindowTarget? Previous { get; set; }
        public PixelRect? Region { get; set; }
        public bool Polling { get; set; }
        public PresetScriptSession? Script { get; set; }
        public Task? ScriptTask { get; set; }
        public string? Failure { get; set; }
        public Exception? ScriptFailure { get; set; }

        public void Acquire()
        {
            lock (_gate) _users++;
        }

        public void Release()
        {
            lock (_gate)
            {
                _users--;
                if (_retired && _users == 0) _cancellation.Dispose();
            }
        }

        public void Retire()
        {
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                // Keep the source alive while cancellation callbacks unwind, including reentrant releases.
                _users++;
            }

            try
            {
                _cancellation.Cancel();
            }
            finally
            {
                Release();
            }
        }
    }
}
