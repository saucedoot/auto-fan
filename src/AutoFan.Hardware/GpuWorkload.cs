using AutoFan.Core;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace AutoFan.Hardware;

internal sealed class GpuWorkload : IDisposable
{
    private const int BufferCount = 2;
    internal const uint PresentSyncInterval = 1;
    private const string WindowClassName = "AutoFanGpuHeat";

    private readonly IDXGIFactory4 _factory;
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipeline;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly uint _rtvStride;
    private readonly ID3D12CommandAllocator[] _allocators;
    private readonly ulong[] _fenceValues;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _fence;
    private readonly EventWaitHandle _fenceEvent;
    private readonly IntPtr _hwnd;
    private readonly WndProc _wndProc;
    private IDXGISwapChain3 _swapChain;
    private ID3D12Resource[] _backBuffers;
    private int _width;
    private int _height;
    private int _frameIndex;
    private ulong _fenceValue;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _gate = new();
    private bool _disposed;
    private volatile bool _faulted;

    private GpuWorkload(
        IDXGIFactory4 factory,
        ID3D12Device device,
        ID3D12CommandQueue queue,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipeline,
        ID3D12DescriptorHeap rtvHeap,
        uint rtvStride,
        ID3D12CommandAllocator[] allocators,
        ID3D12GraphicsCommandList commandList,
        ID3D12Fence fence,
        EventWaitHandle fenceEvent,
        IntPtr hwnd,
        WndProc wndProc,
        IDXGISwapChain3 swapChain,
        ID3D12Resource[] backBuffers,
        int width,
        int height)
    {
        _factory = factory;
        _device = device;
        _queue = queue;
        _rootSignature = rootSignature;
        _pipeline = pipeline;
        _rtvHeap = rtvHeap;
        _rtvStride = rtvStride;
        _allocators = allocators;
        _fenceValues = new ulong[BufferCount];
        _commandList = commandList;
        _fence = fence;
        _fenceEvent = fenceEvent;
        _hwnd = hwnd;
        _wndProc = wndProc;
        _swapChain = swapChain;
        _backBuffers = backBuffers;
        _width = width;
        _height = height;
        _frameIndex = (int)swapChain.CurrentBackBufferIndex;
        _fenceValue = 1;
    }

    public static GpuWorkload? TryCreate(string? preferredName = null)
    {
        try
        {
            return Create(preferredName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool HasFault => _faulted;

    public void Set(WorkloadLevel level) => Set(HeatSettings(level));

    public void Set(HeatProfile profile)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopLocked();
            _faulted = false;
            if (profile.GpuPasses <= 0 || profile.GpuIterations <= 0)
            {
                return;
            }

            Resize(profile.GpuWidth, profile.GpuHeight);
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            int passes = profile.GpuPasses;
            int rounds = profile.GpuIterations;
            _loop = Task.Factory.StartNew(
                () => Run(passes, rounds, token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopLocked();
            WaitIdle();
            foreach (ID3D12Resource buffer in _backBuffers)
            {
                buffer.Dispose();
            }

            _swapChain.Dispose();
            _commandList.Dispose();
            foreach (ID3D12CommandAllocator allocator in _allocators)
            {
                allocator.Dispose();
            }

            _rtvHeap.Dispose();
            _pipeline.Dispose();
            _rootSignature.Dispose();
            _fence.Dispose();
            _fenceEvent.Dispose();
            _queue.Dispose();
            _device.Dispose();
            _factory.Dispose();
            if (_hwnd != IntPtr.Zero)
            {
                Native.DestroyWindow(_hwnd);
            }

            _disposed = true;
        }

        GC.KeepAlive(_wndProc);
    }

    internal static HeatProfile HeatSettings(WorkloadLevel level) =>
        level switch
        {
            WorkloadLevel.High => HeatProfile.DefaultLow,
            WorkloadLevel.Low => HeatProfile.DefaultLow,
            WorkloadLevel.Everyday => HeatProfile.Everyday,
            _ => HeatProfile.Idle,
        };

    private static GpuWorkload Create(string? preferredName)
    {
        IDXGIFactory4 factory = CreateDXGIFactory2<IDXGIFactory4>(debug: false);
        using IDXGIAdapter1 adapter = PickAdapter(factory, preferredName)
            ?? throw new InvalidOperationException("No discrete GPU.");
        if (D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out ID3D12Device? device).Failure
            || device is null)
        {
            throw new InvalidOperationException("Direct3D 12 is not available.");
        }

        ID3D12CommandQueue queue = device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        ReadOnlyMemory<byte> vs = Compiler.Compile(ShaderSource, "VSMain", "heat.hlsl", "vs_5_1");
        ReadOnlyMemory<byte> ps = Compiler.Compile(ShaderSource, "PSMain", "heat.hlsl", "ps_5_1");
        var root = new RootSignatureDescription1(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [new RootParameter1(new RootConstants(0, 0, 2), ShaderVisibility.Pixel)]);
        ID3D12RootSignature rootSignature = device.CreateRootSignature(root);
        var pso = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = vs,
            PixelShader = ps,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RasterizerState = RasterizerDescription.CullNone,
            BlendState = BlendDescription.Opaque,
            DepthStencilState = DepthStencilDescription.None,
            RenderTargetFormats = [Format.R8G8B8A8_UNorm],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = SampleDescription.Default,
        };
        ID3D12PipelineState pipeline = device.CreateGraphicsPipelineState(pso);
        ID3D12DescriptorHeap rtvHeap = device.CreateDescriptorHeap(
            new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, BufferCount));
        uint rtvStride = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        ID3D12CommandAllocator[] allocators = new ID3D12CommandAllocator[BufferCount];
        for (int i = 0; i < BufferCount; i++)
        {
            allocators[i] = device.CreateCommandAllocator(CommandListType.Direct);
        }

        ID3D12GraphicsCommandList commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,
            allocators[0],
            pipeline);
        commandList.Close();
        ID3D12Fence fence = device.CreateFence(0);
        var fenceEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        WndProc wndProc = Native.DefWindowProcW;
        IntPtr hwnd = Native.CreateHiddenWindow(WindowClassName, wndProc, 2560, 1440);
        var swapDesc = new SwapChainDescription1
        {
            BufferCount = BufferCount,
            Width = 2560,
            Height = 1440,
            Format = Format.R8G8B8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Flags = SwapChainFlags.None,
        };
        IDXGISwapChain1 swap1 = factory.CreateSwapChainForHwnd(queue, hwnd, swapDesc);

        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        IDXGISwapChain3 swapChain = swap1.QueryInterface<IDXGISwapChain3>();
        swap1.Dispose();
        ID3D12Resource[] backBuffers = CreateBackBuffers(device, swapChain, rtvHeap, rtvStride);
        return new GpuWorkload(
            factory,
            device,
            queue,
            rootSignature,
            pipeline,
            rtvHeap,
            rtvStride,
            allocators,
            commandList,
            fence,
            fenceEvent,
            hwnd,
            wndProc,
            swapChain,
            backBuffers,
            2560,
            1440);
    }

    private static IDXGIAdapter1? PickAdapter(IDXGIFactory4 factory, string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
            {
                AdapterDescription1 desc = adapter!.Description1;
                string name = desc.Description ?? string.Empty;
                if ((desc.Flags & AdapterFlags.Software) != 0 || GpuDeviceFilter.IsSoftwareAdapter(name))
                {
                    adapter.Dispose();
                    continue;
                }

                if (name.Contains(preferredName, StringComparison.OrdinalIgnoreCase)
                    || preferredName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return adapter;
                }

                adapter.Dispose();
            }
        }

        IDXGIAdapter1? best = null;
        long bestMemory = -1;
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            AdapterDescription1 desc = adapter!.Description1;
            string name = desc.Description ?? string.Empty;
            if ((desc.Flags & AdapterFlags.Software) != 0 || !GpuDeviceFilter.LooksDiscrete(name))
            {
                adapter.Dispose();
                continue;
            }

            long memory = (long)desc.DedicatedVideoMemory;
            if (best is null || memory > bestMemory)
            {
                best?.Dispose();
                best = adapter;
                bestMemory = memory;
            }
            else
            {
                adapter.Dispose();
            }
        }

        if (best is not null)
        {
            return best;
        }

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            AdapterDescription1 desc = adapter!.Description1;
            string name = desc.Description ?? string.Empty;
            if ((desc.Flags & AdapterFlags.Software) != 0 || GpuDeviceFilter.IsSoftwareAdapter(name))
            {
                adapter.Dispose();
                continue;
            }

            return adapter;
        }

        return null;
    }

    private static ID3D12Resource[] CreateBackBuffers(
        ID3D12Device device,
        IDXGISwapChain3 swapChain,
        ID3D12DescriptorHeap rtvHeap,
        uint rtvStride)
    {
        var buffers = new ID3D12Resource[BufferCount];
        CpuDescriptorHandle handle = rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (int i = 0; i < BufferCount; i++)
        {
            buffers[i] = swapChain.GetBuffer<ID3D12Resource>((uint)i);
            device.CreateRenderTargetView(buffers[i], null, handle);
            handle += (int)rtvStride;
        }

        return buffers;
    }

    private void Resize(int width, int height)
    {
        if (width == _width && height == _height)
        {
            return;
        }

        WaitIdle();
        foreach (ID3D12Resource buffer in _backBuffers)
        {
            buffer.Dispose();
        }

        _swapChain.ResizeBuffers(BufferCount, (uint)width, (uint)height, Format.R8G8B8A8_UNorm);
        _backBuffers = CreateBackBuffers(_device, _swapChain, _rtvHeap, _rtvStride);
        _width = width;
        _height = height;
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }

    private void Run(int passes, int iterations, CancellationToken token)
    {
        float time = 0;
        while (!token.IsCancellationRequested)
        {
            Native.Pump();
            try
            {
                Render(time, passes, iterations);
            }
            catch (Exception)
            {
                _faulted = true;
                return;
            }

            time += 0.016f;
        }
    }

    private void Render(float time, int passes, int iterations)
    {
        WaitForFrame(_frameIndex);
        _allocators[_frameIndex].Reset();
        _commandList.Reset(_allocators[_frameIndex], _pipeline);
        ID3D12Resource target = _backBuffers[_frameIndex];
        _commandList.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);
        CpuDescriptorHandle rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        rtv += _frameIndex * (int)_rtvStride;
        _commandList.OMSetRenderTargets(rtv);
        _commandList.RSSetViewport(new Viewport(_width, _height));
        _commandList.RSSetScissorRect(_width, _height);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        Span<uint> constants = stackalloc uint[2];
        constants[0] = BitConverter.SingleToUInt32Bits(time);
        constants[1] = (uint)iterations;
        _commandList.SetGraphicsRoot32BitConstants(0, constants, 0);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        int drawCount = Math.Max(1, passes);
        for (int pass = 0; pass < drawCount; pass++)
        {
            _commandList.DrawInstanced(3, 1, 0, 0);
        }

        _commandList.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
        _commandList.Close();
        _queue.ExecuteCommandList(_commandList);
        // Sync interval 1: wait for a display refresh. Present(0) with tearing
        // ran thousands of frames per second and made GPU VRMs whine.
        _swapChain.Present(PresentSyncInterval, PresentFlags.None);
        if (_device.DeviceRemovedReason.Failure)
        {
            throw new InvalidOperationException("GPU device was removed.");
        }

        Signal(_frameIndex);
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }

    private void Signal(int frameIndex)
    {
        ulong value = _fenceValue;
        _queue.Signal(_fence, value);
        _fenceValues[frameIndex] = value;
        _fenceValue++;
    }

    private void WaitForFrame(int frameIndex)
    {
        ulong expected = _fenceValues[frameIndex];
        if (expected == 0 || _fence.CompletedValue >= expected)
        {
            return;
        }

        _fence.SetEventOnCompletion(expected, _fenceEvent);
        _fenceEvent.WaitOne();
    }

    private void WaitIdle()
    {
        ulong value = _fenceValue;
        _queue.Signal(_fence, value);
        _fence.SetEventOnCompletion(value, _fenceEvent);
        _fenceEvent.WaitOne();
        _fenceValue++;
        Array.Clear(_fenceValues);
    }

    private void StopLocked()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private const string ShaderSource =
        """
        struct PSInput
        {
            float4 pos : SV_Position;
        };

        PSInput VSMain(uint id : SV_VertexID)
        {
            PSInput output;
            float2 uv = float2((id << 1) & 2, id & 2);
            output.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return output;
        }

        cbuffer Heat : register(b0)
        {
            float time;
            uint rounds;
        };

        float4 PSMain(PSInput input) : SV_Target
        {
            float2 uv = input.pos.xy * 0.0015;
            float v = uv.x + uv.y + time;
            uint n = rounds;
            [loop]
            for (uint i = 0; i < n; i++)
            {
                v = sin(v * 1.013 + uv.x) * cos(v * 1.029 + uv.y + time);
                v = v * 1.0001 + 0.002;
            }
            return float4(v, v * 0.5, 1.0 - v, 1);
        }
        """;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static class Native
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolwindow = 0x00000080;
        private const int SwHide = 0;
        private const uint PmRemove = 0x0001;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct WndClassW
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public Point pt;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WndClassW lpWndClass);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PeekMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Msg lpMsg);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageW(ref Msg lpMsg);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        public static IntPtr CreateHiddenWindow(string className, WndProc wndProc, int width, int height)
        {
            IntPtr instance = GetModuleHandleW(null);
            var wc = new WndClassW
            {
                lpfnWndProc = wndProc,
                hInstance = instance,
                lpszClassName = className,
            };
            RegisterClassW(ref wc);
            IntPtr hwnd = CreateWindowExW(
                WsExNoActivate | WsExToolwindow,
                className,
                string.Empty,
                WsPopup,
                0,
                0,
                width,
                height,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a hidden GPU window.");
            }

            ShowWindow(hwnd, SwHide);
            return hwnd;
        }

        public static void Pump()
        {
            while (PeekMessageW(out Msg msg, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
    }
}
